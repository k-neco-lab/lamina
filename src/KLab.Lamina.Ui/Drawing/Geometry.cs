// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public static Rect Empty => new(0, 0, 0, 0);
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Vector2 Origin => new(X, Y);
    public Vector2 Size => new(Width, Height);
    public bool Contains(Vector2 p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;
    public Rect Deflate(Thickness t) =>
        new(X + t.Left, Y + t.Top, MathF.Max(0, Width - t.Horizontal), MathF.Max(0, Height - t.Vertical));
    public Rect Inflate(float amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);
}

public readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
    public static Thickness Zero => new(0, 0, 0, 0);
    public Thickness(float all) : this(all, all, all, all)
    {
    }

    public Thickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical)
    {
    }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
}

public enum Alignment
{
    Start,
    Center,
    End,
    Stretch,
}

public enum Orientation
{
    Horizontal,
    Vertical,
}
