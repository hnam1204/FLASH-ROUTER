using System.Text.Json;
using FlashAI.Cli;
using FlashAI.Foundation.Hosting;

namespace FlashAI.IntegrationTests;

public sealed class CliLauncherTests
{
    [Fact]
    public void ParserDefaultsToStartAndOpen()
    {
        var parsed = LauncherCommandParser.Parse(Array.Empty<string>());

        Assert.True(parsed.IsValid);
        Assert.Equal(LauncherCommand.StartAndOpen, parsed.Command);
    }

    [Theory]
    [InlineData("open", LauncherCommand.Open)]
    [InlineData("status", LauncherCommand.Status)]
    [InlineData("stop", LauncherCommand.Stop)]
    [InlineData("restart", LauncherCommand.Restart)]
    [InlineData("logs", LauncherCommand.Logs)]
    [InlineData("version", LauncherCommand.Version)]
    [InlineData("help", LauncherCommand.Help)]
    [InlineData("--help", LauncherCommand.Help)]
    public void ParserRecognizesSupportedCommands(string value, LauncherCommand expected)
    {
        var parsed = LauncherCommandParser.Parse(new[] { value });

        Assert.True(parsed.IsValid);
        Assert.Equal(expected, parsed.Command);
    }

    [Fact]
    public void ParserRejectsUnknownOrExtraArguments()
    {
        var unknown = LauncherCommandParser.Parse(new[] { "wat" });
        var extra = LauncherCommandParser.Parse(new[] { "status", "extra" });

        Assert.False(unknown.IsValid);
        Assert.Contains("Unknown command", unknown.Error);
        Assert.False(extra.IsValid);
        Assert.Contains("Unexpected arguments", extra.Error);
    }

    [Fact]
    public void ControlNamesAreStableAndDataRootScoped()
    {
        var first = ManagedHostControl.GetPipeName("C:\\Flash Router\\Data");
        var equivalent = ManagedHostControl.GetPipeName("C:\\Flash Router\\Data\\");
        var second = ManagedHostControl.GetPipeName("C:\\Flash Router\\Other");

        Assert.Equal(first, equivalent);
        Assert.NotEqual(first, second);
        Assert.StartsWith("FlashRouter-", first);
        Assert.Equal(ManagedHostControl.GetMutexName("C:\\Flash Router\\Data"),
            "Local\\FlashRouterHost-" + first);
    }

    [Fact]
    public void WorkspaceRootDiscoveryDoesNotDependOnCurrentDirectory()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        Assert.Equal(root, LauncherConfigurationResolver.FindWorkspaceRoot(AppContext.BaseDirectory));
    }

    [Fact]
    public void ControlResponseDoesNotContainTheControlSecret()
    {
        var response = new ManagedHostControlResponse(
            true,
            "Ready",
            null,
            123,
            "http://127.0.0.1:5270",
            "0.1.0",
            "FLASH_ROUTER.exe",
            DateTimeOffset.UtcNow);

        var serialized = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
    }
}
