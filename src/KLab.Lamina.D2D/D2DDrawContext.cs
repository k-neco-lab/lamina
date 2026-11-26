// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.CompilerServices;
using KLab.Lamina.Ui;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using AlphaMode = Vortice.DCommon.AlphaMode;
using Color4 = Vortice.Mathematics.Color4;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace KLab.Lamina.D2D;

/// <summary>
/// Zero-copy Direct2D 1.1 + DirectWrite renderer implementing <see cref="IDrawContext"/> and <see cref="ITextMeasurer"/>.
/// Binds directly to an <see cref="ID3D11Texture2D"/> via <see cref="IDXGISurface"/> with premultiplied alpha and
/// grayscale text antialiasing, and maintains bounded LRU caches for text layouts, solid brushes, and vector geometries.
/// </summary>
public sealed class D2DDrawContext : IDrawContext, IDisposable
{
    public const int DefaultMaxBrushes = 64;
    public const int DefaultMaxGeometries = 256;
    private const float Dpi = 96f;

    private static readonly RawRectF InfiniteBounds =
        new(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);

    private readonly VrD3D11Device d3d;
    private readonly ID2D1DeviceContext context;
    private readonly ID2D1StrokeStyle1 roundStroke;
    private readonly TextLayoutCache textCache;
    private readonly int maxBrushes;
    private readonly int maxGeometries;
    private readonly Dictionary<uint, LinkedListNode<BrushEntry>> brushes = new();
    private readonly LinkedList<BrushEntry> brushLru = new();
    private readonly Dictionary<VectorPath, LinkedListNode<GeometryEntry>> geometries = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<GeometryEntry> geometryLru = new();
    private readonly ConditionalWeakTable<ID3D11Texture2D, ID2D1Bitmap1> imageBitmaps = new();
    private readonly Stack<Matrix3x2> transforms = new();
    private readonly Stack<ScopeKind> scopes = new();
    private ID3D11Texture2D? targetTexture;
    private ID2D1Bitmap1? targetBitmap;
    private bool drawing;
    private bool disposed;

    private enum ScopeKind
    {
        AxisClip,
        Layer,
    }

    private readonly record struct BrushEntry(uint ColorKey, ID2D1SolidColorBrush Brush);
    private readonly record struct GeometryEntry(VectorPath Path, ID2D1PathGeometry Geometry);

