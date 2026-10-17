using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.IntegrationTests;

public sealed class Console01Tests
{
    [Theory]
    [InlineData("sample@example.test|pass\\|word|JBSWY3DPEHPK3PXP", "pass|word")]
    [InlineData("sample@example.test|pass\\\\word|", "pass\\word")]
    public void OneLineParserSupportsEscapedDelimitersWithoutEchoingSecrets(string input, string password)
    {
        Assert.True(CredentialLineCodec.TryParse(input, out var credential, out var error), error);
        Assert.Equal(password, credential!.Password);
        Assert.DoesNotContain(password, error, StringComparison.Ordinal);
    }

    [Fact]
    public void OneShotContextClearsSensitiveValues()
    {
        using var context = new EphemeralCredentialContext("sample@example.test", "secret-value", "JBSWY3DPEHPK3PXP");

        Assert.False(context.IsCleared);
        context.Clear();

        Assert.True(context.IsCleared);
        Assert.Null(context.Account);
        Assert.Null(context.Password);
        Assert.Null(context.TotpSeed);
    }

    [Fact]
    public void EventStreamIsBoundedAndSanitizesCredentialLikeValues()
    {
        var stream = new AutoLoginConsoleBuffer(capacity: 20);
        for (var index = 0; index < 30; index++)
        {
            stream.Publish(Guid.NewGuid(), "Stage", ConsoleEventSeverity.Info, $"password=secret-{index}");
        }

        var events = stream.Snapshot();
        var copied = stream.CopyText();

        Assert.Equal(20, events.Count);
        Assert.DoesNotContain("secret-29", copied, StringComparison.Ordinal);
        Assert.Contains("password=[REDACTED]", copied, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderRegistryMarksOnlyChatGptAsSupported()
    {
        var registry = new ConsoleProviderRegistry();

        Assert.True(registry.TryGet("openai-chatgpt", out var chatGpt));
        Assert.True(chatGpt!.IsSupported);
        Assert.All(registry.List().Where(item => item.Id != "openai-chatgpt"), provider => Assert.False(provider.IsSupported));
    }

    [Fact]
    public async Task PlaywrightRuntimeResolvesThePackageCompatibleManagedChromium()
    {
        var runtime = new PlaywrightBrowserRuntime();

        var status = await runtime.GetStatusAsync();

        Assert.True(status.IsReady, status.Message);
        Assert.StartsWith("1.63.0", status.PackageVersion, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(status.ExpectedRevision));
        Assert.Equal(status.ExpectedRevision, status.ResolvedRevision);
        Assert.Contains("ms-playwright", status.BrowserCachePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ms-playwright", status.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Google\\Chrome", status.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TemporaryProfileFactoryDoesNotCreateAccountBindingOrPersistSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "flash-router-console-" + Guid.NewGuid().ToString("N"));
        var paths = new LocalDataPaths(root, Path.Combine(root, "automation"));
        var factory = new EphemeralBrowserProfileFactory(paths, new FixedBrowserRuntime());

        try
        {
            var preparation = await factory.PrepareAsync(BrowserSessionMode.Temporary);
            Assert.True(preparation.IsSuccess, preparation.Message);
            Assert.NotNull(preparation.Context);
            Assert.Equal(Guid.Empty, preparation.Context!.AccountId);
            Assert.True(Directory.Exists(preparation.Context.ProfileDirectory));

            var generatedDirectory = preparation.Context.ProfileDirectory;
            await preparation.DisposeAsync();
            Assert.False(Directory.Exists(generatedDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FixedBrowserRuntime : IPlaywrightBrowserRuntime
    {
        public Task<PlaywrightBrowserRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PlaywrightBrowserRuntimeStatus(
                PlaywrightChromiumReadiness.ChromiumReady,
                "test",
                Environment.ProcessPath,
                Path.Combine(Path.GetTempPath(), "ms-playwright"),
                "test",
                "test",
                "Test-managed Chromium is ready."));

        public Task<PlaywrightBrowserRuntimeStatus> InstallOrRepairAsync(CancellationToken cancellationToken = default)
            => GetStatusAsync(cancellationToken);
    }
}
