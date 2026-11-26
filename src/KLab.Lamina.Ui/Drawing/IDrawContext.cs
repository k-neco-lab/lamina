// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

// Opaque GPU texture owned by the backend; KLab.Lamina.Ui never looks inside.
public interface ITextureHandle
{
    int Width { get; }
    int Height { get; }
}

public interface IDrawContext : ITextMeasurer
{
    void Clear(Color color);
    void FillRoundedRect(Rect rect, float radius, Color color);
    void StrokeRoundedRect(Rect rect, float radius, Color color, float width);
    // Extra beyond the minimum list: icons are vector paths, so the backend must be able to draw them.
    void FillPath(VectorPath path, Color color);
    void StrokePath(VectorPath path, Color color, float width);
    void DrawText(string text, Rect box, TextStyle style);
    void DrawImage(ITextureHandle texture, Rect destination, float opacity = 1);
    void PushClip(Rect rect, float radius = 0);
    void PopClip();
    void PushTransform(Matrix3x2 transform);
    void PopTransform();
    void PushOpacity(float opacity);
    void PopOpacity();
}
