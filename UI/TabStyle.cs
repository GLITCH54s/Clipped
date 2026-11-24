using Clipped.Core;

namespace Clipped.UI;

/// <summary>Icon catalog and palette shared by the tab editor and the picker's icon bar.</summary>
public static class TabStyle
{
    public sealed record IconOption(string Key, string Glyph, bool TextGlyph);

    public static readonly IconOption[] Icons =
    {
        new("", "–", true),
        new("at", "@", true),
        new("hash", "#", true),
        new("code", "\uE943", false),
        new("phone", "\uE717", false),
        new("mail", "\uE715", false),
        new("folder", "\uE8B7", false),
        new("star", "\uE734", false),
        new("pin", "\uE718", false),
        new("link", "\uE71B", false),
        new("globe", "\uE774", false),
        new("calendar", "\uE787", false),
        new("person", "\uE77B", false),
        new("chat", "\uE8BD", false),
        new("tag", "\uE8EC", false),
        new("note", "\uE7C3", false),
        new("clock", "\uE823", false),
    };

    public static IconOption Option(string? key) =>
        Icons.FirstOrDefault(i => i.Key == (key ?? "")) ?? Icons[0];

    public static Color Parse(string? hex, int fallbackIndex = 0)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex)) return ColorTranslator.FromHtml(hex);
        }
        catch { }
        var p = TabColors.Palette[Math.Max(0, fallbackIndex) % TabColors.Palette.Length];
        return ColorTranslator.FromHtml(p);
    }

    public static void DrawIcon(Graphics g, RectangleF box, string? key, Color color)
    {
        var option = Option(key);
        if (option.Key.Length == 0) return;
        float px = Math.Min(box.Width, box.Height);
        var font = option.TextGlyph ? Theme.UiPx(px * 0.88f, FontStyle.Bold) : Theme.Icon(px * 0.62f);
        Theme.TextIn(g, option.Glyph, font, color, box,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>Handset drawn from strokes; used where glyph fonts are missing.</summary>
    public static void DrawPhone(Graphics g, RectangleF box, Color color)
    {
        float px = Math.Min(box.Width, box.Height);
        using var pen = new Pen(color, Math.Max(1.4f, px * 0.09f))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        var r = new RectangleF(box.X + px * 0.22f, box.Y + px * 0.22f, px * 0.56f, px * 0.56f);
        g.DrawArc(pen, r, 135, 180);
        g.DrawLine(pen, r.X + r.Width * 0.15f, r.Y + r.Height * 0.15f, r.X + r.Width * 0.38f, r.Y + r.Height * 0.30f);
        g.DrawLine(pen, r.Right - r.Width * 0.15f, r.Bottom - r.Height * 0.15f,
            r.Right - r.Width * 0.38f, r.Bottom - r.Height * 0.30f);
    }
}
