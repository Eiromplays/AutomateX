using AutomateX.Engine.Actions;
using Xunit;

namespace AutomateX.Tests;

// Pure wait semantics: mode inference and the timer wake time.
public sealed class WaitTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Delay_seconds_wakes_after_the_delay()
    {
        var config = new WaitConfig(DelaySeconds: 30);
        Assert.False(Wait.IsSignal(config));
        Assert.Equal(Now.AddSeconds(30), Wait.WakeAt(config, Now));
    }

    [Fact]
    public void Until_wakes_at_the_given_time()
    {
        var until = Now.AddHours(2);
        Assert.Equal(until, Wait.WakeAt(new WaitConfig(Until: until), Now));
    }

    [Fact]
    public void Signal_without_timeout_never_wakes()
    {
        var config = new WaitConfig(Mode: "signal");
        Assert.True(Wait.IsSignal(config));
        Assert.Null(Wait.WakeAt(config, Now));
    }

    [Fact]
    public void Signal_with_timeout_wakes_at_the_timeout()
    {
        var config = new WaitConfig(Mode: "signal", TimeoutSeconds: 60);
        Assert.Equal(Now.AddSeconds(60), Wait.WakeAt(config, Now));
    }

    [Fact]
    public void Bare_config_is_an_indefinite_signal_wait()
    {
        var config = new WaitConfig();
        Assert.True(Wait.IsSignal(config));
        Assert.Null(Wait.WakeAt(config, Now));
    }

    [Fact]
    public void Delay_and_until_together_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Wait.WakeAt(new WaitConfig(DelaySeconds: 1, Until: Now), Now));
    }

    [Fact]
    public void Negative_delay_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Wait.WakeAt(new WaitConfig(DelaySeconds: -1), Now));
    }
}
