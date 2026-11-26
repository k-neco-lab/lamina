// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using Valve.VR;
using Xunit;

namespace KLab.Lamina.Tests;

public sealed class SpatialPoseMathTests
{
    [Fact]
    public void OpenVrMatrixRoundTrip_PreservesRotationAndTranslationLosslessly()
    {
        var rotation = Matrix4x4.CreateFromYawPitchRoll(0.45f, -0.32f, 0.18f);
        var original = rotation * Matrix4x4.CreateTranslation(1.25f, 1.62f, -1.85f);

        HmdMatrix34_t vr = SpatialPoseMath.ToOpenVr(original);
        Matrix4x4 restored = SpatialPoseMath.FromOpenVr(vr);

        AssertMatrixClose(original, restored, 1e-6f);

        // Verify column-vector vs row-vector layout: OpenVR stores translation in m3, m7, m11.
        Assert.Equal(original.M41, vr.m3, 6);
        Assert.Equal(original.M42, vr.m7, 6);
        Assert.Equal(original.M43, vr.m11, 6);
    }

    [Fact]
    public void ComputeAttachedEdgePose_BottomToolbarWithGapAndTilt_SnapsToParentEdge()
    {
        var parentPose = Matrix4x4.CreateRotationY(0.5f) * Matrix4x4.CreateTranslation(0f, 1.5f, -1.2f);
        var parent = new OverlayNodePose(parentPose, WidthMeters: 1.0f, HeightMeters: 0.6f);

        const float childW = 0.6f;
        const float childH = 0.12f;
        const float gap = 0.012f;
        const float tiltDeg = -15f;

        var childPose = SpatialPoseMath.ComputeAttachedEdgePose(
            parent,
            childW,
            childH,
            OverlayAttachEdge.Bottom,
            alongNormalized: 0f,
            edgeGapMeters: gap,
            tiltDegrees: tiltDeg);

        // Transform child's top-center edge (0, +childH/2, 0) into parent local space:
        // it must land exactly at parent's bottom edge minus gap: (0, -parentH/2 - gap, 0).
        Assert.True(Matrix4x4.Invert(parentPose, out var invParent));
        var childTopWorld = Vector3.Transform(new Vector3(0f, childH * 0.5f, 0f), childPose);
        var childTopInParent = Vector3.Transform(childTopWorld, invParent);

        Assert.InRange(childTopInParent.X, -1e-5f, 1e-5f);
        Assert.InRange(childTopInParent.Y - (-parent.HeightMeters * 0.5f - gap), -1e-5f, 1e-5f);
        Assert.InRange(childTopInParent.Z, -1e-5f, 1e-5f);
    }

    [Fact]
    public void ResolveAttachedPose_RigidLocalTransform_BindsWristPanelToControllerPose()
    {
        var controllerPose = Matrix4x4.CreateRotationZ(MathF.PI * 0.25f) * Matrix4x4.CreateTranslation(-0.3f, 1.1f, -0.4f);
        var parent = new OverlayNodePose(controllerPose, 0.1f, 0.1f);
        var localOffset = Matrix4x4.CreateTranslation(0.05f, 0.08f, -0.02f);
        var attachment = OverlayAttachment.ToLocalTransform("left-controller", localOffset);

        var wristPose = SpatialPoseMath.ResolveAttachedPose(parent, 0.16f, 0.10f, attachment);
        var expected = localOffset * controllerPose;

        AssertMatrixClose(expected, wristPose, 1e-6f);
    }

    [Fact]
    public void TryIntersectQuad_FlatPanel_ComputesTopLeftUvAndRejectsBackFace()
    {
        var panelPose = Matrix4x4.CreateTranslation(0f, 1.2f, -1.5f);
        var rayOrigin = new Vector3(0.25f, 1.35f, 0f);
        var rayDir = -Vector3.UnitZ;

        bool hit = SpatialPoseMath.TryIntersectQuad(
            rayOrigin,
            rayDir,
            panelPose,
            widthMeters: 1.0f,
            heightMeters: 0.6f,
            out var uv,
            out float distance);

        Assert.True(hit);
        Assert.Equal(1.5f, distance, 5);
        Assert.Equal(0.75f, uv.X, 5);
        Assert.Equal(0.25f, uv.Y, 5);

        // Ray coming from behind the panel (+Z direction) must be rejected.
        bool backHit = SpatialPoseMath.TryIntersectQuad(
            new Vector3(0f, 1.2f, -3.0f),
            Vector3.UnitZ,
            panelPose,
            widthMeters: 1.0f,
            heightMeters: 0.6f,
            out _,
            out _);

        Assert.False(backHit);
    }

    [Fact]
    public void TryIntersectQuad_CurvedPanel_HitsCylindricalArcCloserAtEdgesThanFlatPlane()
    {
        var panelPose = Matrix4x4.CreateTranslation(0f, 1.2f, -1.5f);
        const float width = 1.2f;
        const float height = 0.6f;
        const float curvature = 0.25f;

        // Center ray hits at z = -1.5 (distance = 1.5), UV = (0.5, 0.5).
        bool centerHit = SpatialPoseMath.TryIntersectQuad(
            new Vector3(0f, 1.2f, 0f),
            -Vector3.UnitZ,
            panelPose,
            width,
            height,
            out var centerUv,
            out float centerDist,
            curvature);

        Assert.True(centerHit);
        Assert.Equal(1.5f, centerDist, 4);
        Assert.Equal(0.5f, centerUv.X, 4);
        Assert.Equal(0.5f, centerUv.Y, 4);

        // Off-center ray at x = 0.4m hits the curved cylinder closer than the flat plane (1.5m)
        // because the edges curve toward +Z (toward the viewer).
        bool edgeHit = SpatialPoseMath.TryIntersectQuad(
            new Vector3(0.4f, 1.2f, 0f),
            -Vector3.UnitZ,
            panelPose,
            width,
            height,
            out var edgeUv,
            out float edgeDist,
            curvature);

        Assert.True(edgeHit);
        Assert.True(edgeDist < 1.5f, $"Expected curved edge distance < 1.5m, got {edgeDist}");
        Assert.InRange(edgeUv.X, 0.5f, 1.0f);
        Assert.Equal(0.5f, edgeUv.Y, 4);
    }

    private static void AssertMatrixClose(in Matrix4x4 expected, in Matrix4x4 actual, float epsilon)
    {
        Assert.InRange(MathF.Abs(expected.M11 - actual.M11), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M12 - actual.M12), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M13 - actual.M13), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M14 - actual.M14), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M21 - actual.M21), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M22 - actual.M22), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M23 - actual.M23), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M24 - actual.M24), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M31 - actual.M31), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M32 - actual.M32), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M33 - actual.M33), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M34 - actual.M34), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M41 - actual.M41), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M42 - actual.M42), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M43 - actual.M43), 0f, epsilon);
        Assert.InRange(MathF.Abs(expected.M44 - actual.M44), 0f, epsilon);
    }
}
