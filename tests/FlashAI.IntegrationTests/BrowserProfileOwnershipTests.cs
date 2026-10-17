using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit.Sdk;

namespace FlashAI.IntegrationTests;

public sealed class BrowserProfileOwnershipTests
{
    [Fact]
    public void AvailableProfileCanBeAcquiredAndReleasedWithoutDeletingMetadata()
    {
        var root = CreateProfileDirectory("available");
        try
        {
            var coordinator = new BrowserProfileOwnershipCoordinator();
            var acquired = coordinator.TryAcquire(root, BrowserProfileOwnerKind.Playwright);

            Assert.True(acquired.IsAcquired);
            Assert.Equal(BrowserProfileLifecycleState.OwnedByPlaywright, acquired.State);
            Assert.NotNull(acquired.Lease);

            var blocked = coordinator.TryAcquire(root, BrowserProfileOwnerKind.ManagedChrome);
            Assert.False(blocked.IsAcquired);
            Assert.Equal("PROFILE_IN_USE", blocked.Status);

            File.WriteAllText(Path.Combine(root, "metadata.marker"), "synthetic");
            acquired.Lease!.Dispose();

            var reopened = coordinator.TryAcquire(root, BrowserProfileOwnerKind.ManagedChrome);
            Assert.True(reopened.IsAcquired);
            Assert.True(File.Exists(Path.Combine(root, "metadata.marker")));
            reopened.Lease!.Dispose();
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void ActiveChromeLockIsReportedAsExternalOwnership()
    {
        var root = CreateProfileDirectory("active-lock");
        var lockPath = Path.Combine(root, "SingletonLock");
        try
        {
            using var lockHandle = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var coordinator = new BrowserProfileOwnershipCoordinator();

            var result = coordinator.TryAcquire(root, BrowserProfileOwnerKind.Playwright);

            Assert.False(result.IsAcquired);
            Assert.Equal(BrowserProfileLifecycleState.InUseExternally, result.State);
            Assert.Equal(BrowserProfileOwnerKind.External, result.OwnerKind);
            Assert.Equal("PROFILE_IN_USE", result.Status);
            Assert.True(File.Exists(lockPath));
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void StaleLockMetadataDoesNotBlockAControlledLaunch()
    {
        var root = CreateProfileDirectory("stale-lock");
        var lockPath = Path.Combine(root, "SingletonLock");
        try
        {
            File.WriteAllText(lockPath, "stale synthetic marker");
            var inspector = new ChromeProfileLockInspector();
            Assert.Equal(BrowserProfileLockState.StaleMetadata, inspector.Inspect(root));

            var coordinator = new BrowserProfileOwnershipCoordinator(inspector);
            var result = coordinator.TryAcquire(root, BrowserProfileOwnerKind.Playwright);

            Assert.True(result.IsAcquired);
            Assert.Equal(BrowserProfileLifecycleState.OwnedByPlaywright, result.State);
            Assert.True(File.Exists(lockPath));
            result.Lease!.Dispose();
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task ConcurrentAcquisitionAllowsOnlyOneOwnerPerProfile()
    {
        var root = CreateProfileDirectory("concurrent");
        try
        {
            var coordinator = new BrowserProfileOwnershipCoordinator();
            var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
                Task.Run(() => coordinator.TryAcquire(root, BrowserProfileOwnerKind.Playwright))));

            Assert.Single(attempts, result => result.IsAcquired);
            Assert.Equal(11, attempts.Count(result => result.Status == "PROFILE_IN_USE"));
            attempts.Single(result => result.IsAcquired).Lease!.Dispose();
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void UnicodeProfilePathUsesTheSameCanonicalOwnershipKey()
    {
        var root = CreateProfileDirectory("unicode-ĐỒ-ÁN");
        try
        {
            var coordinator = new BrowserProfileOwnershipCoordinator();
            var first = coordinator.TryAcquire(root, BrowserProfileOwnerKind.ManagedChrome);
            var equivalentPath = Path.Combine(root, ".", "child", "..");
            var second = coordinator.TryAcquire(equivalentPath, BrowserProfileOwnerKind.Playwright);

            Assert.True(first.IsAcquired);
            Assert.False(second.IsAcquired);
            Assert.Equal("PROFILE_IN_USE", second.Status);
            first.Lease!.Dispose();
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void MissingChromeExecutableIsClassifiedWithoutStartingAProcess()
    {
        var root = CreateProfileDirectory("diagnostics");
        try
        {
            using var launcher = new ChromeProcessLauncher();
            var result = launcher.TryLaunchDetailed(
                Path.Combine(root, "missing-chrome.exe"),
                root,
                "https://chatgpt.com/");

            Assert.False(result.IsSuccess);
            Assert.Equal("CHROME_NOT_FOUND", result.Status);
            Assert.DoesNotContain("missing-chrome", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task ProductionPlaywrightFactoryStopsBeforeLaunchForAnActiveExternalLock()
    {
        var chrome = new ChromeExecutableResolver(new ConfigurationBuilder().Build()).Resolve();
        if (string.IsNullOrWhiteSpace(chrome))
        {
            throw SkipException.ForSkip("Google Chrome was not found; production launch seam is unavailable.");
        }

        var root = CreateProfileDirectory("factory-lock");
        try
        {
            using var lockHandle = new FileStream(
                Path.Combine(root, "SingletonLock"),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None);
            var profile = new BrowserProfileSessionContext(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "openai-chatgpt",
                "synthetic",
                root,
                chrome,
                new BrowserProfileBindingVm(Guid.NewGuid(), Guid.NewGuid(), "openai-chatgpt", "Chrome", "synthetic", "Ready", DateTimeOffset.UtcNow, null));
            var factory = new PlaywrightBrowserAuthenticationSessionFactory();

            var exception = await Assert.ThrowsAsync<BrowserAuthSessionException>(() =>
                factory.OpenAsync(profile, BrowserOriginPolicy.ChatGptUri));

            Assert.Equal("PROFILE_IN_USE", exception.SafeCode);
            Assert.Equal("ProfileOwnership", exception.TechnicalExceptionType);
            Assert.True(File.Exists(Path.Combine(root, "SingletonLock")));
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static string CreateProfileDirectory(string suffix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"flash-router-profile-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTestDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
