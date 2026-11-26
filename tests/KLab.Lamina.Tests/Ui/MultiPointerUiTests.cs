// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using KLab.Lamina.Ui;
using Xunit;

namespace KLab.Lamina.Tests;

public sealed class MultiPointerUiTests
{
    private const int LeftHand = 0;
    private const int RightHand = 1;

    private static (UiRoot Root, Button ButtonA, Button ButtonB) CreateTwoButtonPanel()
    {
        var buttonA = new Button { Width = 120, Height = 50 };
        var buttonB = new Button { Width = 120, Height = 50 };

        var stack = new Stack
        {
            Orientation = Orientation.Horizontal,
            Spacing = 40,
            HorizontalAlignment = Alignment.Start,
            VerticalAlignment = Alignment.Start,
        };
        stack.Add(buttonA);
        stack.Add(buttonB);

        var root = new UiRoot(stack, new FakeTextMeasurer(), new Vector2(400, 120), panelId: "test-panel");
        root.Layout();
        return (root, buttonA, buttonB);
    }

    private static (UiRoot Root, Slider SliderA, Slider SliderB) CreateTwoSliderPanel()
    {
        var sliderA = new Slider { Width = 200, Height = 40, Minimum = 0, Maximum = 100, Value = 0 };
        var sliderB = new Slider { Width = 200, Height = 40, Minimum = 0, Maximum = 100, Value = 0 };

        var stack = new Stack
        {
            Orientation = Orientation.Vertical,
            Spacing = 20,
            HorizontalAlignment = Alignment.Start,
            VerticalAlignment = Alignment.Start,
        };
        stack.Add(sliderA);
        stack.Add(sliderB);

        var root = new UiRoot(stack, new FakeTextMeasurer(), new Vector2(300, 140), panelId: "sliders");
        root.Layout();
        return (root, sliderA, sliderB);
    }

    [Fact]
    public void TwoPointers_CanHoverDifferentButtonsSimultaneously_OnSamePanel()
    {
        var (root, buttonA, buttonB) = CreateTwoButtonPanel();

        root.PointerMove(LeftHand, new Vector2(60, 25));
        root.PointerMove(RightHand, new Vector2(220, 25));

        Assert.True(buttonA.IsHovered);
        Assert.True(buttonA.IsHoveredBy(LeftHand));
        Assert.False(buttonA.IsHoveredBy(RightHand));
        Assert.Equal(ButtonState.Hover, buttonA.State);

        Assert.True(buttonB.IsHovered);
        Assert.True(buttonB.IsHoveredBy(RightHand));
        Assert.False(buttonB.IsHoveredBy(LeftHand));
        Assert.Equal(ButtonState.Hover, buttonB.State);

        Assert.Same(buttonA, root.GetHovered(LeftHand));
        Assert.Same(buttonB, root.GetHovered(RightHand));
    }

    [Fact]
    public void TwoPointers_HoverSameButton_OneLeaves_ButtonStaysHoveredUntilSecondLeaves()
    {
        var (root, buttonA, _) = CreateTwoButtonPanel();

        root.PointerMove(LeftHand, new Vector2(60, 25));
        root.PointerMove(RightHand, new Vector2(70, 25));

        Assert.True(buttonA.IsHovered);
        Assert.Equal(2, buttonA.HoverPointerCount);
        Assert.Equal(ButtonState.Hover, buttonA.State);

        // Left hand leaves the panel while right hand stays on buttonA.
        root.PointerLeave(LeftHand);

        Assert.True(buttonA.IsHovered);
        Assert.Equal(1, buttonA.HoverPointerCount);
        Assert.False(buttonA.IsHoveredBy(LeftHand));
        Assert.True(buttonA.IsHoveredBy(RightHand));
        Assert.Equal(ButtonState.Hover, buttonA.State);

        // Right hand also leaves -> hover extinguishes cleanly.
        root.PointerLeave(RightHand);

        Assert.False(buttonA.IsHovered);
        Assert.Equal(0, buttonA.HoverPointerCount);
        Assert.Equal(ButtonState.Normal, buttonA.State);
    }

    [Fact]
    public void LeftHandHoldsButtonA_RightHandMovesAndClicksButtonB_NeverCancelsLeftHandClick()
    {
        var (root, buttonA, buttonB) = CreateTwoButtonPanel();
        int clicksA = 0;
        int clicksB = 0;
        buttonA.Clicked += () => clicksA++;
        buttonB.Clicked += () => clicksB++;

        // Left hand presses Button A.
        root.PointerMove(LeftHand, new Vector2(60, 25));
        root.PointerDown(LeftHand, new Vector2(60, 25));
        Assert.True(buttonA.IsPressed);
        Assert.True(buttonA.IsPressedBy(LeftHand));
        Assert.Same(buttonA, root.GetCaptured(LeftHand));

        // Right hand sweeps wildly across Button A, empty space, and clicks Button B.
        root.PointerMove(RightHand, new Vector2(60, 25));
        root.PointerMove(RightHand, new Vector2(390, 110));
        root.PointerMove(RightHand, new Vector2(220, 25));
        root.PointerDown(RightHand, new Vector2(220, 25));
        root.PointerUp(RightHand, new Vector2(220, 25));

        Assert.Equal(1, clicksB);
        Assert.Equal(0, clicksA);
        Assert.True(buttonA.IsPressed);
        Assert.True(buttonA.IsPressedBy(LeftHand));
        Assert.Same(buttonA, root.GetCaptured(LeftHand));
        Assert.Equal(ButtonState.Pressed, buttonA.State);

        // Left hand releases Button A within ClickSlop -> Button A click fires reliably.
        root.PointerUp(LeftHand, new Vector2(68, 30));

        Assert.Equal(1, clicksA);
        Assert.False(buttonA.IsPressed);
        Assert.Null(root.GetCaptured(LeftHand));
    }

