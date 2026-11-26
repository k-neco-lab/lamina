// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using Valve.VR;

namespace KLab.Lamina;

/// <summary>
/// Thin low-level OpenVR <c>IVROverlay</c> driver contract enabling production SteamVR calls
/// as well as deterministic headless verification without a running vrserver.
/// </summary>
public interface IVrOverlayDriver
{
    bool IsAvailable { get; }

    EVROverlayError CreateOverlay(string key, string name, out ulong handle);
    EVROverlayError DestroyOverlay(ulong handle);
    EVROverlayError ShowOverlay(ulong handle);
    EVROverlayError HideOverlay(ulong handle);
    EVROverlayError SetOverlaySortOrder(ulong handle, uint sortOrder);
    EVROverlayError SetOverlayAlpha(ulong handle, float alpha);
    EVROverlayError SetOverlayWidthInMeters(ulong handle, float widthMeters);
    EVROverlayError SetOverlayCurvature(ulong handle, float curvature);
    EVROverlayError SetOverlayTransformAbsolute(ulong handle, ETrackingUniverseOrigin origin, in HmdMatrix34_t transform);
    EVROverlayError SetOverlayMouseScale(ulong handle, in HmdVector2_t scale);
    EVROverlayError SetOverlayTexelAspect(ulong handle, float aspect);
    EVROverlayError SetOverlayTextureBounds(ulong handle, in VRTextureBounds_t bounds);
    EVROverlayError SetOverlayTexture(ulong handle, in Texture_t texture);
    bool ComputeOverlayIntersection(ulong handle, in VROverlayIntersectionParams_t parameters, out VROverlayIntersectionResults_t results);
}

/// <summary>
/// Production <see cref="IVrOverlayDriver"/> backed directly by <see cref="OpenVR.Overlay"/>.
/// </summary>
public sealed class OpenVrOverlayDriver : IVrOverlayDriver
{
    private readonly Func<CVROverlay?> overlayProvider;

    public OpenVrOverlayDriver(Func<CVROverlay?>? overlayProvider = null)
    {
        this.overlayProvider = overlayProvider ?? (() => OpenVR.Overlay);
    }

    public bool IsAvailable => overlayProvider() is not null;

    public EVROverlayError CreateOverlay(string key, string name, out ulong handle)
    {
        handle = 0;
        var overlay = overlayProvider();
        return overlay is null ? EVROverlayError.RequestFailed : overlay.CreateOverlay(key, name, ref handle);
    }

    public EVROverlayError DestroyOverlay(ulong handle)
    {
        var overlay = overlayProvider();
        return overlay is null ? EVROverlayError.RequestFailed : overlay.DestroyOverlay(handle);
    }

    public EVROverlayError ShowOverlay(ulong handle)
    {
        var overlay = overlayProvider();
        return overlay is null ? EVROverlayError.RequestFailed : overlay.ShowOverlay(handle);
    }

    public EVROverlayError HideOverlay(ulong handle)
    {
        var overlay = overlayProvider();
        return overlay is null ? EVROverlayError.RequestFailed : overlay.HideOverlay(handle);
    }

    public EVROverlayError SetOverlaySortOrder(ulong handle, uint sortOrder)
    {
        var overlay = overlayProvider();
        return overlay is null ? EVROverlayError.RequestFailed : overlay.SetOverlaySortOrder(handle, sortOrder);
    }

    public EVROverlayError SetOverlayAlpha(ulong handle, float alpha)
    {
        var overlay = overlayProvider();
        return overlay is null ? EVROverlayError.RequestFailed : overlay.SetOverlayAlpha(handle, alpha);
    }

    public EVROverlayError SetOverlayWidthInMeters(ulong handle, float widthMeters)
    {
        var overlay = overlayProvider();
        return overlay is null ? EVROverlayError.RequestFailed : overlay.SetOverlayWidthInMeters(handle, widthMeters);
    }

