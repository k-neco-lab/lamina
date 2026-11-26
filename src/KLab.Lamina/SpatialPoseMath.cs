// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using Valve.VR;

namespace KLab.Lamina;

/// <summary>
/// Pure right-handed 3D spatial math for SteamVR overlays.
/// System.Numerics uses row-vector matrices (point * Matrix, child = local * parent),
/// while OpenVR uses column-vector 3x4 matrices (<see cref="HmdMatrix34_t"/>).
/// </summary>
public static class SpatialPoseMath
{
    public const float DefaultMinRayDistanceMeters = 0.02f;
    public const float DefaultMaxRayDistanceMeters = 8.0f;
    private const float BackFaceEpsilon = 0.0001f;
    private const float CurvatureEpsilon = 1e-4f;

    /// <summary>
    /// Losslessly converts an OpenVR column-vector 3x4 pose matrix to a row-vector <see cref="Matrix4x4"/>.
    /// </summary>
    public static Matrix4x4 FromOpenVr(in HmdMatrix34_t m) =>
        new(
            m.m0, m.m4, m.m8, 0f,
            m.m1, m.m5, m.m9, 0f,
            m.m2, m.m6, m.m10, 0f,
            m.m3, m.m7, m.m11, 1f);

    /// <summary>
    /// Losslessly converts a row-vector <see cref="Matrix4x4"/> to an OpenVR column-vector 3x4 pose matrix.
    /// </summary>
    public static HmdMatrix34_t ToOpenVr(in Matrix4x4 m) => new()
    {
        m0 = m.M11,
        m1 = m.M21,
        m2 = m.M31,
        m3 = m.M41,
        m4 = m.M12,
        m5 = m.M22,
        m6 = m.M32,
        m7 = m.M42,
        m8 = m.M13,
        m9 = m.M23,
        m10 = m.M33,
        m11 = m.M43,
    };

    /// <summary>
    /// Composes a child's world pose from a parent's world pose and a local transform (row-vector order: local * parent).
    /// </summary>
    public static Matrix4x4 ComposeChildPose(in Matrix4x4 parentPose, in Matrix4x4 localTransform) =>
        localTransform * parentPose;

    /// <summary>
    /// Creates a pose offset in the parent's local coordinate frame.
    /// </summary>
    public static Matrix4x4 CreateLocalOffset(in Matrix4x4 parentPose, Vector3 localOffset) =>
        Matrix4x4.CreateTranslation(localOffset) * parentPose;

    /// <summary>
    /// Creates an upright pose at <paramref name="position"/> whose local +Z normal faces <paramref name="eyePosition"/>.
    /// </summary>
    public static Matrix4x4 CreateFacingPose(Vector3 position, Vector3 eyePosition)
    {
        var toEye = eyePosition - position;
        var z = toEye.LengthSquared() < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(toEye);
        var x = Vector3.Cross(Vector3.UnitY, z);
        x = x.LengthSquared() < 1e-4f ? Vector3.UnitX : Vector3.Normalize(x);
        var y = Vector3.Cross(z, x);
        return new Matrix4x4(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            position.X, position.Y, position.Z, 1f);
    }

    /// <summary>
    /// Smoothly interpolates between two rigid poses (spherical rotation + linear translation).
    /// </summary>
    public static Matrix4x4 InterpolatePose(in Matrix4x4 from, in Matrix4x4 to, float t)
    {
        var clampedT = Math.Clamp(t, 0f, 1f);
        var rotation = Quaternion.Slerp(
            Quaternion.CreateFromRotationMatrix(from),
            Quaternion.CreateFromRotationMatrix(to),
            clampedT);
        var pose = Matrix4x4.CreateFromQuaternion(rotation);
        pose.Translation = Vector3.Lerp(from.Translation, to.Translation, clampedT);
        return pose;
    }

