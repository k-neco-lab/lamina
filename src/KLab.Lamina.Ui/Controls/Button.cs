// Copyright (c) K Lab. Licensed under the MIT License.

namespace KLab.Lamina.Ui;

public enum ButtonState
{
    Normal,
    Hover,
    Pressed,
    Disabled,
}

public class Button : Panel
{
    private const float HoverDuration = 0.11f;
    private const float PressDuration = 0.07f;

    private bool selected;
    private float hoverAnim;
    private float pressAnim;

    public bool Selected
    {
        get => selected;
        set => SetRender(ref selected, value);
    }

    public bool Borderless { get; set; }

    public event Action? Clicked;

    public override bool IsInteractive => true;

    public ButtonState State => !IsEnabled
        ? ButtonState.Disabled
        : IsPressed ? ButtonState.Pressed : IsHovered ? ButtonState.Hover : ButtonState.Normal;

    // Content is centered inside the button.
    public void SetContent(Control content)
    {
        content.HorizontalAlignment = Alignment.Center;
        content.VerticalAlignment = Alignment.Center;
        Clear();
        Add(content);
    }

    public override void OnPointerEnter(PointerEventArgs e)
    {
        InvalidateRender();
        Root?.RequestTick(this);
    }

    public override void OnPointerLeave(PointerEventArgs e)
    {
        InvalidateRender();
        Root?.RequestTick(this);
    }

    public override void OnPointerDown(PointerEventArgs e)
    {
        if (e.Button == PointerButton.Left)
        {
            InvalidateRender();
            Root?.RequestTick(this);
        }
    }

    public override void OnPointerUp(PointerEventArgs e)
    {
        InvalidateRender();
        Root?.RequestTick(this);
    }

    public override void OnPointerCancel(PointerEventArgs e)
    {
        InvalidateRender();
        Root?.RequestTick(this);
    }

    public override void OnClick(PointerEventArgs e)
    {
        if (e.Button == PointerButton.Left)
        {
            Clicked?.Invoke();
        }
    }

    public override bool Tick(float deltaSeconds)
    {
        float dt = Math.Clamp(deltaSeconds, 0f, 0.05f);
        float targetHover = IsHovered && IsEnabled ? 1f : 0f;
        float targetPress = IsPressed && IsEnabled ? 1f : 0f;
        bool active = false;
        if (MathF.Abs(hoverAnim - targetHover) > 0.005f)
        {
            float step = dt / HoverDuration;
            hoverAnim = targetHover > hoverAnim
                ? MathF.Min(1f, hoverAnim + step)
                : MathF.Max(0f, hoverAnim - step);
            active = MathF.Abs(hoverAnim - targetHover) > 0.005f;
        }
        else
        {
            hoverAnim = targetHover;
        }
        if (MathF.Abs(pressAnim - targetPress) > 0.005f)
        {
            float step = dt / PressDuration;
            pressAnim = targetPress > pressAnim
                ? MathF.Min(1f, pressAnim + step)
                : MathF.Max(0f, pressAnim - step);
            active |= MathF.Abs(pressAnim - targetPress) > 0.005f;
        }
        else
        {
            pressAnim = targetPress;
        }
        InvalidateRender();
        return active;
    }

    protected override void RenderContent(IDrawContext context, Rect area)
    {
        var theme = Theme;
        Color fill;
        Color border;
        if (!IsEnabled)
        {
            fill = theme.DisabledControl;
            border = theme.ControlBorder;
        }
        else
        {
            float h = hoverAnim;
            float p = pressAnim;
            if (Borderless && !Selected)
            {
                if (h <= 0.005f && p <= 0.005f)
                {
                    RenderChildren(context);
                    return;
                }
                fill = Color.Lerp(theme.Hover.WithAlpha(0), theme.Hover, h);
                fill = Color.Lerp(fill, theme.Pressed, p);
                border = Color.Lerp(theme.ControlBorder.WithAlpha(0), theme.ControlBorder, MathF.Max(h, p));
            }
            else
            {
                var rest = Selected ? theme.Selected : theme.Control;
                fill = Color.Lerp(rest, theme.Hover, h);
                fill = Color.Lerp(fill, theme.Pressed, p);
                border = Selected
                    ? theme.SelectedBorder
                    : Color.Lerp(theme.ControlBorder, theme.SelectedBorder, h * 0.45f);
            }
        }
        DrawBox(context, area, fill, border, CornerRadius > 0 ? CornerRadius : theme.ControlRadius,
            theme.ControlBorderWidth);
        RenderChildren(context);
    }
}
