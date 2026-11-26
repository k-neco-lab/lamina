// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

public enum GridUnit
{
    Fixed,
    Auto,
    Star,
}

public readonly record struct GridLength(GridUnit Unit, float Value)
{
    public static GridLength Pixels(float value) => new(GridUnit.Fixed, value);
    public static GridLength Auto => new(GridUnit.Auto, 0);
    public static GridLength Star(float weight = 1) => new(GridUnit.Star, weight);
}

public sealed class Grid : ContainerControl
{
    private readonly Dictionary<Control, (int Row, int Column)> cells = new();
    private float[] columnSizes = Array.Empty<float>();
    private float[] rowSizes = Array.Empty<float>();

    public List<GridLength> Columns { get; } = new() { GridLength.Star() };
    public List<GridLength> Rows { get; } = new() { GridLength.Star() };

    public void Add(Control child, int row, int column)
    {
        cells[child] = (row, column);
        Add(child);
    }

    protected override Vector2 MeasureContent(Vector2 available, ITextMeasurer measurer)
    {
        columnSizes = new float[Columns.Count];
        rowSizes = new float[Rows.Count];
        // Pass 1: fixed tracks, and auto tracks sized by their children measured without limit.
        for (int i = 0; i < Columns.Count; i++)
        {
            columnSizes[i] = Columns[i].Unit == GridUnit.Fixed ? Columns[i].Value : 0;
        }
        for (int i = 0; i < Rows.Count; i++)
        {
            rowSizes[i] = Rows[i].Unit == GridUnit.Fixed ? Rows[i].Value : 0;
        }
        foreach (var child in Items.Where(c => c.Visible))
        {
            var (row, column) = CellOf(child);
            bool autoColumn = Columns[column].Unit == GridUnit.Auto;
            bool autoRow = Rows[row].Unit == GridUnit.Auto;
            if (!autoColumn && !autoRow)
            {
                continue;
            }
            var probe = new Vector2(
                autoColumn ? float.PositiveInfinity : columnSizes[column],
                autoRow ? float.PositiveInfinity : rowSizes[row]);
            var size = child.Measure(probe, measurer);
            if (autoColumn)
            {
                columnSizes[column] = MathF.Max(columnSizes[column], size.X);
            }
            if (autoRow)
            {
                rowSizes[row] = MathF.Max(rowSizes[row], size.Y);
            }
        }
        DistributeStars(Columns, columnSizes, available.X);
        DistributeStars(Rows, rowSizes, available.Y);
        // Pass 2: measure every child at its final cell size so wrapped/ellipsized content sees its real width.
        foreach (var child in Items.Where(c => c.Visible))
        {
            var (row, column) = CellOf(child);
            child.Measure(new Vector2(columnSizes[column], rowSizes[row]), measurer);
        }
        return new Vector2(columnSizes.Sum(), rowSizes.Sum());
    }

    protected override void ArrangeContent(Vector2 size)
    {
        // Star tracks re-resolve against the final size; fixed and auto stay as measured.
        DistributeStars(Columns, columnSizes, size.X);
        DistributeStars(Rows, rowSizes, size.Y);
        foreach (var child in Items.Where(c => c.Visible))
        {
            var (row, column) = CellOf(child);
            child.Arrange(new Rect(
                columnSizes.Take(column).Sum(),
                rowSizes.Take(row).Sum(),
                columnSizes[column],
                rowSizes[row]));
        }
    }

    private (int Row, int Column) CellOf(Control child)
    {
        var (row, column) = cells.TryGetValue(child, out var cell) ? cell : (0, 0);
        return (Math.Clamp(row, 0, Rows.Count - 1), Math.Clamp(column, 0, Columns.Count - 1));
    }

    private static void DistributeStars(List<GridLength> definitions, float[] sizes, float available)
    {
        float totalWeight = definitions.Where(d => d.Unit == GridUnit.Star).Sum(d => d.Value);
        if (totalWeight <= 0 || float.IsInfinity(available))
        {
            return;
        }
        float used = 0;
        for (int i = 0; i < sizes.Length; i++)
        {
            if (definitions[i].Unit != GridUnit.Star)
            {
                used += sizes[i];
            }
        }
        float remaining = MathF.Max(0, available - used);
        for (int i = 0; i < sizes.Length; i++)
        {
            if (definitions[i].Unit == GridUnit.Star)
            {
                sizes[i] = remaining * definitions[i].Value / totalWeight;
            }
        }
    }
}
