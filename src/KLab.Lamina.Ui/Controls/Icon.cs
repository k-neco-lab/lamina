// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

// Vector icon from IconCatalog, drawn on the 20x20 grid scaled to the control size.
public sealed class Icon : Control
{
    private string name;
    private Color? color;
    private float size;

    public Icon(string name, float size = 0)
    {
        this.name = name;
        this.size = size;
    }

    public string Name
    {
        get => name;
        set => SetRender(ref name, value);
    }

    public Color? Color
    {
        get => color;
        set => SetRender(ref color, value);
    }

    // 0 means the theme icon size.
    public float Size
    {
        get => size;
        set => SetLayout(ref size, value);
    }

    protected override Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer)
    {
        float side = Size > 0 ? Size : Theme.IconSize;
        return new Vector2(side, side);
    }

    protected override void ArrangeContent(Vector2 size)
    {
    }

    protected override void RenderContent(IDrawContext context, Rect area)
    {
        var fill = Color ?? (IsEnabled ? Theme.Text : Theme.Disabled);
        float scale = MathF.Min(area.Width, area.Height) / IconCatalog.GridSize;
        var transform = Matrix3x2.CreateScale(scale)
            * Matrix3x2.CreateTranslation((area.Width - IconCatalog.GridSize * scale) / 2,
                (area.Height - IconCatalog.GridSize * scale) / 2);
        if (IconCatalog.IsMirrored(Name))
        {
            transform = Matrix3x2.CreateScale(-1, 1, new Vector2(IconCatalog.GridSize / 2, 0)) * transform;
        }
        var (strokes, fills) = IconCatalog.Get(Name);
        context.PushTransform(transform);
        foreach (var path in strokes)
        {
            context.StrokePath(path, fill, IconCatalog.StrokeWidth);
        }
        foreach (var path in fills)
        {
            context.FillPath(path, fill);
        }
        context.PopTransform();
    }
}
