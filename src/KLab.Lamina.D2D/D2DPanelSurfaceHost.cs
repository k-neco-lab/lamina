// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using KLab.Lamina.Abstractions;
using KLab.Lamina.Ui;
using Vortice.Direct3D11;

namespace KLab.Lamina.D2D;

/// <summary>
/// Double-buffered Direct2D panel surface host implementing both <see cref="IVrOverlayFrameSurface"/> and
/// <see cref="IVrPanelInputSink"/>.
/// Guarantees zero GPU draw calls on clean frames and defers <see cref="SetContent(Control)"/> and <see cref="Resize"/>
/// while any pointer holds an active capture on <see cref="UiRoot"/> so periodic UI refreshes never interrupt
/// in-flight VR clicks or drags.
/// </summary>
public sealed class D2DPanelSurfaceHost : IVrOverlayFrameSurface, IVrPanelInputSink, IDisposable
{
    private readonly record struct PendingContentUpdate(Control Content, int? Width, int? Height, float? Density);
    private readonly record struct PendingResizeUpdate(int Width, int Height, float Density);

    private readonly VrD3D11Device device;
    private readonly D2DDrawContext drawContext;
    private readonly bool ownsDrawContext;
    private readonly SharedTextureSwapChain swapChain;
    private readonly Dictionary<int, Vector2> lastPointerPositions = new();
    private readonly Action<nint, int, int>? onFrameSubmitted;

    private UiRoot? root;
    private Theme activeTheme;
    private Vector2 logicalSize;
    private float renderDensity;
    private PendingContentUpdate? pendingContent;
    private PendingResizeUpdate? pendingResize;
    private bool disposed;

    public D2DPanelSurfaceHost(
        string id,
        VrD3D11Device device,
        D2DDrawContext? drawContext,
        int width,
        int height,
        float renderDensity = 1f,
        uint sortOrder = 0,
        float widthMeters = 0.5f,
        Theme? theme = null,
        Action<nint, int, int>? onFrameSubmitted = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Panel id cannot be empty.", nameof(id));
        }
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        Id = id;
        this.device = device;
        if (drawContext is null)
        {
            this.drawContext = new D2DDrawContext(device);
            ownsDrawContext = true;
        }
        else
        {
            this.drawContext = drawContext;
            ownsDrawContext = false;
        }

        this.renderDensity = MathF.Max(1f, renderDensity);
        logicalSize = new Vector2(width, height);
        activeTheme = theme ?? Theme.Default;
        SortOrder = sortOrder;
        WidthMeters = widthMeters;
        IsVisible = true;
        Pose = Matrix4x4.Identity;
        this.onFrameSubmitted = onFrameSubmitted;