    /// <summary>
    /// Extracts the world-space ray origin and unit forward direction (local -Z) from an aim pose.
    /// </summary>
    public static (Vector3 Origin, Vector3 Direction) ExtractRay(in Matrix4x4 aimPose)
    {
        var forward = Vector3.TransformNormal(-Vector3.UnitZ, aimPose);
        var direction = forward.LengthSquared() < 1e-12f ? -Vector3.UnitZ : Vector3.Normalize(forward);
        return (aimPose.Translation, direction);
    }

    /// <summary>
    /// Computes the local-space center point of a panel edge.
    /// </summary>
    public static Vector3 GetEdgeCenter(float widthMeters, float heightMeters, OverlayAttachEdge edge) => edge switch
    {
        OverlayAttachEdge.Left => new Vector3(-widthMeters * 0.5f, 0f, 0f),
        OverlayAttachEdge.Right => new Vector3(widthMeters * 0.5f, 0f, 0f),
        OverlayAttachEdge.Top => new Vector3(0f, heightMeters * 0.5f, 0f),
        _ => new Vector3(0f, -heightMeters * 0.5f, 0f),
    };

    /// <summary>
    /// Returns the opposite panel edge used as the child's hinge contact edge.
    /// </summary>
    public static OverlayAttachEdge GetOppositeEdge(OverlayAttachEdge edge) => edge switch
    {
        OverlayAttachEdge.Left => OverlayAttachEdge.Right,
        OverlayAttachEdge.Right => OverlayAttachEdge.Left,
        OverlayAttachEdge.Top => OverlayAttachEdge.Bottom,
        _ => OverlayAttachEdge.Top,
    };

    /// <summary>
    /// Computes the world pose of a child panel attached to a parent panel edge or via an explicit local transform.
    /// </summary>
    public static Matrix4x4 ResolveAttachedPose(
        in OverlayNodePose parent,
        float childWidthMeters,
        float childHeightMeters,
        in OverlayAttachment attachment)
    {
        if (attachment.LocalTransform is { } localTransform)
        {
            return ComposeChildPose(parent.WorldPose, localTransform);
        }

        var edge = attachment.Edge ?? OverlayAttachEdge.Bottom;
        return ComputeAttachedEdgePose(
            parent,
            childWidthMeters,
            childHeightMeters,
            edge,
            attachment.AlongNormalized,
            attachment.EdgeGapMeters,
            attachment.TiltDegrees,
            attachment.NormalOffsetMeters);
    }

    /// <summary>
    /// Computes the world pose of a child panel snapped to one edge of a parent panel with optional gap,
    /// sliding alignment along the edge, hinge tilt, and normal offset.
    /// </summary>
    public static Matrix4x4 ComputeAttachedEdgePose(
        in OverlayNodePose parent,
        float childWidthMeters,
        float childHeightMeters,
        OverlayAttachEdge edge,
        float alongNormalized = 0f,
        float edgeGapMeters = 0.012f,
        float tiltDegrees = 0f,
        float normalOffsetMeters = 0f)
    {
        bool verticalEdge = edge is OverlayAttachEdge.Left or OverlayAttachEdge.Right;
        float angleRad = tiltDegrees * (MathF.PI / 180f);
        var rotation = verticalEdge
            ? Matrix4x4.CreateRotationY(angleRad)
            : Matrix4x4.CreateRotationX(angleRad);

        var parentEdge = GetEdgeCenter(parent.WidthMeters, parent.HeightMeters, edge);
        var edgeDir = parentEdge.LengthSquared() > 1e-12f ? Vector3.Normalize(parentEdge) : Vector3.Zero;
        parentEdge += edgeDir * edgeGapMeters;

        float along = Math.Clamp(alongNormalized, -1f, 1f);
        if (verticalEdge)
        {
            parentEdge.Y = along * (parent.HeightMeters - childHeightMeters) * 0.5f;
        }
        else
        {
            parentEdge.X = along * (parent.WidthMeters - childWidthMeters) * 0.5f;
        }

        parentEdge.Z += normalOffsetMeters;

        var childHinge = GetEdgeCenter(childWidthMeters, childHeightMeters, GetOppositeEdge(edge));
        var childCenter = parentEdge - Vector3.TransformNormal(childHinge, rotation);
        var local = rotation * Matrix4x4.CreateTranslation(childCenter);
        return ComposeChildPose(parent.WorldPose, local);
    }

