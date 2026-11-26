// Copyright (c) K Lab. Licensed under the MIT License.

using Vortice.Direct3D11;

namespace KLab.Lamina.D2D;

/// <summary>
/// Double-buffered cross-process shared D3D11 texture swap chain for VR overlay panels.
/// Drawing targets <see cref="BackTexture"/>; after GPU flush, <see cref="Swap"/> atomically promotes it to
/// <see cref="FrontTexture"/> while deferring disposal of resized textures until <see cref="RetireDeferred"/>.
/// </summary>
public sealed class SharedTextureSwapChain : IDisposable
{
    private readonly VrD3D11Device device;
    private ID3D11Texture2D frontTexture;
    private ID3D11Texture2D backTexture;
    private ID3D11Texture2D? retiredTexture;
    private nint frontSharedHandle;
    private nint backSharedHandle;
    private bool disposed;

    public SharedTextureSwapChain(VrD3D11Device device, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        this.device = device;
        Width = width;
        Height = height;
        frontTexture = device.CreateSharedPanelTexture(width, height);
        try
        {
            backTexture = device.CreateSharedPanelTexture(width, height);
            frontSharedHandle = VrD3D11Device.GetSharedHandle(frontTexture);
            backSharedHandle = VrD3D11Device.GetSharedHandle(backTexture);
        }
        catch
        {
            frontTexture.Dispose();
            throw;
        }
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public long SwapCount { get; private set; }
    public bool HasDeferredRetirement => retiredTexture is not null;

    public ID3D11Texture2D FrontTexture
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return frontTexture;
        }
    }

    public ID3D11Texture2D BackTexture
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return backTexture;
        }
    }

    public nint FrontNativePointer => FrontTexture.NativePointer;
    public nint BackNativePointer => BackTexture.NativePointer;
    public nint FrontSharedHandle
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return frontSharedHandle;
        }
    }

    public nint BackSharedHandle
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return backSharedHandle;
        }
    }

    /// <summary>
    /// Atomically swaps front and back textures after the back buffer has been drawn and flushed.
    /// </summary>
    public void Swap()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        (frontTexture, backTexture) = (backTexture, frontTexture);
        (frontSharedHandle, backSharedHandle) = (backSharedHandle, frontSharedHandle);
        SwapCount++;
    }

    /// <summary>
    /// Recreates both shared textures at the new size. The previous front texture is moved to deferred retirement
    /// so external compositors reading it finish safely before it is released on the next frame.
    /// </summary>
    public bool Resize(int width, int height)
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

        if (width == Width && height == Height)
        {
            return false;
        }

        var nextFront = device.CreateSharedPanelTexture(width, height);
        ID3D11Texture2D nextBack;
        nint nextFrontHandle;
        nint nextBackHandle;
        try
        {
            nextBack = device.CreateSharedPanelTexture(width, height);
            nextFrontHandle = VrD3D11Device.GetSharedHandle(nextFront);
            nextBackHandle = VrD3D11Device.GetSharedHandle(nextBack);
        }
        catch
        {
            nextFront.Dispose();
            throw;
        }

        retiredTexture?.Dispose();
        retiredTexture = frontTexture;
        backTexture.Dispose();

        frontTexture = nextFront;
        backTexture = nextBack;
        frontSharedHandle = nextFrontHandle;
        backSharedHandle = nextBackHandle;
        Width = width;
        Height = height;
        return true;
    }

    /// <summary>
    /// Releases any previously retired front texture from an earlier resize.
    /// </summary>
    public void RetireDeferred()
    {
        if (retiredTexture is not null)
        {
            retiredTexture.Dispose();
            retiredTexture = null;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        retiredTexture?.Dispose();
        retiredTexture = null;
        frontTexture.Dispose();
        backTexture.Dispose();
    }
}
