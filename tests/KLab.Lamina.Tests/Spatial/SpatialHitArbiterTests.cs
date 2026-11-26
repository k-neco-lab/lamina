// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using Xunit;

namespace KLab.Lamina.Tests;

public sealed class SpatialHitArbiterTests
{
    [Fact]
    public void CoplanarDepthTolerance_Within25mm_HigherSortOrderWins()
    {
        var arbiter = new SpatialHitArbiter();
        var targets = new[]
        {
            // Closer by 2.0 cm (within 2.5 cm coplanar tolerance), lower sort order.
            new SpatialPanelTarget("background", Matrix4x4.CreateTranslation(0f, 1.2f, -1.48f), 1.0f, 0.6f, SortOrder: 10),
            // Slightly further by 2.0 cm, but higher sort order (modal/toolbar).
            new SpatialPanelTarget("foreground", Matrix4x4.CreateTranslation(0f, 1.2f, -1.50f), 1.0f, 0.6f, SortOrder: 60),
        };

        var ray = VrRay.FromPose(Matrix4x4.CreateTranslation(0f, 1.2f, 0f));
        var hit = arbiter.UpdatePointer(0, ray, isValid: true, isPressed: false, targets);

        Assert.NotNull(hit);
        Assert.Equal("foreground", hit.Value.PanelId);
        Assert.Equal(60u, hit.Value.SortOrder);
    }

    [Fact]
    public void CoplanarDepthTolerance_Beyond25mm_CloserPanelWinsRegardlessOfSortOrder()
    {
        var arbiter = new SpatialHitArbiter();
        var targets = new[]
        {
            // Closer by 4.0 cm (> 2.5 cm tolerance), lower sort order.
            new SpatialPanelTarget("front-panel", Matrix4x4.CreateTranslation(0f, 1.2f, -1.46f), 1.0f, 0.6f, SortOrder: 10),
            // Further by 4.0 cm, higher sort order.
            new SpatialPanelTarget("far-modal", Matrix4x4.CreateTranslation(0f, 1.2f, -1.50f), 1.0f, 0.6f, SortOrder: 80),
        };

        var ray = VrRay.FromPose(Matrix4x4.CreateTranslation(0f, 1.2f, 0f));
        var hit = arbiter.UpdatePointer(0, ray, isValid: true, isPressed: false, targets);

        Assert.NotNull(hit);
        Assert.Equal("front-panel", hit.Value.PanelId);
    }

    [Fact]
    public void SeamGuardMargin_Slipping4mmOffForegroundEdge_LocksForegroundAndPreventsPenetration()
    {
        var arbiter = new SpatialHitArbiter();
        var targets = new[]
        {
            // Background workspace panel spans x in [-1.0, +1.0].
            new SpatialPanelTarget("workspace", Matrix4x4.CreateTranslation(0f, 1.2f, -1.52f), 2.0f, 1.2f, SortOrder: 10, EnableSeamGuard: false),
            // Foreground toolbar spans x in [-0.25, +0.25].
            new SpatialPanelTarget("toolbar", Matrix4x4.CreateTranslation(0f, 1.2f, -1.50f), 0.50f, 0.20f, SortOrder: 30, EnableSeamGuard: true),
        };

        // Slip 4 mm past toolbar's right edge (+0.25 + 0.004 = +0.254 <= 6 mm seam margin).
        var seamRay = VrRay.FromPose(Matrix4x4.CreateTranslation(0.254f, 1.2f, 0f));
        var seamHit = arbiter.UpdatePointer(0, seamRay, isValid: true, isPressed: false, targets);

        Assert.NotNull(seamHit);
        Assert.Equal("toolbar", seamHit.Value.PanelId);
        Assert.True(seamHit.Value.SeamLocked);
        Assert.Equal(1.0f, seamHit.Value.Uv.X, 4);

        // Slip 10 mm past toolbar's right edge (+0.25 + 0.010 = +0.260 > 6 mm seam margin): penetrates to workspace.
        var outsideRay = VrRay.FromPose(Matrix4x4.CreateTranslation(0.260f, 1.2f, 0f));
        var bgHit = arbiter.UpdatePointer(0, outsideRay, isValid: true, isPressed: false, targets);

        Assert.NotNull(bgHit);
        Assert.Equal("workspace", bgHit.Value.PanelId);
        Assert.False(bgHit.Value.SeamLocked);
    }

