// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina;

/// <summary>
/// Raw single-frame controller sample before transient action-set dropout debouncing.
/// </summary>
public readonly record struct VrHandRawSample(
    bool IsPoseValid,
    Matrix4x4 AimPose,
    bool ActionsBound,
    bool TriggerActive,
    bool TriggerPressed,
    bool GripActive = false,
    bool GripPressed = false,
    bool SecondaryActive = false,
    bool SecondaryPressed = false,
    bool RightClickActive = false,
    bool RightClickPressed = false,
    bool MiddleClickActive = false,
    bool MiddleClickPressed = false,
    bool ScrollActive = false,
    Vector2 ScrollDelta = default,
    double TimestampSeconds = 0);

/// <summary>
/// Debounced controller state produced by <see cref="VrHandActionTracker"/>.
/// </summary>
public readonly record struct VrHandTrackedState(
    int PointerId,
    bool IsValid,
    bool IsBound,
    bool IsInGracePeriod,
    int RemainingGraceTicks,
    bool ShouldCancel,
    Matrix4x4 AimPose,
    bool TriggerPressed,
    bool GripPressed,
    bool SecondaryPressed,
    bool RightClickPressed,
    bool MiddleClickPressed,
    Vector2 ScrollDelta,
    double TimestampSeconds);

/// <summary>
/// Action input debounce state machine with <see cref="DefaultDeactivationGraceTicks"/> (3-tick) transient
/// <c>bActive/bound</c> dropout tolerance.
/// Prevents 11ms wireless streaming hiccups or OpenVR ActionSet transitions (<c>hub</c> &lt;-&gt; <c>capture</c>)
/// from spuriously cancelling an in-flight trigger press, while cleanly emitting <see cref="VrHandTrackedState.ShouldCancel"/>
/// once genuine tracking loss or a sustained (&gt; 3 tick) unbinding occurs.
/// </summary>
public sealed class VrHandActionTracker
{
    public const int DefaultDeactivationGraceTicks = 3;

    private sealed class HandSlot
    {
        public bool WasActionsBound;
        public bool WasBound;
        public int GraceTicks;
        public VrHandTrackedState LastState;
    }

    private readonly Dictionary<int, HandSlot> slots = new();

    public VrHandActionTracker(int deactivationGraceTicks = DefaultDeactivationGraceTicks)
    {
        if (deactivationGraceTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deactivationGraceTicks), "Grace ticks must be non-negative.");
        }

        DeactivationGraceTicks = deactivationGraceTicks;
    }

    public int DeactivationGraceTicks { get; }

    /// <summary>
    /// Pure helper implementing the grace-window bound resolution rule.
    /// </summary>
    public static bool ResolveBound(
        bool actionsBound,
        bool wasActionsBound,
        ref int graceTicks,
        int maxGraceTicks = DefaultDeactivationGraceTicks)
    {
        if (actionsBound)
        {
            graceTicks = 0;
            return true;
        }

        if (wasActionsBound)
        {
            graceTicks = maxGraceTicks;
        }

        if (graceTicks > 0)
        {
            graceTicks--;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Advances the debounce state machine for <paramref name="pointerId"/> by one frame.
    /// </summary>
    public VrHandTrackedState Update(int pointerId, in VrHandRawSample sample)
    {
        var slot = GetOrCreate(pointerId);

        if (!sample.IsPoseValid)
        {
            bool shouldCancel = slot.WasBound;
            slot.WasActionsBound = false;
            slot.WasBound = false;
            slot.GraceTicks = 0;

            var lost = new VrHandTrackedState(
                pointerId,
                IsValid: false,
                IsBound: false,
                IsInGracePeriod: false,
                RemainingGraceTicks: 0,
                ShouldCancel: shouldCancel,
                AimPose: Matrix4x4.Identity,
                TriggerPressed: false,
                GripPressed: false,
                SecondaryPressed: false,
                RightClickPressed: false,
                MiddleClickPressed: false,
                ScrollDelta: Vector2.Zero,
                TimestampSeconds: sample.TimestampSeconds);

            slot.LastState = lost;
            return lost;
        }

        bool bound = ResolveBound(sample.ActionsBound, slot.WasActionsBound, ref slot.GraceTicks, DeactivationGraceTicks);
        bool inGrace = !sample.ActionsBound && bound;
        bool cancelled = !bound && slot.WasBound;

        var previous = slot.LastState;
        bool trigger = sample.TriggerActive ? sample.TriggerPressed : (inGrace && previous.TriggerPressed);
        bool grip = sample.GripActive ? sample.GripPressed : (inGrace && previous.GripPressed);
        bool secondary = sample.SecondaryActive ? sample.SecondaryPressed : (inGrace && previous.SecondaryPressed);
        bool rightClick = sample.RightClickActive ? sample.RightClickPressed : (inGrace && previous.RightClickPressed);
        bool middleClick = sample.MiddleClickActive ? sample.MiddleClickPressed : (inGrace && previous.MiddleClickPressed);
        Vector2 scroll = sample.ScrollActive ? sample.ScrollDelta : (inGrace ? previous.ScrollDelta : Vector2.Zero);

        if (!bound)
        {
            trigger = false;
            grip = false;
            secondary = false;
            rightClick = false;
            middleClick = false;
            scroll = Vector2.Zero;
        }

        slot.WasActionsBound = sample.ActionsBound;
        slot.WasBound = bound;

        var tracked = new VrHandTrackedState(
            pointerId,
            IsValid: true,
            IsBound: bound,
            IsInGracePeriod: inGrace,
            RemainingGraceTicks: slot.GraceTicks,
            ShouldCancel: cancelled,
            AimPose: sample.AimPose,
            TriggerPressed: trigger,
            GripPressed: grip,
            SecondaryPressed: secondary,
            RightClickPressed: rightClick,
            MiddleClickPressed: middleClick,
            ScrollDelta: scroll,
            TimestampSeconds: sample.TimestampSeconds);

        slot.LastState = tracked;
        return tracked;
    }

    public void Reset(int pointerId) => slots.Remove(pointerId);

    public void ResetAll() => slots.Clear();

    private HandSlot GetOrCreate(int pointerId)
    {
        if (!slots.TryGetValue(pointerId, out var slot))
        {
            slot = new HandSlot();
            slots[pointerId] = slot;
        }

        return slot;
    }
}
