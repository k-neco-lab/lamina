// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using KLab.Lamina.Abstractions;
using KLab.Lamina.D2D;
using KLab.Lamina.Ui;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Xunit;
using Orientation = KLab.Lamina.Ui.Orientation;

namespace KLab.Lamina.Tests;

public sealed class D2DGpuTests
{
    [Fact]
    public void SharedTextureSwapChain_CreatesSharedTextures_AndOpensAcrossD3D11Devices()
    {
        using var producerDevice = new VrD3D11Device(adapterIndex: 0, allowWarpFallback: false);
        using var consumerDevice = new VrD3D11Device(adapterIndex: 0, allowWarpFallback: false);
        using var drawContext = new D2DDrawContext(producerDevice);
        using var swapChain = new SharedTextureSwapChain(producerDevice, 256, 128);

        Assert.False(producerDevice.IsWarp);
        Assert.False(consumerDevice.IsWarp);

        var frontDesc = swapChain.FrontTexture.Description;
        var backDesc = swapChain.BackTexture.Description;

        Assert.Equal(Format.B8G8R8A8_UNorm, frontDesc.Format);
        Assert.Equal(Format.B8G8R8A8_UNorm, backDesc.Format);
        Assert.True(frontDesc.BindFlags.HasFlag(BindFlags.ShaderResource | BindFlags.RenderTarget));
        Assert.True(backDesc.BindFlags.HasFlag(BindFlags.ShaderResource | BindFlags.RenderTarget));
        Assert.True(frontDesc.MiscFlags.HasFlag(ResourceOptionFlags.Shared));
        Assert.True(backDesc.MiscFlags.HasFlag(ResourceOptionFlags.Shared));
        Assert.NotEqual(nint.Zero, swapChain.FrontSharedHandle);
        Assert.NotEqual(nint.Zero, swapChain.BackSharedHandle);
        Assert.NotEqual(swapChain.FrontSharedHandle, swapChain.BackSharedHandle);

        // Draw a known solid color into back buffer on producerDevice, swap, and read it on consumerDevice via shared handle.
        drawContext.BeginFrame(swapChain.BackTexture);
        drawContext.Clear(new Color(255, 64, 160, 220));
        Assert.True(drawContext.EndFrame());
        swapChain.Swap();

        using var openedOnConsumer = consumerDevice.OpenSharedTexture(swapChain.FrontSharedHandle);
        Assert.NotNull(openedOnConsumer);
        Assert.Equal(256u, openedOnConsumer.Description.Width);
        Assert.Equal(128u, openedOnConsumer.Description.Height);
        Assert.Equal(Format.B8G8R8A8_UNorm, openedOnConsumer.Description.Format);

        var pixels = consumerDevice.ReadPixelsBgra(openedOnConsumer, out int w, out int h, out var format);
        Assert.Equal(256, w);
        Assert.Equal(128, h);
        Assert.Equal(Format.B8G8R8A8_UNorm, format);

        int centerOffset = ((h / 2) * w + (w / 2)) * 4;
        Assert.Equal(220, pixels[centerOffset + 0]); // B
        Assert.Equal(160, pixels[centerOffset + 1]); // G
        Assert.Equal(64, pixels[centerOffset + 2]);  // R
        Assert.Equal(255, pixels[centerOffset + 3]); // A
    }

