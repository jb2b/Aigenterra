using System.Diagnostics;
using System.Text;
using Aigenterra.Application.Recording;

namespace Aigenterra.Adapters.Git;

/// <summary>Runs bounded read-only Git commands on the agent machine. No Codex dependencies.</summary>
public sealed class GitRepositoryContextReader(TimeSpan? timeout = null, string executable = "git") : IRepositoryContextReader
{
    public async Task<RepositoryContext> ReadAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromMilliseconds(500));
        try
        {
            var root = await RunAsync(workingDirectory, ["rev-parse", "--show-toplevel"], deadline.Token);
            if (root.ExitCode != 0) return new(capturedAt, "not_repository");
            var common = await RunAsync(workingDirectory, ["rev-parse", "--path-format=absolute", "--git-common-dir"], deadline.Token);
            var trees = await RunAsync(workingDirectory, ["worktree", "list", "--porcelain", "-z"], deadline.Token);
            var branch = await RunAsync(workingDirectory, ["symbolic-ref", "--quiet", "--short", "HEAD"], deadline.Token);
            var head = await RunAsync(workingDirectory, ["rev-parse", "--verify", "HEAD"], deadline.Token);
            var status = await RunAsync(workingDirectory, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], deadline.Token);
            var finalHead = await RunAsync(workingDirectory, ["rev-parse", "--verify", "HEAD"], deadline.Token);
            if (common.ExitCode != 0 || trees.ExitCode != 0 || status.ExitCode != 0)
                return new(capturedAt, "git_error");
            if (head.Output != finalHead.Output || head.ExitCode != finalHead.ExitCode)
                return new(capturedAt, "changed_during_capture");

            var changed = new List<string>();
            var untracked = new List<string>();
            var entries = status.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (entry.Length < 4) return new(capturedAt, "git_error");
                (entry.StartsWith("??", StringComparison.Ordinal) ? untracked : changed).Add(entry[3..]);
                // In -z format renames/copies include a second, original path.
                if (entry[0] is 'R' or 'C' || entry[1] is 'R' or 'C')
                {
                    if (++index >= entries.Length) return new(capturedAt, "git_error");
                    changed.Add(entries[index]);
                }
            }
            var primary = trees.Output.Split('\0').FirstOrDefault(x => x.StartsWith("worktree ", StringComparison.Ordinal));
            return new(capturedAt, "captured", primary?[9..], common.Output.TrimEnd('\r', '\n'),
                root.Output.TrimEnd('\r', '\n'), branch.ExitCode == 0 ? branch.Output.TrimEnd('\r', '\n') : null,
                head.ExitCode == 0 ? head.Output.Trim() : null, entries.Length != 0, changed, untracked);
        }
        catch (OperationCanceledException) { return new(capturedAt, "timeout"); }
        catch (System.ComponentModel.Win32Exception) { return new(capturedAt, "git_unavailable"); }
        catch (IOException) { return new(capturedAt, "output_limit"); }
        catch (InvalidOperationException) { return new(capturedAt, "git_error"); }
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string cwd, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(cwd);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException();
        try
        {
            var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
            var output = new StringBuilder();
            var buffer = new char[4096];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (output.Length + read > 1024 * 1024) throw new IOException("Git output limit exceeded.");
                output.Append(buffer, 0, read);
            }
            await process.WaitForExitAsync(cancellationToken);
            await error;
            return (process.ExitCode, output.ToString());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
