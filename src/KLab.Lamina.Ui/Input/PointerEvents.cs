// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

public enum PointerButton
{
    Left,
    Right,
    Middle,
}

// Position is in the receiving control's local coordinates.
public sealed class PointerEventArgs
{
    public PointerEventArgs(
        Vector2 position,
        PointerButton button = PointerButton.Left,
        Vector2 delta = default,
        double time = 0,
        int pointerId = 0)
    {
        Position = position;
        Button = button;
        Delta = delta;
        Time = time;
        PointerId = pointerId;
    }

    // Isolates concurrent VR pointers (e.g. Left = 0, Right = 1).
    public int PointerId { get; }

    // UiRoot clock in seconds (sum of Update steps), so inertia is deterministic and testable.
    public double Time { get; }

    public Vector2 Position { get; }
    public PointerButton Button { get; }

    // Drag: movement since the previous drag event. Scroll: wheel/stick amount, positive = content moves down.
    public Vector2 Delta { get; }
    public bool Handled { get; set; }
}
