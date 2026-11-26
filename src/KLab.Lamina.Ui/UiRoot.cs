// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using KLab.Lamina.Abstractions;

namespace KLab.Lamina.Ui;

/// <summary>
/// Owns one retained-mode control tree: layout, dirty flags, per-frame ticking,
/// and native multi-pointer routing isolated by pointerId (e.g. Left = 0, Right = 1).
/// </summary>
public sealed class UiRoot : IVrPanelInputSink
{
    // Press-and-release counts as a click while the pointer stays within this distance of the control
    // (18px accommodates natural VR trigger-pull pitch jitter while still allowing deliberate drag-off cancel).
    public const float ClickSlop = 18f;

    private sealed class PointerCaptureState
    {
        public PointerCaptureState(Control target, PointerButton button, Vector2 pressPosition)
        {
            Target = target;
            Button = button;
            PressPosition = pressPosition;
        }

        public Control Target { get; }
        public PointerButton Button { get; }
        public Vector2 PressPosition { get; }
        public bool Cancelled { get; set; }
    }

    private readonly ITextMeasurer measurer;
    private readonly List<Control> tickers = new();
    private readonly Dictionary<int, Control> _hoveredByPointer = new();
    private readonly Dictionary<int, PointerCaptureState> _capturedByPointer = new();
    private readonly Dictionary<int, Vector2> _lastPointerPosition = new();
    private Theme theme;
    private Vector2 size;
    private double time;

    public UiRoot(Control content, ITextMeasurer measurer, Vector2 size, Theme? theme = null, string panelId = "")
    {
        Content = content;
        this.measurer = measurer;
        this.size = size;
        this.theme = theme ?? Theme.Default;
        PanelId = panelId;
        content.Attach(null, this);
        MarkLayout();
    }

    public string PanelId { get; }
    public Control Content { get; }
    public bool NeedsLayout { get; private set; }
    public bool NeedsRender { get; private set; }
    public bool IsDirty => NeedsLayout || NeedsRender;
    public int LayoutCount { get; private set; }
    public int RenderCount { get; private set; }

    public Control? Hovered => GetHovered(0);
    public Control? Captured => GetCaptured(0);
    public bool HasActiveCapture => _capturedByPointer.Count > 0;
    public int ActiveCaptureCount => _capturedByPointer.Count;
    public bool CapturedUsesContinuousDrag => IsPointerContinuousDrag(0);

    public Vector2 PixelSize => size;

    public Control? GetHovered(int pointerId) =>
        _hoveredByPointer.TryGetValue(pointerId, out var control) ? control : null;

    public Control? GetCaptured(int pointerId) =>
        _capturedByPointer.TryGetValue(pointerId, out var state) ? state.Target : null;

    public bool IsPointerCaptured(int pointerId) => _capturedByPointer.ContainsKey(pointerId);

    public bool IsPointerContinuousDrag(int pointerId) =>
        _capturedByPointer.TryGetValue(pointerId, out var state) && !state.Target.CancelsPressWhenPointerLeaves;

    public Theme Theme
    {
        get => theme;
        set
        {
            if (!ReferenceEquals(theme, value))
            {
                theme = value;
                MarkLayout();
            }
        }
    }

    public Vector2 Size
    {
        get => size;
        set
        {
            if (size != value)
            {
                size = value;
                MarkLayout();
            }
        }
    }

    public void MarkLayout()
    {
        NeedsLayout = true;
        NeedsRender = true;
    }

    public void MarkRender() => NeedsRender = true;

    public void RequestTick(Control control)
    {
        if (!tickers.Contains(control))
        {
            tickers.Add(control);
        }
    }

    public void Layout()
    {
        if (!NeedsLayout)
        {
            return;
        }
        NeedsLayout = false;
        Content.Measure(size, measurer);
        Content.Arrange(new Rect(0, 0, size.X, size.Y));
        NeedsRender = true;
        LayoutCount++;
    }

    // Draws the whole tree when anything changed since the last draw. Returns false when nothing was redrawn,
    // so the caller can skip the texture submit as well.
    public bool RenderIfDirty(IDrawContext context)
    {
        Layout();
        if (!NeedsRender)
        {
            return false;
        }
        NeedsRender = false;
        context.Clear(Color.Transparent);
        Content.Render(context);
        RenderCount++;
        return true;
    }

