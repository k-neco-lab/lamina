// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

public sealed class Slider : Control
{
    private const float TrackThickness = 4f;
    private float minimum;
    private float maximum = 1;
    private float value;

    public event Action<float>? ValueChanged;

    public override bool IsInteractive => true;

    public override bool CancelsPressWhenPointerLeaves => false;

    public float Minimum
    {
        get => minimum;
        set => SetRender(ref minimum, value);
    }

    public float Maximum
    {
        get => maximum;
        set => SetRender(ref maximum, value);
    }

    public float Value
    {
        get => value;
        set
        {
            float clamped = Math.Clamp(value, Minimum, MathF.Max(Minimum, Maximum));
            if (this.value != clamped)
            {
                this.value = clamped;
                InvalidateRender();
                ValueChanged?.Invoke(clamped);
            }
        }
    }

    public override void OnPointerDown(PointerEventArgs e) => SetFromPosition(e.Position.X);

    public override void OnDrag(PointerEventArgs e) => SetFromPosition(e.Position.X);

    protected override Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer) =>
        new(float.IsInfinity(available.X) ? 160 : available.X, Theme.ControlHeight);

    protected override void ArrangeContent(Vector2 size)
    {
    }

    protected override void RenderContent(IDrawContext context, Rect area)
    {
        var theme = Theme;
        float knob = area.Height * 0.6f;
        float span = MathF.Max(1, area.Width - knob);
        float fraction = Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0;
        float centerY = area.Height / 2;
        float knobX = knob / 2 + span * fraction;
        var track = new Rect(knob / 2, centerY - TrackThickness / 2, area.Width - knob, TrackThickness);
        context.FillRoundedRect(track, TrackThickness / 2, theme.SliderTrack);
        context.FillRoundedRect(track with { Width = knobX - knob / 2 }, TrackThickness / 2, theme.Accent);
        context.FillRoundedRect(new Rect(knobX - knob / 2, centerY - knob / 2, knob, knob), knob / 2, IsEnabled
            ? theme.Text : theme.Disabled);
    }

    private void SetFromPosition(float x)
    {
        float knob = Bounds.Height * 0.6f;
        float span = MathF.Max(1, Bounds.Width - knob);
        float fraction = Math.Clamp((x - knob / 2) / span, 0, 1);
        Value = Minimum + (Maximum - Minimum) * fraction;
    }
}
