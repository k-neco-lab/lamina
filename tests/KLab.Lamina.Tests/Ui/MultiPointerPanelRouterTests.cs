// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using KLab.Lamina.Abstractions;
using KLab.Lamina.Ui;
using Xunit;

namespace KLab.Lamina.Tests;

public sealed class MultiPointerPanelRouterTests
{
    private const int LeftHand = 0;
    private const int RightHand = 1;

    private static (UiRoot Root, Button ButtonA, Button ButtonB) CreatePanel(string id)
    {
        var buttonA = new Button { Width = 100, Height = 50 };
        var buttonB = new Button { Width = 100, Height = 50 };
        var stack = new Stack
        {
            Orientation = Orientation.Horizontal,
            Spacing = 20,
            HorizontalAlignment = Alignment.Start,
            VerticalAlignment = Alignment.Start,
        };
        stack.Add(buttonA);
        stack.Add(buttonB);

        var root = new UiRoot(stack, new FakeTextMeasurer(), new Vector2(240, 100), panelId: id);
        root.Layout();
        return (root, buttonA, buttonB);
    }

    [Fact]
    public void Router_PreservesPressAnchorDeadzone_ForTriggerJitter()
    {
        var router = new MultiPointerPanelRouter();
        var (panel, buttonA, _) = CreatePanel("panel-1");
        int clicks = 0;
        buttonA.Clicked += () => clicks++;

        // Press near the right edge of Button A (x=95, y=25 on 240x100 panel).
        var pressUv = new Vector2(95f / 240f, 25f / 100f);
        router.UpdatePointer(LeftHand, panel, pressUv, isPressed: false);
        router.UpdatePointer(LeftHand, panel, pressUv, isPressed: true);

        // Jitter 25px to the right (within 28px PressAnchorDeadzonePixels, even though 95+25=120 is past ClickSlop).
        var jitterUv = new Vector2(120f / 240f, 25f / 100f);
        router.UpdatePointer(LeftHand, panel, jitterUv, isPressed: true);
        Assert.True(buttonA.IsPressed);

        // Release while still within deadzone -> anchored to pressUv so click succeeds.
        router.UpdatePointer(LeftHand, panel, jitterUv, isPressed: false);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Router_AllowsConcurrentCaptureOnSamePanel_AndIndependentCrossPanelRouting()
    {
        var router = new MultiPointerPanelRouter();
        var (panel1, p1BtnA, p1BtnB) = CreatePanel("panel-1");
        var (panel2, p2BtnA, _) = CreatePanel("panel-2");

        int p1ClicksA = 0, p1ClicksB = 0, p2ClicksA = 0;
        p1BtnA.Clicked += () => p1ClicksA++;
        p1BtnB.Clicked += () => p1ClicksB++;
        p2BtnA.Clicked += () => p2ClicksA++;

        // Both hands hover and press different buttons on panel1 simultaneously.
        var uvA = new Vector2(50f / 240f, 25f / 100f);
        var uvB = new Vector2(170f / 240f, 25f / 100f);

        router.UpdatePointer(LeftHand, panel1, uvA, isPressed: true);
        router.UpdatePointer(RightHand, panel1, uvB, isPressed: true);

        Assert.Same(panel1, router.GetCapturedPanel(LeftHand));
        Assert.Same(panel1, router.GetCapturedPanel(RightHand));
        Assert.True(p1BtnA.IsPressedBy(LeftHand));
        Assert.True(p1BtnB.IsPressedBy(RightHand));

        // Right hand releases panel1, moves to panel2, and clicks p2BtnA while left hand still holds p1BtnA.
        router.UpdatePointer(RightHand, panel1, uvB, isPressed: false);
        router.UpdatePointer(RightHand, panel2, uvA, isPressed: false);
        router.UpdatePointer(RightHand, panel2, uvA, isPressed: true);
        router.UpdatePointer(RightHand, panel2, uvA, isPressed: false);

        Assert.Equal(1, p1ClicksB);
        Assert.Equal(1, p2ClicksA);
        Assert.True(p1BtnA.IsPressedBy(LeftHand));

        // Left hand finally releases panel1.
        router.UpdatePointer(LeftHand, panel1, uvA, isPressed: false);
        Assert.Equal(1, p1ClicksA);
    }

    [Fact]
    public void Router_CancelPointer_OnlyCancelsTargetHand()
    {
        var router = new MultiPointerPanelRouter();
        var (panel, btnA, btnB) = CreatePanel("panel-1");
        int clicksA = 0, clicksB = 0;
        btnA.Clicked += () => clicksA++;
        btnB.Clicked += () => clicksB++;

        var uvA = new Vector2(50f / 240f, 25f / 100f);
        var uvB = new Vector2(170f / 240f, 25f / 100f);

        router.UpdatePointer(LeftHand, panel, uvA, isPressed: true);
        router.UpdatePointer(RightHand, panel, uvB, isPressed: true);

        router.CancelPointer(LeftHand);

        Assert.Null(router.GetCapturedPanel(LeftHand));
        Assert.Same(panel, router.GetCapturedPanel(RightHand));
        Assert.False(btnA.IsPressed);
        Assert.True(btnB.IsPressed);

        router.UpdatePointer(RightHand, panel, uvB, isPressed: false);
        Assert.Equal(0, clicksA);
        Assert.Equal(1, clicksB);
    }

    [Fact]
    public void Router_DoesNotClampContinuousDragControlsWithDeadzone()
    {
        var router = new MultiPointerPanelRouter();
        var slider = new Slider { Width = 200, Height = 40, Minimum = 0, Maximum = 100, Value = 0 };
        var root = new UiRoot(slider, new FakeTextMeasurer(), new Vector2(200, 40), panelId: "slider-panel");
        root.Layout();

        // Press at x=100 (mid) and immediately drag 15px (inside 28px button deadzone).
        router.UpdatePointer(LeftHand, root, new Vector2(0.50f, 0.5f), isPressed: true);
        float startVal = slider.Value;

        router.UpdatePointer(LeftHand, root, new Vector2(0.575f, 0.5f), isPressed: true);
        Assert.True(slider.Value > startVal + 4f, $"Expected slider to move immediately inside 28px deadzone; start={startVal}, now={slider.Value}");
    }
}
