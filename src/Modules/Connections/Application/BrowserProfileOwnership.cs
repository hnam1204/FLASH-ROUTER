using System.Collections.Concurrent;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

public sealed class ChromeProfileLockInspector : IChromeProfileLockInspector
{
    private static readonly string[] LockFileNames =
    [
        "SingletonLock",
        "SingletonCookie",
        "SingletonSocket"
    ];

    public BrowserProfileLockState Inspect(string profileDirectory)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory) || !Directory.Exists(profileDirectory))
        {
            return BrowserProfileLockState.None;
        }

        var foundMetadata = false;
        foreach (var name in LockFileNames)
        {
            var path = Path.Combine(profileDirectory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            foundMetadata = true;
            if (!CanOpenExclusively(path))
            {
                return BrowserProfileLockState.Active;
            }
        }

        return foundMetadata
            ? BrowserProfileLockState.StaleMetadata
            : BrowserProfileLockState.None;
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.SequentialScan);
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

public sealed class BrowserProfileOwnershipCoordinator : IBrowserProfileOwnershipCoordinator
{
    private readonly IChromeProfileLockInspector _lockInspector;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, LeaseState> _leases = new(StringComparer.OrdinalIgnoreCase);

    public BrowserProfileOwnershipCoordinator(IChromeProfileLockInspector? lockInspector = null)
    {
        _lockInspector = lockInspector ?? new ChromeProfileLockInspector();
    }

    public BrowserProfileOwnershipResult TryAcquire(
        string profileDirectory,
        BrowserProfileOwnerKind ownerKind)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory))
        {
            return new BrowserProfileOwnershipResult(
                false,
                BrowserProfileLifecycleState.Failed,
                null,
                "INVALID_PROFILE_PATH",
                "Không xác định được thư mục profile Chrome.");
        }

        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(profileDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (ArgumentException)
        {
            return new BrowserProfileOwnershipResult(
                false,
                BrowserProfileLifecycleState.Failed,
                null,
                "INVALID_PROFILE_PATH",
                "Đường dẫn profile Chrome không hợp lệ.");
        }

        lock (_sync)
        {
            if (_leases.TryGetValue(canonicalPath, out var existing))
            {
                return InUse(existing.OwnerKind);
            }

            if (_lockInspector.Inspect(canonicalPath) == BrowserProfileLockState.Active)
            {
                return new BrowserProfileOwnershipResult(
                    false,
                    BrowserProfileLifecycleState.InUseExternally,
                    BrowserProfileOwnerKind.External,
                    "PROFILE_IN_USE",
                    "Chrome profile đang được sử dụng. Hãy đóng cửa sổ tương ứng hoặc tiếp tục đăng nhập thủ công trong Chrome.");
            }

            var leaseState = new LeaseState(ownerKind, canonicalPath);
            _leases[canonicalPath] = leaseState;

            return new BrowserProfileOwnershipResult(
                true,
                GetLifecycleState(ownerKind),
                ownerKind,
                "PROFILE_ACQUIRED",
                "Profile ownership acquired.",
                new Lease(this, leaseState));
        }
    }

    private static BrowserProfileOwnershipResult InUse(BrowserProfileOwnerKind ownerKind)
        => new(
            false,
            GetLifecycleState(ownerKind),
            ownerKind,
            "PROFILE_IN_USE",
            "Chrome profile đang được FLASH ROUTER sử dụng. Hãy đóng phiên hiện tại trước khi thử lại.");

    private static BrowserProfileLifecycleState GetLifecycleState(BrowserProfileOwnerKind ownerKind)
        => ownerKind switch
        {
            BrowserProfileOwnerKind.ManagedChrome => BrowserProfileLifecycleState.OwnedByManagedChrome,
            BrowserProfileOwnerKind.Playwright => BrowserProfileLifecycleState.OwnedByPlaywright,
            BrowserProfileOwnerKind.PermanentDeletion => BrowserProfileLifecycleState.Deleting,
            _ => BrowserProfileLifecycleState.InUseExternally
        };

    private void Release(LeaseState state)
    {
        lock (_sync)
        {
            ((ICollection<KeyValuePair<string, LeaseState>>)_leases).Remove(
                new KeyValuePair<string, LeaseState>(state.CanonicalPath, state));
        }
    }

    private sealed record LeaseState(BrowserProfileOwnerKind OwnerKind, string CanonicalPath);

    private sealed class Lease : IDisposable
    {
        private readonly BrowserProfileOwnershipCoordinator _owner;
        private readonly LeaseState _state;
        private int _released;

        public Lease(BrowserProfileOwnershipCoordinator owner, LeaseState state)
        {
            _owner = owner;
            _state = state;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.Release(_state);
            }
        }
    }
}
