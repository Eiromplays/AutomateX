using AutomateX.Engine.Actions;
using Xunit;

namespace AutomateX.Tests;

// schedule.workflow timing rules: exactly one of delaySeconds / runAt, always future.
public sealed class ScheduleResolutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Delay_seconds_resolves_relative_to_now()
    {
        Assert.Equal(Now.AddSeconds(7200), ScheduleResolution.Resolve(7200, null, Now));
    }

    [Fact]
    public void Explicit_run_at_passes_through()
    {
        var at = Now.AddDays(1);
        Assert.Equal(at, ScheduleResolution.Resolve(null, at, Now));
    }

    [Fact]
    public void Neither_provided_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ScheduleResolution.Resolve(null, null, Now));
    }

    [Fact]
    public void Both_provided_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ScheduleResolution.Resolve(60, Now.AddDays(1), Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Non_future_times_are_rejected(int delaySeconds)
    {
        Assert.Throws<ArgumentException>(() => ScheduleResolution.Resolve(delaySeconds, null, Now));
    }

    [Fact]
    public void A_past_run_at_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ScheduleResolution.Resolve(null, Now.AddMinutes(-1), Now));
    }
}
