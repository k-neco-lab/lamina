// Copyright (c) K Lab. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;

namespace KLab.Lamina.Ui;

public enum PathVerb
{
    MoveTo,
    LineTo,
    CubicTo,
    Close,
}

public readonly record struct PathSegment(PathVerb Verb, Vector2 P1, Vector2 P2, Vector2 P3);

// Path made of move/line/cubic/close only; SVG quadratic and arc commands are converted on parse.
public sealed class VectorPath
{
    private readonly List<PathSegment> segments = new();

    public IReadOnlyList<PathSegment> Segments => segments;

    public void MoveTo(Vector2 p) => segments.Add(new PathSegment(PathVerb.MoveTo, p, default, default));

    public void LineTo(Vector2 p) => segments.Add(new PathSegment(PathVerb.LineTo, p, default, default));

    public void CubicTo(Vector2 c1, Vector2 c2, Vector2 end) =>
        segments.Add(new PathSegment(PathVerb.CubicTo, c1, c2, end));

    public void Close() => segments.Add(new PathSegment(PathVerb.Close, default, default, default));

    public static VectorPath Parse(string data) => SvgPathParser.Parse(data);

    public static VectorPath RoundedRect(float x, float y, float w, float h, float r)
    {
        r = MathF.Min(r, MathF.Min(w, h) / 2);
        const float k = 0.5522847f;
        var path = new VectorPath();
        path.MoveTo(new Vector2(x + r, y));
        path.LineTo(new Vector2(x + w - r, y));
        path.CubicTo(new(x + w - r + r * k, y), new(x + w, y + r - r * k), new(x + w, y + r));
        path.LineTo(new Vector2(x + w, y + h - r));
        path.CubicTo(new(x + w, y + h - r + r * k), new(x + w - r + r * k, y + h), new(x + w - r, y + h));
        path.LineTo(new Vector2(x + r, y + h));
        path.CubicTo(new(x + r - r * k, y + h), new(x, y + h - r + r * k), new(x, y + h - r));
        path.LineTo(new Vector2(x, y + r));
        path.CubicTo(new(x, y + r - r * k), new(x + r - r * k, y), new(x + r, y));
        path.Close();
        return path;
    }

    public static VectorPath Circle(float cx, float cy, float r) => RoundedRect(cx - r, cy - r, r * 2, r * 2, r);
}

