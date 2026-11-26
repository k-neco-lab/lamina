// Copyright (c) K Lab. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;

namespace KLab.Lamina.D2D;

/// <summary>
/// Owns a D3D11.1 + DXGI + Direct2D 1.1 + DirectWrite device stack for zero-copy VR overlay UI rendering.
/// Prefers a hardware GPU adapter by default; WARP software fallback is opt-in for headless test environments.
/// </summary>
public sealed class VrD3D11Device : IDisposable
{
    public const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
    public const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);

    private static readonly FeatureLevel[] PreferredFeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
    ];

    private bool disposed;

    public VrD3D11Device(int adapterIndex = 0, bool multithreadProtected = true, bool allowWarpFallback = false)
    {
        AdapterIndex = Math.Max(0, adapterIndex);

        ID3D11Device? createdDevice = null;
        ID3D11DeviceContext? createdContext = null;
        bool usingWarp = false;

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (factory.EnumAdapters1((uint)AdapterIndex, out var adapter).Success && adapter is not null)
            {
                using (adapter)
                {
                    var hr = D3D11.D3D11CreateDevice(
                        adapter,
                        DriverType.Unknown,
                        DeviceCreationFlags.BgraSupport,
                        PreferredFeatureLevels,
                        out createdDevice,
                        out _,
                        out createdContext);
                    if (hr.Failure)
                    {
                        createdDevice?.Dispose();
                        createdContext?.Dispose();
                        createdDevice = null;
                        createdContext = null;
                        if (!allowWarpFallback)
                        {
                            hr.CheckError();
                        }
                    }
                }
            }
            else if (!allowWarpFallback)
            {
                throw new InvalidOperationException($"No DXGI adapter available at index {AdapterIndex}.");
            }

            if (createdDevice is null)
            {
                D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Warp,
                    DeviceCreationFlags.BgraSupport,
                    PreferredFeatureLevels,
                    out createdDevice,
                    out _,
                    out createdContext).CheckError();
                usingWarp = true;
            }

            Device = createdDevice!;
            Context = createdContext!;
            IsWarp = usingWarp;

            if (multithreadProtected)
            {
                using var multithread = Device.QueryInterface<ID3D11Multithread>();
                multithread.SetMultithreadProtected(true);
            }

            DxgiDevice = Device.QueryInterface<IDXGIDevice>();
            D2DFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
            D2DDevice = D2DFactory.CreateDevice(DxgiDevice);
            DWriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory2>();
        }
        catch
        {
            DWriteFactory?.Dispose();
            D2DDevice?.Dispose();
            D2DFactory?.Dispose();
            DxgiDevice?.Dispose();
            createdContext?.Dispose();
            createdDevice?.Dispose();
            throw;
        }
    }

    public int AdapterIndex { get; }
    public bool IsWarp { get; }
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public IDXGIDevice DxgiDevice { get; }
    public ID2D1Factory1 D2DFactory { get; }
    public ID2D1Device D2DDevice { get; }
    public IDWriteFactory2 DWriteFactory { get; }

    public bool IsDeviceLost => !disposed && Device.DeviceRemovedReason.Failure;

    public static bool IsDeviceRemoved(int hresult) =>
        hresult == DxgiErrorDeviceRemoved || hresult == DxgiErrorDeviceReset;

    /// <summary>
    /// Creates a cross-process shared BGRA8 render-target + shader-resource texture suitable for Direct2D drawing
    /// and zero-copy SteamVR compositor submission.
    /// </summary>
    public ID3D11Texture2D CreateSharedPanelTexture(int width, int height)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Texture width must be positive.");
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Texture height must be positive.");
        }

        return Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            MiscFlags = ResourceOptionFlags.Shared,
        });
    }

    /// <summary>
    /// Extracts the legacy DXGI cross-process shared handle from a texture created with <see cref="ResourceOptionFlags.Shared"/>.
    /// </summary>
    public static nint GetSharedHandle(ID3D11Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        using var resource = texture.QueryInterface<IDXGIResource>();
        return resource.SharedHandle;
    }

    /// <summary>
    /// Opens a shared texture created on another D3D11 device on the same adapter via its DXGI shared handle.
    /// </summary>
    public ID3D11Texture2D OpenSharedTexture(nint sharedHandle)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sharedHandle == nint.Zero)
        {
            throw new ArgumentException("Shared handle cannot be zero.", nameof(sharedHandle));
        }

        return Device.OpenSharedResource<ID3D11Texture2D>(sharedHandle);
    }

    /// <summary>
    /// Creates a shared BGRA8 shader-resource texture and uploads premultiplied BGRA pixel data via UpdateSubresource + Flush.
    /// </summary>
    public D2DTextureHandle CreateImageTexture(int width, int height, byte[] bgraPixels)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(bgraPixels);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image width must be positive.");
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Image height must be positive.");
        }
        if (bgraPixels.Length < width * height * 4)
        {
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4.", nameof(bgraPixels));
        }

        var texture = Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            MiscFlags = ResourceOptionFlags.Shared,
        });
        Context.UpdateSubresource(bgraPixels, texture, 0, (uint)(width * 4));
        Context.Flush();
        return new D2DTextureHandle(texture, ownsTexture: true);
    }

    /// <summary>
    /// Copies a GPU texture into a CPU-readable staging texture and returns tightly packed BGRA pixel rows.
    /// </summary>
    public byte[] ReadPixelsBgra(ID3D11Texture2D texture, out int width, out int height, out Format format)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(texture);

        var description = texture.Description;
        width = (int)description.Width;
        height = (int)description.Height;
        format = description.Format;

        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        description.SampleDescription = new SampleDescription(1, 0);

        using var staging = Device.CreateTexture2D(description);
        Context.CopyResource(staging, texture);
        var mapped = Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var bytes = new byte[width * height * 4];
            int rowBytes = width * 4;
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(mapped.DataPointer + row * (int)mapped.RowPitch, bytes, row * rowBytes, rowBytes);
            }
            return bytes;
        }
        finally
        {
            Context.Unmap(staging, 0);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        DWriteFactory.Dispose();
        D2DDevice.Dispose();
        D2DFactory.Dispose();
        DxgiDevice.Dispose();
        Context.ClearState();
        Context.Flush();
        Context.Dispose();
        Device.Dispose();
    }
}
