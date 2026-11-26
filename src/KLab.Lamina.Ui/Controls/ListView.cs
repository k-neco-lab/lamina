// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

// Fixed-row-height list that only creates controls for rows inside (or touching) the viewport.
public sealed class ListView : ContainerControl
{
    private readonly Dictionary<int, Control> realized = new();
    private readonly Dictionary<int, double> lastDragTimes = new();
    private int itemCount;
    private float itemHeight = 32;
    private (int First, int Last) range = (0, -1);
    private ITextMeasurer? measurer;

    public ScrollState Scroll { get; } = new();

    // Creates the row control for an index; called only for rows that become visible.
    public Func<int, Control>? ItemFactory { get; set; }

    public override bool IsInteractive => true;

    public override bool CancelsPressWhenPointerLeaves => false;

    public override Vector2 ChildOrigin => new(0, -Scroll.Offset);

    public int RealizedCount => realized.Count;

    public (int First, int Last) VisibleRange => range;

    public int ItemCount
    {
        get => itemCount;
        set
        {
            if (value != itemCount)
            {
                itemCount = Math.Max(0, value);
                Reset();
            }
        }
    }

    public float ItemHeight
    {
        get => itemHeight;
        set
        {
            if (value != itemHeight)
            {
                itemHeight = MathF.Max(1, value);
                Reset();
            }
        }
    }

    // Drops every realized row; they are rebuilt on the next layout (data changed).
    public void Reset()
    {
        foreach (int index in realized.Keys.ToList())
        {
            Drop(index);
        }
        InvalidateLayout();
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
        OffsetChanged(Scroll.Drag(-e.Delta.Y, dt));
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
        OffsetChanged(Scroll.ScrollBy(-e.Delta.Y));
        e.Handled = true;
    }

    public override bool Tick(float deltaSeconds)
    {
        OffsetChanged(Scroll.Step(deltaSeconds));
        return Scroll.IsCoasting;
    }

    protected override Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer)
    {
        this.measurer = measurer;
        return new Vector2(
            float.IsInfinity(available.X) ? 0 : available.X,
            float.IsInfinity(available.Y) ? ItemCount * ItemHeight : available.Y);
    }

    protected override void ArrangeContent(Vector2 size)
    {
        Scroll.SetExtent(size.Y, ItemCount * ItemHeight);
        range = ComputeRange(size.Y);
        foreach (int stale in realized.Keys.Where(i => i < range.First || i > range.Last).ToList())
        {
            Drop(stale);
        }
        for (int i = range.First; i <= range.Last; i++)
        {
            if (!realized.TryGetValue(i, out var row))
            {
                if (ItemFactory is null)
                {
                    continue;
                }
                row = ItemFactory(i);
                realized[i] = row;
                Adopt(row);
            }
            row.Measure(new Vector2(size.X, ItemHeight), measurer!);
            row.Arrange(new Rect(0, i * ItemHeight, size.X, ItemHeight));
        }
    }

    protected override void RenderContent(IDrawContext context, Rect area)
    {
        context.PushClip(area);
        context.PushTransform(Matrix3x2.CreateTranslation(0, -Scroll.Offset));
        RenderChildren(context);
        context.PopTransform();
        context.PopClip();
    }

    private (int First, int Last) ComputeRange(float viewportHeight)
    {
        if (ItemCount == 0 || viewportHeight <= 0)
        {
            return (0, -1);
        }
        int first = Math.Clamp((int)MathF.Floor(Scroll.Offset / ItemHeight), 0, ItemCount - 1);
        int last = Math.Clamp((int)MathF.Ceiling((Scroll.Offset + viewportHeight) / ItemHeight) - 1, first,
            ItemCount - 1);
        return (first, last);
    }

    private void Drop(int index)
    {
        if (realized.Remove(index, out var row))
        {
            Release(row);
        }
    }

    private void OffsetChanged(bool moved)
    {
        if (!moved)
        {
            return;
        }
        // New rows only need creating when the visible index range changes; otherwise a redraw is enough.
        if (ComputeRange(Scroll.ViewportLength) != range)
        {
            InvalidateLayout();
        }
        else
        {
            InvalidateRender();
        }
    }
}
