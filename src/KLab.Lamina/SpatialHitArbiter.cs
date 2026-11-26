// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina;

/// <summary>
/// Authoritative per-pointer spatial raycast result produced by <see cref="SpatialHitArbiter"/>.
/// </summary>
public readonly record struct SpatialHit(
    string PanelId,
    Vector2 Uv,
    float Distance,
    uint SortOrder,
    int LayerPriority = 0,
    bool SeamLocked = false,
    bool IsCaptured = false);

/// <summary>
/// Single authoritative per-pointer spatial raycast arbiter across all interactive 3D overlay panels.
/// Enforces:
/// 1. First-frame deterministic hit resolution shared by all downstream consumers;
/// 2. Coplanar depth arbitration (<see cref="CoplanarDepthToleranceMeters"/> = 2.5 cm) by <c>SortOrder</c>
///    to prevent Z-fighting flicker;
/// 3. Seam anti-penetration guard (<see cref="SeamGuardMarginMeters"/> = 6 mm) around foreground panels;
/// 4. Per-pointer press-to-release capture lock isolated by <c>pointerId</c>.
/// </summary>
public sealed class SpatialHitArbiter
{
    public const float CoplanarDepthToleranceMeters = 0.025f;
    public const float SeamGuardMarginMeters = 0.006f;

    private sealed class PointerArbitrationState
    {
        public SpatialHit? CurrentHit;
        public SpatialHit? PreviousHit;
        public string? CapturedPanelId;
        public Vector2 LastCapturedUv;
        public float LastCapturedDistance;
        public bool WasPressed;
    }

    private readonly IOverlayRaycaster raycaster;
    private readonly Dictionary<int, PointerArbitrationState> pointers = new();

    public SpatialHitArbiter(IOverlayRaycaster? raycaster = null)
    {
        this.raycaster = raycaster ?? GeometricOverlayRaycaster.Instance;
    }

    public float CoplanarToleranceMeters { get; set; } = CoplanarDepthToleranceMeters;

    public float SeamMarginMeters { get; set; } = SeamGuardMarginMeters;

    public SpatialHit? GetCurrentHit(int pointerId) =>
        pointers.TryGetValue(pointerId, out var state) ? state.CurrentHit : null;

    public bool IsCaptured(int pointerId) =>
        pointers.TryGetValue(pointerId, out var state) && state.CapturedPanelId is not null;

    public string? GetCapturedPanelId(int pointerId) =>
        pointers.TryGetValue(pointerId, out var state) ? state.CapturedPanelId : null;

    public void ReleaseCapture(int pointerId)
    {
        if (pointers.TryGetValue(pointerId, out var state))
        {
            state.CapturedPanelId = null;
        }
    }

    public void ResetPointer(int pointerId)
    {
        if (pointers.TryGetValue(pointerId, out var state))
        {
            state.CurrentHit = null;
            state.PreviousHit = null;
            state.CapturedPanelId = null;
            state.WasPressed = false;
        }
    }

    public void Reset() => pointers.Clear();

    /// <summary>
    /// Resolves one frame of spatial raycasting and capture ownership for <paramref name="pointerId"/>.
    /// </summary>
    public SpatialHit? UpdatePointer(
        int pointerId,
        in VrRay ray,
        bool isValid,
        bool isPressed,
        IReadOnlyList<SpatialPanelTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var state = GetOrCreate(pointerId);
        if (!isValid)
        {
            state.CapturedPanelId = null;
            state.CurrentHit = null;
            state.PreviousHit = null;
            state.WasPressed = false;
            return null;
        }

        if (!isPressed)
        {
            state.CapturedPanelId = null;
        }

        if (state.CapturedPanelId is { } capturedId)
        {
            var locked = ResolveCaptured(state, capturedId, ray, targets);
            if (locked is not null)
            {
                state.CurrentHit = locked;
                state.PreviousHit = locked;
                state.WasPressed = isPressed;
                return locked;
            }

            // Captured panel disappeared or became hidden; drop capture cleanly.
            state.CapturedPanelId = null;
        }

        var hit = ResolveRay(ray, targets, state.PreviousHit);
        state.CurrentHit = hit;

        if (isPressed && !state.WasPressed && hit is { } found)
        {
            state.CapturedPanelId = found.PanelId;
            state.LastCapturedUv = found.Uv;
            state.LastCapturedDistance = found.Distance;
            var capturedHit = found with { IsCaptured = true };
            state.CurrentHit = capturedHit;
            state.PreviousHit = capturedHit;
            state.WasPressed = isPressed;
            return capturedHit;
        }

        state.PreviousHit = hit;
        state.WasPressed = isPressed;
        return hit;
    }

    /// <summary>
    /// Performs a stateless single-ray arbitration across <paramref name="targets"/> using coplanar
    /// sort-order resolution and foreground seam-guard anti-penetration.
    /// </summary>
    public SpatialHit? ResolveRay(
        in VrRay ray,
        IReadOnlyList<SpatialPanelTarget> targets,
        SpatialHit? previousHit = null)
    {
        ArgumentNullException.ThrowIfNull(targets);

        List<SpatialHit>? direct = null;
        List<SpatialHit>? seam = null;

        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!target.IsVisible || target.WidthMeters <= 0f || target.HeightMeters <= 0f)
            {
                continue;
            }

