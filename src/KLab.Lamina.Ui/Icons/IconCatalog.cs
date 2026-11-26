// Copyright (c) K Lab. Licensed under the MIT License.

// Icon geometry on a 20x20 grid, carried over from the old UiIconDrawing path data.
// Stroked shapes use round caps and joins at IconCatalog.StrokeWidth; filled shapes use the foreground color.
namespace KLab.Lamina.Ui;

public sealed record IconShape(string[] Strokes, string[] Fills);

public static class IconCatalog
{
    public const float GridSize = 20;
    public const float StrokeWidth = 1.5f;

    // Icons drawn as the horizontal mirror of their stored path (redo is undo flipped).
    private static readonly HashSet<string> Mirrored = new() { "redo" };

    // Legacy glyph code points and aliases that older settings and layouts still use as icon names.
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["back"] = "left",
        ["\uE700"] = "menu",
        ["\uE710"] = "add",
        ["\uE8C8"] = "copy",
        ["\uE70B"] = "edit",
        ["\uE76C"] = "right",
        ["\uE70D"] = "down",
        ["\uE80F"] = "home",
        ["\uE7F4"] = "screen",
        ["\uE8A5"] = "note",
        ["\uE74C"] = "extensions",
        ["\uE713"] = "settings",
        ["\uE9D9"] = "diagnostics",
        ["\uE921"] = "tray",
        ["\uE7E8"] = "power",
        ["\uE72E"] = "shield",
        ["\uE77B"] = "user",
    };

    private static Dictionary<string, IconShape> Shapes => IconShapes.All;

    public static IEnumerable<string> Names => Shapes.Keys.Order(StringComparer.Ordinal);

    public static bool Contains(string name) => Shapes.ContainsKey(Canonical(name));

    public static string Canonical(string name) => Aliases.TryGetValue(name, out var alias) ? alias : name;

    // Unknown names fall back to the "info" icon, like the old renderer.
    public static (VectorPath[] Strokes, VectorPath[] Fills) Get(string name)
    {
        var shape = Shapes.TryGetValue(Canonical(name), out var found) ? found : Shapes["info"];
        var strokes = shape.Strokes.Select(VectorPath.Parse).ToArray();
        var fills = shape.Fills.Select(VectorPath.Parse).ToArray();
        return (strokes, fills);
    }

    public static bool IsMirrored(string name) => Mirrored.Contains(Canonical(name));
}
