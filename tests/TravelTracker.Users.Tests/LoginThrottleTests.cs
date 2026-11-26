using TravelTracker.Users.Api.Services;

namespace TravelTracker.Users.Tests;

/** Exercises the username lockout directly with a clock the test moves by hand. */
public sealed class LoginThrottleTests
{
    sealed class ManualClock : TimeProvider
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan span) => now += span;
    }

    [Fact]
    public void FailuresOlderThanTheWindowStopCounting()
    {
        var clock = new ManualClock();
        var throttle = new LoginThrottle(clock);
        // one failure every 10 minutes never puts five inside a 15 minute window
        for (var attempt = 0; attempt < 10; attempt++)
        {
            throttle.RecordFailure("ada");
            Assert.Null(throttle.RetryAfter("ada"));
            clock.Advance(TimeSpan.FromMinutes(10));
        }
    }

    [Fact]
    public void FiveFailuresInsideTheWindowLockAndTheLockExpires()
    {
        var clock = new ManualClock();
        var throttle = new LoginThrottle(clock);
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++) throttle.RecordFailure("ada");
        Assert.True(throttle.RetryAfter("ada") > TimeSpan.Zero);
        clock.Advance(LoginThrottle.LockDuration);
        Assert.Null(throttle.RetryAfter("ada"));
    }

    [Fact]
    public void LockLastsTheFullDurationFromTheFifthFailure()
    {
        var clock = new ManualClock();
        var throttle = new LoginThrottle(clock);
        foreach (var minutes in new[] { 3, 3, 3, 5 })
        {
            throttle.RecordFailure("ada");
            clock.Advance(TimeSpan.FromMinutes(minutes));
        }
        throttle.RecordFailure("ada");
        clock.Advance(LoginThrottle.LockDuration - TimeSpan.FromMinutes(1));
        Assert.True(throttle.RetryAfter("ada") > TimeSpan.Zero);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(throttle.RetryAfter("ada"));
    }

    [Fact]
    public void AFullTrackerStillLocksNewUsernamesWithoutLockingOthers()
    {
        var throttle = new LoginThrottle(new ManualClock());
        for (var index = 0; index < 10_000; index++) throttle.RecordFailure($"user{index}");
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++) throttle.RecordFailure("newcomer");
        Assert.True(throttle.RetryAfter("newcomer") > TimeSpan.Zero);
        Assert.Null(throttle.RetryAfter("bystander"));
    }

    [Fact]
    public async Task ConcurrentFailuresAndExpiryNeverLoseAFreshFailure()
    {
        var clock = new ManualClock();
        var throttle = new LoginThrottle(clock);
        for (var index = 0; index < 10_000; index++) throttle.RecordFailure($"user{index}");
        clock.Advance(LoginThrottle.LockDuration);
        // every tracked entry is expired, so these failures prune while other threads record fresh ones
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => throttle.RecordFailure("ada"))));
        Assert.True(throttle.RetryAfter("ada") > TimeSpan.Zero);
    }

    [Fact]
    public void UsernamesKeepIndependentState()
    {
        var throttle = new LoginThrottle(new ManualClock());
        for (var index = 0; index < 2_000; index++) throttle.RecordFailure($"victim{index}");
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures - 1; attempt++) throttle.RecordFailure("ada");
        for (var index = 0; index < 2_000; index++) throttle.Reset($"other{index}");
        for (var index = 0; index < 2_000; index++) Assert.Null(throttle.RetryAfter($"victim{index}"));
        throttle.RecordFailure("ada");
        Assert.True(throttle.RetryAfter("ada") > TimeSpan.Zero);
    }
}
