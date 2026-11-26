// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

public sealed class Stack : ContainerControl
{
    private Orientation orientation = Orientation.Vertical;
    private float spacing;

    public Orientation Orientation
    {
        get => orientation;
        set => SetLayout(ref orientation, value);
    }

    public float Spacing
    {
        get => spacing;
        set => SetLayout(ref spacing, value);
    }

    private bool Horizontal => Orientation == Orientation.Horizontal;

    protected override Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer)
    {
        float main = 0, cross = 0;
        int count = 0;
        foreach (var child in Items.Where(c => c.Visible))
        {
            var probe = Horizontal
                ? new Vector2(float.PositiveInfinity, available.Y)
                : new Vector2(available.X, float.PositiveInfinity);
            var size = child.Measure(probe, measurer);
            main += Horizontal ? size.X : size.Y;
            cross = MathF.Max(cross, Horizontal ? size.Y : size.X);
            count++;
        }
        main += Spacing * Math.Max(0, count - 1);
        return Horizontal ? new Vector2(main, cross) : new Vector2(cross, main);
    }

    protected override void ArrangeContent(Vector2 size)
    {
        float cursor = 0;
        foreach (var child in Items.Where(c => c.Visible))
        {
            float length = Horizontal ? child.DesiredSize.X : child.DesiredSize.Y;
            child.Arrange(Horizontal
                ? new Rect(cursor, 0, length, size.Y)
                : new Rect(0, cursor, size.X, length));
            cursor += length + Spacing;
        }
    }
}
