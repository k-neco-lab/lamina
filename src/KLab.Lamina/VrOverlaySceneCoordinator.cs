// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using KLab.Lamina.Abstractions;

namespace KLab.Lamina;

/// <summary>
/// Registration entry for a spatial overlay panel managed by <see cref="VrOverlaySceneCoordinator"/>.
/// </summary>
public sealed class VrOverlayPanelEntry
{
    public required IVrOverlayFrameSurface Surface { get; init; }
    public IVrPanelInputSink? InputSink { get; init; }
    public float Curvature { get; set; }
    public float Alpha { get; set; } = 1f;
    public bool EnableSeamGuard { get; set; } = true;
    public int LayerPriority { get; set; }
    public OverlayAttachment? Attachment { get; set; }
    public Func<nint>? SharedTextureProvider { get; set; }
    public Action<Matrix4x4>? OnResolvedPoseChanged { get; set; }
    public Matrix4x4 ResolvedWorldPose { get; internal set; } = Matrix4x4.Identity;
    public bool ResolvedVisible { get; internal set; } = true;
    public ulong OverlayHandle { get; internal set; }
}

/// <summary>
/// Out-of-the-box Layer 1 frame pump that wires together:
/// 1. Parent-child 3D spatial panel attachments (<see cref="SpatialPoseMath"/>);
/// 2. Per-hand action-set transient dropout debouncing (<see cref="VrHandActionTracker"/>);
/// 3. Dual-hand authoritative spatial ray arbitration (<see cref="SpatialHitArbiter"/>);
/// 4. Multi-pointer cross-panel input routing (<see cref="MultiPointerPanelRouter"/>);
/// 5. Optional SteamVR <c>IVROverlay</c> synchronization (<see cref="VrOverlayBackend"/>).
/// Any UI-engine panel implementing <see cref="IVrOverlayFrameSurface"/> + <see cref="IVrPanelInputSink"/>
/// can be plugged in directly without coupling to any 2D UI framework.
/// </summary>
public sealed class VrOverlaySceneCoordinator : IDisposable
{
    private readonly Dictionary<string, VrOverlayPanelEntry> panels = new(StringComparer.Ordinal);
    private readonly List<string> panelOrder = new();
    private readonly List<SpatialPanelTarget> targetBuffer = new();
    private readonly bool ownsBackend;
    private bool disposed;

    public VrOverlaySceneCoordinator(
        VrOverlayBackend? backend = null,
        IOverlayRaycaster? raycaster = null,
        VrHandActionTracker? handTracker = null,
        SpatialHitArbiter? arbiter = null,
        MultiPointerPanelRouter? router = null,
        bool ownsBackend = false)
    {
        Backend = backend;
        this.ownsBackend = ownsBackend;
        HandTracker = handTracker ?? new VrHandActionTracker();
        Arbiter = arbiter ?? new SpatialHitArbiter(raycaster);
        Router = router ?? new MultiPointerPanelRouter();
    }

    public VrOverlayBackend? Backend { get; }
    public VrHandActionTracker HandTracker { get; }
    public SpatialHitArbiter Arbiter { get; }
    public MultiPointerPanelRouter Router { get; }

    public int PanelCount => panels.Count;

    public bool TryGetPanel(string panelId, out VrOverlayPanelEntry entry) =>
        panels.TryGetValue(panelId, out entry!);

