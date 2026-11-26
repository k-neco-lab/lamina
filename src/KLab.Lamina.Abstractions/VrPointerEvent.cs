// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Abstractions;

public enum VrPointerButton
{
    Left = 0,
    Right = 1,
    Middle = 2,
}

public enum VrPointerEventKind
{
    Move = 0,
    Down = 1,
    Up = 2,
    Leave = 3,
    Cancel = 4,
    Scroll = 5,
}

/// <summary>
/// UI-engine-agnostic pointer event routed from Layer 1 spatial raycasting to a panel sink.
/// PointerId isolates concurrent VR hands (e.g. Left = 0, Right = 1).
/// </summary>
public readonly record struct VrPointerEvent(
    int PointerId,
    VrPointerEventKind Kind,
    Vector2 NormalizedUv,
    Vector2 PixelPosition,
    VrPointerButton Button = VrPointerButton.Left,
    Vector2 ScrollDelta = default,
    double TimestampSeconds = 0);
