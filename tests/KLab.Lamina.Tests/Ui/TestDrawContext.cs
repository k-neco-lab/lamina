// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using KLab.Lamina.Ui;

namespace KLab.Lamina.Tests;

internal sealed class FakeTextMeasurer : ITextMeasurer
{
    public Vector2 MeasureText(string text, TextStyle style, float maxWidth = float.PositiveInfinity)
    {
        float width = MathF.Min(maxWidth, text.Length * style.Size * 0.6f);
        float height = style.Size * 1.3f;
        return new Vector2(width, height);
    }
}

internal sealed class RecordingDrawContext : IDrawContext
{
    private readonly FakeTextMeasurer measurer = new();

    public int ClearCount { get; private set; }
    public int FillRectCount { get; private set; }

    public Vector2 MeasureText(string text, TextStyle style, float maxWidth = float.PositiveInfinity) =>
        measurer.MeasureText(text, style, maxWidth);

    public void Clear(Color color) => ClearCount++;
    public void FillRoundedRect(Rect rect, float radius, Color color) => FillRectCount++;
    public void StrokeRoundedRect(Rect rect, float radius, Color color, float width) { }
    public void FillPath(VectorPath path, Color color) { }
    public void StrokePath(VectorPath path, Color color, float width) { }
    public void DrawText(string text, Rect box, TextStyle style) { }
    public void DrawImage(ITextureHandle texture, Rect destination, float opacity = 1) { }
    public void PushClip(Rect rect, float radius = 0) { }
    public void PopClip() { }
    public void PushTransform(Matrix3x2 transform) { }
    public void PopTransform() { }
    public void PushOpacity(float opacity) { }
    public void PopOpacity() { }
}
