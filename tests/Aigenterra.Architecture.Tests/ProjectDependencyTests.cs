using System.Xml.Linq;

namespace Aigenterra.Architecture.Tests;

/// <summary>
/// Guards the intended project dependency graph and technology boundaries.
/// </summary>
public sealed class ProjectDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Aigenterra.Domain"] = [],
            ["Aigenterra.Application"] = ["Aigenterra.Domain"],
            ["Aigenterra.Infrastructure"] = ["Aigenterra.Application"],
            ["Aigenterra.Adapters.Codex"] = ["Aigenterra.Application"],
            ["Aigenterra.Collector"] = [],
            ["Aigenterra.ServiceDefaults"] = [],
            ["Aigenterra.Api"] =
            [
                "Aigenterra.Adapters.Codex",
                "Aigenterra.Application",
                "Aigenterra.Infrastructure",
                "Aigenterra.ServiceDefaults"
            ],
            ["Aigenterra.AppHost"] = ["Aigenterra.Api"]
        };

    [Fact]
    public void Production_projects_have_exactly_the_allowed_project_references()
    {
        var solutionRoot = FindSolutionRoot();

        foreach (var (projectName, expectedReferences) in ExpectedProjectReferences)
        {
            var project = LoadProject(solutionRoot, projectName);
            var actualReferences = project
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => GetReferencedProjectName(include!))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
        }
    }

    [Theory]
    [InlineData("Aigenterra.Domain")]
    [InlineData("Aigenterra.Application")]
    public void Core_projects_have_no_runtime_framework_or_package_references(string projectName)
    {
        var project = LoadProject(FindSolutionRoot(), projectName);
        var runtimeDependencies = project
            .Descendants()
            .Where(element =>
                element.Name.LocalName is "FrameworkReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? element.Name.LocalName)
            .ToArray();

        Assert.Empty(runtimeDependencies);
    }

    [Fact]
    public void Collector_depends_only_on_opentelemetry_transport_packages()
    {
        var project = LoadProject(FindSolutionRoot(), "Aigenterra.Collector");
        var projectOrFrameworkReferences = project
            .Descendants()
            .Where(element => element.Name.LocalName is
                "ProjectReference" or "FrameworkReference")
            .ToArray();
        var packages = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(package => !string.IsNullOrWhiteSpace(package))
            .Select(package => package!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(projectOrFrameworkReferences);
        Assert.Equal(
            ["OpenTelemetry", "OpenTelemetry.Exporter.OpenTelemetryProtocol"],
            packages);
    }

    [Theory]
    [InlineData("Aigenterra.Domain")]
    [InlineData("Aigenterra.Application")]
    public void Core_source_does_not_reference_outer_or_serialization_technology(string projectName)
    {
        var projectDirectory = Path.Combine(FindSolutionRoot(), "src", projectName);
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
        string[] forbiddenDependencies =
        [
            "Aigenterra.Api",
            "Aigenterra.Adapters.Codex",
            "Aigenterra.AppHost",
            "Aigenterra.Collector",
            "Aigenterra.Infrastructure",
            "Aigenterra.ServiceDefaults",
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Aspire.Hosting",
            "System.Text.Json"
        ];

        Assert.DoesNotContain(
            forbiddenDependencies,
            dependency => source.Contains(dependency, StringComparison.Ordinal));
    }

    private static XDocument LoadProject(string solutionRoot, string projectName)
    {
        var projectPath = Path.Combine(solutionRoot, "src", projectName, $"{projectName}.csproj");

        Assert.True(File.Exists(projectPath), $"Expected project file was not found: {projectPath}");

        return XDocument.Load(projectPath);
    }

    private static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aigenterra.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find Aigenterra.slnx above {AppContext.BaseDirectory}.");
    }

    private static string GetReferencedProjectName(string include)
    {
        var normalizedPath = include
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFileNameWithoutExtension(normalizedPath);
    }
}