    // Frame step: advances inertia and other per-frame animations.
    public void Update(float deltaSeconds)
    {
        time += deltaSeconds;
        for (int i = tickers.Count - 1; i >= 0; i--)
        {
            if (!tickers[i].Tick(deltaSeconds))
            {
                tickers.RemoveAt(i);
            }
        }
    }

    public void OnPointerEvent(in VrPointerEvent pointerEvent)
    {
        var button = MapButton(pointerEvent.Button);
        switch (pointerEvent.Kind)
        {
            case VrPointerEventKind.Move:
                PointerMove(pointerEvent.PointerId, pointerEvent.PixelPosition);
                break;
            case VrPointerEventKind.Down:
                PointerDown(pointerEvent.PointerId, pointerEvent.PixelPosition, button);
                break;
            case VrPointerEventKind.Up:
                PointerUp(pointerEvent.PointerId, pointerEvent.PixelPosition, button);
                break;
            case VrPointerEventKind.Leave:
                PointerLeave(pointerEvent.PointerId);
                break;
            case VrPointerEventKind.Cancel:
                PointerCancel(pointerEvent.PointerId);
                break;
            case VrPointerEventKind.Scroll:
                Scroll(pointerEvent.PointerId, pointerEvent.PixelPosition, pointerEvent.ScrollDelta);
                break;
        }
    }

    // Single-pointer convenience overloads (pointerId = 0).
    public void PointerMove(Vector2 position) => PointerMove(0, position);
    public void PointerLeave() => PointerLeave(0);
    public void PointerDown(Vector2 position, PointerButton button = PointerButton.Left) => PointerDown(0, position, button);
    public void PointerUp(Vector2 position, PointerButton button = PointerButton.Left) => PointerUp(0, position, button);
    public void PointerCancel() => PointerCancel(0);
    public void Scroll(Vector2 position, Vector2 delta) => Scroll(0, position, delta);

    public void PointerMove(int pointerId, Vector2 position)
    {
        var previousPos = _lastPointerPosition.TryGetValue(pointerId, out var last) ? last : position;
        var delta = position - previousPos;
        _lastPointerPosition[pointerId] = position;

        if (_capturedByPointer.TryGetValue(pointerId, out var capture))
        {
            UpdateCapturedHover(pointerId, capture, position);
            if (!capture.Cancelled)
            {
                capture.Target.OnDrag(Args(capture.Target, position, capture.Button, delta, pointerId));
            }
            return;
        }

        SetHovered(pointerId, Content.HitTest(position), position);
    }

    public void PointerLeave(int pointerId)
    {
        if (!_capturedByPointer.ContainsKey(pointerId))
        {
            var last = _lastPointerPosition.TryGetValue(pointerId, out var pos) ? pos : Vector2.Zero;
            SetHovered(pointerId, null, last);
        }
    }

    public void PointerDown(int pointerId, Vector2 position, PointerButton button = PointerButton.Left)
    {
        _lastPointerPosition[pointerId] = position;
        if (_capturedByPointer.ContainsKey(pointerId))
        {
            return;
        }

        var hit = Content.HitTest(position);
        SetHovered(pointerId, hit, position);
        if (hit is null)
        {
            return;
        }

        _capturedByPointer[pointerId] = new PointerCaptureState(hit, button, position);
        if (button == PointerButton.Left)
        {
            hit.AddPressPointer(pointerId);
        }
        hit.OnPointerDown(Args(hit, position, button, Vector2.Zero, pointerId));
    }

    public void PointerUp(int pointerId, Vector2 position, PointerButton button = PointerButton.Left)
    {
        _lastPointerPosition[pointerId] = position;
        if (!_capturedByPointer.Remove(pointerId, out var capture))
        {
            return;
        }

        var target = capture.Target;
        target.RemovePressPointer(pointerId);

        var args = Args(target, position, button, Vector2.Zero, pointerId);
        target.OnPointerUp(args);

        if (!capture.Cancelled && IsWithinSlop(target, position))
        {
            target.OnClick(args);
        }

        SetHovered(pointerId, Content.HitTest(position), position);
    }

