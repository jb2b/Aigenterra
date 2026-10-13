namespace Aigenterra.Architecture.Tests;

public sealed class DatabaseModeTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("External", false)]
    [InlineData("external", false)]
    [InlineData("Container", true)]
    public void Database_mode_is_explicit_with_external_default(string? mode, bool container) =>
        Assert.Equal(container, AppHost.DatabaseMode.UseContainer(mode));

    [Fact]
    public void Invalid_mode_does_not_silently_switch_database() =>
        Assert.Throws<InvalidOperationException>(() => AppHost.DatabaseMode.UseContainer("unknown"));
}