    /// <summary>
    /// Intersects a world-space ray with the local z = 0 plane of <paramref name="panelPose"/>.
    /// Rejects rays approaching from behind the panel (+Z is front normal).
    /// </summary>
    public static bool TryIntersectPlane(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        in Matrix4x4 panelPose,
        out Vector2 localHit,
        out float distance,
        float minDistanceMeters = DefaultMinRayDistanceMeters,
        float maxDistanceMeters = DefaultMaxRayDistanceMeters)
    {
        localHit = default;
        distance = 0f;
        if (!Matrix4x4.Invert(panelPose, out var inverse))
        {
            return false;
        }

        var localOrigin = Vector3.Transform(rayOrigin, inverse);
        var localDir = Vector3.TransformNormal(rayDirection, inverse);
        if (localDir.Z >= -BackFaceEpsilon)
        {
            return false;
        }

        distance = -localOrigin.Z / localDir.Z;
        if (distance < minDistanceMeters || distance > maxDistanceMeters)
        {
            return false;
        }

        var hitPoint = localOrigin + localDir * distance;
        localHit = new Vector2(hitPoint.X, hitPoint.Y);
        return true;
    }

    /// <summary>
    /// Intersects a world-space ray with a flat or cylindrically curved overlay panel.
    /// UV is (0, 0) at top-left and (1, 1) at bottom-right.
    /// </summary>
    public static bool TryIntersectQuad(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        in Matrix4x4 panelPose,
        float widthMeters,
        float heightMeters,
        out Vector2 uv,
        out float distance,
        float curvature = 0f,
        float minDistanceMeters = DefaultMinRayDistanceMeters,
        float maxDistanceMeters = DefaultMaxRayDistanceMeters)
    {
        uv = default;
        distance = 0f;
        if (widthMeters <= 0f || heightMeters <= 0f)
        {
            return false;
        }

        float clampedCurvature = Math.Clamp(curvature, 0f, 1f);
        if (clampedCurvature > CurvatureEpsilon)
        {
            return TryIntersectCurvedQuad(
                rayOrigin,
                rayDirection,
                panelPose,
                widthMeters,
                heightMeters,
                clampedCurvature,
                out uv,
                out distance,
                minDistanceMeters,
                maxDistanceMeters);
        }

        if (!TryIntersectPlane(rayOrigin, rayDirection, panelPose, out var local, out distance, minDistanceMeters, maxDistanceMeters))
        {
            return false;
        }

        uv = new Vector2(local.X / widthMeters + 0.5f, 0.5f - local.Y / heightMeters);
        return uv.X >= 0f && uv.X <= 1f && uv.Y >= 0f && uv.Y <= 1f;
    }

    /// <summary>
    /// Intersects a ray with an expanded seam border around a flat overlay panel, clamping the resulting UV to [0, 1].
    /// </summary>
    public static bool TryIntersectSeam(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        in Matrix4x4 panelPose,
        float widthMeters,
        float heightMeters,
        float seamMarginMeters,
        out Vector2 clampedUv,
        out float distance,
        float minDistanceMeters = DefaultMinRayDistanceMeters,
        float maxDistanceMeters = DefaultMaxRayDistanceMeters)
    {
        clampedUv = default;
        distance = 0f;
        if (widthMeters <= 0f || heightMeters <= 0f || seamMarginMeters <= 0f)
        {
            return false;
        }

        if (!TryIntersectPlane(rayOrigin, rayDirection, panelPose, out var local, out distance, minDistanceMeters, maxDistanceMeters))
        {
            return false;
        }

        float halfW = widthMeters * 0.5f;
        float halfH = heightMeters * 0.5f;
        if (MathF.Abs(local.X) > halfW + seamMarginMeters || MathF.Abs(local.Y) > halfH + seamMarginMeters)
        {
            return false;
        }

        clampedUv = new Vector2(
            Math.Clamp(local.X / widthMeters + 0.5f, 0f, 1f),
            Math.Clamp(0.5f - local.Y / heightMeters, 0f, 1f));
        return true;
    }

