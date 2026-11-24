using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using Clipped.Core;

namespace Clipped.UI;

/// <summary>
/// Renders Segoe UI Emoji glyphs in color by drawing the font's COLR layers
/// (the Windows font has no bitmap strikes, so DirectWrite-free GDI outlines plus
/// the CPAL palette reproduce exactly what Windows shows).
/// </summary>
internal static class EmojiArt
{
    private const uint GgoNative = 2;
    private const uint GgoGlyphIndex = 0x80;
    private const uint GmemFixed = 0x0040;

    private static readonly Dictionary<(string Emoji, int Px, int Ink), Bitmap?> Cache = new();
    private static readonly Dictionary<int, IntPtr> Dcs = new();
    private static FontData? _font;
    private static bool _failed;

    /// <summary>Color bitmap for the emoji, or null when the font cannot draw it.</summary>
    public static Bitmap? Get(string emoji, int px, Color ink)
    {
        if (_failed || string.IsNullOrEmpty(emoji)) return null;
        px = Math.Clamp(px, 10, 256);
        var key = (emoji, px, ink.ToArgb());
        if (Cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            _font ??= FontData.Load(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "seguiemj.ttf"));
            var art = Render(DcFor(px), emoji, px, ink);
            if (Cache.Count > 600)
            {
                foreach (var bmp in Cache.Values) bmp?.Dispose();
                Cache.Clear();
            }
            Cache[key] = art;
            return art;
        }
        catch (Exception ex)
        {
            Log.Error("Emoji render failed for " + emoji, ex);
            _failed = true;
            return null;
        }
    }

    public static void Clear()
    {
        foreach (var bmp in Cache.Values) bmp?.Dispose();
        Cache.Clear();
    }

    private static IntPtr DcFor(int px)
    {
        if (Dcs.TryGetValue(px, out var dc)) return dc;
        dc = CreateCompatibleDC(IntPtr.Zero);
        var font = CreateFont(-px, 0, 0, 0, 400, 0, 0, 0, 1, 5, 2, 4, 0, "Segoe UI Emoji");
        SelectObject(dc, font);
        SetMapMode(dc, 1);
        Dcs[px] = dc;
        return dc;
    }

    private static Bitmap? Render(IntPtr dc, string emoji, int px, Color ink)
    {
        var glyphs = Shape(dc, emoji);
        if (glyphs.Length == 0) return null;

        var shapes = new List<(GraphicsPath Path, Color Color)>();
        float cursor = 0;
        foreach (var gid in glyphs)
        {
            var record = _font!.Base(gid);
            if (record is null)
            {
                var outline = Outline(dc, gid);
                if (outline is not null)
                {
                    outline.Transform(new Matrix(1, 0, 0, 1, cursor, 0));
                    shapes.Add((outline, ink));
                }
            }
            else
            {
                for (int i = 0; i < record.Value.Count; i++)
                {
                    var layer = _font.Layer(record.Value.First + i);
                    var outline = Outline(dc, layer.Glyph);
                    if (outline is null) continue;
                    outline.Transform(new Matrix(1, 0, 0, 1, cursor, 0));
                    shapes.Add((outline, layer.Palette == 0xFFFF ? ink : _font.PaletteColor(layer.Palette)));
                }
            }
            cursor += Advance(dc, gid);
        }
        if (shapes.Count == 0) return null;

        var bounds = shapes[0].Path.GetBounds();
        foreach (var (path, _) in shapes) bounds = RectangleF.Union(bounds, path.GetBounds());
        if (bounds.Width < 0.5f || bounds.Height < 0.5f) return null;

        int box = px + px / 4;
        var art = new Bitmap(box, box, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(art);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Math.Min((box - 4f) / bounds.Width, (box - 4f) / bounds.Height);
        g.TranslateTransform(
            box / 2f - bounds.Width * scale / 2f - bounds.X * scale,
            box / 2f - bounds.Height * scale / 2f - bounds.Y * scale);
        g.ScaleTransform(scale, scale);
        foreach (var (path, color) in shapes)
        {
            if (color.A == 0) continue;
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
        return art;
    }

    private static ushort[] Shape(IntPtr dc, string text)
    {
        IntPtr chars = Marshal.StringToCoTaskMemUni(text);
        IntPtr cache = IntPtr.Zero;
        try
        {
            var control = new ScriptControl();
            var state = new ScriptState();
            var items = new ScriptItem[4];
            if (ScriptItemize(chars, text.Length, 4, ref control, ref state, items, out _) != 0)
                return Array.Empty<ushort>();

            var analysis = items[0].Analysis;
            int max = text.Length * 4 + 4;
            var glyphs = new ushort[max];
            var clusters = new ushort[text.Length];
            var attrs = new ScriptVisAttr[max];
            if (ScriptShape(dc, ref cache, chars, text.Length, max, ref analysis,
                    glyphs, clusters, attrs, out int count) != 0)
                return Array.Empty<ushort>();

            var result = new ushort[count];
            Array.Copy(glyphs, result, count);
            return result;
        }
        finally
        {
            if (cache != IntPtr.Zero) ScriptFreeCache(ref cache);
            Marshal.FreeCoTaskMem(chars);
        }
    }

    private static GraphicsPath? Outline(IntPtr dc, ushort glyph)
    {
        var matrix = new Mat2 { M11 = 0x10000, M22 = 0x10000 };
        var metrics = new GlyphMetrics();
        uint size = GetGlyphOutline(dc, glyph, GgoNative | GgoGlyphIndex, ref metrics, 0, IntPtr.Zero, ref matrix);
        if (size is 0 or 0xFFFFFFFF) return null;

        IntPtr buffer = GlobalAlloc(GmemFixed, size);
        try
        {
            if (GetGlyphOutline(dc, glyph, GgoNative | GgoGlyphIndex, ref metrics, size, buffer, ref matrix) == 0xFFFFFFFF)
                return null;

            var path = new GraphicsPath(FillMode.Winding);
            int at = 0;
            while (at + 16 <= size)
            {
                var header = Marshal.PtrToStructure<PolygonHeader>(buffer + at);
                if (header.Size < 16) break;
                PointF current = Point(header.Start);
                int p = at + 16;
                int end = at + (int)header.Size;
                bool started = false;
                while (p + 4 <= end)
                {
                    var curve = Marshal.PtrToStructure<PolyCurve>(buffer + p);
                    int pts = p + 4;
                    var points = new PointF[curve.Count];
                    for (int i = 0; i < curve.Count; i++)
                        points[i] = Point(Marshal.PtrToStructure<PointFx>(buffer + pts + i * 8));

                    if (curve.Type == 1)
                    {
                        foreach (var pt in points)
                        {
                            if (!started) { path.StartFigure(); started = true; }
                            path.AddLine(current, pt);
                            current = pt;
                        }
                    }
                    else if (curve.Type == 2)
                    {
                        for (int i = 0; i < points.Length - 1; i++)
                        {
                            var control = points[i];
                            var to = i == points.Length - 2 ? points[^1] : Mid(points[i], points[i + 1]);
                            if (!started) { path.StartFigure(); started = true; }
                            AddQuad(path, current, control, to);
                            current = to;
                        }
                    }
                    p = pts + curve.Count * 8;
                }
                if (started) path.CloseFigure();
                at += (int)header.Size;
            }
            return path.PointCount > 0 ? path : null;
        }
        finally
        {
            GlobalFree(buffer);
        }
    }

    private static float Advance(IntPtr dc, ushort glyph)
    {
        var matrix = new Mat2 { M11 = 0x10000, M22 = 0x10000 };
        var metrics = new GlyphMetrics();
        GetGlyphOutline(dc, glyph, GgoNative | GgoGlyphIndex, ref metrics, 0, IntPtr.Zero, ref matrix);
        return metrics.IncX;
    }

    private static PointF Point(PointFx p) => new(p.X / 65536f, -p.Y / 65536f);

    private static PointF Mid(PointF a, PointF b) => new((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);

    private static void AddQuad(GraphicsPath path, PointF from, PointF control, PointF to)
    {
        var c1 = new PointF(from.X + 2f / 3f * (control.X - from.X), from.Y + 2f / 3f * (control.Y - from.Y));
        var c2 = new PointF(to.X + 2f / 3f * (control.X - to.X), to.Y + 2f / 3f * (control.Y - to.Y));
        path.AddBezier(from, c1, c2, to);
    }

    private sealed class FontData
    {
        private byte[] _data = null!;
        private int _baseOffset, _baseCount, _layerOffset, _colorOffset, _paletteStart, _paletteSize;

        public static FontData Load(string path)
        {
            var font = new FontData { _data = File.ReadAllBytes(path) };
            int count = font.U16(4), colr = 0, cpal = 0;
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                var tag = Encoding.ASCII.GetString(font._data, record, 4);
                if (tag == "COLR") colr = font.I32(record + 8);
                if (tag == "CPAL") cpal = font.I32(record + 8);
            }
            if (colr == 0 || cpal == 0) throw new InvalidDataException("font has no COLR/CPAL tables");

            font._baseCount = font.U16(colr + 2);
            font._baseOffset = colr + font.I32(colr + 4);
            font._layerOffset = colr + font.I32(colr + 8);

            font._paletteSize = font.U16(cpal + 2);
            font._colorOffset = cpal + font.I32(cpal + 8);
            font._paletteStart = font.U16(cpal + 12);
            return font;
        }

        private ushort U16(int at) => (ushort)((_data[at] << 8) | _data[at + 1]);

        private int I32(int at) =>
            (_data[at] << 24) | (_data[at + 1] << 16) | (_data[at + 2] << 8) | _data[at + 3];

        public (int First, int Count)? Base(ushort glyph)
        {
            int lo = 0, hi = _baseCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2, at = _baseOffset + mid * 6;
                int g = U16(at);
                if (g == glyph) return (U16(at + 2), U16(at + 4));
                if (g < glyph) lo = mid + 1;
                else hi = mid - 1;
            }
            return null;
        }

        public (ushort Glyph, ushort Palette) Layer(int index)
        {
            int at = _layerOffset + index * 4;
            return (U16(at), U16(at + 2));
        }

        public Color PaletteColor(int palette)
        {
            int at = _colorOffset + (_paletteStart + palette) * 4;
            return Color.FromArgb(_data[at + 3], _data[at + 2], _data[at + 1], _data[at]);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScriptControl
    {
        public uint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScriptState
    {
        public ushort Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScriptAnalysis
    {
        public ushort Script;
        public ushort Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScriptItem
    {
        public int Position;
        public ScriptAnalysis Analysis;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScriptVisAttr
    {
        public ushort Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mat2
    {
        public int M11, M12, M21, M22;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphMetrics
    {
        public uint BlackBoxX, BlackBoxY;
        public int OriginX, OriginY;
        public short IncX, IncY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointFx
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PolygonHeader
    {
        public uint Size;
        public uint Type;
        public PointFx Start;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PolyCurve
    {
        public ushort Type;
        public ushort Count;
    }

    [DllImport("usp10.dll")]
    private static extern int ScriptItemize(IntPtr chars, int count, int maxItems,
        ref ScriptControl control, ref ScriptState state, [Out] ScriptItem[] items, out int itemCount);

    [DllImport("usp10.dll")]
    private static extern int ScriptShape(IntPtr dc, ref IntPtr cache, IntPtr chars, int count,
        int maxGlyphs, ref ScriptAnalysis analysis, [Out] ushort[] glyphs, [Out] ushort[] clusters,
        [Out] ScriptVisAttr[] attrs, out int glyphCount);

    [DllImport("usp10.dll")]
    private static extern void ScriptFreeCache(ref IntPtr cache);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation,
        int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision,
        uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern int SetMapMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint GetGlyphOutline(IntPtr dc, uint glyph, uint format,
        ref GlyphMetrics metrics, uint bufferSize, IntPtr buffer, ref Mat2 matrix);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, uint bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
