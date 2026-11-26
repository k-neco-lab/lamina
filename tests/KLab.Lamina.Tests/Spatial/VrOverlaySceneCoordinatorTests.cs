// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using KLab.Lamina.Abstractions;
using Valve.VR;
using Xunit;

namespace KLab.Lamina.Tests;

public sealed class VrOverlaySceneCoordinatorTests
{
    private sealed class TestPanelSurface : IVrOverlayFrameSurface, IVrPanelInputSink
    {
        public TestPanelSurface(string id, int width, int height, float widthMeters, uint sortOrder, Matrix4x4 pose)
        {
            Id = id;
            PixelWidth = width;
            PixelHeight = height;
            WidthMeters = widthMeters;
            SortOrder = sortOrder;
            Pose = pose;
        }

        public string Id { get; }
        public string PanelId => Id;
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public Vector2 PixelSize => new(PixelWidth, PixelHeight);
        public uint SortOrder { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool IsDirty { get; private set; }
        public Matrix4x4 Pose { get; set; }
        public float WidthMeters { get; set; }
        public float HeightMeters => PixelWidth <= 0 ? 0f : WidthMeters * PixelHeight / PixelWidth;

        public List<VrPointerEvent> Events { get; } = new();
        private readonly HashSet<int> capturedPointers = new();

        public void MarkDirty() => IsDirty = true;

        public void SubmitSharedTexture(nint sharedTexturePointer, int width, int height)
        {
            IsDirty = false;
        }

        public bool IsPointerCaptured(int pointerId) => capturedPointers.Contains(pointerId);

        public bool IsPointerContinuousDrag(int pointerId) => false;

        public void OnPointerEvent(in VrPointerEvent pointerEvent)
        {
            Events.Add(pointerEvent);
            if (pointerEvent.Kind == VrPointerEventKind.Down)
            {
                capturedPointers.Add(pointerEvent.PointerId);
            }
            else if (pointerEvent.Kind is VrPointerEventKind.Up or VrPointerEventKind.Cancel)
            {
                capturedPointers.Remove(pointerEvent.PointerId);
            }
        }
    }

    private sealed class RecordingOverlayDriver : IVrOverlayDriver
    {
        private ulong nextHandle = 100;

        public bool IsAvailable { get; set; } = true;
        public Dictionary<ulong, string> ActiveHandles { get; } = new();
        public Dictionary<ulong, HmdMatrix34_t> LastTransforms { get; } = new();
        public Dictionary<ulong, Texture_t> LastTextures { get; } = new();
        public int TransformCallCount { get; private set; }
        public int TextureCallCount { get; private set; }
        public int DestroyCallCount { get; private set; }

        public EVROverlayError CreateOverlay(string key, string name, out ulong handle)
        {
            handle = nextHandle++;
            ActiveHandles[handle] = key;
            return EVROverlayError.None;
        }

        public EVROverlayError DestroyOverlay(ulong handle)
        {
            DestroyCallCount++;
            ActiveHandles.Remove(handle);
            return EVROverlayError.None;
        }

        public EVROverlayError ShowOverlay(ulong handle) => EVROverlayError.None;
        public EVROverlayError HideOverlay(ulong handle) => EVROverlayError.None;
        public EVROverlayError SetOverlaySortOrder(ulong handle, uint sortOrder) => EVROverlayError.None;
        public EVROverlayError SetOverlayAlpha(ulong handle, float alpha) => EVROverlayError.None;
        public EVROverlayError SetOverlayWidthInMeters(ulong handle, float widthMeters) => EVROverlayError.None;
        public EVROverlayError SetOverlayCurvature(ulong handle, float curvature) => EVROverlayError.None;

        public EVROverlayError SetOverlayTransformAbsolute(ulong handle, ETrackingUniverseOrigin origin, in HmdMatrix34_t transform)
        {
            TransformCallCount++;
            LastTransforms[handle] = transform;
            return EVROverlayError.None;
        }

