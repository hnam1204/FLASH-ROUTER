using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.IntegrationTests;

public sealed class AuthBrowser04RoutingTests
{
    [Fact]
    public async Task IntegratedFactoryRejectsLocalMockDestinationBeforeBrowserLaunch()
    {
        var ownership = new RecordingOwnershipCoordinator();
        var factory = new PlaywrightBrowserAuthenticationSessionFactory(ownership);
        var profile = new BrowserProfileSessionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "openai-chatgpt",
            "synthetic-profile",
            Path.Combine(Path.GetTempPath(), "flash-router-routing-test-profile"),
            "missing-chrome",
            new BrowserProfileBindingVm(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "openai-chatgpt",
                "Chrome",
                "synthetic-profile",
                "Ready",
                DateTimeOffset.UtcNow,
                null));

        var exception = await Assert.ThrowsAsync<BrowserAuthSessionException>(() =>
            factory.OpenAsync(profile, new Uri("http://127.0.0.1:5299/login")));

        Assert.Equal("INTEGRATED_START_ORIGIN_REQUIRED", exception.SafeCode);
        Assert.DoesNotContain("127.0.0.1", exception.SafeMessage, StringComparison.Ordinal);
        Assert.Equal(0, ownership.AcquireCount);
    }

    [Theory]
    [InlineData("https://chatgpt.com/")]
    [InlineData("https://chatgpt.com")]
    public void OfficialStartAllowsOnlyThePublicChatGptLandingPage(string value)
    {
        Assert.True(BrowserOriginPolicy.IsOfficialChatGptStart(new Uri(value)));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5299/login")]
    [InlineData("https://chatgpt.com/login")]
    [InlineData("https://chatgpt.com/?returnTo=/login")]
    [InlineData("https://auth.openai.com/")]
    public void OfficialStartRejectsMockDirectLoginAndRedirectDestinations(string value)
    {
        Assert.False(BrowserOriginPolicy.IsOfficialChatGptStart(new Uri(value)));
    }

    private sealed class RecordingOwnershipCoordinator : IBrowserProfileOwnershipCoordinator
    {
        public int AcquireCount { get; private set; }

        public BrowserProfileOwnershipResult TryAcquire(
            string profileDirectory,
            BrowserProfileOwnerKind ownerKind)
        {
            AcquireCount++;
            return new BrowserProfileOwnershipResult(
                false,
                BrowserProfileLifecycleState.Failed,
                null,
                "TEST_UNEXPECTED_OWNERSHIP",
                "The routing guard should run before ownership acquisition.");
        }
    }
}