    /// <summary>
    /// Registers a spatial panel surface and optional multi-pointer input sink.
    /// </summary>
    public VrOverlayPanelEntry RegisterPanel(
        IVrOverlayFrameSurface surface,
        IVrPanelInputSink? inputSink = null,
        float curvature = 0f,
        float alpha = 1f,
        bool enableSeamGuard = true,
        int layerPriority = 0,
        OverlayAttachment? attachment = null,
        Func<nint>? sharedTextureProvider = null,
        Action<Matrix4x4>? onResolvedPoseChanged = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(surface);

        if (panels.ContainsKey(surface.Id))
        {
            throw new InvalidOperationException($"Panel '{surface.Id}' is already registered.");
        }

        ulong handle = 0;
        if (Backend is not null && Backend.Driver.IsAvailable)
        {
            handle = Backend.EnsureOverlay(surface.Id, surface.Id, surface.SortOrder);
        }

        var entry = new VrOverlayPanelEntry
        {
            Surface = surface,
            InputSink = inputSink ?? surface as IVrPanelInputSink,
            Curvature = Math.Clamp(curvature, 0f, 1f),
            Alpha = Math.Clamp(alpha, 0f, 1f),
            EnableSeamGuard = enableSeamGuard,
            LayerPriority = layerPriority,
            Attachment = attachment,
            SharedTextureProvider = sharedTextureProvider,
            OnResolvedPoseChanged = onResolvedPoseChanged,
            ResolvedWorldPose = surface.Pose,
            ResolvedVisible = surface.IsVisible,
            OverlayHandle = handle,
        };

        panels[surface.Id] = entry;
        panelOrder.Add(surface.Id);
        return entry;
    }

    /// <summary>
    /// Unregisters a panel, cancelling any pointers currently targeting it and destroying its OpenVR overlay handle.
    /// </summary>
    public bool UnregisterPanel(string panelId, double timestampSeconds = 0)
    {
        if (!panels.Remove(panelId, out var entry))
        {
            return false;
        }

        panelOrder.Remove(panelId);

        if (entry.InputSink is not null)
        {
            Router.RemovePanel(entry.InputSink, timestampSeconds);
        }

        Backend?.DestroyOverlay(panelId);
        return true;
    }