    // Cancels ONLY this pointerId's in-flight press and hover without affecting any other pointer.
    public void PointerCancel(int pointerId)
    {
        var last = _lastPointerPosition.TryGetValue(pointerId, out var pos) ? pos : Vector2.Zero;
        if (_capturedByPointer.Remove(pointerId, out var capture))
        {
            capture.Cancelled = true;
            var target = capture.Target;
            target.RemovePressPointer(pointerId);
            if (_hoveredByPointer.TryGetValue(pointerId, out var hovered) && ReferenceEquals(hovered, target))
            {
                _hoveredByPointer.Remove(pointerId);
                target.RemoveHoverPointer(pointerId);
            }
            target.OnPointerCancel(Args(target, last, capture.Button, Vector2.Zero, pointerId));
        }

        SetHovered(pointerId, null, last);
    }

    public void CancelAllPointers()
    {
        var activeIds = new HashSet<int>(_capturedByPointer.Keys);
        foreach (int id in _hoveredByPointer.Keys)
        {
            activeIds.Add(id);
        }
        foreach (int id in activeIds)
        {
            PointerCancel(id);
        }
    }

    public void Scroll(int pointerId, Vector2 position, Vector2 delta)
    {
        _lastPointerPosition[pointerId] = position;
        for (var c = Content.HitTest(position); c is not null; c = c.Parent)
        {
            var args = Args(c, position, PointerButton.Left, delta, pointerId);
            c.OnScroll(args);
            if (args.Handled)
            {
                return;
            }
        }
    }

    internal void DetachSubtree(Control control)
    {
        foreach (var child in control.Children)
        {
            DetachSubtree(child);
        }

        tickers.Remove(control);

        List<int>? staleCaptures = null;
        foreach (var (pid, cap) in _capturedByPointer)
        {
            if (ReferenceEquals(cap.Target, control))
            {
                staleCaptures ??= new List<int>();
                staleCaptures.Add(pid);
            }
        }
        if (staleCaptures is not null)
        {
            foreach (int pid in staleCaptures)
            {
                _capturedByPointer.Remove(pid);
            }
        }

        List<int>? staleHovers = null;
        foreach (var (pid, hov) in _hoveredByPointer)
        {
            if (ReferenceEquals(hov, control))
            {
                staleHovers ??= new List<int>();
                staleHovers.Add(pid);
            }
        }
        if (staleHovers is not null)
        {
            foreach (int pid in staleHovers)
            {
                _hoveredByPointer.Remove(pid);
            }
        }

        control.ClearPointerStates();
    }

    private void UpdateCapturedHover(int pointerId, PointerCaptureState capture, Vector2 position)
    {
        var target = capture.Target;
        if (target.CancelsPressWhenPointerLeaves && !capture.Cancelled && !IsWithinSlop(target, position))
        {
            capture.Cancelled = true;
            target.RemovePressPointer(pointerId);
            if (_hoveredByPointer.TryGetValue(pointerId, out var hov) && ReferenceEquals(hov, target))
            {
                _hoveredByPointer.Remove(pointerId);
                target.RemoveHoverPointer(pointerId);
            }
            target.OnPointerLeave(Args(target, position, capture.Button, Vector2.Zero, pointerId));
        }
    }

    private static bool IsWithinSlop(Control target, Vector2 position)
    {
        var local = target.ToLocal(position);
        var area = new Rect(0, 0, target.Bounds.Width, target.Bounds.Height).Inflate(ClickSlop);
        return area.Contains(local);
    }

    private void SetHovered(int pointerId, Control? next, Vector2 position)
    {
        _hoveredByPointer.TryGetValue(pointerId, out var previous);
        if (ReferenceEquals(next, previous))
        {
            return;
        }

        if (previous is not null)
        {
            _hoveredByPointer.Remove(pointerId);
            previous.RemoveHoverPointer(pointerId);
            previous.OnPointerLeave(Args(previous, position, PointerButton.Left, Vector2.Zero, pointerId));
        }

        if (next is not null)
        {
            _hoveredByPointer[pointerId] = next;
            next.AddHoverPointer(pointerId);
            next.OnPointerEnter(Args(next, position, PointerButton.Left, Vector2.Zero, pointerId));
        }
    }

    private PointerEventArgs Args(Control target, Vector2 position, PointerButton button, Vector2 delta, int pointerId) =>
        new(target.ToLocal(position), button, delta, time, pointerId);

    private static PointerButton MapButton(VrPointerButton button) => button switch
    {
        VrPointerButton.Right => PointerButton.Right,
        VrPointerButton.Middle => PointerButton.Middle,
        _ => PointerButton.Left,
    };
}
