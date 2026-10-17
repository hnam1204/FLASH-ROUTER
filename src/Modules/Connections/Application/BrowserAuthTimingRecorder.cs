using System.Diagnostics;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

internal enum BrowserAuthTimingStage
{
    ProfileLease,
    ChromeLaunch,
    OfficialNavigation,
    ExistingSessionCheck,
    LoginCta,
    EmailStep,
    PasswordStep,
    MfaStep,
    AuthenticatedObservation
}

internal sealed class BrowserAuthTimingRecorder
{
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly object _sync = new();
    private long? _profileLeaseDurationMs;
    private long? _chromeLaunchDurationMs;
    private long? _officialNavigationDurationMs;
    private long? _existingSessionCheckDurationMs;
    private long? _loginCtaDurationMs;
    private long? _emailStepDurationMs;
    private long? _passwordStepDurationMs;
    private long? _mfaStepDurationMs;
    private long? _authenticatedObservationDurationMs;

    public static Stopwatch StartStage() => Stopwatch.StartNew();

    public void Complete(BrowserAuthTimingStage stage, Stopwatch stopwatch)
    {
        var durationMs = Math.Max(0L, stopwatch.ElapsedMilliseconds);
        lock (_sync)
        {
            switch (stage)
            {
                case BrowserAuthTimingStage.ProfileLease:
                    _profileLeaseDurationMs ??= durationMs;
                    break;
                case BrowserAuthTimingStage.ChromeLaunch:
                    _chromeLaunchDurationMs ??= durationMs;
                    break;
                case BrowserAuthTimingStage.OfficialNavigation:
                    _officialNavigationDurationMs ??= durationMs;
                    break;
                case BrowserAuthTimingStage.ExistingSessionCheck:
                    _existingSessionCheckDurationMs ??= durationMs;
                    break;
                case BrowserAuthTimingStage.LoginCta:
                    _loginCtaDurationMs ??= durationMs;
                    break;
                case BrowserAuthTimingStage.EmailStep:
                    _emailStepDurationMs ??= durationMs;
                    break;
                case BrowserAuthTimingStage.PasswordStep:
                    _passwordStepDurationMs ??= durationMs;
                    break;
                case BrowserAuthTimingStage.MfaStep:
                    _mfaStepDurationMs ??= durationMs;
                    break;
                case BrowserAuthTimingStage.AuthenticatedObservation:
                    _authenticatedObservationDurationMs ??= durationMs;
                    break;
            }
        }
    }

    public void Merge(BrowserAuthTimingSnapshot timing)
    {
        lock (_sync)
        {
            if (timing.ProfileLeaseDurationMs is not null)
            {
                _profileLeaseDurationMs = timing.ProfileLeaseDurationMs;
            }

            if (timing.ChromeLaunchDurationMs is not null)
            {
                _chromeLaunchDurationMs = timing.ChromeLaunchDurationMs;
            }

            if (timing.OfficialNavigationDurationMs is not null)
            {
                _officialNavigationDurationMs = timing.OfficialNavigationDurationMs;
            }
        }
    }

    public BrowserAuthTimingSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new BrowserAuthTimingSnapshot(
                ProfileLeaseDurationMs: _profileLeaseDurationMs,
                ChromeLaunchDurationMs: _chromeLaunchDurationMs,
                OfficialNavigationDurationMs: _officialNavigationDurationMs,
                ExistingSessionCheckDurationMs: _existingSessionCheckDurationMs,
                LoginCtaDurationMs: _loginCtaDurationMs,
                EmailStepDurationMs: _emailStepDurationMs,
                PasswordStepDurationMs: _passwordStepDurationMs,
                MfaStepDurationMs: _mfaStepDurationMs,
                AuthenticatedObservationDurationMs: _authenticatedObservationDurationMs,
                TotalAttemptDurationMs: Math.Max(0L, _total.ElapsedMilliseconds));
        }
    }
}
