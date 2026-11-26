// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

// Vertical scrolling viewport with wheel, drag and inertia.
public sealed class ScrollView : ContainerControl
{
    private const float IndicatorWidth = 4f;
    private const float IndicatorMargin = 2f;
    private const float MinIndicatorLength = 16f;
    private readonly Dictionary<int, double> lastDragTimes = new();

    public ScrollState Scroll { get; } = new();

    public override bool IsInteractive => true;

    public override bool CancelsPressWhenPointerLeaves => false;

    public override Vector2 ChildOrigin => new(0, -Scroll.Offset);

    public Control? Content
    {
        get => Items.Count > 0 ? Items[0] : null;
        set
        {
            Clear();
            if (value is not null)
            {
                Add(value);
            }
        }
    }

    public override void OnPointerDown(PointerEventArgs e)
    {
        Scroll.Stop();
        lastDragTimes[e.PointerId] = 0;
    }

    public override void OnDrag(PointerEventArgs e)
    {
        float dt = lastDragTimes.TryGetValue(e.PointerId, out double prev) && prev > 0
            ? (float)(e.Time - prev)
            : 0;
        lastDragTimes[e.PointerId] = e.Time;
        if (Scroll.Drag(-e.Delta.Y, dt))
        {
            InvalidateRender();
        }
    }

    public override void OnPointerUp(PointerEventArgs e)
    {
        lastDragTimes.Remove(e.PointerId);
        if (Scroll.IsCoasting)
        {
            RequestTick();
        }
    }

    public override void OnPointerCancel(PointerEventArgs e)
    {
        lastDragTimes.Remove(e.PointerId);
    }

    public override void OnScroll(PointerEventArgs e)
    {
        if (Scroll.ScrollBy(-e.Delta.Y))
        {
            InvalidateRender();
        }
        e.Handled = true;
    }

    public override bool Tick(float deltaSeconds)
    {
        if (Scroll.Step(deltaSeconds))
        {
            InvalidateRender();
        }
        return Scroll.IsCoasting;
    }

    protected override Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer)
    {
        var content = Content?.Measure(new Vector2(available.X, float.PositiveInfinity), measurer) ?? Vector2.Zero;
        return new Vector2(content.X, MathF.Min(content.Y, available.Y));
    }

    protected override void ArrangeContent(Vector2 size)
    {
        float height = Content?.DesiredSize.Y ?? 0;
        Content?.Arrange(new Rect(0, 0, size.X, height));
        Scroll.SetExtent(size.Y, height);
    }

    protected override void RenderContent(IDrawContext context, Rect area)
    {
        context.PushClip(area);
        context.PushTransform(Matrix3x2.CreateTranslation(0, -Scroll.Offset));
        RenderChildren(context);
        context.PopTransform();
        DrawIndicator(context, area);
        context.PopClip();
    }

    private void DrawIndicator(IDrawContext context, Rect area)
    {
        if (Scroll.MaxOffset <= 0)
        {
            return;
        }
        float length = MathF.Max(MinIndicatorLength, area.Height * Scroll.ViewportLength / Scroll.ContentLength);
        float top = (area.Height - length) * Scroll.Offset / Scroll.MaxOffset;
        var thumb = new Rect(area.Width - IndicatorWidth - IndicatorMargin, top, IndicatorWidth, length);
        context.FillRoundedRect(thumb, IndicatorWidth / 2, Theme.Muted);
    }
}
