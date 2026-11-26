// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

public abstract class Control
{
    private readonly HashSet<int> hoveredPointers = new();
    private readonly HashSet<int> pressedPointers = new();
    private bool visible = true;
    private bool enabled = true;
    private float width = float.NaN;
    private float height = float.NaN;
    private Thickness margin;
    private Alignment horizontal = Alignment.Stretch;
    private Alignment vertical = Alignment.Stretch;
    private float opacity = 1;

    public Control? Parent { get; internal set; }
    public UiRoot? Root { get; internal set; }

    // Slot after Arrange, relative to the parent's content origin.
    public Rect Bounds { get; private set; }
    public Vector2 DesiredSize { get; private set; }

    // A control that handles pointer input. HitTest returns the deepest one under the pointer.
    public virtual bool IsInteractive => false;

    // Multi-pointer reference-counted hover and press state: stays true while ANY pointer hovers/presses this control.
    public bool IsHovered => hoveredPointers.Count > 0;
    public bool IsPressed => pressedPointers.Count > 0;
    public int HoverPointerCount => hoveredPointers.Count;
    public int PressPointerCount => pressedPointers.Count;
    public bool IsHoveredBy(int pointerId) => hoveredPointers.Contains(pointerId);
    public bool IsPressedBy(int pointerId) => pressedPointers.Contains(pointerId);

    public Theme Theme => Root?.Theme ?? Theme.Default;

    public bool Visible
    {
        get => visible;
        set => SetLayout(ref visible, value);
    }

    public bool IsEnabled
    {
        get => enabled;
        set => SetRender(ref enabled, value);
    }

    // NaN means "size to content".
    public float Width
    {
        get => width;
        set => SetLayout(ref width, value);
    }

    public float Height
    {
        get => height;
        set => SetLayout(ref height, value);
    }

    public Thickness Margin
    {
        get => margin;
        set => SetLayout(ref margin, value);
    }

    public Alignment HorizontalAlignment
    {
        get => horizontal;
        set => SetLayout(ref horizontal, value);
    }

    public Alignment VerticalAlignment
    {
        get => vertical;
        set => SetLayout(ref vertical, value);
    }

    public float Opacity
    {
        get => opacity;
        set => SetRender(ref opacity, Math.Clamp(value, 0, 1));
    }

    // True for press-and-release controls (buttons): moving off them mid-press cancels the click.
    // Drag controls (slider, scroll views) keep tracking the pointer instead.
    public virtual bool CancelsPressWhenPointerLeaves => true;

    public virtual IEnumerable<Control> Children => Array.Empty<Control>();

    // Where child coordinates start inside this control (ScrollView shifts it by its offset).
    public virtual Vector2 ChildOrigin => Vector2.Zero;

    public Vector2 Measure(Vector2 available, ITextMeasurer measurer)
    {
        if (!Visible)
        {
            return DesiredSize = Vector2.Zero;
        }
        var inner = new Vector2(
            MathF.Max(0, available.X - Margin.Horizontal),
            MathF.Max(0, available.Y - Margin.Vertical));
        if (!float.IsNaN(Width))
        {
            inner.X = Width;
        }
        if (!float.IsNaN(Height))
        {
            inner.Y = Height;
        }
        var size = MeasureContent(inner, measurer);
        if (!float.IsNaN(Width))
        {
            size.X = Width;
        }
        if (!float.IsNaN(Height))
        {
            size.Y = Height;
        }
        return DesiredSize = new Vector2(size.X + Margin.Horizontal, size.Y + Margin.Vertical);
    }

    public void Arrange(Rect slot)
    {
        if (!Visible)
        {
            Bounds = Rect.Empty;
            return;
        }
        var area = slot.Deflate(Margin);
        // A fixed size never stretches.
        var ha = float.IsNaN(Width) || HorizontalAlignment != Alignment.Stretch ? HorizontalAlignment : Alignment.Start;
        var va = float.IsNaN(Height) || VerticalAlignment != Alignment.Stretch ? VerticalAlignment : Alignment.Start;
        float w = Fit(ha, area.Width, DesiredSize.X - Margin.Horizontal);
        float h = Fit(va, area.Height, DesiredSize.Y - Margin.Vertical);
        float x = area.X + Offset(ha, area.Width - w);
        float y = area.Y + Offset(va, area.Height - h);
        Bounds = new Rect(x, y, w, h);
        ArrangeContent(new Vector2(w, h));
    }

