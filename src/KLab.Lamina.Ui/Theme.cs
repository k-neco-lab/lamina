// Copyright (c) K Lab. Licensed under the MIT License.

namespace KLab.Lamina.Ui;

// Every value below is carried over unchanged from the old ThemePalette, DesktopPalette, UiMetrics, UiTheme and
// DesktopUiTheme defaults. Look and feel changes belong to a later package.
public sealed record Theme
{
    public Color Text { get; init; } = Color.Parse("#F4F5F7");
    public Color Muted { get; init; } = Color.Parse("#A8B0BC");
    public Color Disabled { get; init; } = Color.Parse("#6E7480");
    public Color Panel { get; init; } = Color.Parse("#F614161B");
    public Color Border { get; init; } = Color.Parse("#38FFFFFF");
    public Color Accent { get; init; } = Color.Parse("#FF9D42");
    public Color Attention { get; init; } = Color.Parse("#FF9D42");
    public Color Control { get; init; } = Color.Parse("#FF222630");
    public Color Hover { get; init; } = Color.Parse("#FF382A1E");
    public Color Pressed { get; init; } = Color.Parse("#FF4B3019");
    public Color Selected { get; init; } = Color.Parse("#FF352517");
    public Color ControlBorder { get; init; } = Color.Parse("#30FFFFFF");
    public Color DisabledControl { get; init; } = Color.Parse("#901A1D24");
    public Color Popup { get; init; } = Color.Parse("#FA161920");
    public Color StrongBorder { get; init; } = Color.Parse("#70FFFFFF");
    public Color SliderTrack { get; init; } = Color.Parse("#FF3A3F4B");
    public Color Section { get; init; } = Color.Parse("#FF1B1E26");
    public Color SelectedBorder { get; init; } = Color.Parse("#D0FF9D42");
    public Color Divider { get; init; } = Color.Parse("#24FFFFFF");
    public Color Danger { get; init; } = Color.Parse("#FF5A5A");
    public Color OnAccent { get; init; } = Color.Parse("#FFFFFF");
    public Color Scrim { get; init; } = Color.Parse("#B0000000");
    public Color Dialog { get; init; } = Color.Parse("#F614161B");
    public Color ContentPanel { get; init; } = Color.Parse("#FF1E2129");
    public Color NavigationPanel { get; init; } = Color.Parse("#FF161920");
    public Color NavigationSelected { get; init; } = Color.Parse("#33FF9D42");

    public Color DesktopBackground { get; init; } = Color.Parse("#FF101010");
    public Color DesktopCard { get; init; } = Color.Parse("#FF181818");
    public Color DesktopBorder { get; init; } = Color.Parse("#FF303030");
    public Color DesktopText { get; init; } = Color.Parse("#FFF2F2F2");
    public Color DesktopMuted { get; init; } = Color.Parse("#FF999999");
    public Color DesktopControl { get; init; } = Color.Parse("#FF242424");
    public Color DesktopControlBorder { get; init; } = Color.Parse("#FF414141");
    public Color DesktopHover { get; init; } = Color.Parse("#FF34302C");
    public Color DesktopPressed { get; init; } = Color.Parse("#FF684329");
    public Color DesktopFocus { get; init; } = Color.Parse("#FFDA9255");
    public Color DesktopAttention { get; init; } = Color.Parse("#FFFFAF70");
    public Color DesktopSwitchOn { get; init; } = Color.Parse("#FFB86930");
    public Color DesktopSwitchOff { get; init; } = Color.Parse("#FF444444");

    public float PanelPadding { get; init; } = 24f;
    public float GroupPadding { get; init; } = 20f;
    public float Gap { get; init; } = 12f;
    public float SmallGap { get; init; } = 8f;
    public float ControlHeight { get; init; } = 34f;
    public float IconSize { get; init; } = 32f;
    public float HeaderHeight { get; init; } = 48f;
    public float ScrollbarWidth { get; init; } = 14f;
    public float PanelRadius { get; init; } = 12f;
    public float ControlRadius { get; init; } = 6f;
    public float PanelBorderWidth { get; init; } = 1f;
    public float ControlBorderWidth { get; init; } = 1f;
    public float NavigationWidth { get; init; } = 104f;
    public float NavigationItemHeight { get; init; } = 52f;
    public float NavigationGap { get; init; } = 6f;
    public float NavigationIconSize { get; init; } = 26f;
    public float NavigationPadding { get; init; } = 22f;
    public float NavigationInset { get; init; } = 8f;
    public float NavigationLabelGap { get; init; } = 3f;
    public float ContentInset { get; init; } = 12f;
    public float ContentRadius { get; init; } = 24f;
    public float NavigationRadius { get; init; } = 16f;
    public float SidebarRadius { get; init; } = 20f;
    public float PageTabRadius { get; init; } = 12f;
    public float PageTabHeight { get; init; } = 40f;
    public float SwitchWidth { get; init; } = 72f;
    public float SwitchHeight { get; init; } = 40f;
    public float SwitchThumbSize { get; init; } = 32f;
    public float IconStrokeWidth { get; init; } = 1.65f;
    public float IconGlyphSize { get; init; } = 20f;
    public float DialogWidth { get; init; } = 560f;
    public float DialogMinHeight { get; init; } = 280f;
    public float DialogPadding { get; init; } = 30f;

    // Fixed sizes from the old VR canvas theme (UiTheme).
    public float DefaultControlFontSize { get; init; } = 14;
    public float FontScale { get; init; } = 1f;
    public int ContentDensity { get; init; } = 2;

    // Colors from the old desktop window theme (DesktopUiTheme).
    public Color DesktopWindowBackground { get; init; } = Color.Parse("#181818");
    public Color DesktopSidebarBackground { get; init; } = Color.Parse("#131313");
    public Color DesktopSidebarBorder { get; init; } = Color.Parse("#232323");
    public Color DesktopCardBackground { get; init; } = Color.Parse("#202020");
    public Color DesktopSubtleBackground { get; init; } = Color.Parse("#1A1A1A");
    public Color DesktopInputBackground { get; init; } = Color.Parse("#151515");
    public Color DesktopBorderColor { get; init; } = Color.Parse("#2D2D2D");
    public Color DesktopMutedText { get; init; } = Color.Parse("#8E8E8E");
    public Color DesktopAccent { get; init; } = Color.Parse("#FF9D42");
    public Color DesktopError { get; init; } = Color.Parse("#FF7474");
    public Color DesktopWarning { get; init; } = Color.Parse("#FFAE5C");
    public Color DesktopSuccess { get; init; } = Color.Parse("#30D158");

    // The old app asks WPF for "Segoe UI, Microsoft YaHei UI"; DirectWrite resolves the first installed family.
    public string FontFamily { get; init; } = "Segoe UI";

    public static Theme Default { get; } = new();

    public TextStyle Body(Color? color = null) =>
        new(FontFamily, DefaultControlFontSize * Math.Clamp(FontScale, 0.8f, 1.25f), FontWeight.Regular, color ?? Text);
}