        var (texW, texH) = ComputeTextureSize(width, height, this.renderDensity);
        swapChain = new SharedTextureSwapChain(device, texW, texH);
    }

    public string Id { get; }
    public string PanelId => Id;
    public int PixelWidth => (int)logicalSize.X;
    public int PixelHeight => (int)logicalSize.Y;
    public Vector2 PixelSize => logicalSize;
    public int TextureWidth => swapChain.Width;
    public int TextureHeight => swapChain.Height;
    public float RenderDensity => renderDensity;
    public uint SortOrder { get; set; }
    public bool IsVisible { get; set; }
    public bool IsDirty => root?.IsDirty ?? false;
    public Matrix4x4 Pose { get; set; }
    public float WidthMeters { get; set; }
    public float HeightMeters => logicalSize.X <= 0f ? 0f : WidthMeters * logicalSize.Y / logicalSize.X;

    public UiRoot? Root => root;
    public SharedTextureSwapChain SwapChain => swapChain;
    public D2DDrawContext DrawContext => drawContext;
    public bool HasActiveCapture => root?.HasActiveCapture ?? false;
    public bool HasPendingContent => pendingContent.HasValue;
    public bool HasPendingResize => pendingResize.HasValue;

    public long GpuRenderCount { get; private set; }
    public long SubmittedFrameCount { get; private set; }
    public nint LastSubmittedTexturePointer { get; private set; }
    public int LastSubmittedWidth { get; private set; }
    public int LastSubmittedHeight { get; private set; }

    public ID3D11Texture2D FrontTexture => swapChain.FrontTexture;
    public nint FrontSharedHandle => swapChain.FrontSharedHandle;

    public Theme Theme
    {
        get => activeTheme;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            activeTheme = value;
            if (root is not null)
            {
                root.Theme = value;
            }
        }
    }

    /// <summary>
    /// Replaces the panel's root control. If any pointer currently holds an active capture on <see cref="UiRoot"/>,
    /// the replacement is queued and applied automatically as soon as all active captures are released.
    /// </summary>
    public void SetContent(Control newContent)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(newContent);

        if (HasActiveCapture)
        {
            pendingContent = new PendingContentUpdate(newContent, null, null, null);
            return;
        }

        pendingContent = null;
        ApplyContent(newContent, PixelWidth, PixelHeight, renderDensity);
    }

    /// <summary>
    /// Replaces the panel's root control along with new logical dimensions / render density, honoring active-capture deferral.
    /// </summary>
    public void SetContent(Control newContent, int width, int height, float? density = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(newContent);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        float targetDensity = MathF.Max(1f, density ?? renderDensity);
        if (HasActiveCapture)
        {
            pendingContent = new PendingContentUpdate(newContent, width, height, targetDensity);
            pendingResize = null;
            return;
        }

        pendingContent = null;
        pendingResize = null;
        ApplyContent(newContent, width, height, targetDensity);
    }

    /// <summary>
    /// Resizes the panel's logical size and backing double-buffered shared textures.
    /// Deferred safely while any pointer holds an active capture.
    /// </summary>
    public void Resize(int width, int height, float? density = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        float targetDensity = MathF.Max(1f, density ?? renderDensity);
        if (HasActiveCapture)
        {
            pendingResize = new PendingResizeUpdate(width, height, targetDensity);
            return;
        }

        pendingResize = null;
        ApplyResize(width, height, targetDensity);
    }

    public void MarkDirty() => root?.MarkRender();

    public void Tick(float deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        root?.Update(deltaSeconds);
    }

    /// <summary>
    /// Renders the UI tree into the back shared texture only when dirty, flushes D3D11, swaps buffers, and submits
    /// the new front shared texture. Returns false with zero GPU draw calls when the panel is clean.
    /// </summary>
    public bool RenderIfDirty()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        swapChain.RetireDeferred();

        if (root is null || !root.IsDirty)
        {
            return false;
        }

        drawContext.BeginFrame(swapChain.BackTexture);
        if (renderDensity != 1f)
        {
            drawContext.PushTransform(Matrix3x2.CreateScale(renderDensity));
        }

        bool rendered;
        try
        {
            rendered = root.RenderIfDirty(drawContext);
        }
        finally
        {
            if (renderDensity != 1f)
            {
                drawContext.PopTransform();
            }
            drawContext.EndFrame();
        }

        if (!rendered)
        {
            return false;
        }

        swapChain.Swap();
        GpuRenderCount++;
        SubmitSharedTexture(swapChain.FrontNativePointer, swapChain.Width, swapChain.Height);
        return true;
    }

    public void SubmitSharedTexture(nint sharedTexturePointer, int width, int height)
    {
        LastSubmittedTexturePointer = sharedTexturePointer;
        LastSubmittedWidth = width;
        LastSubmittedHeight = height;
        SubmittedFrameCount++;
        onFrameSubmitted?.Invoke(sharedTexturePointer, width, height);
    }

    public bool IsPointerCaptured(int pointerId) => root?.IsPointerCaptured(pointerId) ?? false;

    public bool IsPointerContinuousDrag(int pointerId) => root?.IsPointerContinuousDrag(pointerId) ?? false;

    public void OnPointerEvent(in VrPointerEvent pointerEvent)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        switch (pointerEvent.Kind)
        {
            case VrPointerEventKind.Move:
            case VrPointerEventKind.Down:
            case VrPointerEventKind.Up:
            case VrPointerEventKind.Scroll:
                lastPointerPositions[pointerEvent.PointerId] = pointerEvent.PixelPosition;
                break;
            case VrPointerEventKind.Leave:
            case VrPointerEventKind.Cancel:
                lastPointerPositions.Remove(pointerEvent.PointerId);
                break;
        }

        root?.OnPointerEvent(pointerEvent);

        if ((pointerEvent.Kind == VrPointerEventKind.Up || pointerEvent.Kind == VrPointerEventKind.Cancel) && !HasActiveCapture)
        {
            FlushPendingUpdates();
        }
    }

    /// <summary>
    /// Cancels all active pointer captures/hovers and flushes any queued content or resize updates.
    /// </summary>
    public void CancelAllPointers()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lastPointerPositions.Clear();
        root?.CancelAllPointers();
        if (!HasActiveCapture)
        {
            FlushPendingUpdates();
        }
    }

    private void FlushPendingUpdates()
    {
        if (HasActiveCapture)
        {
            return;
        }

        bool applied = false;
        if (pendingContent is { } contentUpdate)
        {
            pendingContent = null;
            int targetW = contentUpdate.Width ?? pendingResize?.Width ?? PixelWidth;
            int targetH = contentUpdate.Height ?? pendingResize?.Height ?? PixelHeight;
            float targetDensity = contentUpdate.Density ?? pendingResize?.Density ?? renderDensity;
            pendingResize = null;
            ApplyContent(contentUpdate.Content, targetW, targetH, targetDensity);
            applied = true;
        }
        else if (pendingResize is { } resizeUpdate)
        {
            pendingResize = null;
            ApplyResize(resizeUpdate.Width, resizeUpdate.Height, resizeUpdate.Density);
            applied = true;
        }

        if (applied && root is not null)
        {
            root.Layout();
            foreach (var (pointerId, pos) in lastPointerPositions)
            {
                root.PointerMove(pointerId, pos);
            }
            root.MarkRender();
        }
    }

    private void ApplyContent(Control content, int width, int height, float density)
    {
        ApplyTextureResize(width, height, density);
        logicalSize = new Vector2(width, height);
        renderDensity = density;
        root = new UiRoot(content, drawContext, logicalSize, activeTheme, Id);
        root.MarkLayout();
    }

    private void ApplyResize(int width, int height, float density)
    {
        ApplyTextureResize(width, height, density);
        logicalSize = new Vector2(width, height);
        renderDensity = density;
        if (root is not null)
        {
            root.Size = logicalSize;
            root.MarkLayout();
        }
    }

    private void ApplyTextureResize(int width, int height, float density)
    {
        var (texW, texH) = ComputeTextureSize(width, height, density);
        if (texW != swapChain.Width || texH != swapChain.Height)
        {
            drawContext.ClearTargetCache();
            swapChain.Resize(texW, texH);
        }
    }

    private static (int Width, int Height) ComputeTextureSize(int logicalWidth, int logicalHeight, float density) =>
        (Math.Max(1, (int)MathF.Round(logicalWidth * density)),
         Math.Max(1, (int)MathF.Round(logicalHeight * density)));

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        drawContext.ClearTargetCache();
        swapChain.Dispose();
        if (ownsDrawContext)
        {
            drawContext.Dispose();
        }
    }
}
