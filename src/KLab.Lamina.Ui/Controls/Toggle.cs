// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

public sealed class Toggle : Control
{
    private const float TrackAspect = 2f;
    private const float KnobInset = 3f;
    private const float TransitionDuration = 0.12f;
    private bool isOn;
    private float animProgress;
    private bool attachedOnce;

    public event Action<bool>? Toggled;

    public override bool IsInteractive => true;

    public bool IsOn
    {
        get => isOn;
        set
        {
            if (isOn != value)
            {
                isOn = value;
                if (!attachedOnce && Root is null)
                {
                    animProgress = value ? 1f : 0f;
                }
                else
                {
                    attachedOnce = true;
                    Root?.RequestTick(this);
                }
                InvalidateRender();
                Toggled?.Invoke(value);
            }
        }
    }

    public override bool Tick(float deltaSeconds)
    {
        attachedOnce = true;
        float target = isOn ? 1f : 0f;
        if (MathF.Abs(animProgress - target) < 0.005f)
        {
            animProgress = target;
            InvalidateRender();
            return false;
        }
        float step = Math.Clamp(deltaSeconds, 0f, 0.05f) / TransitionDuration;
        animProgress = isOn ? MathF.Min(1f, animProgress + step) : MathF.Max(0f, animProgress - step);
        InvalidateRender();
        return MathF.Abs(animProgress - target) >= 0.005f;
    }

    public override void OnClick(PointerEventArgs e)
    {
        if (e.Button == PointerButton.Left)
        {
            IsOn = !IsOn;
        }
    }

    protected override Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer)
    {
        float height = Theme.ControlHeight * 0.7f;
        return new Vector2(height * TrackAspect, height);
    }

    protected override void ArrangeContent(Vector2 size)
    {
    }

    protected override void RenderContent(IDrawContext context, Rect area)
    {
        var theme = Theme;
        float t = animProgress * animProgress * (3f - 2f * animProgress);
        var track = IsEnabled
            ? Color.Lerp(theme.DesktopSwitchOff, theme.DesktopSwitchOn, t)
            : theme.DisabledControl;
        context.FillRoundedRect(area, area.Height / 2, track);
        float knob = area.Height - KnobInset * 2;
        float x = KnobInset + (area.Width - KnobInset * 2 - knob) * t;
        context.FillRoundedRect(new Rect(x, KnobInset, knob, knob), knob / 2, IsEnabled ? theme.Text : theme.Disabled);
    }
}
