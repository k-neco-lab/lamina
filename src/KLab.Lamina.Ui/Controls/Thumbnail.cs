// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

// A picture from a texture handle. Cover crops the picture to fill the box (thumbnails); otherwise the whole picture
// is fitted inside it (previews).
public sealed class Thumbnail : Control
{
    private readonly ITextureHandle texture;
    private readonly bool cover;

    public Thumbnail(ITextureHandle texture, bool cover)
    {
        this.texture = texture;
        this.cover = cover;
    }

    protected override Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer)
        => new(float.IsInfinity(available.X) ? texture.Width : available.X,
            float.IsInfinity(available.Y) ? texture.Height : available.Y);

    protected override void ArrangeContent(Vector2 size)
    {
    }

    protected override void RenderContent(IDrawContext context, Rect area)
    {
        if (texture.Width <= 0 || texture.Height <= 0)
        {
            return;
        }
        float sx = area.Width / texture.Width, sy = area.Height / texture.Height;
        float scale = cover ? MathF.Max(sx, sy) : MathF.Min(sx, sy);
        float w = texture.Width * scale, h = texture.Height * scale;
        context.PushClip(area, cover ? 8 : 0);
        context.DrawImage(texture, new Rect(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
        context.PopClip();
    }
}
