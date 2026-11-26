// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;
using KLab.Lamina.Ui;
using Vortice.DirectWrite;

namespace KLab.Lamina.D2D;

/// <summary>
/// Bounded LRU cache of DirectWrite <see cref="IDWriteTextFormat"/> and <see cref="IDWriteTextLayout"/> objects
/// with an explicit CJK system font fallback chain (zh-CN -> Microsoft YaHei UI, ja-JP -> Yu Gothic UI,
/// ko-KR -> Malgun Gothic, plus the OS system fallback).
/// </summary>
public sealed class TextLayoutCache : ITextMeasurer, IDisposable
{
    public const int DefaultMaxLayouts = 512;
    public const int DefaultMaxFormats = 128;
    private const float MaxMeasureWidth = 100000f;

    private readonly IDWriteFactory2 factory;
    private readonly bool ownsFactory;
    private readonly int maxLayouts;
    private readonly int maxFormats;
    private readonly Dictionary<FormatKey, LinkedListNode<CacheEntry<FormatKey, IDWriteTextFormat>>> formats = new();
    private readonly LinkedList<CacheEntry<FormatKey, IDWriteTextFormat>> formatLru = new();
    private readonly Dictionary<LayoutKey, LinkedListNode<CacheEntry<LayoutKey, IDWriteTextLayout>>> layouts = new();
    private readonly LinkedList<CacheEntry<LayoutKey, IDWriteTextLayout>> layoutLru = new();
    private IDWriteFontFallback? fontFallback;
    private string locale = "zh-CN";
    private bool disposed;

    private readonly record struct FormatKey(
        string Family,
        float Size,
        Ui.FontWeight Weight,
        TextAlign Align,
        TextTrimming Trimming);

    private readonly record struct LayoutKey(string Text, FormatKey Format, float Width, float Height);

    private readonly record struct CacheEntry<TKey, TValue>(TKey Key, TValue Value);

