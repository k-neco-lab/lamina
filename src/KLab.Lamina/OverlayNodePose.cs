// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina;

/// <summary>
/// Edge of a parent overlay panel where a child panel can be attached in 3D space.
/// </summary>
public enum OverlayAttachEdge
{
    Left = 0,
    Right = 1,
    Top = 2,
    Bottom = 3,
}

/// <summary>
/// Spatial pose and visual geometry of a 3D overlay panel node.
/// </summary>
public readonly record struct OverlayNodePose(
    Matrix4x4 WorldPose,
    float WidthMeters,
    float HeightMeters,
    float Curvature = 0f,
    float Alpha = 1f,
    uint SortOrder = 0,
    bool IsVisible = true);

/// <summary>
/// Describes how a child overlay panel attaches to a parent overlay panel or controller pose.
/// Supports either edge-hinge docking (e.g. a toolbar snapped to a panel's bottom/side edge)
/// or an explicit local transform offset (e.g. a wrist dial bound to a controller pose).
/// </summary>
public readonly record struct OverlayAttachment(
    string ParentId,
    OverlayAttachEdge? Edge = null,
    float AlongNormalized = 0f,
    float EdgeGapMeters = 0.012f,
    float TiltDegrees = 0f,
    float NormalOffsetMeters = 0f,
    Matrix4x4? LocalTransform = null)
{
    /// <summary>
    /// Creates an edge-snapped parent-child panel attachment (e.g. toolbar under main panel).
    /// </summary>
    public static OverlayAttachment ToEdge(
        string parentId,
        OverlayAttachEdge edge,
        float alongNormalized = 0f,
        float edgeGapMeters = 0.012f,
        float tiltDegrees = 0f,
        float normalOffsetMeters = 0f) =>
        new(parentId, edge, Math.Clamp(alongNormalized, -1f, 1f), edgeGapMeters, tiltDegrees, normalOffsetMeters, null);

    /// <summary>
    /// Creates a rigid local-transform attachment (e.g. wrist panel bound to a controller pose).
    /// </summary>
    public static OverlayAttachment ToLocalTransform(string parentId, Matrix4x4 localTransform) =>
        new(parentId, null, 0f, 0f, 0f, 0f, localTransform);
}