    /// <summary>
    /// Resolves world poses and inherited visibility across all parent-child attached panels with cycle protection.
    /// </summary>
    public void ResolveAttachedPoses()
    {
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < panelOrder.Count; i++)
        {
            ResolvePanelNode(panelOrder[i], resolved, visiting);
        }
    }

    /// <summary>
    /// Executes one complete Layer 1 frame:
    /// resolves 3D panel attachments, debounces each hand's actions, arbitrates rays,
    /// routes multi-pointer events to panel sinks, and synchronizes state to <see cref="Backend"/>.
    /// </summary>
    public void StepFrame(IReadOnlyList<(int PointerId, VrHandRawSample Sample)> hands, double timestampSeconds = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(hands);

        ResolveAttachedPoses();

        targetBuffer.Clear();
        for (int i = 0; i < panelOrder.Count; i++)
        {
            var entry = panels[panelOrder[i]];
            targetBuffer.Add(new SpatialPanelTarget(
                entry.Surface.Id,
                entry.ResolvedWorldPose,
                entry.Surface.WidthMeters,
                entry.Surface.HeightMeters,
                entry.Surface.SortOrder,
                entry.Curvature,
                entry.ResolvedVisible,
                entry.EnableSeamGuard,
                entry.LayerPriority,
                entry.OverlayHandle));
        }

        for (int i = 0; i < hands.Count; i++)
        {
            var (pointerId, rawSample) = hands[i];
            var tracked = HandTracker.Update(pointerId, rawSample);
            double ts = rawSample.TimestampSeconds != 0 ? rawSample.TimestampSeconds : timestampSeconds;

            if (!tracked.IsValid || !tracked.IsBound)
            {
                Arbiter.UpdatePointer(pointerId, default, isValid: false, isPressed: false, targetBuffer);

                if (tracked.ShouldCancel ||
                    Router.HasActiveCapture(pointerId) ||
                    Router.GetHoveredPanel(pointerId) is not null)
                {
                    Router.CancelPointer(pointerId, ts);
                }

                continue;
            }

            var ray = VrRay.FromPose(tracked.AimPose);
            var hit = Arbiter.UpdatePointer(pointerId, ray, isValid: true, isPressed: tracked.TriggerPressed, targetBuffer);

            IVrPanelInputSink? hitSink = null;
            if (hit is { } found && panels.TryGetValue(found.PanelId, out var hitEntry))
            {
                hitSink = hitEntry.InputSink;
            }

            Router.UpdatePointer(
                pointerId,
                hitSink,
                hit?.Uv ?? default,
                tracked.TriggerPressed,
                VrPointerButton.Left,
                tracked.ScrollDelta,
                ts);
        }

        SyncOverlaysToBackend();
    }

    /// <summary>
    /// Pushes pose, dimensions, alpha, curvature, sort order, visibility, and shared textures to <see cref="Backend"/>.
    /// </summary>
    public void SyncOverlaysToBackend()
    {
        if (Backend is null || !Backend.Driver.IsAvailable)
        {
            return;
        }

        for (int i = 0; i < panelOrder.Count; i++)
        {
            var entry = panels[panelOrder[i]];
            var surface = entry.Surface;

            if (entry.OverlayHandle == 0)
            {
                entry.OverlayHandle = Backend.EnsureOverlay(surface.Id, surface.Id, surface.SortOrder);
            }

            Backend.SetSortOrder(surface.Id, surface.SortOrder);
            Backend.SetPixelSize(surface.Id, surface.PixelWidth, surface.PixelHeight);
            Backend.SetWidthMeters(surface.Id, surface.WidthMeters);
            Backend.SetAlpha(surface.Id, entry.Alpha);
            Backend.SetCurvature(surface.Id, entry.Curvature);
            Backend.SetTransform(surface.Id, entry.ResolvedWorldPose);

            if (entry.SharedTextureProvider is not null)
            {
                nint ptr = entry.SharedTextureProvider();
                if (ptr != 0)
                {
                    Backend.SubmitD3D11Texture(surface.Id, ptr);
                }
            }

            Backend.SetVisible(surface.Id, entry.ResolvedVisible);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var entry in panels.Values)
        {
            if (entry.InputSink is not null)
            {
                Router.RemovePanel(entry.InputSink);
            }

            Backend?.DestroyOverlay(entry.Surface.Id);
        }

        panels.Clear();
        panelOrder.Clear();
        targetBuffer.Clear();
        Arbiter.Reset();
        HandTracker.ResetAll();

        if (ownsBackend)
        {
            Backend?.Dispose();
        }
    }

    private void ResolvePanelNode(string panelId, HashSet<string> resolved, HashSet<string> visiting)
    {
        if (resolved.Contains(panelId) || !panels.TryGetValue(panelId, out var entry))
        {
            return;
        }

        if (!visiting.Add(panelId))
        {
            // Cycle detected in attachment graph: fall back to own pose and hide to prevent infinite recursion.
            entry.ResolvedWorldPose = entry.Surface.Pose;
            entry.ResolvedVisible = false;
            resolved.Add(panelId);
            return;
        }

        if (entry.Attachment is { } attachment && panels.TryGetValue(attachment.ParentId, out var parent))
        {
            ResolvePanelNode(attachment.ParentId, resolved, visiting);
            var parentNode = new OverlayNodePose(
                parent.ResolvedWorldPose,
                parent.Surface.WidthMeters,
                parent.Surface.HeightMeters,
                parent.Curvature,
                parent.Alpha,
                parent.Surface.SortOrder,
                parent.ResolvedVisible);

            var nextPose = SpatialPoseMath.ResolveAttachedPose(
                parentNode,
                entry.Surface.WidthMeters,
                entry.Surface.HeightMeters,
                attachment);

            entry.ResolvedWorldPose = nextPose;
            entry.ResolvedVisible = entry.Surface.IsVisible && parent.ResolvedVisible;
            entry.OnResolvedPoseChanged?.Invoke(nextPose);
        }
        else
        {
            entry.ResolvedWorldPose = entry.Surface.Pose;
            entry.ResolvedVisible = entry.Surface.IsVisible;
        }

        visiting.Remove(panelId);
        resolved.Add(panelId);
    }
}
