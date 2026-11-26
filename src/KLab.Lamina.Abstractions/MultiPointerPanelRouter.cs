// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Abstractions;

/// <summary>
/// Layer 1 multi-pointer cross-panel router with per-pointer capture isolation and VR press-anchor deadzone protection.
/// Concurrent pointers (e.g. Left = 0, Right = 1) can hover, press, and drag controls on different panels
/// or on the same panel simultaneously without cancelling or starving each other.
/// </summary>
public sealed class MultiPointerPanelRouter
{
    public const float DefaultPressAnchorDeadzonePixels = 28f;

    private sealed class PointerState
    {
        public IVrPanelInputSink? HoveredPanel;
        public IVrPanelInputSink? CapturedPanel;
        public VrPointerButton CapturedButton;
        public bool WasPressed;
        public Vector2 LastUv;
        public Vector2 PressAnchorUv;
        public Vector2 PressAnchorPx;
        public bool HasPressAnchor;
        public bool PressAnchorBroken;
    }

    private readonly Dictionary<int, PointerState> pointers = new();

    public float PressAnchorDeadzonePixels { get; set; } = DefaultPressAnchorDeadzonePixels;

    public IVrPanelInputSink? GetHoveredPanel(int pointerId) =>
        pointers.TryGetValue(pointerId, out var state) ? state.HoveredPanel : null;

    public IVrPanelInputSink? GetCapturedPanel(int pointerId) =>
        pointers.TryGetValue(pointerId, out var state) ? state.CapturedPanel : null;

    public bool HasActiveCapture(int pointerId) => GetCapturedPanel(pointerId) is not null;

    /// <summary>
    /// Routes one frame of spatial raycast state for <paramref name="pointerId"/>.
    /// </summary>
    public void UpdatePointer(
        int pointerId,
        IVrPanelInputSink? hitPanel,
        Vector2 normalizedUv,
        bool isPressed,
        VrPointerButton button = VrPointerButton.Left,
        Vector2 scrollDelta = default,
        double timestampSeconds = 0)
    {
        var state = GetOrCreate(pointerId);
        var target = state.CapturedPanel ?? hitPanel;

        if (state.HoveredPanel is { } previous && !ReferenceEquals(previous, target))
        {
            var leavePx = state.LastUv * previous.PixelSize;
            previous.OnPointerEvent(new VrPointerEvent(
                pointerId,
                VrPointerEventKind.Leave,
                state.LastUv,
                leavePx,
                button,
                default,
                timestampSeconds));
        }

        state.HoveredPanel = target;

        if (target is null)
        {
            state.WasPressed = isPressed;
            return;
        }

        if (hitPanel is not null || state.CapturedPanel is not null)
        {
            state.LastUv = normalizedUv;
        }

        bool pressedNow = isPressed && !state.WasPressed && hitPanel is not null && state.CapturedPanel is null;
        bool releasedNow = !isPressed && state.WasPressed && ReferenceEquals(state.CapturedPanel, target);

        Vector2 effectiveUv = ReferenceEquals(state.CapturedPanel, target)
            ? ResolvePressedUv(pointerId, state, target, state.LastUv)
            : state.LastUv;

        target.OnPointerEvent(new VrPointerEvent(
            pointerId,
            VrPointerEventKind.Move,
            effectiveUv,
            effectiveUv * target.PixelSize,
            button,
            default,
            timestampSeconds));

        if (pressedNow)
        {
            state.CapturedPanel = target;
            state.CapturedButton = button;
            state.PressAnchorUv = state.LastUv;
            state.PressAnchorPx = state.LastUv * target.PixelSize;
            state.HasPressAnchor = true;
            state.PressAnchorBroken = false;

            target.OnPointerEvent(new VrPointerEvent(
                pointerId,
                VrPointerEventKind.Down,
                state.LastUv,
                state.PressAnchorPx,
                button,
                default,
                timestampSeconds));
        }
        else if (releasedNow)
        {
            var releaseUv = ResolvePressedUv(pointerId, state, target, state.LastUv);
            var releaseButton = state.CapturedButton;
            state.CapturedPanel = null;
            state.HasPressAnchor = false;
            state.PressAnchorBroken = false;

            target.OnPointerEvent(new VrPointerEvent(
                pointerId,
                VrPointerEventKind.Up,
                releaseUv,
                releaseUv * target.PixelSize,
                releaseButton,
                default,
                timestampSeconds));

            // If the ray already left the captured panel before release, transition hover cleanly.
            if (!ReferenceEquals(hitPanel, target))
            {
                target.OnPointerEvent(new VrPointerEvent(
                    pointerId,
                    VrPointerEventKind.Leave,
                    releaseUv,
                    releaseUv * target.PixelSize,
                    releaseButton,
                    default,
                    timestampSeconds));
                state.HoveredPanel = hitPanel;
                if (hitPanel is not null)
                {
                    hitPanel.OnPointerEvent(new VrPointerEvent(
                        pointerId,
                        VrPointerEventKind.Move,
                        normalizedUv,
                        normalizedUv * hitPanel.PixelSize,
                        button,
                        default,
                        timestampSeconds));
                }
            }
        }

        if (scrollDelta != Vector2.Zero && hitPanel is not null)
        {
            target.OnPointerEvent(new VrPointerEvent(
                pointerId,
                VrPointerEventKind.Scroll,
                state.LastUv,
                state.LastUv * target.PixelSize,
                button,
                scrollDelta,
                timestampSeconds));
        }

        state.WasPressed = isPressed;
    }

