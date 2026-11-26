// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

// Single-content box: background, rounded corners, border and padding.
public class Panel : ContainerControl
{
    private Color background = Color.Transparent;
    private Color borderColor = Color.Transparent;
    private float borderWidth;
    private float cornerRadius;
    private Thickness padding;

    public Color Background
    {
        get => background;
        set => SetRender(ref background, value);
    }

    public Color BorderColor
    {
        get => borderColor;
        set => SetRender(ref borderColor, value);
    }

    public float BorderWidth
    {
        get => borderWidth;
        set => SetRender(ref borderWidth, value);
    }

    public float CornerRadius
    {
        get => cornerRadius;
        set => SetRender(ref cornerRadius, value);
    }

    public Thickness Padding
    {
        get => padding;
        set => SetLayout(ref padding, value);
    }

    protected override Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer)
    {
        var inner = new Vector2(MathF.Max(0, available.X - Padding.Horizontal),
            MathF.Max(0, available.Y - Padding.Vertical));
        var size = Vector2.Zero;
        foreach (var child in Items)
        {
            size = Vector2.Max(size, child.Measure(inner, measurer));
        }
        return size + new Vector2(Padding.Horizontal, Padding.Vertical);
    }

    protected override void ArrangeContent(Vector2 size)
    {
        var slot = new Rect(0, 0, size.X, size.Y).Deflate(Padding);
        foreach (var child in Items)
        {
            child.Arrange(slot);
        }
    }

    protected override void RenderContent(IDrawContext context, Rect area)
    {
        DrawBox(context, area, Background, BorderColor, CornerRadius, BorderWidth);
        RenderChildren(context);
    }

    protected static void DrawBox(
        IDrawContext context, Rect area, Color fill, Color stroke, float radius, float borderWidth)
    {
        if (fill.A > 0)
        {
            context.FillRoundedRect(area, radius, fill);
        }
        if (borderWidth > 0 && stroke.A > 0)
        {
            // Stroke is centered on the edge, so inset by half the width to stay inside the bounds.
            context.StrokeRoundedRect(area.Inflate(-borderWidth / 2), MathF.Max(0, radius - borderWidth / 2), stroke,
                borderWidth);
        }
    }
}
