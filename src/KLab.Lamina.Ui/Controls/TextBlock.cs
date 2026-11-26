// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

public sealed class TextBlock : Control
{
    private string text;
    private float fontSize;
    private FontWeight weight = FontWeight.Regular;
    private Color? color;
    private TextAlign align = TextAlign.Left;

    public TextBlock(string text = "", float fontSize = 0)
    {
        this.text = text;
        this.fontSize = fontSize;
    }

    // Changing the content re-measures and redraws; assigning the same string does nothing.
    public string Text
    {
        get => text;
        set => SetLayout(ref text, value ?? string.Empty);
    }

    // 0 means the theme default control font size.
    public float FontSize
    {
        get => fontSize;
        set => SetLayout(ref fontSize, value);
    }

    public FontWeight Weight
    {
        get => weight;
        set => SetLayout(ref weight, value);
    }

    // Null follows the theme: Text color, or Disabled while the control is disabled.
    public Color? Color
    {
        get => color;
        set => SetRender(ref color, value);
    }

    public TextAlign Align
    {
        get => align;
        set => SetRender(ref align, value);
    }

    internal TextStyle Style()
    {
        var theme = Theme;
        var fill = Color ?? (IsEnabled ? theme.Text : theme.Disabled);
        float baseSize = FontSize > 0 ? FontSize : theme.DefaultControlFontSize;
        float scaledSize = baseSize * Math.Clamp(theme.FontScale, 0.8f, 1.25f);
        return new TextStyle(theme.FontFamily, scaledSize, Weight, fill, Align);
    }

    protected override Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer)
    {
        if (Text.Length == 0)
        {
            return Vector2.Zero;
        }
        var size = measurer.MeasureText(Text, Style(), available.X);
        return new Vector2(MathF.Min(size.X, available.X), size.Y);
    }

    protected override void ArrangeContent(Vector2 size)
    {
    }

    protected override void RenderContent(IDrawContext context, Rect area)
    {
        if (Text.Length == 0)
        {
            return;
        }
        var style = Style();
        float lineHeight = context.MeasureText(Text, style, area.Width).Y;
        context.DrawText(Text, new Rect(0, (area.Height - lineHeight) / 2, area.Width, lineHeight), style);
    }
}