    /// <summary>
    /// Cancels only <paramref name="pointerId"/> (e.g. tracking loss or hand blocked) without affecting any other pointer.
    /// </summary>
    public void CancelPointer(int pointerId, double timestampSeconds = 0)
    {
        if (!pointers.TryGetValue(pointerId, out var state))
        {
            return;
        }

        var captured = state.CapturedPanel;
        var hovered = state.HoveredPanel;
        var lastUv = state.LastUv;
        var button = state.CapturedButton;

        state.CapturedPanel = null;
        state.HoveredPanel = null;
        state.HasPressAnchor = false;
        state.PressAnchorBroken = false;
        state.WasPressed = false;

        if (captured is not null)
        {
            captured.OnPointerEvent(new VrPointerEvent(
                pointerId,
                VrPointerEventKind.Cancel,
                lastUv,
                lastUv * captured.PixelSize,
                button,
                default,
                timestampSeconds));
        }

        if (hovered is not null && !ReferenceEquals(hovered, captured))
        {
            hovered.OnPointerEvent(new VrPointerEvent(
                pointerId,
                VrPointerEventKind.Leave,
                lastUv,
                lastUv * hovered.PixelSize,
                button,
                default,
                timestampSeconds));
        }
    }

    /// <summary>
    /// Detaches a closed or hidden panel, cancelling only pointers currently targeting it.
    /// </summary>
    public void RemovePanel(IVrPanelInputSink panel, double timestampSeconds = 0)
    {
        foreach (var (pointerId, state) in pointers)
        {
            if (ReferenceEquals(state.CapturedPanel, panel) || ReferenceEquals(state.HoveredPanel, panel))
            {
                CancelPointer(pointerId, timestampSeconds);
            }
        }
    }

    private Vector2 ResolvePressedUv(int pointerId, PointerState state, IVrPanelInputSink target, Vector2 currentUv)
    {
        if (!state.HasPressAnchor || state.PressAnchorBroken || target.IsPointerContinuousDrag(pointerId))
        {
            return currentUv;
        }

        var currentPx = currentUv * target.PixelSize;
        if (Vector2.Distance(currentPx, state.PressAnchorPx) <= PressAnchorDeadzonePixels)
        {
            return state.PressAnchorUv;
        }

        state.PressAnchorBroken = true;
        return currentUv;
    }

    private PointerState GetOrCreate(int pointerId)
    {
        if (!pointers.TryGetValue(pointerId, out var state))
        {
            state = new PointerState();
            pointers[pointerId] = state;
        }
        return state;
    }
}