    [Fact]
    public void D2DPanelSurfaceHost_RendersTranslucentRoundedCjkPanelWithIconAndButtons_WithCleanPremultipliedAlpha()
    {
        using var device = new VrD3D11Device(adapterIndex: 0, allowWarpFallback: false);
        using var drawContext = new D2DDrawContext(device);
        using var host = new D2DPanelSurfaceHost("panel-cjk", device, drawContext, 480, 220);

        Assert.Equal(TextAntialiasMode.Grayscale, drawContext.TextAntialiasMode);

        var panel = BuildSamplePanel(out _, out _);
        host.SetContent(panel);

        Assert.True(host.RenderIfDirty());
        Assert.Equal(1, host.GpuRenderCount);
        Assert.Equal(1, host.SubmittedFrameCount);
        Assert.Equal(host.FrontTexture.NativePointer, host.LastSubmittedTexturePointer);
        Assert.Equal(480, host.LastSubmittedWidth);
        Assert.Equal(220, host.LastSubmittedHeight);

        var pixels = device.ReadPixelsBgra(host.FrontTexture, out int width, out int height, out var format);
        Assert.Equal(480, width);
        Assert.Equal(220, height);
        Assert.Equal(Format.B8G8R8A8_UNorm, format);

        // Verify transparent margin corners outside the rounded panel have Alpha == 0 and RGB == 0 (clean premultiplied).
        (int x, int y)[] cornerProbes =
        [
            (0, 0),
            (4, 4),
            (width - 1, 0),
            (width - 5, 4),
            (0, height - 1),
            (width - 1, height - 1),
        ];
        foreach (var (x, y) in cornerProbes)
        {
            int idx = (y * width + x) * 4;
            Assert.Equal(0, pixels[idx + 0]);
            Assert.Equal(0, pixels[idx + 1]);
            Assert.Equal(0, pixels[idx + 2]);
            Assert.Equal(0, pixels[idx + 3]);
        }

        // Verify premultiplied alpha invariant (B,G,R <= A) across the entire texture,
        // and verify non-transparent pixels & bright text/icon glyphs were rendered.
        int nonTransparentPixels = 0;
        int translucentBackgroundPixels = 0;
        int brightHeaderGlyphPixels = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                byte b = pixels[idx + 0];
                byte g = pixels[idx + 1];
                byte r = pixels[idx + 2];
                byte a = pixels[idx + 3];

                Assert.True(b <= a && g <= a && r <= a,
                    $"Premultiplied invariant violated at ({x},{y}): BGRA=({b},{g},{r},{a})");

                if (a > 0)
                {
                    nonTransparentPixels++;
                }
                if (a is >= 195 and <= 225)
                {
                    translucentBackgroundPixels++;
                }
                if (y is >= 24 and <= 80 && x is >= 24 and <= 440 && r >= 180 && g >= 180 && b >= 180 && a >= 220)
                {
                    brightHeaderGlyphPixels++;
                }
            }
        }

        Assert.True(nonTransparentPixels > 40000, $"Expected >40000 rendered pixels, got {nonTransparentPixels}");
        Assert.True(translucentBackgroundPixels > 10000, $"Expected >10000 translucent panel pixels, got {translucentBackgroundPixels}");
        Assert.True(brightHeaderGlyphPixels > 200, $"Expected >200 bright CJK text/icon pixels in header, got {brightHeaderGlyphPixels}");
    }

    [Fact]
    public void D2DPanelSurfaceHost_CleanSecondFrame_SkipsGpuDraw()
    {
        using var device = new VrD3D11Device(adapterIndex: 0, allowWarpFallback: false);
        using var drawContext = new D2DDrawContext(device);
        using var host = new D2DPanelSurfaceHost("panel-clean", device, drawContext, 320, 160);

        host.SetContent(BuildSamplePanel(out _, out _));

        Assert.True(host.RenderIfDirty());
        Assert.Equal(1, host.GpuRenderCount);
        Assert.Equal(1, host.SwapChain.SwapCount);
        Assert.Equal(1, host.SubmittedFrameCount);
        nint frontAfterFirstFrame = host.FrontTexture.NativePointer;

        // Second frame with no state changes must perform zero GPU rendering and zero swaps.
        Assert.False(host.RenderIfDirty());
        Assert.Equal(1, host.GpuRenderCount);
        Assert.Equal(1, host.SwapChain.SwapCount);
        Assert.Equal(1, host.SubmittedFrameCount);
        Assert.Equal(frontAfterFirstFrame, host.FrontTexture.NativePointer);
    }

    [Fact]
    public void D2DPanelSurfaceHost_SetContentDuringActiveCapture_PreservesClickAndAppliesOnRelease()
    {
        using var device = new VrD3D11Device(adapterIndex: 0, allowWarpFallback: false);
        using var drawContext = new D2DDrawContext(device);
        using var host = new D2DPanelSurfaceHost("panel-capture", device, drawContext, 480, 220);

        var initialPanel = BuildSamplePanel(out var buttonA, out _);
        int buttonAClicks = 0;
        buttonA.Clicked += () => buttonAClicks++;

        host.SetContent(initialPanel);
        Assert.True(host.RenderIfDirty());
        Assert.Equal(1, host.GpuRenderCount);

        // Compute center of Button A in root coordinates.
        var buttonACenter = CenterInRoot(buttonA);
        var buttonAUv = new Vector2(buttonACenter.X / host.PixelWidth, buttonACenter.Y / host.PixelHeight);

        // Left hand (PointerId = 0) presses Button A.
        host.OnPointerEvent(new VrPointerEvent(0, VrPointerEventKind.Down, buttonAUv, buttonACenter, VrPointerButton.Left));
        Assert.True(host.HasActiveCapture);
        Assert.True(host.IsPointerCaptured(0));

        // Simulate periodic background list refresh calling SetContent while left hand is holding Button A.
        var replacementPanel = new Panel
        {
            Background = new Color(255, 24, 190, 88),
            CornerRadius = 12,
            Padding = new Thickness(16),
        };
        replacementPanel.Add(new TextBlock("已更新内容", 22) { Color = Color.White });

        host.SetContent(replacementPanel);

        // Content replacement must be deferred while captured!
        Assert.True(host.HasPendingContent);
        Assert.Same(initialPanel, host.Root!.Content);
        Assert.Equal(0, buttonAClicks);

        // Left hand releases Button A -> Clicked must still fire, then pending content applies immediately.
        host.OnPointerEvent(new VrPointerEvent(0, VrPointerEventKind.Up, buttonAUv, buttonACenter, VrPointerButton.Left));

        Assert.Equal(1, buttonAClicks);
        Assert.False(host.HasActiveCapture);
        Assert.False(host.HasPendingContent);
        Assert.Same(replacementPanel, host.Root!.Content);
        Assert.True(host.IsDirty);

        // Next RenderIfDirty renders the replacement green panel to GPU.
        Assert.True(host.RenderIfDirty());
        Assert.Equal(2, host.GpuRenderCount);

        var pixels = device.ReadPixelsBgra(host.FrontTexture, out int width, out int height, out _);
        // Probe near bottom-right interior where replacement green fill is unobstructed by text.
        int probeIdx = ((height - 30) * width + (width - 30)) * 4;
        Assert.Equal(88, pixels[probeIdx + 0]);  // B
        Assert.Equal(190, pixels[probeIdx + 1]); // G
        Assert.Equal(24, pixels[probeIdx + 2]);  // R
        Assert.Equal(255, pixels[probeIdx + 3]); // A
    }

    [Fact]
    public void D2DPanelSurfaceHost_MultiHandCaptureAndDeferredResize_TransitionsSafely()
    {
        using var device = new VrD3D11Device(adapterIndex: 0, allowWarpFallback: false);
        using var drawContext = new D2DDrawContext(device);
        using var host = new D2DPanelSurfaceHost("panel-resize", device, drawContext, 480, 220);

        var panel = BuildSamplePanel(out var buttonA, out var buttonB);
        int aClicks = 0;
        int bClicks = 0;
        buttonA.Clicked += () => aClicks++;
        buttonB.Clicked += () => bClicks++;

        host.SetContent(panel);
        Assert.True(host.RenderIfDirty());

        var posA = CenterInRoot(buttonA);
        var posB = CenterInRoot(buttonB);

        // Both hands press simultaneously.
        host.OnPointerEvent(new VrPointerEvent(0, VrPointerEventKind.Down, posA / host.PixelSize, posA));
        host.OnPointerEvent(new VrPointerEvent(1, VrPointerEventKind.Down, posB / host.PixelSize, posB));
        Assert.True(host.IsPointerCaptured(0));
        Assert.True(host.IsPointerCaptured(1));

        // Request resize while both hands hold capture -> must defer.
        host.Resize(640, 320);
        Assert.True(host.HasPendingResize);
        Assert.Equal(480, host.TextureWidth);
        Assert.Equal(220, host.TextureHeight);

        // Left hand releases -> right hand still holds capture, so resize remains deferred.
        host.OnPointerEvent(new VrPointerEvent(0, VrPointerEventKind.Up, posA / host.PixelSize, posA));
        Assert.Equal(1, aClicks);
        Assert.True(host.HasActiveCapture);
        Assert.True(host.HasPendingResize);
        Assert.Equal(480, host.TextureWidth);

        // Right hand releases -> all captures cleared, resize applies cleanly.
        host.OnPointerEvent(new VrPointerEvent(1, VrPointerEventKind.Up, posB / host.PixelSize, posB));
        Assert.Equal(1, bClicks);
        Assert.False(host.HasActiveCapture);
        Assert.False(host.HasPendingResize);
        Assert.Equal(640, host.PixelWidth);
        Assert.Equal(320, host.PixelHeight);
        Assert.Equal(640, host.TextureWidth);
        Assert.Equal(320, host.TextureHeight);
        Assert.True(host.SwapChain.HasDeferredRetirement);

        // Render resized frame; previous front texture is retired safely.
        Assert.True(host.RenderIfDirty());
        Assert.False(host.SwapChain.HasDeferredRetirement);
        Assert.Equal(640, host.LastSubmittedWidth);
        Assert.Equal(320, host.LastSubmittedHeight);
    }

    [Fact]
    public void D2DDrawContext_LruCaches_EnforceConfiguredLimits()
    {
        using var device = new VrD3D11Device(adapterIndex: 0, allowWarpFallback: false);
        using var drawContext = new D2DDrawContext(device, maxLayouts: 4, maxBrushes: 4, maxGeometries: 4);
        using var target = device.CreateSharedPanelTexture(256, 128);

        drawContext.BeginFrame(target);
        drawContext.Clear(Color.Transparent);
        for (int i = 0; i < 10; i++)
        {
            var color = new Color(255, (byte)(20 + i * 20), (byte)(100 + i * 10), (byte)(200 - i * 10));
            drawContext.FillRoundedRect(new Rect(i * 10, 10, 20, 20), 4, color);
            drawContext.DrawText($"项目 {i}", new Rect(10, 40, 200, 30), new TextStyle("Segoe UI", 16, FontWeight.Regular, color));
            drawContext.FillPath(VectorPath.Circle(20 + i * 10, 90, 6), color);
        }
        Assert.True(drawContext.EndFrame());

        Assert.True(drawContext.TextCache.CachedLayoutCount <= 4);
        Assert.True(drawContext.CachedBrushCount <= 4);
        Assert.True(drawContext.CachedGeometryCount <= 4);
    }

    private static Vector2 CenterInRoot(Control control)
    {
        var point = new Vector2(control.Bounds.Width / 2f, control.Bounds.Height / 2f);
        for (Control? current = control; current is not null; current = current.Parent)
        {
            point += current.Bounds.Origin;
        }
        return point;
    }

    private static Panel BuildSamplePanel(out Button buttonA, out Button buttonB)
    {
        var outer = new Panel
        {
            Margin = new Thickness(16),
            Background = new Color(210, 20, 26, 38),
            BorderColor = new Color(190, 120, 180, 255),
            BorderWidth = 2f,
            CornerRadius = 18f,
            Padding = new Thickness(18),
        };

        var rootStack = new Stack
        {
            Orientation = Orientation.Vertical,
            Spacing = 18f,
        };

        var header = new Stack
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10f,
        };
        header.Add(new Icon("settings", 24f) { Color = Color.White, VerticalAlignment = Alignment.Center });
        header.Add(new TextBlock("猫尾草工作室 Hako 悬浮层", 20f)
        {
            Weight = FontWeight.SemiBold,
            Color = Color.White,
            VerticalAlignment = Alignment.Center,
        });

        var buttons = new Stack
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14f,
        };

        buttonA = new Button { Width = 170f, Height = 46f, CornerRadius = 10f };
        buttonA.SetContent(new TextBlock("按钮 A", 16f) { Color = Color.White });

        buttonB = new Button { Width = 170f, Height = 46f, CornerRadius = 10f };
        buttonB.SetContent(new TextBlock("按钮 B", 16f) { Color = Color.White });

        buttons.Add(buttonA);
        buttons.Add(buttonB);

        rootStack.Add(header);
        rootStack.Add(buttons);
        outer.Add(rootStack);
        return outer;
    }
}