            if (raycaster.TryIntersect(ray, target, out var directHit))
            {
                direct ??= new List<SpatialHit>();
                direct.Add(new SpatialHit(
                    target.PanelId,
                    directHit.Uv,
                    directHit.Distance,
                    target.SortOrder,
                    target.LayerPriority,
                    SeamLocked: false,
                    IsCaptured: false));
            }
            else if (target.EnableSeamGuard &&
                     SeamMarginMeters > 0f &&
                     raycaster.TryIntersectSeam(ray, target, SeamMarginMeters, out var seamHit))
            {
                seam ??= new List<SpatialHit>();
                seam.Add(new SpatialHit(
                    target.PanelId,
                    seamHit.Uv,
                    seamHit.Distance,
                    target.SortOrder,
                    target.LayerPriority,
                    SeamLocked: true,
                    IsCaptured: false));
            }
        }

        var bestDirect = direct is not null ? SelectBest(direct, CoplanarToleranceMeters) : null;
        if (seam is null)
        {
            return bestDirect;
        }

        var bestSeam = SelectBest(seam, CoplanarToleranceMeters);
        if (bestSeam is not { } seamCandidate)
        {
            return bestDirect;
        }

        if (bestDirect is not { } directCandidate)
        {
            // Keep edge micro-jitter locked only when we were already hovering this foreground panel.
            return previousHit is { } prev && string.Equals(prev.PanelId, seamCandidate.PanelId, StringComparison.Ordinal)
                ? seamCandidate
                : null;
        }

        // Anti-penetration seam lock: if the ray slipped <= SeamMarginMeters off a foreground panel
        // onto a background panel behind or coplanar with it, keep the foreground panel in front.
        bool foregroundOverBackground =
            seamCandidate.Distance <= directCandidate.Distance + CoplanarToleranceMeters &&
            (seamCandidate.SortOrder > directCandidate.SortOrder ||
             seamCandidate.Distance < directCandidate.Distance - CoplanarToleranceMeters ||
             seamCandidate.LayerPriority > directCandidate.LayerPriority);

        return foregroundOverBackground ? seamCandidate : directCandidate;
    }

    /// <summary>
    /// Deterministic, transitive selection: finds the nearest distance, then within
    /// <paramref name="coplanarToleranceMeters"/> selects the highest <c>SortOrder</c>
    /// (breaking ties by smallest distance, highest <c>LayerPriority</c>, then <c>PanelId</c>).
    /// </summary>
    public static SpatialHit? SelectBest(
        IReadOnlyList<SpatialHit> candidates,
        float coplanarToleranceMeters = CoplanarDepthToleranceMeters)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        float minDist = float.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Distance < minDist)
            {
                minDist = candidates[i].Distance;
            }
        }

        float coplanarLimit = minDist + MathF.Max(0f, coplanarToleranceMeters);
        SpatialHit best = default;
        bool hasBest = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate.Distance > coplanarLimit)
            {
                continue;
            }

            if (!hasBest || IsBetterInCoplanarBand(candidate, best))
            {
                best = candidate;
                hasBest = true;
            }
        }

        return hasBest ? best : null;
    }

    private SpatialHit? ResolveCaptured(
        PointerArbitrationState state,
        string capturedPanelId,
        in VrRay ray,
        IReadOnlyList<SpatialPanelTarget> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!string.Equals(target.PanelId, capturedPanelId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!target.IsVisible || target.WidthMeters <= 0f || target.HeightMeters <= 0f)
            {
                return null;
            }

            if (raycaster.TryIntersect(ray, target, out var directHit))
            {
                state.LastCapturedUv = directHit.Uv;
                state.LastCapturedDistance = directHit.Distance;
            }
            else if (raycaster.TryProjectToPlane(ray, target, out var projectedHit))
            {
                state.LastCapturedUv = projectedHit.Uv;
                state.LastCapturedDistance = projectedHit.Distance;
            }

            return new SpatialHit(
                target.PanelId,
                state.LastCapturedUv,
                state.LastCapturedDistance,
                target.SortOrder,
                target.LayerPriority,
                SeamLocked: false,
                IsCaptured: true);
        }

        return null;
    }

    private static bool IsBetterInCoplanarBand(in SpatialHit candidate, in SpatialHit current)
    {
        if (candidate.SortOrder != current.SortOrder)
        {
            return candidate.SortOrder > current.SortOrder;
        }

        if (MathF.Abs(candidate.Distance - current.Distance) > 1e-5f)
        {
            return candidate.Distance < current.Distance;
        }

        if (candidate.LayerPriority != current.LayerPriority)
        {
            return candidate.LayerPriority > current.LayerPriority;
        }

        return string.CompareOrdinal(candidate.PanelId, current.PanelId) < 0;
    }

    private PointerArbitrationState GetOrCreate(int pointerId)
    {
        if (!pointers.TryGetValue(pointerId, out var state))
        {
            state = new PointerArbitrationState();
            pointers[pointerId] = state;
        }

        return state;
    }
}
