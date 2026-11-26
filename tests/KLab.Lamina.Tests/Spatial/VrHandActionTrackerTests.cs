// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using Xunit;

namespace KLab.Lamina.Tests;

public sealed class VrHandActionTrackerTests
{
    [Fact]
    public void TransientDropout_OneOrTwoTicks_KeepsBoundAndPressedAndRecoversCleanly()
    {
        var tracker = new VrHandActionTracker(deactivationGraceTicks: 3);
        var pose = Matrix4x4.CreateTranslation(0f, 1.2f, 0f);

        // Tick 0: Active & trigger pressed.
        var t0 = tracker.Update(0, new VrHandRawSample(
            IsPoseValid: true,
            AimPose: pose,
            ActionsBound: true,
            TriggerActive: true,
            TriggerPressed: true,
            ScrollActive: true,
            ScrollDelta: new Vector2(0f, 0.5f)));

        Assert.True(t0.IsBound);
        Assert.True(t0.TriggerPressed);
        Assert.False(t0.IsInGracePeriod);
        Assert.False(t0.ShouldCancel);

        // Tick 1 (1-frame dropout): bActive=false on action set transition.
        var t1 = tracker.Update(0, new VrHandRawSample(
            IsPoseValid: true,
            AimPose: pose,
            ActionsBound: false,
            TriggerActive: false,
            TriggerPressed: false));

        Assert.True(t1.IsBound);
        Assert.True(t1.IsInGracePeriod);
        Assert.Equal(2, t1.RemainingGraceTicks);
        Assert.True(t1.TriggerPressed);
        Assert.False(t1.ShouldCancel);

        // Tick 2 (2-frame dropout): still inside 3-tick grace window.
        var t2 = tracker.Update(0, new VrHandRawSample(
            IsPoseValid: true,
            AimPose: pose,
            ActionsBound: false,
            TriggerActive: false,
            TriggerPressed: false));

        Assert.True(t2.IsBound);
        Assert.True(t2.IsInGracePeriod);
        Assert.Equal(1, t2.RemainingGraceTicks);
        Assert.True(t2.TriggerPressed);
        Assert.False(t2.ShouldCancel);

        // Tick 3: Actions recover -> grace resets, press continues seamlessly.
        var t3 = tracker.Update(0, new VrHandRawSample(
            IsPoseValid: true,
            AimPose: pose,
            ActionsBound: true,
            TriggerActive: true,
            TriggerPressed: true));

        Assert.True(t3.IsBound);
        Assert.False(t3.IsInGracePeriod);
        Assert.Equal(0, t3.RemainingGraceTicks);
        Assert.True(t3.TriggerPressed);
        Assert.False(t3.ShouldCancel);
    }

    [Fact]
    public void SustainedDropout_MoreThanThreeTicks_DropsBoundAndEmitsCancelOnce()
    {
        var tracker = new VrHandActionTracker(deactivationGraceTicks: 3);
        var pose = Matrix4x4.CreateTranslation(0f, 1.2f, 0f);

        // Initial bound & pressed frame.
        tracker.Update(0, new VrHandRawSample(true, pose, ActionsBound: true, TriggerActive: true, TriggerPressed: true));

        // Ticks 1, 2, 3: all 3 grace ticks remain bound & pressed.
        for (int tick = 1; tick <= 3; tick++)
        {
            var grace = tracker.Update(0, new VrHandRawSample(true, pose, ActionsBound: false, TriggerActive: false, TriggerPressed: false));
            Assert.True(grace.IsBound, $"Expected IsBound=true on grace tick {tick}");
            Assert.True(grace.TriggerPressed, $"Expected TriggerPressed=true on grace tick {tick}");
            Assert.False(grace.ShouldCancel, $"Expected ShouldCancel=false on grace tick {tick}");
        }

        // Tick 4 (> 3 ticks): genuine unbinding -> IsBound=false, TriggerPressed=false, ShouldCancel=true!
        var expired = tracker.Update(0, new VrHandRawSample(true, pose, ActionsBound: false, TriggerActive: false, TriggerPressed: false));
        Assert.False(expired.IsBound);
        Assert.False(expired.IsInGracePeriod);
        Assert.False(expired.TriggerPressed);
        Assert.True(expired.ShouldCancel);

        // Tick 5: already unbound -> ShouldCancel=false (no duplicate cancels).
        var after = tracker.Update(0, new VrHandRawSample(true, pose, ActionsBound: false, TriggerActive: false, TriggerPressed: false));
        Assert.False(after.IsBound);
        Assert.False(after.ShouldCancel);
    }

    [Fact]
    public void PoseTrackingLoss_CancelsImmediatelyWithoutGraceDelay()
    {
        var tracker = new VrHandActionTracker(deactivationGraceTicks: 3);
        var pose = Matrix4x4.CreateTranslation(0f, 1.2f, 0f);

        tracker.Update(0, new VrHandRawSample(true, pose, ActionsBound: true, TriggerActive: true, TriggerPressed: true));

        var lost = tracker.Update(0, new VrHandRawSample(IsPoseValid: false, Matrix4x4.Identity, ActionsBound: false, TriggerActive: false, TriggerPressed: false));

        Assert.False(lost.IsValid);
        Assert.False(lost.IsBound);
        Assert.False(lost.TriggerPressed);
        Assert.True(lost.ShouldCancel);
    }
}
