// Copyright (c) K Lab. Licensed under the MIT License.

using System.Globalization;

namespace KLab.Lamina.Ui;

public readonly record struct Color(byte A, byte R, byte G, byte B)
{
    public static Color Transparent => new(0, 0, 0, 0);
    public static Color White => new(255, 255, 255, 255);
    public static Color Black => new(255, 0, 0, 0);

    // Accepts "#RRGGBB" and "#AARRGGBB", the two formats used by the theme files.
    public static Color Parse(string text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '#' || (text.Length != 7 && text.Length != 9))
            throw new FormatException("Invalid color: " + text);
        uint value = uint.Parse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return text.Length == 7
            ? new Color(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : new Color((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public Color WithAlpha(byte alpha) => this with { A = alpha };

    public static Color Lerp(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Color(Mix(from.A, to.A, t), Mix(from.R, to.R, t), Mix(from.G, to.G, t), Mix(from.B, to.B, t));
    }

    private static byte Mix(byte a, byte b, float t) => (byte)MathF.Round(a + (b - a) * t);
}