    public TextLayoutCache(
        IDWriteFactory2? factory = null,
        int maxLayouts = DefaultMaxLayouts,
        int maxFormats = DefaultMaxFormats,
        string locale = "zh-CN")
    {
        if (maxLayouts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLayouts), "Max layouts must be positive.");
        }
        if (maxFormats <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFormats), "Max formats must be positive.");
        }

        if (factory is null)
        {
            this.factory = DWrite.DWriteCreateFactory<IDWriteFactory2>();
            ownsFactory = true;
        }
        else
        {
            this.factory = factory;
            ownsFactory = false;
        }

        this.maxLayouts = maxLayouts;
        this.maxFormats = maxFormats;
        this.locale = string.IsNullOrWhiteSpace(locale) ? "zh-CN" : locale;
        fontFallback = BuildFallback(this.locale);
    }

    public int MaxLayouts => maxLayouts;
    public int MaxFormats => maxFormats;
    public int CachedLayoutCount => layouts.Count;
    public int CachedFormatCount => formats.Count;

    public string Locale
    {
        get => locale;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            string normalized = string.IsNullOrWhiteSpace(value) ? "zh-CN" : value;
            if (string.Equals(locale, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            locale = normalized;
            ClearCaches();
            fontFallback?.Dispose();
            fontFallback = BuildFallback(locale);
        }
    }

    public IDWriteTextLayout Get(string text, TextStyle style, float width, float height)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(style);

        var formatKey = new FormatKey(style.FontFamily, style.Size, style.Weight, style.Align, style.Trimming);
        var key = new LayoutKey(text ?? string.Empty, formatKey, Quantize(width), Quantize(height));

        if (layouts.TryGetValue(key, out var existing))
        {
            layoutLru.Remove(existing);
            layoutLru.AddFirst(existing);
            return existing.Value.Value;
        }

        if (layouts.Count >= maxLayouts && layoutLru.Last is { } oldest)
        {
            layoutLru.RemoveLast();
            layouts.Remove(oldest.Value.Key);
            oldest.Value.Value.Dispose();
        }

        var layout = factory.CreateTextLayout(key.Text, GetOrCreateFormat(formatKey), key.Width, key.Height);
        var node = new LinkedListNode<CacheEntry<LayoutKey, IDWriteTextLayout>>(new CacheEntry<LayoutKey, IDWriteTextLayout>(key, layout));
        layoutLru.AddFirst(node);
        layouts[key] = node;
        return layout;
    }

    public Vector2 MeasureText(string text, TextStyle style, float maxWidth = float.PositiveInfinity)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrEmpty(text))
        {
            return Vector2.Zero;
        }

        float width = float.IsInfinity(maxWidth) || float.IsNaN(maxWidth) ? MaxMeasureWidth : MathF.Max(0f, maxWidth);
        var metrics = Get(text, style, width, MaxMeasureWidth).Metrics;
        return new Vector2(MathF.Ceiling(metrics.WidthIncludingTrailingWhitespace), MathF.Ceiling(metrics.Height));
    }

    public void ClearCaches()
    {
        foreach (var node in layoutLru)
        {
            node.Value.Dispose();
        }
        foreach (var node in formatLru)
        {
            node.Value.Dispose();
        }
        layouts.Clear();
        layoutLru.Clear();
        formats.Clear();
        formatLru.Clear();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        ClearCaches();
        fontFallback?.Dispose();
        fontFallback = null;
        if (ownsFactory)
        {
            factory.Dispose();
        }
    }

    private static float Quantize(float value) => MathF.Round(MathF.Max(0f, value) * 4f) / 4f;

    private IDWriteTextFormat GetOrCreateFormat(FormatKey key)
    {
        if (formats.TryGetValue(key, out var existing))
        {
            formatLru.Remove(existing);
            formatLru.AddFirst(existing);
            return existing.Value.Value;
        }

        if (formats.Count >= maxFormats && formatLru.Last is { } oldest)
        {
            formatLru.RemoveLast();
            formats.Remove(oldest.Value.Key);
            oldest.Value.Value.Dispose();
        }

        var format = factory.CreateTextFormat(
            key.Family,
            null!,
            (Vortice.DirectWrite.FontWeight)(int)key.Weight,
            FontStyle.Normal,
            FontStretch.Normal,
            key.Size,
            locale);

        if (fontFallback is not null)
        {
            using var format1 = format.QueryInterfaceOrNull<IDWriteTextFormat1>();
            if (format1 is not null)
            {
                format1.FontFallback = fontFallback;
            }
        }

        format.WordWrapping = WordWrapping.NoWrap;
        format.TextAlignment = key.Align switch
        {
            TextAlign.Center => Vortice.DirectWrite.TextAlignment.Center,
            TextAlign.Right => Vortice.DirectWrite.TextAlignment.Trailing,
            _ => Vortice.DirectWrite.TextAlignment.Leading,
        };

        if (key.Trimming == TextTrimming.Ellipsis)
        {
            using var sign = factory.CreateEllipsisTrimmingSign(format);
            format.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, sign);
        }

        var node = new LinkedListNode<CacheEntry<FormatKey, IDWriteTextFormat>>(new CacheEntry<FormatKey, IDWriteTextFormat>(key, format));
        formatLru.AddFirst(node);
        formats[key] = node;
        return format;
    }

    private IDWriteFontFallback? BuildFallback(string culture)
    {
        try
        {
            using var builder = factory.CreateFontFallbackBuilder();
            string cjkFamily = culture.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                ? "Yu Gothic UI"
                : culture.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
                    ? "Malgun Gothic"
                    : "Microsoft YaHei UI";
            string cjkLocale = culture.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                ? "ja-JP"
                : culture.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
                    ? "ko-KR"
                    : "zh-CN";
            var cjkRanges = new[]
            {
                new UnicodeRange { First = 0x2E80, Last = 0x9FFF },
                new UnicodeRange { First = 0xF900, Last = 0xFAFF },
                new UnicodeRange { First = 0xFF00, Last = 0xFFEF },
                new UnicodeRange { First = 0x20000, Last = 0x2FA1F },
            };
            unsafe
            {
                fixed (char* familyPtr = cjkFamily)
                {
                    nint* familyPtrs = stackalloc nint[1];
                    familyPtrs[0] = (nint)familyPtr;
                    builder.AddMapping(cjkRanges, (uint)cjkRanges.Length, (nint)familyPtrs, 1, null!, cjkLocale, null!, 1f);
                }
            }
            using var systemFallback = factory.SystemFontFallback;
            builder.AddMappings(systemFallback);
            return builder.CreateFontFallback();
        }
        catch
        {
            return null;
        }
    }
}