        public EVROverlayError SetOverlayMouseScale(ulong handle, in HmdVector2_t scale) => EVROverlayError.None;
        public EVROverlayError SetOverlayTexelAspect(ulong handle, float aspect) => EVROverlayError.None;
        public EVROverlayError SetOverlayTextureBounds(ulong handle, in VRTextureBounds_t bounds) => EVROverlayError.None;

        public EVROverlayError SetOverlayTexture(ulong handle, in Texture_t texture)
        {
            TextureCallCount++;
            LastTextures[handle] = texture;
            return EVROverlayError.None;
        }

        public bool ComputeOverlayIntersection(ulong handle, in VROverlayIntersectionParams_t parameters, out VROverlayIntersectionResults_t results)
        {
            results = default;
            return false;
        }
    }

    [Fact]
    public void EndToEndFrameLoop_DualHands_TransientDropoutDoesNotCancel_SustainedDropoutCancelsOnlyAffectedHand()
    {
        var driver = new RecordingOverlayDriver();
        using var backend = new VrOverlayBackend(driver, keyPrefix: "test.klab");
        using var coordinator = new VrOverlaySceneCoordinator(backend);

        // Main panel centered at (0, 1.3, -1.5), size 1.0 x 0.5m.
        var mainPanel = new TestPanelSurface("main", 1000, 500, 1.0f, sortOrder: 20, Matrix4x4.CreateTranslation(0f, 1.3f, -1.5f));
        // Attached bottom toolbar: size 0.6 x 0.15m, snapped below main panel with 0.012m gap.
        // Center Y = 1.3 - 0.25 - 0.012 - 0.075 = 0.963m.
        var toolbar = new TestPanelSurface("toolbar", 600, 150, 0.6f, sortOrder: 30, Matrix4x4.Identity);

        nint mainTex = new(0x1234);
        nint toolbarTex = new(0x5678);

        coordinator.RegisterPanel(mainPanel, mainPanel, sharedTextureProvider: () => mainTex);
        coordinator.RegisterPanel(
            toolbar,
            toolbar,
            attachment: OverlayAttachment.ToEdge("main", OverlayAttachEdge.Bottom, alongNormalized: 0f, edgeGapMeters: 0.012f),
            sharedTextureProvider: () => toolbarTex);

        var leftRayPose = Matrix4x4.CreateTranslation(0f, 1.3f, 0f);    // Hits mainPanel center
        var rightRayPose = Matrix4x4.CreateTranslation(0f, 0.963f, 0f); // Hits attached toolbar center

        // Frame 1: Left presses on mainPanel, Right presses on attached toolbar.
        coordinator.StepFrame(new[]
        {
            (0, new VrHandRawSample(true, leftRayPose, ActionsBound: true, TriggerActive: true, TriggerPressed: true, TimestampSeconds: 1.0)),
            (1, new VrHandRawSample(true, rightRayPose, ActionsBound: true, TriggerActive: true, TriggerPressed: true, TimestampSeconds: 1.0)),
        });

        Assert.Contains(mainPanel.Events, e => e.PointerId == 0 && e.Kind == VrPointerEventKind.Down);
        Assert.Contains(toolbar.Events, e => e.PointerId == 1 && e.Kind == VrPointerEventKind.Down);
        Assert.Equal(2, driver.ActiveHandles.Count);
        Assert.Equal(2, driver.TextureCallCount);
        Assert.Equal(ETextureType.DirectX, driver.LastTextures[driver.ActiveHandles.Keys.First()].eType);

        // Frames 2 & 3: Left suffers a 2-tick transient action-set dropout while Right stays active.
        for (int f = 0; f < 2; f++)
        {
            coordinator.StepFrame(new[]
            {
                (0, new VrHandRawSample(true, leftRayPose, ActionsBound: false, TriggerActive: false, TriggerPressed: false, TimestampSeconds: 1.01 + f * 0.01)),
                (1, new VrHandRawSample(true, rightRayPose, ActionsBound: true, TriggerActive: true, TriggerPressed: true, TimestampSeconds: 1.01 + f * 0.01)),
            });
        }

        // Neither hand was cancelled!
        Assert.DoesNotContain(mainPanel.Events, e => e.Kind == VrPointerEventKind.Cancel);
        Assert.DoesNotContain(toolbar.Events, e => e.Kind == VrPointerEventKind.Cancel);

        // Frame 4: Left recovers and releases trigger -> clean Up event on mainPanel!
        coordinator.StepFrame(new[]
        {
            (0, new VrHandRawSample(true, leftRayPose, ActionsBound: true, TriggerActive: true, TriggerPressed: false, TimestampSeconds: 1.04)),
            (1, new VrHandRawSample(true, rightRayPose, ActionsBound: true, TriggerActive: true, TriggerPressed: true, TimestampSeconds: 1.04)),
        });

        Assert.Contains(mainPanel.Events, e => e.PointerId == 0 && e.Kind == VrPointerEventKind.Up);
        Assert.DoesNotContain(mainPanel.Events, e => e.Kind == VrPointerEventKind.Cancel);

        // Frames 5..8: Right suffers a 4-tick sustained dropout (> 3 grace ticks) while Left hovers mainPanel.
        for (int f = 0; f < 4; f++)
        {
            coordinator.StepFrame(new[]
            {
                (0, new VrHandRawSample(true, leftRayPose, ActionsBound: true, TriggerActive: true, TriggerPressed: false, TimestampSeconds: 1.05 + f * 0.01)),
                (1, new VrHandRawSample(true, rightRayPose, ActionsBound: false, TriggerActive: false, TriggerPressed: false, TimestampSeconds: 1.05 + f * 0.01)),
            });
        }

        // Right hand cleanly receives Cancel on toolbar, while Left hand remains hovered on mainPanel unaffected.
        Assert.Contains(toolbar.Events, e => e.PointerId == 1 && e.Kind == VrPointerEventKind.Cancel);
        Assert.Same(mainPanel, coordinator.Router.GetHoveredPanel(0));
        Assert.Null(coordinator.Router.GetCapturedPanel(1));

        // Verify backend cached redundant transforms/textures across frames (only 2 initial transform calls + 2 initial textures).
        Assert.Equal(2, driver.TransformCallCount);
        Assert.Equal(2, driver.TextureCallCount);
    }