    public void Render(IDrawContext context)
    {
        if (!Visible || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }
        context.PushTransform(Matrix3x2.CreateTranslation(Bounds.X, Bounds.Y));
        bool faded = Opacity < 1;
        if (faded)
        {
            context.PushOpacity(Opacity);
        }
        RenderContent(context, new Rect(0, 0, Bounds.Width, Bounds.Height));
        if (faded)
        {
            context.PopOpacity();
        }
        context.PopTransform();
    }

    // Point is in the parent's content coordinates.
    public virtual Control? HitTest(Vector2 point)
    {
        if (!Visible || !Bounds.Contains(point))
        {
            return null;
        }
        var local = point - Bounds.Origin - ChildOrigin;
        foreach (var child in Children.Reverse())
        {
            if (child.HitTest(local) is { } hit)
            {
                return hit;
            }
        }
        return IsInteractive && IsEnabled ? this : null;
    }

    // Converts a point in root coordinates into this control's own coordinates.
    public Vector2 ToLocal(Vector2 rootPoint)
    {
        var origin = Vector2.Zero;
        for (var c = this; c is not null; c = c.Parent)
        {
            origin += c.Bounds.Origin;
            if (c.Parent is { } parent)
            {
                origin += parent.ChildOrigin;
            }
        }
        return rootPoint - origin;
    }

    public virtual void OnPointerEnter(PointerEventArgs e)
    {
    }

    public virtual void OnPointerLeave(PointerEventArgs e)
    {
    }

    public virtual void OnPointerDown(PointerEventArgs e)
    {
    }

    public virtual void OnPointerUp(PointerEventArgs e)
    {
    }

    public virtual void OnPointerCancel(PointerEventArgs e)
    {
    }

    public virtual void OnClick(PointerEventArgs e)
    {
    }

    public virtual void OnDrag(PointerEventArgs e)
    {
    }

    public virtual void OnScroll(PointerEventArgs e)
    {
    }

    // Per-frame work (inertia, tweens). Return true while the control still needs more frames.
    public virtual bool Tick(float deltaSeconds) => false;

    public void InvalidateLayout() => Root?.MarkLayout();

    public void InvalidateRender() => Root?.MarkRender();

    internal bool AddHoverPointer(int pointerId)
    {
        bool wasHovered = hoveredPointers.Count > 0;
        hoveredPointers.Add(pointerId);
        return !wasHovered && hoveredPointers.Count > 0;
    }

    internal bool RemoveHoverPointer(int pointerId)
    {
        bool wasHovered = hoveredPointers.Count > 0;
        hoveredPointers.Remove(pointerId);
        return wasHovered && hoveredPointers.Count == 0;
    }

    internal bool AddPressPointer(int pointerId)
    {
        bool wasPressed = pressedPointers.Count > 0;
        pressedPointers.Add(pointerId);
        return !wasPressed && pressedPointers.Count > 0;
    }

    internal bool RemovePressPointer(int pointerId)
    {
        bool wasPressed = pressedPointers.Count > 0;
        pressedPointers.Remove(pointerId);
        return wasPressed && pressedPointers.Count == 0;
    }

    internal void ClearPointerStates()
    {
        hoveredPointers.Clear();
        pressedPointers.Clear();
    }

    // Called by containers when adopting a child.
    internal void Attach(Control? parent, UiRoot? root)
    {
        if (Root is { } oldRoot && !ReferenceEquals(oldRoot, root))
        {
            oldRoot.DetachSubtree(this);
        }
        Parent = parent;
        SetRoot(root);
    }

    internal virtual void SetRoot(UiRoot? root)
    {
        Root = root;
        foreach (var child in Children)
        {
            child.SetRoot(root);
        }
        if (root is not null)
        {
            root.MarkLayout();
        }
    }

    protected abstract Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer);

    protected abstract void ArrangeContent(Vector2 size);

    protected abstract void RenderContent(IDrawContext context, Rect area);

    protected void SetLayout<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        InvalidateLayout();
    }

    protected void SetRender<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        InvalidateRender();
    }

    protected void RequestTick() => Root?.RequestTick(this);

    private static float Fit(Alignment alignment, float available, float desired) =>
        alignment == Alignment.Stretch || float.IsInfinity(desired) ? available : MathF.Min(available, desired);

    private static float Offset(Alignment alignment, float free) => alignment switch
    {
        Alignment.Center => free / 2,
        Alignment.End => free,
        _ => 0,
    };
}