    [Fact]
    public void TwoPointers_SimultaneouslyDragTwoSlidersIndependently()
    {
        var (root, sliderA, sliderB) = CreateTwoSliderPanel();

        // Left hand grabs Slider A near left edge; right hand grabs Slider B near right edge.
        root.PointerDown(LeftHand, new Vector2(20, 20));
        root.PointerDown(RightHand, new Vector2(180, 80));

        Assert.Same(sliderA, root.GetCaptured(LeftHand));
        Assert.Same(sliderB, root.GetCaptured(RightHand));
        Assert.Equal(2, root.ActiveCaptureCount);
        Assert.True(root.IsPointerContinuousDrag(LeftHand));
        Assert.True(root.IsPointerContinuousDrag(RightHand));

        // Drag both sliders in opposite directions on interleaved frames.
        root.PointerMove(LeftHand, new Vector2(150, 20));
        root.PointerMove(RightHand, new Vector2(50, 80));

        float aMid = sliderA.Value;
        float bMid = sliderB.Value;
        Assert.True(aMid > 60f, $"Expected SliderA > 60 after rightward drag, got {aMid}");
        Assert.True(bMid < 40f, $"Expected SliderB < 40 after leftward drag, got {bMid}");

        // Continue dragging even when pointer leaves vertical bounds (continuous drag stays captured).
        root.PointerMove(LeftHand, new Vector2(195, 130));
        root.PointerMove(RightHand, new Vector2(5, -30));

        Assert.True(sliderA.Value > aMid);
        Assert.True(sliderB.Value < bMid);

        root.PointerUp(LeftHand, new Vector2(195, 130));
        Assert.Null(root.GetCaptured(LeftHand));
        Assert.Same(sliderB, root.GetCaptured(RightHand));

        root.PointerUp(RightHand, new Vector2(5, -30));
        Assert.False(root.HasActiveCapture);
    }

    [Fact]
    public void PointerCancel_ReleasesOnlyCancelledPointer_PreservesOtherHandCaptureAndHover()
    {
        var (root, buttonA, buttonB) = CreateTwoButtonPanel();
        int clicksA = 0;
        int clicksB = 0;
        buttonA.Clicked += () => clicksA++;
        buttonB.Clicked += () => clicksB++;

        root.PointerMove(LeftHand, new Vector2(60, 25));
        root.PointerDown(LeftHand, new Vector2(60, 25));

        root.PointerMove(RightHand, new Vector2(220, 25));
        root.PointerDown(RightHand, new Vector2(220, 25));

        // Left hand loses tracking -> cancelled without firing OnClick.
        root.PointerCancel(LeftHand);

        Assert.Equal(0, clicksA);
        Assert.False(buttonA.IsPressed);
        Assert.False(buttonA.IsHovered);
        Assert.Null(root.GetCaptured(LeftHand));
        Assert.Null(root.GetHovered(LeftHand));

        // Right hand must remain completely intact and complete its click cleanly.
        Assert.True(buttonB.IsPressed);
        Assert.True(buttonB.IsHovered);
        Assert.Same(buttonB, root.GetCaptured(RightHand));
        Assert.Same(buttonB, root.GetHovered(RightHand));

        root.PointerUp(RightHand, new Vector2(220, 25));

        Assert.Equal(1, clicksB);
        Assert.Equal(0, clicksA);
    }

    [Fact]
    public void DraggingPastClickSlop_CancelsOnlyThatPointerPress()
    {
        var (root, buttonA, buttonB) = CreateTwoButtonPanel();
        int clicksA = 0;
        int clicksB = 0;
        buttonA.Clicked += () => clicksA++;
        buttonB.Clicked += () => clicksB++;

        root.PointerDown(LeftHand, new Vector2(60, 25));
        root.PointerDown(RightHand, new Vector2(220, 25));

        // Left hand drags far outside Button A (> ClickSlop = 18px).
        root.PointerMove(LeftHand, new Vector2(60, 100));
        Assert.False(buttonA.IsPressed);

        // Right hand jitters within ClickSlop (<= 18px) and releases.
        root.PointerMove(RightHand, new Vector2(220, 60)); // bottom=50, +10px <= 18px slop
        root.PointerUp(RightHand, new Vector2(220, 60));
        root.PointerUp(LeftHand, new Vector2(60, 100));

        Assert.Equal(0, clicksA);
        Assert.Equal(1, clicksB);
    }

    [Fact]
    public void DetachingHoveredOrCapturedControl_CleansUpPointerStateSafely()
    {
        var (root, buttonA, buttonB) = CreateTwoButtonPanel();
        var stack = (Stack)root.Content;

        root.PointerMove(LeftHand, new Vector2(60, 25));
        root.PointerDown(LeftHand, new Vector2(60, 25));
        root.PointerMove(RightHand, new Vector2(220, 25));

        stack.Remove(buttonA);

        Assert.Null(root.GetCaptured(LeftHand));
        Assert.Null(root.GetHovered(LeftHand));
        Assert.False(buttonA.IsPressed);
        Assert.False(buttonA.IsHovered);
        Assert.Same(buttonB, root.GetHovered(RightHand));
    }

    [Fact]
    public void RenderIfDirty_SkipsDrawWhenClean()
    {
        var (root, _, _) = CreateTwoButtonPanel();
        var ctx = new RecordingDrawContext();

        Assert.True(root.RenderIfDirty(ctx));
        Assert.Equal(1, root.RenderCount);
        Assert.Equal(1, ctx.ClearCount);

        Assert.False(root.RenderIfDirty(ctx));
        Assert.Equal(1, root.RenderCount);
        Assert.Equal(1, ctx.ClearCount);
    }
}