    public EVROverlayError SetOverlayCurvature(ulong handle, float curvature)
    {
        var overlay = overlayProvider();
        return overlay is null ? EVROverlayError.RequestFailed : overlay.SetOverlayCurvature(handle, curvature);
    }

    public EVROverlayError SetOverlayTransformAbsolute(ulong handle, ETrackingUniverseOrigin origin, in HmdMatrix34_t transform)
    {
        var overlay = overlayProvider();
        if (overlay is null)
        {
            return EVROverlayError.RequestFailed;
        }

        var copy = transform;
        return overlay.SetOverlayTransformAbsolute(handle, origin, ref copy);
    }

    public EVROverlayError SetOverlayMouseScale(ulong handle, in HmdVector2_t scale)
    {
        var overlay = overlayProvider();
        if (overlay is null)
        {
            return EVROverlayError.RequestFailed;
        }

        var copy = scale;
        return overlay.SetOverlayMouseScale(handle, ref copy);
    }

    public EVROverlayError SetOverlayTexelAspect(ulong handle, float aspect)
    {
        var overlay = overlayProvider();
        return overlay is null ? EVROverlayError.RequestFailed : overlay.SetOverlayTexelAspect(handle, aspect);
    }

    public EVROverlayError SetOverlayTextureBounds(ulong handle, in VRTextureBounds_t bounds)
    {
        var overlay = overlayProvider();
        if (overlay is null)
        {
            return EVROverlayError.RequestFailed;
        }

        var copy = bounds;
        return overlay.SetOverlayTextureBounds(handle, ref copy);
    }

    public EVROverlayError SetOverlayTexture(ulong handle, in Texture_t texture)
    {
        var overlay = overlayProvider();
        if (overlay is null)
        {
            return EVROverlayError.RequestFailed;
        }

        var copy = texture;
        return overlay.SetOverlayTexture(handle, ref copy);
    }

    public bool ComputeOverlayIntersection(
        ulong handle,
        in VROverlayIntersectionParams_t parameters,
        out VROverlayIntersectionResults_t results)
    {
        results = default;
        var overlay = overlayProvider();
        if (overlay is null || handle == 0)
        {
            return false;
        }

        var copy = parameters;
        return overlay.ComputeOverlayIntersection(handle, ref copy, ref results);
    }
}

/// <summary>
/// Manages SteamVR <c>IVROverlay</c> handle lifecycles, cached property updates (pose, width, alpha,
/// curvature, visibility, sort order), and zero-copy D3D11 shared <see cref="Texture_t"/> submission.
/// </summary>
public sealed class VrOverlayBackend : IDisposable
{
    private sealed class OverlaySlot
    {
        public required string PanelId { get; init; }
        public required string OverlayKey { get; init; }
        public ulong Handle;
        public bool Visible;
        public bool PoseSubmitted;
        public Matrix4x4 Pose = Matrix4x4.Identity;
        public float WidthMeters = float.NaN;
        public float Alpha = float.NaN;
        public float Curvature = float.NaN;
        public uint SortOrder = uint.MaxValue;
        public int PixelWidth;
        public int PixelHeight;
        public nint NativeTexture;
        public bool TextureNeedsResubmitAfterHide;
    }

    private readonly IVrOverlayDriver driver;
    private readonly string keyPrefix;
    private readonly ETrackingUniverseOrigin trackingOrigin;
    private readonly Dictionary<string, OverlaySlot> slots = new(StringComparer.Ordinal);
    private bool disposed;

    public VrOverlayBackend(
        IVrOverlayDriver? driver = null,
        string? keyPrefix = null,
        ETrackingUniverseOrigin trackingOrigin = ETrackingUniverseOrigin.TrackingUniverseStanding)
    {
        this.driver = driver ?? new OpenVrOverlayDriver();
        this.keyPrefix = string.IsNullOrWhiteSpace(keyPrefix)
            ? $"klab.vroverlay.{Environment.ProcessId}"
            : keyPrefix;
        this.trackingOrigin = trackingOrigin;
    }

