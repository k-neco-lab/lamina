// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Abstractions;

/// <summary>
/// UI-engine-agnostic multi-pointer input receiver implemented by Layer 2 panel hosts or UI roots.
/// </summary>
public interface IVrPanelInputSink
{
    string PanelId { get; }
    Vector2 PixelSize { get; }

    bool IsPointerCaptured(int pointerId);

    /// <summary>
    /// Returns true when the control currently captured by <paramref name="pointerId"/> expects
    /// continuous drag updates (e.g. Slider, ScrollView) rather than press-anchor deadzone clamping.
    /// </summary>
    bool IsPointerContinuousDrag(int pointerId);

    void OnPointerEvent(in VrPointerEvent pointerEvent);
}