    public D2DDrawContext(
        VrD3D11Device d3d,
        int maxLayouts = TextLayoutCache.DefaultMaxLayouts,
        int maxBrushes = DefaultMaxBrushes,
        int maxGeometries = DefaultMaxGeometries,
        string locale = "zh-CN")
    {
        ArgumentNullException.ThrowIfNull(d3d);
        if (maxBrushes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBrushes), "Max brushes must be positive.");
        }
        if (maxGeometries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGeometries), "Max geometries must be positive.");
        }

        this.d3d = d3d;
        this.maxBrushes = maxBrushes;
        this.maxGeometries = maxGeometries;

        context = d3d.D2DDevice.CreateDeviceContext(DeviceContextOptions.None);
        context.UnitMode = UnitMode.Pixels;
        context.AntialiasMode = AntialiasMode.PerPrimitive;
        context.TextAntialiasMode = TextAntialiasMode.Grayscale;

        roundStroke = d3d.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties1
        {
            StartCap = CapStyle.Round,
            EndCap = CapStyle.Round,
            LineJoin = LineJoin.Round,
        });
        textCache = new TextLayoutCache(d3d.DWriteFactory, maxLayouts: maxLayouts, locale: locale);
        transforms.Push(Matrix3x2.Identity);
    }

    public TextLayoutCache TextCache => textCache;
    public int CachedBrushCount => brushes.Count;
    public int CachedGeometryCount => geometries.Count;
    public TextAntialiasMode TextAntialiasMode => context.TextAntialiasMode;

    /// <summary>
    /// Zero-copy binds <paramref name="target"/> via <see cref="IDXGISurface"/> and begins a Direct2D drawing frame.
    /// </summary>
    public void BeginFrame(ID3D11Texture2D target)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        if (drawing)
        {
            throw new InvalidOperationException("BeginFrame called while a frame is already active.");
        }

        if (!ReferenceEquals(target, targetTexture))
        {
            targetBitmap?.Dispose();
            targetBitmap = null;
            targetTexture = null;

            using var surface = target.QueryInterface<IDXGISurface>();
            targetBitmap = context.CreateBitmapFromDxgiSurface(
                surface,
                new BitmapProperties1(
                    new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                    Dpi,
                    Dpi,
                    BitmapOptions.Target | BitmapOptions.CannotDraw));
            targetTexture = target;
        }

        context.Target = targetBitmap;
        context.BeginDraw();
        drawing = true;
        scopes.Clear();
        transforms.Clear();
        transforms.Push(Matrix3x2.Identity);
        context.Transform = Matrix3x2.Identity;
    }

    /// <summary>
    /// Ends the Direct2D drawing frame, detaches the render target, and flushes the D3D11 immediate context.
    /// Returns false if the underlying GPU device was removed.
    /// </summary>
    public bool EndFrame()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!drawing)
        {
            throw new InvalidOperationException("EndFrame called without a matching BeginFrame.");
        }

        drawing = false;
        var result = context.EndDraw();
        context.Target = null;
        d3d.Context.Flush();
        return !result.Failure || result.Code != Vortice.DXGI.ResultCode.DeviceRemoved.Code;
    }

    /// <summary>
    /// Releases the cached target <see cref="ID2D1Bitmap1"/> so resized or disposed textures are never pinned by COM references.
    /// </summary>
    public void ClearTargetCache()
    {
        if (drawing)
        {
            throw new InvalidOperationException("Cannot clear target cache while drawing.");
        }
        context.Target = null;
        targetBitmap?.Dispose();
        targetBitmap = null;
        targetTexture = null;
    }

    public void SetLocale(string locale) => textCache.Locale = locale;

    public Vector2 MeasureText(string text, TextStyle style, float maxWidth = float.PositiveInfinity) =>
        textCache.MeasureText(text, style, maxWidth);

    public void Clear(Color color) => context.Clear(ToColor4(color));

    public void FillRoundedRect(Rect rect, float radius, Color color)
    {
        if (color.A == 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }
        var brush = GetBrush(color);
        context.FillRoundedRectangle(new RoundedRectangle(ToRaw(rect), radius, radius), brush);
    }

    public void StrokeRoundedRect(Rect rect, float radius, Color color, float width)
    {
        if (color.A == 0 || width <= 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }
        var brush = GetBrush(color);
        context.DrawRoundedRectangle(new RoundedRectangle(ToRaw(rect), radius, radius), brush, width, roundStroke);
    }

    public void FillPath(VectorPath path, Color color)
    {
        if (color.A == 0 || path.Segments.Count == 0)
        {
            return;
        }
        var brush = GetBrush(color);
        context.FillGeometry(GetGeometry(path), brush);
    }

    public void StrokePath(VectorPath path, Color color, float width)
    {
        if (color.A == 0 || width <= 0 || path.Segments.Count == 0)
        {
            return;
        }
        var brush = GetBrush(color);
        context.DrawGeometry(GetGeometry(path), brush, width, roundStroke);
    }

    public void DrawText(string text, Rect box, TextStyle style)
    {
        if (string.IsNullOrEmpty(text) || box.Width <= 0 || box.Height <= 0 || style.Color.A == 0)
        {
            return;
        }
        var brush = GetBrush(style.Color);
        var layout = textCache.Get(text, style, box.Width, box.Height);
        context.DrawTextLayout(box.Origin, layout, brush, DrawTextOptions.Clip);
    }

    public void DrawImage(ITextureHandle texture, Rect destination, float opacity = 1f)
    {
        if (texture is not D2DTextureHandle gpu)
        {
            throw new ArgumentException("Texture handle must be a D2DTextureHandle.", nameof(texture));
        }
        if (opacity <= 0f || destination.Width <= 0 || destination.Height <= 0)
        {
            return;
        }

        if (!imageBitmaps.TryGetValue(gpu.Texture, out var bitmap))
        {
            using var surface = gpu.Texture.QueryInterface<IDXGISurface>();
            bitmap = context.CreateBitmapFromDxgiSurface(
                surface,
                new BitmapProperties1(
                    new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                    Dpi,
                    Dpi,
                    BitmapOptions.None));
            imageBitmaps.Add(gpu.Texture, bitmap);
        }

        context.DrawBitmap(bitmap, ToRaw(destination), opacity, InterpolationMode.Linear, null, null);
    }

    public void PushClip(Rect rect, float radius = 0f)
    {
        if (radius <= 0f)
        {
            context.PushAxisAlignedClip(ToRaw(rect), AntialiasMode.PerPrimitive);
            scopes.Push(ScopeKind.AxisClip);
            return;
        }

        using var mask = d3d.D2DFactory.CreateRoundedRectangleGeometry(new RoundedRectangle(ToRaw(rect), radius, radius));
        PushLayer(new LayerParameters1
        {
            ContentBounds = InfiniteBounds,
            GeometricMask = mask,
            MaskAntialiasMode = AntialiasMode.PerPrimitive,
            MaskTransform = Matrix3x2.Identity,
            Opacity = 1f,
        });
    }

    public void PopClip() => PopScope();

    public void PushTransform(Matrix3x2 transform)
    {
        var combined = transform * transforms.Peek();
        transforms.Push(combined);
        context.Transform = combined;
    }

    public void PopTransform()
    {
        transforms.Pop();
        context.Transform = transforms.Peek();
    }

    public void PushOpacity(float opacity) =>
        PushLayer(new LayerParameters1
        {
            ContentBounds = InfiniteBounds,
            MaskTransform = Matrix3x2.Identity,
            Opacity = Math.Clamp(opacity, 0f, 1f),
        });

    public void PopOpacity() => PopScope();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        context.Target = null;
        targetBitmap?.Dispose();
        targetBitmap = null;
        targetTexture = null;

        foreach (var entry in brushLru)
        {
            entry.Brush.Dispose();
        }
        brushes.Clear();
        brushLru.Clear();

        foreach (var entry in geometryLru)
        {
            entry.Geometry.Dispose();
        }
        geometries.Clear();
        geometryLru.Clear();

        foreach (var pair in imageBitmaps)
        {
            pair.Value.Dispose();
        }

        textCache.Dispose();
        roundStroke.Dispose();
        context.Dispose();
    }

    private ID2D1SolidColorBrush GetBrush(Color color)
    {
        uint key = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        if (brushes.TryGetValue(key, out var existing))
        {
            brushLru.Remove(existing);
            brushLru.AddFirst(existing);
            return existing.Value.Brush;
        }

        if (brushes.Count >= maxBrushes && brushLru.Last is { } oldest)
        {
            brushLru.RemoveLast();
            brushes.Remove(oldest.Value.ColorKey);
            oldest.Value.Brush.Dispose();
        }

        var brush = context.CreateSolidColorBrush(ToColor4(color));
        var node = new LinkedListNode<BrushEntry>(new BrushEntry(key, brush));
        brushLru.AddFirst(node);
        brushes[key] = node;
        return brush;
    }

    private ID2D1PathGeometry GetGeometry(VectorPath path)
    {
        if (geometries.TryGetValue(path, out var existing))
        {
            geometryLru.Remove(existing);
            geometryLru.AddFirst(existing);
            return existing.Value.Geometry;
        }

        if (geometries.Count >= maxGeometries && geometryLru.Last is { } oldest)
        {
            geometryLru.RemoveLast();
            geometries.Remove(oldest.Value.Path);
            oldest.Value.Geometry.Dispose();
        }

        var geometry = d3d.D2DFactory.CreatePathGeometry();
        using (var sink = geometry.Open())
        {
            bool open = false;
            foreach (var segment in path.Segments)
            {
                switch (segment.Verb)
                {
                    case PathVerb.MoveTo:
                        if (open)
                        {
                            sink.EndFigure(FigureEnd.Open);
                        }
                        sink.BeginFigure(segment.P1, FigureBegin.Filled);
                        open = true;
                        break;
                    case PathVerb.LineTo:
                        sink.AddLine(segment.P1);
                        break;
                    case PathVerb.CubicTo:
                        sink.AddBezier(new BezierSegment(segment.P1, segment.P2, segment.P3));
                        break;
                    case PathVerb.Close:
                        sink.EndFigure(FigureEnd.Closed);
                        open = false;
                        break;
                }
            }
            if (open)
            {
                sink.EndFigure(FigureEnd.Open);
            }
            sink.Close();
        }

        var node = new LinkedListNode<GeometryEntry>(new GeometryEntry(path, geometry));
        geometryLru.AddFirst(node);
        geometries[path] = node;
        return geometry;
    }

    private void PushLayer(LayerParameters1 parameters)
    {
        context.PushLayer(ref parameters, null);
        scopes.Push(ScopeKind.Layer);
    }

    private void PopScope()
    {
        if (scopes.Pop() == ScopeKind.AxisClip)
        {
            context.PopAxisAlignedClip();
        }
        else
        {
            context.PopLayer();
        }
    }

    private static RawRectF ToRaw(Rect r) => new(r.X, r.Y, r.Right, r.Bottom);

    private static Color4 ToColor4(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
}