    [Fact]
    public void AttachedChildPanel_InheritsParentVisibilityAndUpdatesTransformWhenParentMoves()
    {
        var driver = new RecordingOverlayDriver();
        using var backend = new VrOverlayBackend(driver, keyPrefix: "test.attach");
        using var coordinator = new VrOverlaySceneCoordinator(backend);

        var parent = new TestPanelSurface("parent", 800, 400, 0.8f, 10, Matrix4x4.CreateTranslation(0f, 1.5f, -1.0f));
        var child = new TestPanelSurface("child", 400, 100, 0.4f, 20, Matrix4x4.Identity);

        Matrix4x4? lastChildPose = null;
        coordinator.RegisterPanel(parent, parent);
        coordinator.RegisterPanel(
            child,
            child,
            attachment: OverlayAttachment.ToEdge("parent", OverlayAttachEdge.Right, edgeGapMeters: 0.02f),
            onResolvedPoseChanged: pose => lastChildPose = pose);

        coordinator.StepFrame(Array.Empty<(int, VrHandRawSample)>());
        Assert.NotNull(lastChildPose);
        // Expected child X = 0 + 0.4 (parent halfW) + 0.02 (gap) + 0.2 (child halfW) = 0.62m.
        Assert.Equal(0.62f, lastChildPose!.Value.Translation.X, 4);

        // Move parent +0.5m along X and hide it -> child follows pose and inherits IsVisible=false.
        parent.Pose = Matrix4x4.CreateTranslation(0.5f, 1.5f, -1.0f);
        parent.IsVisible = false;
        coordinator.StepFrame(Array.Empty<(int, VrHandRawSample)>());

        Assert.Equal(1.12f, lastChildPose.Value.Translation.X, 4);
        Assert.True(coordinator.TryGetPanel("child", out var childEntry));
        Assert.False(childEntry.ResolvedVisible);
    }
}