    public IVrOverlayDriver Driver => driver;

    public int ActiveOverlayCount => slots.Count;

    public bool TryGetHandle(string panelId, out ulong handle)
    {
        if (slots.TryGetValue(panelId, out var slot) && slot.Handle != 0)
        {
            handle = slot.Handle;
            return true;
        }

        handle = 0;
        return false;
    }

    /// <summary>
    /// Creates an OpenVR overlay handle for <paramref name="panelId"/> if one does not already exist.
    /// </summary>
    public ulong EnsureOverlay(string panelId, string? displayTitle = null, uint sortOrder = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrWhiteSpace(panelId))
        {
            throw new ArgumentException("Panel id cannot be empty.", nameof(panelId));
        }

        if (slots.TryGetValue(panelId, out var existing))
        {
            return existing.Handle;
        }

        string key = $"{keyPrefix}.{panelId}";
        string title = string.IsNullOrWhiteSpace(displayTitle) ? panelId : displayTitle;
        Check(driver.CreateOverlay(key, title, out ulong handle), nameof(driver.CreateOverlay), panelId);

        var slot = new OverlaySlot
        {
            PanelId = panelId,
            OverlayKey = key,
            Handle = handle,
        };
        slots[panelId] = slot;

        var defaultBounds = new VRTextureBounds_t { uMin = 0f, uMax = 1f, vMin = 0f, vMax = 1f };
        Check(driver.SetOverlayTextureBounds(handle, defaultBounds), nameof(driver.SetOverlayTextureBounds), panelId);
        Check(driver.SetOverlayTexelAspect(handle, 1f), nameof(driver.SetOverlayTexelAspect), panelId);
        SetSortOrder(panelId, sortOrder);
        return handle;
    }

    public void SetTransform(string panelId, in Matrix4x4 worldPose)
    {
        var slot = RequireSlot(panelId);
        if (slot.PoseSubmitted && slot.Pose == worldPose)
        {
            return;
        }

        var vrPose = SpatialPoseMath.ToOpenVr(worldPose);
        Check(driver.SetOverlayTransformAbsolute(slot.Handle, trackingOrigin, vrPose), nameof(driver.SetOverlayTransformAbsolute), panelId);
        slot.Pose = worldPose;
        slot.PoseSubmitted = true;
    }

    public void SetWidthMeters(string panelId, float widthMeters)
    {
        var slot = RequireSlot(panelId);
        if (float.IsFinite(slot.WidthMeters) && MathF.Abs(slot.WidthMeters - widthMeters) < 1e-6f)
        {
            return;
        }

        Check(driver.SetOverlayWidthInMeters(slot.Handle, widthMeters), nameof(driver.SetOverlayWidthInMeters), panelId);
        slot.WidthMeters = widthMeters;
    }

    public void SetAlpha(string panelId, float alpha)
    {
        var slot = RequireSlot(panelId);
        float clamped = Math.Clamp(alpha, 0f, 1f);
        if (float.IsFinite(slot.Alpha) && MathF.Abs(slot.Alpha - clamped) < 1e-6f)
        {
            return;
        }

        Check(driver.SetOverlayAlpha(slot.Handle, clamped), nameof(driver.SetOverlayAlpha), panelId);
        slot.Alpha = clamped;
    }

    public void SetCurvature(string panelId, float curvature)
    {
        var slot = RequireSlot(panelId);
        float clamped = Math.Clamp(curvature, 0f, 1f);
        if (float.IsFinite(slot.Curvature) && MathF.Abs(slot.Curvature - clamped) < 1e-6f)
        {
            return;
        }

        Check(driver.SetOverlayCurvature(slot.Handle, clamped), nameof(driver.SetOverlayCurvature), panelId);
        slot.Curvature = clamped;
    }

    public void SetSortOrder(string panelId, uint sortOrder)
    {
        var slot = RequireSlot(panelId);
        if (slot.SortOrder == sortOrder)
        {
            return;
        }

        Check(driver.SetOverlaySortOrder(slot.Handle, sortOrder), nameof(driver.SetOverlaySortOrder), panelId);
        slot.SortOrder = sortOrder;
    }

    public void SetPixelSize(string panelId, int pixelWidth, int pixelHeight)
    {
        var slot = RequireSlot(panelId);
        if (slot.PixelWidth == pixelWidth && slot.PixelHeight == pixelHeight)
        {
            return;
        }

        var scale = new HmdVector2_t { v0 = pixelWidth, v1 = pixelHeight };
        Check(driver.SetOverlayMouseScale(slot.Handle, scale), nameof(driver.SetOverlayMouseScale), panelId);
        slot.PixelWidth = pixelWidth;
        slot.PixelHeight = pixelHeight;
    }

    public void SetVisible(string panelId, bool visible)
    {
        var slot = RequireSlot(panelId);
        if (slot.Visible == visible)
        {
            return;
        }

        var err = visible ? driver.ShowOverlay(slot.Handle) : driver.HideOverlay(slot.Handle);
        Check(err, visible ? nameof(driver.ShowOverlay) : nameof(driver.HideOverlay), panelId);
        slot.Visible = visible;

        if (!visible)
        {
            slot.TextureNeedsResubmitAfterHide = true;
            return;
        }

        if (slot.TextureNeedsResubmitAfterHide && slot.NativeTexture != 0)
        {
            var texture = CreateDirectXTexture(slot.NativeTexture);
            Check(driver.SetOverlayTexture(slot.Handle, texture), nameof(driver.SetOverlayTexture), panelId);
            slot.TextureNeedsResubmitAfterHide = false;
        }
    }

    /// <summary>
    /// Submits a D3D11 shared texture pointer (<c>ID3D11Texture2D*</c>) to SteamVR via <c>SetOverlayTexture</c>.
    /// </summary>
    public void SubmitD3D11Texture(string panelId, nint nativeTexturePointer, EColorSpace colorSpace = EColorSpace.Gamma)
    {
        var slot = RequireSlot(panelId);
        if (nativeTexturePointer == 0 || slot.NativeTexture == nativeTexturePointer)
        {
            return;
        }

        var texture = CreateDirectXTexture(nativeTexturePointer, colorSpace);
        Check(driver.SetOverlayTexture(slot.Handle, texture), nameof(driver.SetOverlayTexture), panelId);
        slot.NativeTexture = nativeTexturePointer;
        slot.TextureNeedsResubmitAfterHide = false;
    }

    public void DestroyOverlay(string panelId)
    {
        if (!slots.Remove(panelId, out var slot))
        {
            return;
        }

        if (slot.Handle != 0 && driver.IsAvailable)
        {
            driver.DestroyOverlay(slot.Handle);
        }

        slot.Handle = 0;
        slot.Visible = false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var slot in slots.Values)
        {
            if (slot.Handle != 0 && driver.IsAvailable)
            {
                driver.DestroyOverlay(slot.Handle);
            }

            slot.Handle = 0;
            slot.Visible = false;
        }

        slots.Clear();
    }

    private OverlaySlot RequireSlot(string panelId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!slots.TryGetValue(panelId, out var slot) || slot.Handle == 0)
        {
            throw new KeyNotFoundException($"Overlay panel '{panelId}' has not been created.");
        }

        return slot;
    }

    private static Texture_t CreateDirectXTexture(nint nativeTexturePointer, EColorSpace colorSpace = EColorSpace.Gamma) => new()
    {
        handle = nativeTexturePointer,
        eType = ETextureType.DirectX,
        eColorSpace = colorSpace,
    };

    private static void Check(EVROverlayError error, string operation, string panelId)
    {
        if (error != EVROverlayError.None)
        {
            throw new InvalidOperationException($"OpenVR {operation} failed for panel '{panelId}': {error}");
        }
    }
}