    [Fact]
    public void DualHands_CaptureDifferentPanels_LocksAreFullyIsolated()
    {
        var arbiter = new SpatialHitArbiter();
        var targets = new[]
        {
            new SpatialPanelTarget("panel-a", Matrix4x4.CreateTranslation(-0.6f, 1.2f, -1.5f), 0.8f, 0.5f, SortOrder: 20),
            new SpatialPanelTarget("panel-b", Matrix4x4.CreateTranslation(0.6f, 1.2f, -1.5f), 0.8f, 0.5f, SortOrder: 20),
        };

        var leftOnA = VrRay.FromPose(Matrix4x4.CreateTranslation(-0.6f, 1.2f, 0f));
        var rightOnB = VrRay.FromPose(Matrix4x4.CreateTranslation(0.6f, 1.2f, 0f));

        // Frame 1: Left presses on A, Right hovers B.
        var l1 = arbiter.UpdatePointer(0, leftOnA, isValid: true, isPressed: true, targets);
        var r1 = arbiter.UpdatePointer(1, rightOnB, isValid: true, isPressed: false, targets);

        Assert.Equal("panel-a", l1?.PanelId);
        Assert.True(arbiter.IsCaptured(0));
        Assert.Equal("panel-b", r1?.PanelId);
        Assert.False(arbiter.IsCaptured(1));

        // Frame 2: Left drags all the way over onto B while still pressed; Right presses B.
        var l2 = arbiter.UpdatePointer(0, rightOnB, isValid: true, isPressed: true, targets);
        var r2 = arbiter.UpdatePointer(1, rightOnB, isValid: true, isPressed: true, targets);

        // Left remains locked to panel-a (clamped at right edge u=1), while Right captures panel-b!
        Assert.Equal("panel-a", l2?.PanelId);
        Assert.True(l2?.IsCaptured);
        Assert.Equal(1.0f, l2!.Value.Uv.X, 4);
        Assert.Equal("panel-b", r2?.PanelId);
        Assert.True(r2?.IsCaptured);
        Assert.Equal("panel-a", arbiter.GetCapturedPanelId(0));
        Assert.Equal("panel-b", arbiter.GetCapturedPanelId(1));
    }

    [Fact]
    public void DualHands_CaptureSamePanelSimultaneously_ReleasingOneKeepsOtherCaptured()
    {
        var arbiter = new SpatialHitArbiter();
        var targets = new[]
        {
            new SpatialPanelTarget("shared-panel", Matrix4x4.CreateTranslation(0f, 1.2f, -1.5f), 1.0f, 0.6f, SortOrder: 30),
        };

        var leftRay = VrRay.FromPose(Matrix4x4.CreateTranslation(-0.2f, 1.2f, 0f));
        var rightRay = VrRay.FromPose(Matrix4x4.CreateTranslation(0.2f, 1.2f, 0f));

        // Both hands press simultaneously on shared-panel.
        arbiter.UpdatePointer(0, leftRay, isValid: true, isPressed: true, targets);
        arbiter.UpdatePointer(1, rightRay, isValid: true, isPressed: true, targets);
        Assert.True(arbiter.IsCaptured(0));
        Assert.True(arbiter.IsCaptured(1));

        // Left releases while Right stays pressed.
        arbiter.UpdatePointer(0, leftRay, isValid: true, isPressed: false, targets);
        var r2 = arbiter.UpdatePointer(1, rightRay, isValid: true, isPressed: true, targets);

        Assert.False(arbiter.IsCaptured(0));
        Assert.True(arbiter.IsCaptured(1));
        Assert.Equal("shared-panel", r2?.PanelId);
        Assert.True(r2?.IsCaptured);
    }
}
