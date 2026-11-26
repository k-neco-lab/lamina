// Copyright (c) K Lab. Licensed under the MIT License.

namespace KLab.Lamina.Ui;

// Offset, drag and inertia shared by ScrollView and ListView.
public sealed class ScrollState
{
    public const float FrictionPerSecond = 4f;
    public const float StopVelocity = 8f;

    public float Offset { get; private set; }
    public float Velocity { get; private set; }
    public float ViewportLength { get; private set; }
    public float ContentLength { get; private set; }
    public float MaxOffset => MathF.Max(0, ContentLength - ViewportLength);

    // Returns true when the offset moved.
    public bool SetExtent(float viewport, float content)
    {
        ViewportLength = viewport;
        ContentLength = content;
        return ScrollTo(Offset);
    }

    public bool ScrollTo(float offset)
    {
        float clamped = Math.Clamp(offset, 0, MaxOffset);
        bool moved = clamped != Offset;
        Offset = clamped;
        if (moved && (clamped == 0 || clamped == MaxOffset))
        {
            Velocity = 0;
        }
        return moved;
    }

    public bool ScrollBy(float delta) => ScrollTo(Offset + delta);

    // delta is the change of Offset for this drag step; the speed becomes the inertia after release.
    public bool Drag(float delta, float deltaSeconds)
    {
        if (deltaSeconds > 0)
        {
            Velocity = delta / deltaSeconds;
        }
        return ScrollBy(delta);
    }

    public void Stop() => Velocity = 0;

    public bool IsCoasting => MathF.Abs(Velocity) > StopVelocity;

    // Advances inertia; returns true when the offset changed.
    public bool Step(float deltaSeconds)
    {
        if (!IsCoasting)
        {
            Velocity = 0;
            return false;
        }
        bool moved = ScrollBy(Velocity * deltaSeconds);
        Velocity *= MathF.Exp(-FrictionPerSecond * deltaSeconds);
        return moved;
    }
}
