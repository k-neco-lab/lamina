// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using Valve.VR;

namespace KLab.Lamina;

/// <summary>
/// World-space ray derived from a VR controller or head aim pose.
/// </summary>
public readonly record struct VrRay(Vector3 Origin, Vector3 Direction, Matrix4x4 AimPose)
{
    public static VrRay FromPose(in Matrix4x4 aimPose)
    {
        var (origin, direction) = SpatialPoseMath.ExtractRay(aimPose);
        return new VrRay(origin, direction, aimPose);
    }
}

/// <summary>
/// Descriptor of an interactive 3D overlay surface submitted to <see cref="SpatialHitArbiter"/>.
/// </summary>
public readonly record struct SpatialPanelTarget(
    string PanelId,
    Matrix4x4 Pose,
    float WidthMeters,
    float HeightMeters,
    uint SortOrder = 0,
    float Curvature = 0f,
    bool IsVisible = true,
    bool EnableSeamGuard = true,
    int LayerPriority = 0,
    ulong OverlayHandle = 0);

/// <summary>
/// Raw intersection result returned by an <see cref="IOverlayRaycaster"/>.
/// </summary>
public readonly record struct OverlayRayHit(Vector2 Uv, float Distance);

/// <summary>
/// Pluggable ray-to-overlay intersection strategy so production code can query OpenVR
/// <c>IVROverlay.ComputeOverlayIntersection</c> while headless tests use exact geometric raycasting.
/// </summary>
public interface IOverlayRaycaster
{
    bool TryIntersect(in VrRay ray, in SpatialPanelTarget target, out OverlayRayHit hit);

    bool TryIntersectSeam(in VrRay ray, in SpatialPanelTarget target, float seamMarginMeters, out OverlayRayHit hit);

    bool TryProjectToPlane(in VrRay ray, in SpatialPanelTarget target, out OverlayRayHit hit);
}

/// <summary>
/// Pure geometric raycaster supporting both flat and cylindrically curved panels without requiring SteamVR.
/// </summary>
public sealed class GeometricOverlayRaycaster : IOverlayRaycaster
{
    public static GeometricOverlayRaycaster Instance { get; } = new();

    public bool TryIntersect(in VrRay ray, in SpatialPanelTarget target, out OverlayRayHit hit)
    {
        if (SpatialPoseMath.TryIntersectQuad(
            ray.Origin,
            ray.Direction,
            target.Pose,
            target.WidthMeters,
            target.HeightMeters,
            out var uv,
            out float distance,
            target.Curvature))
        {
            hit = new OverlayRayHit(uv, distance);
            return true;
        }

        hit = default;
        return false;
    }

    public bool TryIntersectSeam(in VrRay ray, in SpatialPanelTarget target, float seamMarginMeters, out OverlayRayHit hit)
    {
        if (SpatialPoseMath.TryIntersectSeam(
            ray.Origin,
            ray.Direction,
            target.Pose,
            target.WidthMeters,
            target.HeightMeters,
            seamMarginMeters,
            out var uv,
            out float distance))
        {
            hit = new OverlayRayHit(uv, distance);
            return true;
        }

        hit = default;
        return false;
    }

    public bool TryProjectToPlane(in VrRay ray, in SpatialPanelTarget target, out OverlayRayHit hit)
    {
        if (SpatialPoseMath.TryProjectClamped(
            ray.Origin,
            ray.Direction,
            target.Pose,
            target.WidthMeters,
            target.HeightMeters,
            out var uv,
            out float distance))
        {
            hit = new OverlayRayHit(uv, distance);
            return true;
        }

        hit = default;
        return false;
    }
}

/// <summary>
/// Production raycaster backed by OpenVR <c>IVROverlay.ComputeOverlayIntersection</c>, falling back
/// to geometric raycasting when an overlay handle is not bound or for seam/off-panel capture projection.
/// </summary>
public sealed class OpenVrOverlayRaycaster : IOverlayRaycaster
{
    private readonly Func<CVROverlay?> overlayProvider;
    private readonly ETrackingUniverseOrigin trackingOrigin;
    private readonly GeometricOverlayRaycaster fallback = GeometricOverlayRaycaster.Instance;

    public OpenVrOverlayRaycaster(
        Func<CVROverlay?>? overlayProvider = null,
        ETrackingUniverseOrigin trackingOrigin = ETrackingUniverseOrigin.TrackingUniverseStanding)
    {
        this.overlayProvider = overlayProvider ?? (() => OpenVR.Overlay);
        this.trackingOrigin = trackingOrigin;
    }

    public bool TryIntersect(in VrRay ray, in SpatialPanelTarget target, out OverlayRayHit hit)
    {
        var overlay = target.OverlayHandle != 0 ? overlayProvider() : null;
        if (overlay is not null)
        {
            var parameters = new VROverlayIntersectionParams_t
            {
                vSource = new HmdVector3_t { v0 = ray.Origin.X, v1 = ray.Origin.Y, v2 = ray.Origin.Z },
                vDirection = new HmdVector3_t { v0 = ray.Direction.X, v1 = ray.Direction.Y, v2 = ray.Direction.Z },
                eOrigin = trackingOrigin,
            };
            var results = new VROverlayIntersectionResults_t();
            if (overlay.ComputeOverlayIntersection(target.OverlayHandle, ref parameters, ref results))
            {
                // OpenVR overlay UVs have (0,0) at bottom-left; normalize to top-left (0,0).
                var uv = new Vector2(
                    Math.Clamp(results.vUVs.v0, 0f, 1f),
                    Math.Clamp(1f - results.vUVs.v1, 0f, 1f));
                hit = new OverlayRayHit(uv, results.fDistance);
                return true;
            }

            hit = default;
            return false;
        }

        return fallback.TryIntersect(ray, target, out hit);
    }

    public bool TryIntersectSeam(in VrRay ray, in SpatialPanelTarget target, float seamMarginMeters, out OverlayRayHit hit) =>
        fallback.TryIntersectSeam(ray, target, seamMarginMeters, out hit);

    public bool TryProjectToPlane(in VrRay ray, in SpatialPanelTarget target, out OverlayRayHit hit) =>
        fallback.TryProjectToPlane(ray, target, out hit);
}