internal static class SvgPathParser
{
    public static VectorPath Parse(string data)
    {
        var path = new VectorPath();
        var scanner = new Scanner(data);
        Vector2 current = default, start = default, lastControl = default;
        char command = ' ';
        char previous = ' ';
        while (scanner.SkipSeparators())
        {
            if (char.IsLetter(scanner.Peek()))
            {
                command = scanner.Next();
            }
            else if (command == ' ')
            {
                throw new FormatException("Path data must start with a command");
            }
            else if (command == 'M')
            {
                command = 'L';
            }
            else if (command == 'm')
            {
                command = 'l';
            }
            bool relative = char.IsLower(command);
            Vector2 origin = relative ? current : Vector2.Zero;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    current = start = origin + scanner.Point();
                    path.MoveTo(current);
                    break;
                case 'L':
                    current = origin + scanner.Point();
                    path.LineTo(current);
                    break;
                case 'H':
                    current = new Vector2((relative ? current.X : 0) + scanner.Number(), current.Y);
                    path.LineTo(current);
                    break;
                case 'V':
                    current = new Vector2(current.X, (relative ? current.Y : 0) + scanner.Number());
                    path.LineTo(current);
                    break;
                case 'C':
                    {
                        var c1 = origin + scanner.Point();
                        var c2 = origin + scanner.Point();
                        var end = origin + scanner.Point();
                        path.CubicTo(c1, c2, end);
                        lastControl = c2;
                        current = end;
                        break;
                    }
                case 'S':
                    {
                        var c1 = previous is 'C' or 'S' ? current * 2 - lastControl : current;
                        var c2 = origin + scanner.Point();
                        var end = origin + scanner.Point();
                        path.CubicTo(c1, c2, end);
                        lastControl = c2;
                        current = end;
                        break;
                    }
                case 'Q':
                    {
                        var c = origin + scanner.Point();
                        var end = origin + scanner.Point();
                        AddQuad(path, current, c, end);
                        lastControl = c;
                        current = end;
                        break;
                    }
                case 'A':
                    {
                        float rx = scanner.Number(), ry = scanner.Number(), rotation = scanner.Number();
                        bool large = scanner.Flag(), sweep = scanner.Flag();
                        var end = origin + scanner.Point();
                        AddArc(path, current, rx, ry, rotation, large, sweep, end);
                        current = end;
                        break;
                    }
                case 'Z':
                    path.Close();
                    current = start;
                    break;
                default:
                    throw new FormatException("Unsupported path command " + command);
            }
            previous = char.ToUpperInvariant(command);
        }
        return path;
    }

    private static void AddQuad(VectorPath path, Vector2 p0, Vector2 c, Vector2 p1) =>
        path.CubicTo(p0 + (c - p0) * (2f / 3f), p1 + (c - p1) * (2f / 3f), p1);

    // SVG implementation notes F.6: endpoint parameterization to center parameterization, then cubic pieces.
    private static void AddArc(
        VectorPath path, Vector2 from, float rx, float ry, float degrees, bool large, bool sweep, Vector2 to)
    {
        rx = MathF.Abs(rx);
        ry = MathF.Abs(ry);
        if (from == to)
        {
            return;
        }
        if (rx == 0 || ry == 0)
        {
            path.LineTo(to);
            return;
        }
        float phi = degrees * MathF.PI / 180, cos = MathF.Cos(phi), sin = MathF.Sin(phi);
        var d = (from - to) / 2;
        float x1 = cos * d.X + sin * d.Y, y1 = -sin * d.X + cos * d.Y;
        float lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1)
        {
            float scale = MathF.Sqrt(lambda);
            rx *= scale;
            ry *= scale;
        }
        float numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        float denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        float factor = MathF.Sqrt(MathF.Max(0, numerator / denominator)) * (large == sweep ? -1 : 1);
        float cxp = factor * rx * y1 / ry, cyp = -factor * ry * x1 / rx;
        var center = new Vector2(
            cos * cxp - sin * cyp + (from.X + to.X) / 2,
            sin * cxp + cos * cyp + (from.Y + to.Y) / 2);
        float theta = MathF.Atan2((y1 - cyp) / ry, (x1 - cxp) / rx);
        float end = MathF.Atan2((-y1 - cyp) / ry, (-x1 - cxp) / rx);
        float delta = end - theta;
        if (sweep && delta < 0)
        {
            delta += 2 * MathF.PI;
        }
        else if (!sweep && delta > 0)
        {
            delta -= 2 * MathF.PI;
        }
        int pieces = (int)MathF.Ceiling(MathF.Abs(delta) / (MathF.PI / 2) - 1e-4f);
        float step = delta / Math.Max(1, pieces);
        float t = 4f / 3f * MathF.Tan(step / 4);
        for (int i = 0; i < pieces; i++)
        {
            float a0 = theta + step * i, a1 = a0 + step;
            Vector2 Map(float angle, float ox, float oy)
            {
                float px = rx * (MathF.Cos(angle) + ox), py = ry * (MathF.Sin(angle) + oy);
                return new Vector2(cos * px - sin * py + center.X, sin * px + cos * py + center.Y);
            }
            path.CubicTo(
                Map(a0, -t * MathF.Sin(a0), t * MathF.Cos(a0)),
                Map(a1, t * MathF.Sin(a1), -t * MathF.Cos(a1)),
                i == pieces - 1 ? to : Map(a1, 0, 0));
        }
    }

    private ref struct Scanner
    {
        private readonly string text;
        private int index;

        public Scanner(string text)
        {
            this.text = text;
            index = 0;
        }

        public bool SkipSeparators()
        {
            while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == ','))
            {
                index++;
            }
            return index < text.Length;
        }

        public char Peek() => text[index];

        public char Next() => text[index++];

        public Vector2 Point() => new(Number(), Number());

        public bool Flag()
        {
            SkipSeparators();
            return text[index++] == '1';
        }

        public float Number()
        {
            SkipSeparators();
            int begin = index;
            if (index < text.Length && (text[index] == '-' || text[index] == '+'))
            {
                index++;
            }
            bool dot = false;
            while (index < text.Length)
            {
                char c = text[index];
                if (char.IsDigit(c))
                {
                    index++;
                }
                else if (c == '.' && !dot)
                {
                    dot = true;
                    index++;
                }
                else if ((c == 'e' || c == 'E') && index > begin)
                {
                    index++;
                    if (index < text.Length && (text[index] == '-' || text[index] == '+'))
                    {
                        index++;
                    }
                }
                else
                {
                    break;
                }
            }
            return float.Parse(text.AsSpan(begin, index - begin), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