    /// <summary>
    /// Projects a ray onto the panel plane and clamps the UV into [0, 1] (used during active pointer capture).
    /// </summary>
    public static bool TryProjectClamped(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        in Matrix4x4 panelPose,
        float widthMeters,
        float heightMeters,
        out Vector2 clampedUv,
        out float distance,
        float minDistanceMeters = DefaultMinRayDistanceMeters,
        float maxDistanceMeters = DefaultMaxRayDistanceMeters)
    {
        clampedUv = default;
        distance = 0f;
        if (widthMeters <= 0f || heightMeters <= 0f)
        {
            return false;
        }

        if (!TryIntersectPlane(rayOrigin, rayDirection, panelPose, out var local, out distance, minDistanceMeters, maxDistanceMeters))
        {
            return false;
        }

        clampedUv = new Vector2(
            Math.Clamp(local.X / widthMeters + 0.5f, 0f, 1f),
            Math.Clamp(0.5f - local.Y / heightMeters, 0f, 1f));
        return true;
    }

    private static bool TryIntersectCurvedQuad(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        in Matrix4x4 panelPose,
        float widthMeters,
        float heightMeters,
        float curvature,
        out Vector2 uv,
        out float distance,
        float minDistanceMeters,
        float maxDistanceMeters)
    {
        uv = default;
        distance = 0f;
        if (!Matrix4x4.Invert(panelPose, out var inverse))
        {
            return false;
        }

        var localOrigin = Vector3.Transform(rayOrigin, inverse);
        var localDir = Vector3.TransformNormal(rayDirection, inverse);

        // OpenVR cylindrical curvature: arc span theta = 2*pi*curvature, radius R = width / theta,
        // cylinder axis parallel to +Y through (0, 0, +R) so the center (0, 0, 0) is tangent to z = 0
        // and the edges curve toward +Z (toward the viewer).
        float arcAngle = 2f * MathF.PI * curvature;
        float radius = widthMeters / arcAngle;
        float halfArc = arcAngle * 0.5f;
        float halfH = heightMeters * 0.5f;

        float ox = localOrigin.X;
        float oz = localOrigin.Z - radius;
        float dx = localDir.X;
        float dz = localDir.Z;

        float a = dx * dx + dz * dz;
        if (a < 1e-10f)
        {
            return false;
        }

        float b = 2f * (ox * dx + oz * dz);
        float c = ox * ox + oz * oz - radius * radius;
        float disc = b * b - 4f * a * c;
        if (disc < 0f)
        {
            return false;
        }

        float sqrtDisc = MathF.Sqrt(disc);
        float inv2a = 0.5f / a;
        float t0 = (-b - sqrtDisc) * inv2a;
        float t1 = (-b + sqrtDisc) * inv2a;

        // Evaluate candidate intersections from nearest to farthest; keep the first front-facing hit inside the arc.
        Span<float> candidates = stackalloc float[2];
        candidates[0] = MathF.Min(t0, t1);
        candidates[1] = MathF.Max(t0, t1);

        for (int i = 0; i < 2; i++)
        {
            float t = candidates[i];
            if (t < minDistanceMeters || t > maxDistanceMeters)
            {
                continue;
            }

            var hit = localOrigin + localDir * t;
            if (MathF.Abs(hit.Y) > halfH)
            {
                continue;
            }

            // Inner (front) normal points toward the cylinder axis (0, hit.Y, R).
            var inwardNormal = new Vector3(-hit.X, 0f, radius - hit.Z);
            if (Vector3.Dot(localDir, inwardNormal) >= -BackFaceEpsilon * radius)
            {
                continue;
            }

            float phi = MathF.Atan2(hit.X, radius - hit.Z);
            if (MathF.Abs(phi) > halfArc)
            {
                continue;
            }

            uv = new Vector2(0.5f + phi / arcAngle, 0.5f - hit.Y / heightMeters);
            distance = t;
            return true;
        }

        return false;
    }
}
