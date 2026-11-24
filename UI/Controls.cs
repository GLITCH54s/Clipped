using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Clipped.Core;

namespace Clipped.UI;

/// <summary>A flat, dark list control with optional two-line rows and right-aligned tags.</summary>
public sealed class ItemList : Control
{
    public sealed class Entry
    {
        public string Title = "";
        public string Sub = "";
        public string Tag = "";
        public string Badge = "";
        public string Icon = "";
        public object? Payload;
        public Color? Accent;
    }

    private readonly List<Entry> _items = new();
    private int _selected = -1;
    private int _hover = -1;
    private float _s = 1f;
    private int _scroll;

    public int RowHeight { get; set; } = 46;
    public bool ShowBadges { get; set; }
    public bool Compact { get; set; }
    public event EventHandler? SelectionChanged;
    public event EventHandler? ItemActivated;

    public IReadOnlyList<Entry> Items => _items;
    public int SelectedIndex => _selected;
    public Entry? Selected => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public ItemList()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Font = Theme.Ui(9.5f);
    }

    public void SetItems(IEnumerable<Entry> items, int select = 0)
    {
        _items.Clear();
        _items.AddRange(items);
        _selected = _items.Count == 0 ? -1 : Math.Clamp(select, 0, _items.Count - 1);
        _scroll = 0;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Select(int index)
    {
        if (_items.Count == 0) { _selected = -1; return; }
        _selected = Math.Clamp(index, 0, _items.Count - 1);
        EnsureVisible();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private float S(float px) => px * (DeviceDpi / 96f);

    protected override void OnPaint(PaintEventArgs e)
    {
        _s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        if (_items.Count == 0)
        {
            Theme.TextIn(g, "Nothing here yet", Theme.Ui(9.5f), Theme.Faint,
                ClientRectangle, TextFormatFlags.HorizontalCenter);
            return;
        }

        int rowH = (int)S(RowHeight);
        int visible = Math.Max(1, ClientSize.Height / rowH);
        int maxScroll = Math.Max(0, _items.Count - visible);
        _scroll = Math.Clamp(_scroll, 0, maxScroll);
        int start = _scroll;
        int end = Math.Min(_items.Count, start + visible + 1);

        for (int i = start; i < end; i++)
        {
            var r = new RectangleF(S(2), i * rowH - _scroll * rowH + S(2), ClientSize.Width - S(6), rowH - S(4));
            DrawRow(g, i, r);
        }

        if (maxScroll > 0)
        {
            float trackH = ClientSize.Height;
            float thumbH = Math.Max(S(24), trackH * visible / _items.Count);
            float thumbY = (trackH - thumbH) * _scroll / maxScroll;
            Theme.FillRound(g, new RectangleF(ClientSize.Width - S(4), thumbY, S(3), thumbH), S(2), Theme.BorderHi);
        }
    }

    private void DrawRow(Graphics g, int index, RectangleF r)
    {
        var item = _items[index];
        bool selected = index == _selected;
        bool hover = index == _hover;

        if (selected || hover)
        {
            Theme.FillRound(g, r, S(6), selected ? Theme.Selected : Theme.Hover);
        }
        if (selected)
        {
            Theme.FillRound(g, new RectangleF(r.X + S(1), r.Y + S(7), S(3), r.Height - S(14)), S(1.5f), Theme.Accent);
        }
        else if (item.Accent is Color c)
        {
            Theme.FillRound(g, new RectangleF(r.X + S(1), r.Y + S(9), S(2), r.Height - S(18)), S(1), c);
        }

        float padL = r.X + S(12);
        float badgeW = 0;
        if (ShowBadges && (item.Badge.Length > 0 || item.Icon.Length > 0))
        {
            badgeW = S(22);
            var badgeRect = new RectangleF(padL, r.Y + (r.Height - S(20)) / 2, badgeW, S(20));
            if (item.Icon.Length > 0)
            {
                TabStyle.DrawIcon(g, badgeRect, item.Icon, selected ? Theme.Accent : Theme.Muted);
            }
            else
            {
                Theme.FillRound(g, badgeRect, S(5), selected ? Theme.WithAlpha(Theme.Accent, Theme.IsLight ? 26 : 44) : Theme.Chip);
                Theme.TextIn(g, item.Badge, Theme.Ui(8.5f, FontStyle.Bold),
                    selected ? Theme.Accent : Theme.Muted,
                    badgeRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            badgeW += S(10);
        }

        float textX = padL + badgeW;
        float tagW = item.Tag.Length > 0 ? (int)Theme.Measure(item.Tag, Theme.Ui(8f)).Width + (int)S(16) : 0;
        float textW = r.Width - (textX - r.X) - tagW - S(16);

        var titleRect = new RectangleF(textX, r.Y + S(Compact ? 8 : 6), textW, S(Compact ? 18 : 19));
        Theme.TextIn(g, item.Title, Theme.Ui(Compact ? 9.5f : 10f, selected ? FontStyle.Bold : FontStyle.Regular),
            Theme.Text, titleRect,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        if (Compact)
        {
            // right-aligned time in subtitle slot
            var subRect = new RectangleF(textX, r.Y + S(8), textW, S(18));
            Theme.TextIn(g, item.Sub, Theme.Ui(8.5f), Theme.Faint, subRect,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
        else if (item.Sub.Length > 0)
        {
            var subRect = new RectangleF(textX, r.Y + S(24), textW, S(16));
            Theme.TextIn(g, item.Sub, Theme.Ui(8.5f), Theme.Faint, subRect,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        if (tagW > 0)
        {
            var tagRect = new RectangleF(r.Right - tagW - S(8), r.Y + (r.Height - S(19)) / 2, tagW, S(19));
            Theme.FillRound(g, tagRect, S(9.5f), Theme.Chip);
            Theme.TextIn(g, item.Tag, Theme.Ui(7.8f), Theme.Muted, tagRect,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private int HitRow(Point p)
    {
        int rowH = (int)S(RowHeight);
        int index = p.Y / rowH + _scroll;
        if (p.Y < 0 || p.Y > ClientSize.Height) return -1;
        return index >= 0 && index < _items.Count ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int row = HitRow(e.Location);
        if (row != _hover)
        {
            _hover = row;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1)
        {
            _hover = -1;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var row = HitRow(e.Location);
        if (row < 0) return;
        Select(row);
        if (e.Clicks >= 2) ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int rowH = (int)S(RowHeight);
        int visible = Math.Max(1, ClientSize.Height / rowH);
        int maxScroll = Math.Max(0, _items.Count - visible);
        _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * 3, 0, maxScroll);
        Invalidate();
    }

    private void EnsureVisible()
    {
        int rowH = (int)S(RowHeight);
        int visible = Math.Max(1, ClientSize.Height / rowH);
        if (_selected < _scroll) _scroll = _selected;
        else if (_selected >= _scroll + visible) _scroll = _selected - visible + 1;
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Down) { Select(_selected + 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Up) { Select(_selected - 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Enter) { ItemActivated?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }
}

public sealed class PillButton : Control
{
    private bool _hover;
    private bool _down;

    public enum Style { Outline, Primary, Ghost, Danger, Subtle }

    public Style Kind { get; set; } = Style.Outline;

    public PillButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Ui(9f);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _down = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.SurfaceOf(this));
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        float s = DeviceDpi / 96f;

        Color fill, border, text;
        switch (Kind)
        {
            case Style.Primary:
                fill = _down ? Theme.AccentDeep : _hover ? Theme.AccentHi : Theme.Accent;
                border = fill;
                text = Theme.OnAccent;
                break;
            case Style.Danger:
                fill = _hover ? Theme.WithAlpha(Theme.Danger, 24) : Color.Transparent;
                border = Theme.WithAlpha(Theme.Danger, 90);
                text = Theme.Danger;
                break;
            case Style.Ghost:
                fill = _hover ? Theme.Hover : Color.Transparent;
                border = Color.Transparent;
                text = Theme.Text;
                break;
            case Style.Subtle:
                fill = _hover ? Theme.Hover : Theme.Chip;
                border = Color.Transparent;
                text = Theme.Text;
                break;
            default:
                fill = _hover ? Theme.Hover : Color.Transparent;
                border = _hover ? Theme.BorderHi : Theme.Border;
                text = Enabled ? Theme.Text : Theme.Faint;
                break;
        }

        if (fill.A > 0) Theme.FillRound(g, r, s * 7, fill);
        if (border.A > 0) Theme.StrokeRound(g, r, s * 7, border);
        Theme.TextIn(g, Text, Font, text, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

public sealed class SwitchToggle : Control
{
    private bool _checked;
    private bool _hover;
    private float _t;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            _t = value ? 1 : 0;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SwitchToggle()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(42, 24);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.SurfaceOf(this));
        float s = DeviceDpi / 96f;
        var track = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        var on = Theme.Accent;
        var off = _hover ? Theme.BorderHi : Theme.Border;
        var fill = _checked ? on : off;
        Theme.FillRound(g, track, track.Height / 2, fill);
        float pad = s * 3;
        float knob = track.Height - pad * 2;
        float x = _checked ? track.Right - knob - pad : track.X + pad;
        using var brush = new SolidBrush(Color.White);
        g.FillEllipse(brush, x, track.Y + pad, knob, knob);
    }
}

public class RoundPanel : Panel
{
    public int Radius { get; set; } = 8;

    /// <summary>Null means "use the palette color for this surface".</summary>
    public Color? FillColor { get; set; }

    /// <summary>Null means "use the palette border".</summary>
    public Color? BorderColor { get; set; }

    protected virtual Color ThemeFill => Theme.Surface;
    protected virtual Color ThemeBorder => Theme.Border;

    public Color Fill => FillColor ?? ThemeFill;

    public RoundPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.PaintCanvasBehind(g, this);
        float s = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        var fill = FillColor ?? ThemeFill;
        var border = BorderColor ?? ThemeBorder;
        var radius = Radius * s;
        if (fill.A > 0)
        {
            Theme.FillRound(g, r, radius, fill);

            // 1px glass sheen along the top edge, like the Windows clipboard cards.
            var sheen = Color.FromArgb(Theme.IsLight ? 110 : 22, 255, 255, 255);
            using var pen = new Pen(sheen, Math.Max(1f, s));
            g.DrawLine(pen, r.Left + radius, r.Top + s, r.Right - radius, r.Top + s);
        }
        if (border.A > 0) Theme.StrokeRound(g, r, radius, border);
    }
}

public sealed class SearchField : RoundPanel
{
    public TextBox Input { get; }

    protected override Color ThemeFill => Theme.Field;

    public SearchField()
    {
        Radius = 8;
        Padding = new Padding(0);

        Input = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            Font = Theme.Ui(11f),
            PlaceholderText = "Search clips and snippets",
        };
        Controls.Add(Input);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        float s = DeviceDpi / 96f;
        int h = Input.PreferredHeight;
        Input.SetBounds((int)(14 * s), (Height - h) / 2, Width - (int)(28 * s), h);
    }
}

public sealed class HotkeyBox : Control
{
    private readonly List<Keys> _held = new();
    private bool _capturing;

    public string Value { get; private set; } = "";
    public event EventHandler? ValueChanged;

    public HotkeyBox()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        Height = 38;
    }

    public void SetValue(string value)
    {
        Value = value ?? "";
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        _capturing = true;
        _held.Clear();
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _capturing = false;
        _held.Clear();
        Invalidate();
    }

    public override bool PreProcessMessage(ref Message msg)
    {
        const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        if (!_capturing) return base.PreProcessMessage(ref msg);
        if (msg.Msg is not (WM_KEYDOWN or WM_SYSKEYDOWN))
            return true; // swallow everything else while capturing

        var key = (Keys)(int)msg.WParam;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return true;
        if (key == Keys.Escape) { _capturing = false; Invalidate(); return true; }

        var combo = new Core.HotkeyHook.Combo
        {
            Ctrl = (Control.ModifierKeys & Keys.Control) != 0,
            Alt = (Control.ModifierKeys & Keys.Alt) != 0,
            Shift = (Control.ModifierKeys & Keys.Shift) != 0,
            Win = (NativeKeys.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (NativeKeys.GetAsyncKeyState(0x5C) & 0x8000) != 0,
            Key = (int)key,
        };
        Value = combo.ToString();
        _capturing = false;
        ValueChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
        return true;
    }

    private static class NativeKeys
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.SurfaceOf(this));
        float s = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, r, 8 * s, Theme.Field);
        Theme.StrokeRound(g, r, 8 * s, _capturing ? Theme.Accent : Theme.Border);

        string text = _capturing ? "Press a shortcut…" : string.IsNullOrEmpty(Value) ? "Not set" : Value;
        Theme.TextIn(g, text, Theme.Ui(10f), _capturing ? Theme.AccentHi : string.IsNullOrEmpty(Value) ? Theme.Faint : Theme.Text,
            new RectangleF(12 * s, 0, Width - 24 * s, Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        if (!_capturing)
            Theme.TextIn(g, "click to change", Theme.Ui(8f), Theme.Faint,
                new RectangleF(0, 0, Width - 12 * s, Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

public sealed class FlatTextBox : TextBox
{
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ApplyControlTheme(this, Theme.IsLight);
    }

    public FlatTextBox(bool multiline = false)
    {
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Field;
        ForeColor = Theme.Text;
        Font = Theme.Ui(9.5f);
        Multiline = multiline;
        if (multiline)
        {
            ScrollBars = ScrollBars.Vertical;
            AcceptsReturn = true;
        }
    }
}

public sealed class Card : RoundPanel
{
    public string Caption { get; set; } = "";

    public Card()
    {
        Radius = 8;
        Margin = new Padding(0, 0, 0, 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Caption.Length == 0) return;
        float s = DeviceDpi / 96f;
        Theme.TextAt(e.Graphics, Caption, Theme.Ui(9.4f, FontStyle.Bold), Theme.Muted, 18 * s, 13 * s);
    }
}

public sealed class TextLabel : Control
{
    private ContentAlignment _align = ContentAlignment.MiddleLeft;

    public TextLabel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Ui(9f);
        Height = 20;
    }

    public bool Ellipsis { get; set; } = true;

    public ContentAlignment Alignment
    {
        get => _align;
        set { _align = value; Invalidate(); }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.SurfaceOf(this));
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        flags |= _align switch
        {
            ContentAlignment.MiddleCenter or ContentAlignment.TopCenter or ContentAlignment.BottomCenter => TextFormatFlags.HorizontalCenter,
            ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.Left,
        };
        flags |= TextFormatFlags.SingleLine;
        if (Ellipsis) flags |= TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, Text ?? "", Font, ClientRectangle, ForeColor, flags);
    }
}

/// <summary>Rounded field wrapper that hosts a flat text box.</summary>
public sealed class FieldBox : RoundPanel
{
    public TextBox Input { get; }

    protected override Color ThemeFill => Theme.Field;

    public FieldBox(bool multiline = false)
    {
        Radius = 6;
        var box = new FlatTextBox(multiline);
        box.TextChanged += (_, _) => Invalidate();
        Input = box;
        Controls.Add(box);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        float s = DeviceDpi / 96f;
        int padX = (int)(11 * s);
        if (Input.Multiline)
            Input.SetBounds(padX, (int)(9 * s), Width - padX * 2, Height - (int)(18 * s));
        else
            Input.SetBounds(padX, (Height - Input.PreferredHeight) / 2, Width - padX * 2, Input.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
    }
}

/// <summary>Rounded-square icon chooser, styled like the workspace icon row.</summary>
public sealed class IconPicker : Control
{
    private string _value = "";
    private int _hover = -1;
    private readonly List<(RectangleF Rect, int Index)> _hits = new();
    private float _s = 1f;

    public event EventHandler? ValueChanged;

    public string Value
    {
        get => _value;
        set
        {
            value ??= "";
            if (_value == value) return;
            _value = value;
            Invalidate();
        }
    }

    public IconPicker()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Height = 84;
    }

    private float Cell => 34 * _s;
    private float Gap => 6 * _s;
    private int Columns => Math.Max(1, (int)((Width + Gap) / (Cell + Gap)));

    protected override void OnPaint(PaintEventArgs e)
    {
        _s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Theme.PaintCanvasBehind(g, this);

        _hits.Clear();
        int cols = Columns;
        for (int i = 0; i < TabStyle.Icons.Length; i++)
        {
            int col = i % cols, row = i / cols;
            var r = new RectangleF(col * (Cell + Gap), row * (Cell + Gap), Cell, Cell);
            var option = TabStyle.Icons[i];
            bool selected = option.Key == _value;

            if (selected)
            {
                Theme.FillRound(g, r, 9 * _s, Theme.WithAlpha(Theme.Accent, Theme.IsLight ? 22 : 30));
                Theme.StrokeRound(g, r, 9 * _s, Theme.Accent, 1.4f);
            }
            else if (i == _hover)
            {
                Theme.FillRound(g, r, 9 * _s, Theme.Hover);
            }

            var ink = selected ? Theme.Text : Theme.Muted;
            TabStyle.DrawIcon(g, new RectangleF(r.X + 4 * _s, r.Y + 4 * _s, r.Width - 8 * _s, r.Height - 8 * _s),
                option.Key, ink);
            _hits.Add((r, i));
        }
    }

    public int HeightForWidth(int width)
    {
        float s = DeviceDpi / 96f;
        int cols = Math.Max(1, (int)((width + 6 * s) / (40 * s)));
        int rows = (TabStyle.Icons.Length + cols - 1) / cols;
        return (int)(rows * 40 * s - 6 * s);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        foreach (var h in _hits)
            if (h.Rect.Contains(e.Location)) { hover = h.Index; break; }
        if (hover != _hover) { _hover = hover; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int index = -1;
        foreach (var h in _hits)
            if (h.Rect.Contains(e.Location)) { index = h.Index; break; }
        if (index < 0) return;
        var key = TabStyle.Icons[index].Key;
        if (key == _value) return;
        _value = key;
        Invalidate();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Row of color dots with a selection ring, like the workspace color row.</summary>
public sealed class ColorPicker : Control
{
    private string _value = "";
    private int _hover = -1;
    private readonly List<(RectangleF Rect, int Index)> _hits = new();
    private float _s = 1f;

    public event EventHandler? ValueChanged;

    public string Value
    {
        get => _value;
        set
        {
            value ??= "";
            if (_value == value) return;
            _value = value;
            Invalidate();
        }
    }

    public ColorPicker()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Height = 32;
    }

    private float Dot => 17 * _s;
    private float Gap => 4 * _s;

    private static string HexAt(int index) =>
        index == 0 ? "" : TabColors.Palette[index - 1];

    private static int Count => TabColors.Palette.Length + 1;

    protected override void OnPaint(PaintEventArgs e)
    {
        _s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.PaintCanvasBehind(g, this);

        _hits.Clear();
        for (int i = 0; i < Count; i++)
        {
            string hex = HexAt(i);
            float x = i * (Dot + Gap);
            var r = new RectangleF(x, (Height - Dot) / 2f, Dot, Dot);
            bool selected = i == 0
                ? string.IsNullOrEmpty(_value)
                : string.Equals(hex, _value, StringComparison.OrdinalIgnoreCase);

            if (selected)
            {
                using var ring = new Pen(Theme.Text, Math.Max(1.4f, 1.6f * _s));
                g.DrawEllipse(ring, r.X - 3.5f * _s, r.Y - 3.5f * _s, r.Width + 7 * _s, r.Height + 7 * _s);
            }
            else if (i == _hover)
            {
                using var halo = new Pen(Theme.BorderHi, Math.Max(1f, 1.2f * _s));
                g.DrawEllipse(halo, r.X - 2.5f * _s, r.Y - 2.5f * _s, r.Width + 5 * _s, r.Height + 5 * _s);
            }

            if (i == 0)
            {
                using var pen = new Pen(Theme.Faint, Math.Max(1.2f, 1.4f * _s));
                g.DrawEllipse(pen, r);
            }
            else
            {
                using var brush = new SolidBrush(TabStyle.Parse(hex, i - 1));
                g.FillEllipse(brush, r);
            }
            _hits.Add((new RectangleF(x, 0, Dot + Gap, Height), i));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        foreach (var h in _hits)
            if (h.Rect.Contains(e.Location)) { hover = h.Index; break; }
        if (hover != _hover) { _hover = hover; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int index = -1;
        foreach (var h in _hits)
            if (h.Rect.Contains(e.Location)) { index = h.Index; break; }
        if (index < 0) return;
        string hex = HexAt(index);
        if (string.Equals(hex, _value, StringComparison.OrdinalIgnoreCase)) return;
        _value = hex;
        Invalidate();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
