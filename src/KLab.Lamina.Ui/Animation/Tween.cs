// Copyright (c) K Lab. Licensed under the MIT License.

namespace KLab.Lamina.Ui;

public enum Easing
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
}

public static class EasingFunctions
{
    public static float Apply(Easing easing, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return easing switch
        {
            Easing.EaseIn => t * t * t,
            Easing.EaseOut => 1 - (1 - t) * (1 - t) * (1 - t),
            Easing.EaseInOut => t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2,
            _ => t,
        };
    }
}

// Frame-driven value animation: the frame loop calls Advance(deltaSeconds) and reads Value.
// A zero duration jumps straight to the target, which matches the old UI where hover and press were instant.
public sealed class Tween
{
    private float from;
    private float to;
    private float elapsed;
    private float duration;

    public Tween(float initial = 0)
    {
        from = to = Value = initial;
    }

    public Easing Easing { get; set; } = Easing.Linear;
    public float Value { get; private set; }
    public float Target => to;
    public bool IsRunning => elapsed < duration;

    public void Start(float target, float durationSeconds)
    {
        from = Value;
        to = target;
        duration = MathF.Max(0, durationSeconds);
        elapsed = 0;
        if (duration == 0)
        {
            Value = target;
        }
    }

    public void Snap(float value)
    {
        from = to = Value = value;
        elapsed = duration = 0;
    }

    // Returns true while the value is still moving, so callers know whether the panel needs another frame.
    public bool Advance(float deltaSeconds)
    {
        if (!IsRunning)
        {
            return false;
        }
        elapsed = MathF.Min(duration, elapsed + MathF.Max(0, deltaSeconds));
        float t = duration <= 0 ? 1 : elapsed / duration;
        Value = from + (to - from) * EasingFunctions.Apply(Easing, t);
        return IsRunning;
    }
}
