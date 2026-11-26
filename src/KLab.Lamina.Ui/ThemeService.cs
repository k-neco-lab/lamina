// Copyright (c) K Lab. Licensed under the MIT License.

namespace KLab.Lamina.Ui;

// Reactive runtime theme manager propagating InterfaceFontScale and UI/Settings/Hint
// background transparency across all VR UiPanelHosts and desktop console surfaces.
public sealed class ThemeService
{
    public const float MinFontScale = 0.8f;
    public const float MaxFontScale = 1.25f;
    public const double MaxTransparencyPercent = 90.0;

    private Theme current = Theme.Default;
    private Theme settingsTheme = Theme.Default;
    private Theme hintTheme = Theme.Default;

    public ThemeService()
    {
        Apply(1.0, 15.0, 15.0, 15.0);
    }

    public Theme Current => current;
    public Theme SettingsTheme => settingsTheme;
    public Theme HintTheme => hintTheme;
    public float FontScale { get; private set; } = 1f;
    public double InterfaceTransparency { get; private set; } = 15.0;
    public double SettingsTransparency { get; private set; } = 15.0;
    public double HintTransparency { get; private set; } = 15.0;
    public float InterfaceOpacity => ToOpacity(InterfaceTransparency);
    public float SettingsOpacity => ToOpacity(SettingsTransparency);
    public float HintOpacity => ToOpacity(HintTransparency);

    public event Action? Changed;

    public bool Apply(double fontScale, double interfaceTransparencyPercent,
        double settingsTransparencyPercent, double hintTransparencyPercent)
    {
        float nextScale = double.IsFinite(fontScale)
            ? Math.Clamp((float)fontScale, MinFontScale, MaxFontScale)
            : 1f;
        double nextUi = ClampTransparency(interfaceTransparencyPercent);
        double nextSettings = ClampTransparency(settingsTransparencyPercent);
        double nextHint = ClampTransparency(hintTransparencyPercent);

        if (MathF.Abs(nextScale - FontScale) < 1e-4f &&
            Math.Abs(nextUi - InterfaceTransparency) < 1e-4 &&
            Math.Abs(nextSettings - SettingsTransparency) < 1e-4 &&
            Math.Abs(nextHint - HintTransparency) < 1e-4)
        {
            return false;
        }

        FontScale = nextScale;
        InterfaceTransparency = nextUi;
        SettingsTransparency = nextSettings;
        HintTransparency = nextHint;

        current = BuildTheme(nextScale, nextUi);
        settingsTheme = BuildTheme(nextScale, nextSettings);
        hintTheme = BuildTheme(nextScale, nextHint);
        Changed?.Invoke();
        return true;
    }

    public Theme ThemeForHost(string hostName)
    {
        if (string.Equals(hostName, "settings", StringComparison.OrdinalIgnoreCase))
        {
            return settingsTheme;
        }
        if (hostName.Contains("hint", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(hostName, "toolbar-tip", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(hostName, "grab-focus", StringComparison.OrdinalIgnoreCase))
        {
            return hintTheme;
        }
        return current;
    }

    private static double ClampTransparency(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.0, MaxTransparencyPercent) : 15.0;

    private static float ToOpacity(double transparencyPercent) =>
        Math.Clamp(1f - (float)(transparencyPercent / 100.0), 0.1f, 1f);

    private static Theme BuildTheme(float fontScale, double transparencyPercent)
    {
        float opacity = ToOpacity(transparencyPercent);
        byte panelAlpha = (byte)Math.Clamp((int)MathF.Round(255f * opacity), 25, 255);
        byte popupAlpha = (byte)Math.Clamp((int)MathF.Round(255f * MathF.Min(1f, opacity + 0.04f)), 25, 255);
        var baseTheme = Theme.Default;
        return baseTheme with
        {
            FontScale = fontScale,
            Panel = baseTheme.Panel.WithAlpha(panelAlpha),
            Dialog = baseTheme.Dialog.WithAlpha(panelAlpha),
            Popup = baseTheme.Popup.WithAlpha(popupAlpha),
            ContentPanel = baseTheme.ContentPanel.WithAlpha(panelAlpha),
            NavigationPanel = baseTheme.NavigationPanel.WithAlpha(panelAlpha),
            Section = baseTheme.Section.WithAlpha(panelAlpha),
        };
    }
}
