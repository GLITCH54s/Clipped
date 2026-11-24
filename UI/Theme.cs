using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;
using Clipped.Core;

namespace Clipped.UI;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public sealed record Palette(
    bool IsLight,
    Color Bg,
    Color BgSoft,
    Color Surface,
    Color SurfaceAlt,
    Color Hover,
    Color Selected,
    Color Chip,
    Color Field,
    Color Border,
    Color BorderHi,
    Color Text,
    Color Muted,
    Color Faint,
    Color Accent,
    Color AccentHi,
    Color AccentDeep,
    Color OnAccent,
    Color Danger,
    Color Warn,
    Color Ok);

public static class Theme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\CurrentVersion\DWM";
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    private static Palette _palette = Build(ThemeMode.System);
    private static bool _monitoring;

    /// <summary>Raised with the previous and new palette whenever the theme changes.</summary>
    public static event Action<Palette, Palette>? Changed;

    public static ThemeMode Mode { get; private set; } = ThemeMode.System;
    public static Palette Current => _palette;
    public static bool IsLight => _palette.IsLight;

    public static Color Bg => _palette.Bg;
    public static Color BgSoft => _palette.BgSoft;
    public static Color Surface => _palette.Surface;
    public static Color SurfaceAlt => _palette.SurfaceAlt;
    public static Color Hover => _palette.Hover;
    public static Color Selected => _palette.Selected;
    public static Color Chip => _palette.Chip;
    public static Color Field => _palette.Field;
    public static Color Border => _palette.Border;
    public static Color BorderHi => _palette.BorderHi;
    public static Color Text => _palette.Text;
    public static Color Muted => _palette.Muted;
    public static Color Faint => _palette.Faint;
    public static Color Accent => _palette.Accent;
    public static Color AccentHi => _palette.AccentHi;
    public static Color AccentDeep => _palette.AccentDeep;
    public static Color OnAccent => _palette.OnAccent;
    public static Color Danger => _palette.Danger;
    public static Color Warn => _palette.Warn;
    public static Color Ok => _palette.Ok;

    public static ThemeMode ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => ThemeMode.Light,
        "dark" => ThemeMode.Dark,
        _ => ThemeMode.System,
    };

    /// <summary>Follows Windows by default; a forced light/dark value overrides it.</summary>
    public static void Init(ThemeMode mode)
    {
        var env = Environment.GetEnvironmentVariable("CLIPPED_THEME");
        if (!string.IsNullOrWhiteSpace(env) &&
            Enum.TryParse<ThemeMode>(env, true, out var forced))
        {
            mode = forced;
        }
        Mode = mode;
        _palette = Build(mode);
    }

    public static void SetMode(ThemeMode mode)
    {
        Mode = mode;
        var next = Build(mode);
        if (next == _palette) return;
        var old = _palette;
        _palette = next;
        Changed?.Invoke(old, next);
    }

    public static void Refresh() => SetMode(Mode);

    public static void StartMonitoring()
    {
        if (_monitoring) return;
        _monitoring = true;
        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            try { Refresh(); }
            catch (Exception ex) { Log.Error("Theme refresh failed", ex); }
        };
    }

    private static Palette Build(ThemeMode mode)
    {
        bool light = mode == ThemeMode.Light || (mode == ThemeMode.System && SystemUsesLightTheme());
        var accent = SystemAccent();
        var accentHi = Mix(accent, Color.White, light ? 0.14f : 0.16f);
        var accentDeep = Mix(accent, Color.Black, 0.18f);
        var onAccent = Luminance(accent) > 0.62 ? Hex("#1A1A1A") : Color.White;

        if (light)
        {
            // Neutral Windows surfaces, pulled a few percent toward the user's accent.
            return new Palette(
                true,
                Mix(Hex("#F3F3F3"), accent, 0.035f),
                Mix(Hex("#EAEAEA"), accent, 0.04f),
                Mix(Hex("#FFFFFF"), accent, 0.015f),
                Mix(Hex("#F7F7F7"), accent, 0.025f),
                Mix(Hex("#F0F0F1"), accent, 0.045f),
                Mix(Hex("#E7E7EA"), accent, 0.05f),
                Mix(Hex("#ECECEF"), accent, 0.035f),
                Hex("#FFFFFF"),
                Mix(Hex("#E1E1E4"), accent, 0.05f),
                Mix(Hex("#C7C7CC"), accent, 0.04f),
                Hex("#1A1A1A"), Hex("#5C5C5C"), Hex("#8A8A8A"),
                accent, accentHi, accentDeep, onAccent,
                Hex("#C42B1C"), Hex("#9D5D00"), Hex("#0F7B0F"));
        }

        return new Palette(
            false,
            Mix(Hex("#1F1F23"), accent, 0.055f),
            Mix(Hex("#19191D"), accent, 0.05f),
            Mix(Hex("#2B2B30"), accent, 0.05f),
            Mix(Hex("#333338"), accent, 0.055f),
            Mix(Hex("#3A3A41"), accent, 0.06f),
            Mix(Hex("#3E3E47"), accent, 0.07f),
            Mix(Hex("#3A3A40"), accent, 0.05f),
            Mix(Hex("#2D2D33"), accent, 0.045f),
            Mix(Hex("#3A3A40"), accent, 0.06f),
            Mix(Hex("#4F4F58"), accent, 0.05f),
            Color.White, Hex("#C5C5C9"), Hex("#8E8E95"),
            accent, accentHi, accentDeep, onAccent,
            Hex("#FF99A4"), Hex("#FCE100"), Hex("#6CCB5F"));
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false);
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color SystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccentKey, false);
            if (key?.GetValue("AccentColorMenu") is int v)
            {
                // Stored as 0xAABBGGRR.
                int r = v & 0xFF, g = (v >> 8) & 0xFF, b = (v >> 16) & 0xFF;
                if (r + g + b > 24) return Color.FromArgb(255, r, g, b);
            }
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DwmKey, false);
            if (key?.GetValue("AccentColor") is int v)
            {
                int r = v & 0xFF, g = (v >> 8) & 0xFF, b = (v >> 16) & 0xFF;
                if (r + g + b > 24) return Color.FromArgb(255, r, g, b);
            }
        }
        catch { }

        return Hex("#0067C0");
    }

    private static double Luminance(Color c) =>
        (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            return Color.FromArgb(
                Convert.ToInt32(hex[..2], 16),
                Convert.ToInt32(hex[2..4], 16),
                Convert.ToInt32(hex[4..6], 16));
        }
        return Color.White;
    }

    public static Color WithAlpha(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c);

    /// <summary>The color a transparent child control is actually drawn on top of.</summary>
    public static Color SurfaceOf(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is RoundPanel panel) return panel.Fill;
            if (parent.BackColor.A == 255) return parent.BackColor;
        }
        return Bg;
    }

    /// <summary>Re-maps cached palette colors on every control after a live theme change.</summary>
    public static void ApplyTo(Control root, Palette old) => ApplyTo(root, old, _palette);

    private static void ApplyTo(Control control, Palette old, Palette now)
    {
        if (control.BackColor.A != 0)
            control.BackColor = Map(control.BackColor, old, now);

        if (control is TextBox)
        {
            control.BackColor = now.Field;
            control.ForeColor = now.Text;
            Native.ApplyControlTheme(control, now.IsLight);
        }
        else if (control is Panel)
        {
            Native.ApplyControlTheme(control, now.IsLight, "ScrollBar");
        }

        if (control is not TextBox && control.ForeColor.A != 0)
            control.ForeColor = Map(control.ForeColor, old, now);

        control.Invalidate();
        foreach (Control child in control.Controls) ApplyTo(child, old, now);
    }

    private static Color Map(Color color, Palette old, Palette now)
    {
        if (color == old.Bg) return now.Bg;
        if (color == old.BgSoft) return now.BgSoft;
        if (color == old.Surface) return now.Surface;
        if (color == old.SurfaceAlt) return now.SurfaceAlt;
        if (color == old.Hover) return now.Hover;
        if (color == old.Selected) return now.Selected;
        if (color == old.Chip) return now.Chip;
        if (color == old.Field) return now.Field;
        if (color == old.Border) return now.Border;
        if (color == old.BorderHi) return now.BorderHi;
        if (color == old.Text) return now.Text;
        if (color == old.Muted) return now.Muted;
        if (color == old.Faint) return now.Faint;
        if (color == old.Accent) return now.Accent;
        if (color == old.AccentHi) return now.AccentHi;
        if (color == old.AccentDeep) return now.AccentDeep;
        if (color == old.OnAccent) return now.OnAccent;
        if (color == old.Danger) return now.Danger;
        if (color == old.Warn) return now.Warn;
        if (color == old.Ok) return now.Ok;
        return color;
    }

    private static readonly Dictionary<string, Font> FontCache = new();
    private static readonly object FontGate = new();
    private static string? _uiFamily;
    private static string? _displayFamily;
    private static string? _iconFamily;

    private static string? FindFamily(params string[] names)
    {
        using var collection = new InstalledFontCollection();
        var available = collection.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
            if (available.Contains(n)) return n;
        return null;
    }

    /// <summary>Segoe Fluent/MDL2 glyph font used for the picker's bottom icon bar.</summary>
    public static Font Icon(float pixels, FontStyle style = FontStyle.Regular)
    {
        lock (FontGate)
        {
            _iconFamily ??= FindFamily("Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol") ?? "Segoe UI Symbol";
            var key = "i|" + _iconFamily + "|" + pixels + "|" + (int)style;
            if (!FontCache.TryGetValue(key, out var font))
            {
                font = new Font(_iconFamily, pixels, style, GraphicsUnit.Pixel);
                FontCache[key] = font;
            }
            return font;
        }
    }

    public static Font UiPx(float pixels, FontStyle style = FontStyle.Regular)
    {
        lock (FontGate)
        {
            _uiFamily ??= FindFamily("Segoe UI Variable Text", "Segoe UI", "Tahoma") ?? "Segoe UI";
            var key = "p|" + _uiFamily + "|" + pixels + "|" + (int)style;
            if (!FontCache.TryGetValue(key, out var font))
            {
                font = new Font(_uiFamily, pixels, style, GraphicsUnit.Pixel);
                FontCache[key] = font;
            }
            return font;
        }
    }

    public static Font Ui(float points, FontStyle style = FontStyle.Regular)
    {
        lock (FontGate)
        {
            _uiFamily ??= FindFamily("Segoe UI Variable Text", "Segoe UI", "Tahoma") ?? "Segoe UI";
            var key = "u|" + _uiFamily + "|" + points + "|" + (int)style;
            if (!FontCache.TryGetValue(key, out var font))
            {
                font = new Font(_uiFamily, points, style, GraphicsUnit.Point);
                FontCache[key] = font;
            }
            return font;
        }
    }

    public static Font Display(float points, FontStyle style = FontStyle.Regular)
    {
        lock (FontGate)
        {
            _displayFamily ??= FindFamily("Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI") ?? "Segoe UI";
            var key = "d|" + _displayFamily + "|" + points + "|" + (int)style;
            if (!FontCache.TryGetValue(key, out var font))
            {
                font = new Font(_displayFamily, points, style, GraphicsUnit.Point);
                FontCache[key] = font;
            }
            return font;
        }
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0.1f)
        {
            path.AddRectangle(r);
            return path;
        }
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color color)
    {
        using var path = RoundRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void StrokeRound(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var path = RoundRect(RectangleF.Inflate(r, -0.5f * width, -0.5f * width), radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    public static void TextAt(Graphics g, string text, Font font, Color color, float x, float y)
    {
        TextRenderer.DrawText(g, text, font,
            new Point((int)Math.Round(x), (int)Math.Round(y)), color,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    public static void TextIn(Graphics g, string text, Font font, Color color, RectangleF bounds,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1) return;
        TextRenderer.DrawText(g, text ?? "", font,
            Rectangle.Round(bounds), color,
            flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    public static Size Measure(string text, Font font, TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine)
        => TextRenderer.MeasureText(text ?? "", font, new Size(int.MaxValue, int.MaxValue), flags);

    /// <summary>Flat window surface — tinted toward the accent, like the Windows clipboard.</summary>
    public static void PaintCanvas(Graphics g, Size size)
    {
        int w = Math.Max(1, size.Width), h = Math.Max(1, size.Height);
        using var brush = new SolidBrush(Bg);
        g.FillRectangle(brush, 0, 0, w, h);
    }

    /// <summary>Soft glass lighting for single-window surfaces such as the picker.</summary>
    public static void PaintCanvasGlass(Graphics g, Size size)
    {
        int w = Math.Max(1, size.Width), h = Math.Max(1, size.Height);
        using var brush = new LinearGradientBrush(new Rectangle(0, 0, w, h), CanvasTop, CanvasBottom, 90f);
        g.FillRectangle(brush, 0, 0, w, h);
    }

    /// <summary>Paints the canvas color that would sit behind a child control, so glass panels blend in.</summary>
    public static void PaintCanvasBehind(Graphics g, Control control)
    {
        var parent = control.Parent;
        if (parent is Form)
        {
            var bounds = control.Bounds;
            var brushRect = new RectangleF(-bounds.X, -bounds.Y,
                Math.Max(1, parent.ClientSize.Width), Math.Max(1, parent.ClientSize.Height));
            using var brush = new LinearGradientBrush(brushRect, CanvasTop, CanvasBottom, 90f);
            g.FillRectangle(brush, 0, 0, Math.Max(1, control.Width), Math.Max(1, control.Height));
            return;
        }

        g.Clear(SurfaceOf(control));
    }

    public static Color CanvasTop => Mix(Bg, Accent, IsLight ? 0.05f : 0.09f);
    public static Color CanvasBottom => IsLight ? Mix(Bg, Color.White, 0.55f) : Mix(Bg, Color.Black, 0.18f);
}

public static class AppIcon
{
    private static Icon? _icon;

    // The logo: a clipboard character with a smirk, drawn in the Windows accent.
    // Reference space is a 149x133 raster of the artwork; K maps it onto the icon.
    private const float RefBodyL = 41f, RefBodyR = 111f, RefBodyT = 26f, RefBodyB = 111f;
    private const float RefTop = 14f;

    public static Icon Get(int size = 64)
    {
        if (_icon is not null) return _icon;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float k = size * 0.00887f;
            float ox = size * 0.19f, oy = size * 0.07f;
            float X(float rx) => ox + (rx - RefBodyL) * k;
            float Y(float ry) => oy + (ry - RefTop) * k;

            using var pen = new Pen(Theme.Accent, Math.Max(1.1f, size * 0.040f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            // clipboard body
            g.DrawRectangle(pen, X(RefBodyL), Y(RefBodyT),
                            X(RefBodyR) - X(RefBodyL), Y(RefBodyB) - Y(RefBodyT));

            // clip arch
            g.DrawLine(pen, X(64f), Y(26f), X(64f), Y(23f));
            g.DrawLine(pen, X(82f), Y(26f), X(82f), Y(23f));
            g.DrawArc(pen, X(64f), Y(14f), X(82f) - X(64f), Y(32f) - Y(14f), 180f, 180f);

            // torn paper edge
            g.DrawLine(pen, X(50f), Y(29.5f), X(96f), Y(29.5f));
            g.DrawLine(pen, X(50f), Y(29.5f), X(50f), Y(44f));
            g.DrawLine(pen, X(96f), Y(29.5f), X(96f), Y(44f));
            var zig = new PointF[17];
            for (int i = 0; i < zig.Length; i++)
            {
                float t = i / 16f;
                float rx = 50f + 46f * t;
                float ry = (i % 2 == 0) ? 40.8f : 44.4f;
                zig[i] = new PointF(X(rx), Y(ry));
            }
            g.DrawLines(pen, zig);

            // the face: a smirk, not a smile
            using var fill = new SolidBrush(Theme.Accent);
            g.FillEllipse(fill, X(55.1f), Y(58.6f), X(63.9f) - X(55.1f), Y(67.4f) - Y(58.6f));

            var squint = new PointF[29];
            for (int i = 0; i < squint.Length; i++)
            {
                float t = i / 28f;
                float rx = 81.5f + 12f * t;
                float ry = 63.7f - 5f * (float)Math.Sin(Math.PI * t) - 0.9f * t;
                squint[i] = new PointF(X(rx), Y(ry));
            }
            g.DrawLines(pen, squint);

            g.DrawBezier(pen, X(58f), Y(80f), X(69f), Y(88.5f), X(80f), Y(85.5f), X(89.5f), Y(78f));
            g.DrawLine(pen, X(89.5f), Y(78f), X(93.5f), Y(72f));
        }

        var handle = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(handle);
            _icon = (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
        return _icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}

public static class WindowsIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Clipped";
    private const string ProtocolKey = @"Software\Classes\clipped";
    private const string AppName = "Clipped";
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    private static readonly bool Packaged = DetectPackaged();

    // Packaged (MSIX) builds get their protocol, startup task and Installed-apps entry from the manifest.
    public static bool IsPackaged => Packaged;

    private static bool DetectPackaged()
    {
        try
        {
            uint length = 0;
            return GetCurrentPackageFullName(ref length, null) != APPMODEL_ERROR_NO_PACKAGE;
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, System.Text.StringBuilder? packageFullName);

    public static bool StartupEnabled
    {
        get
        {
            if (IsPackaged) return false;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) is string s && s.Length > 0;
        }
    }

    public static void SetStartup(bool enabled)
    {
        if (IsPackaged) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (key is null) return;
        if (enabled) key.SetValue(AppName, "\"" + Paths.ExePath + "\"");
        else key.DeleteValue(AppName, false);
    }

    public static bool IntegrationRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, false);
            return key is not null;
        }
    }

    // A setup program owns the entry when its uninstaller, not this exe, removes the install.
    public static bool InstallerOwnsUninstallEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, false);
        var value = key?.GetValue("UninstallString") as string;
        if (string.IsNullOrEmpty(value)) return false;
        return !value.Contains(Paths.ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static string AppVersion
    {
        get
        {
            var v = typeof(WindowsIntegration).Assembly.GetName().Version;
            return v is null || v.Build < 0 ? "0.1.0" : v.ToString(3);
        }
    }

    public static string InstallFolder => Path.GetDirectoryName(Paths.ExePath) ?? "";

    public static string InstallSizeText
    {
        get
        {
            var kb = FolderSizeKb(InstallFolder);
            if (kb <= 0) return "unknown";
            return kb >= 1024 ? (kb / 1024.0).ToString("0.#") + " MB" : kb + " KB";
        }
    }

    // Runs whatever owns the uninstall entry: the bundled uninstaller, or this exe's own --uninstall.
    public static void LaunchUninstall()
    {
        string? command = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, false);
            command = key?.GetValue("UninstallString") as string;
        }
        catch { }

        if (string.IsNullOrWhiteSpace(command))
            command = "\"" + Paths.ExePath + "\" --uninstall";

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo { UseShellExecute = true };
            if (command.StartsWith('"'))
            {
                var close = command.IndexOf('"', 1);
                if (close > 0)
                {
                    psi.FileName = command[1..close];
                    psi.Arguments = command[(close + 1)..].Trim();
                }
                else psi.FileName = command;
            }
            else
            {
                var space = command.IndexOf(' ');
                psi.FileName = space > 0 ? command[..space] : command;
                psi.Arguments = space > 0 ? command[(space + 1)..].Trim() : "";
            }
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Error("uninstall launch failed", ex);
        }
    }

    private static int FolderSizeKb(string dir)
    {
        try
        {
            long bytes = 0;
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                bytes += file.Length;
            return (int)Math.Min(int.MaxValue, bytes / 1024);
        }
        catch
        {
            return 0;
        }
    }

    public static void RegisterIntegration(bool enabled)
    {
        if (IsPackaged)
        {
            Log.Info("Windows integration is owned by the MSIX manifest");
            return;
        }

        try
        {
            if (enabled)
            {
                var exe = Paths.ExePath;

                if (!InstallerOwnsUninstallEntry())
                {
                    using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, true);
                    if (key is not null)
                    {
                        var dir = Path.GetDirectoryName(exe) ?? "";
                        key.SetValue("DisplayName", AppName);
                        key.SetValue("DisplayVersion", AppVersion);
                        key.SetValue("Publisher", "Clipped contributors");
                        key.SetValue("DisplayIcon", exe + ",0");
                        key.SetValue("InstallLocation", dir);
                        key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                        key.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall");
                        key.SetValue("ModifyPath", "\"" + exe + "\" --modify");
                        key.SetValue("NoModify", 0, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                        key.SetValue("URLInfoAbout", "https://github.com/GLITCH54s/clipped");
                        key.SetValue("URLUpdateInfo", "https://github.com/GLITCH54s/clipped/releases");
                        // Keep the original install date when the toggle is re-applied.
                        if (key.GetValue("InstallDate") is not string existing || existing.Length != 8)
                            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                        var sizeKb = FolderSizeKb(dir);
                        if (sizeKb > 0) key.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
                    }
                }

                using (var key = Registry.CurrentUser.CreateSubKey(ProtocolKey, true))
                {
                    if (key is not null)
                    {
                        key.SetValue("", "URL:Clipped");
                        key.SetValue("URL Protocol", "");
                        key.SetValue("FriendlyTypeName", "Clipped link");
                        using var icon = key.CreateSubKey("DefaultIcon", true);
                        icon?.SetValue("", exe + ",0");
                        using var cmd = key.CreateSubKey(@"shell\open\command", true);
                        cmd?.SetValue("", "\"" + exe + "\" \"%1\"");
                    }
                }

                CreateStartShortcut(exe);
            }
            else
            {
                if (!InstallerOwnsUninstallEntry())
                    Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                Registry.CurrentUser.DeleteSubKeyTree(ProtocolKey, false);
                DeleteStartShortcut();
            }

            NotifyShell();
            Log.Info("Windows integration " + (enabled ? "enabled" : "disabled"));
        }
        catch (Exception ex)
        {
            Log.Error("Windows integration change failed", ex);
        }
    }

    private static string StartMenuPath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs\Clipped");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "Clipped.lnk");
        }
    }

    private static void CreateStartShortcut(string exe)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic lnk = shell.CreateShortcut(StartMenuPath);
            lnk.TargetPath = exe;
            lnk.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
            lnk.IconLocation = exe + ",0";
            lnk.Description = "Clipped — clipboard picker and snippet library";
            lnk.Arguments = "--picker";
            lnk.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Start shortcut failed", ex);
        }
    }

    private static void DeleteStartShortcut()
    {
        try { File.Delete(StartMenuPath); } catch { }
    }

    public static void NotifyShell()
    {
        try
        {
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void OpenSettingsPage(string page)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:" + page)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open ms-settings", ex);
        }
    }
}
