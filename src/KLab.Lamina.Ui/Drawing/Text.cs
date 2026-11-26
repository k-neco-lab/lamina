// Copyright (c) K Lab. Licensed under the MIT License.

namespace KLab.Lamina.Ui;

public enum FontWeight
{
    Regular = 400,
    Medium = 500,
    SemiBold = 600,
    Bold = 700,
}

public enum TextAlign
{
    Left,
    Center,
    Right,
}

public enum TextTrimming
{
    None,
    Ellipsis,
}

public sealed record TextStyle(
    string FontFamily,
    float Size,
    FontWeight Weight,
    Color Color,
    TextAlign Align = TextAlign.Left,
    TextTrimming Trimming = TextTrimming.Ellipsis);

public interface ITextMeasurer
{
    // Single-line extent of the text; maxWidth limits the layout box (use +infinity for none).
    System.Numerics.Vector2 MeasureText(string text, TextStyle style, float maxWidth = float.PositiveInfinity);
}
