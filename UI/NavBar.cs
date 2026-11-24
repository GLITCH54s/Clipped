using System.Drawing.Drawing2D;

namespace Clipped.UI;

public sealed class NavBar : Control
{
    public sealed class NavItem
    {
        public string Id = "";
        public string Label = "";
        public string Glyph = "";
    }

    private readonly List<NavItem> _items = new();
    private string _active = "";
    private int _hover = -1;
    private float _s = 1f;

    public event EventHandler<string>? PageSelected;

    public NavBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
    }

    public void SetItems(IEnumerable<NavItem> items, string active)
    {
        _items.Clear();
        _items.AddRange(items);
        _active = active;
        Invalidate();
    }

    public void SetActive(string id)
    {
        _active = id;
        Invalidate();
    }

    private RectangleF ItemRect(int index)
    {
        float pad = S(10);
        return new RectangleF(pad, S(74) + index * S(44), Width - pad * 2, S(38));
    }

    private float S(float v) => v * _s;

    protected override void OnPaint(PaintEventArgs e)
    {
        _s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.PaintCanvas(g, ClientSize);

        Theme.TextAt(g, "Clipped", Theme.Display(14f, FontStyle.Bold), Theme.Text, S(20), S(20));

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var r = ItemRect(i);
            bool active = item.Id == _active;
            bool hover = i == _hover;

            if (active || hover)
            {
                Theme.FillRound(g, r, S(6), active ? Theme.Selected : Theme.Hover);
            }
            if (active)
            {
                Theme.FillRound(g, new RectangleF(r.X, r.Y + S(8), S(3), r.Height - S(16)), S(1.5f), Theme.Accent);
            }

            DrawGlyph(g, item.Glyph, new RectangleF(r.X + S(14), r.Y + (r.Height - S(16)) / 2, S(16), S(16)),
                active ? Theme.Accent : Theme.Faint);

            Theme.TextIn(g, item.Label, Theme.Ui(9.6f, active ? FontStyle.Bold : FontStyle.Regular),
                active ? Theme.Text : Theme.Muted,
                new RectangleF(r.X + S(40), r.Y, r.Width - S(46), r.Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        // footer
        Theme.TextAt(g, "v0.1.0  ·  MIT", Theme.Ui(8.2f), Theme.Faint, S(20), Height - S(48));
        Theme.TextAt(g, "open source", Theme.Ui(8.2f), Theme.Faint, S(20), Height - S(30));
    }

    private void DrawGlyph(Graphics g, string glyph, RectangleF r, Color color)
    {
        using var pen = new Pen(color, Math.Max(1.2f, S(1.4f)));
        switch (glyph)
        {
            case "grid":
                g.DrawRectangle(pen, r.X, r.Y, S(6), S(6));
                g.DrawRectangle(pen, r.X + S(9), r.Y, S(6), S(6));
                g.DrawRectangle(pen, r.X, r.Y + S(9), S(6), S(6));
                g.DrawRectangle(pen, r.X + S(9), r.Y + S(9), S(6), S(6));
                break;
            case "clock":
                g.DrawEllipse(pen, r.X, r.Y, r.Width - S(1), r.Height - S(1));
                g.DrawLine(pen, r.X + r.Width / 2, r.Y + r.Height / 2, r.X + r.Width / 2, r.Y + S(3));
                g.DrawLine(pen, r.X + r.Width / 2, r.Y + r.Height / 2, r.Right - S(3), r.Y + r.Height / 2 + S(3));
                break;
            case "gear":
                g.DrawEllipse(pen, r.X + S(2), r.Y + S(2), r.Width - S(4), r.Height - S(4));
                g.DrawEllipse(pen, r.X + S(6), r.Y + S(6), r.Width - S(12), r.Height - S(12));
                g.DrawLine(pen, r.X + r.Width / 2, r.Y - S(1), r.X + r.Width / 2, r.Y + S(1));
                g.DrawLine(pen, r.X + r.Width / 2, r.Bottom - S(1), r.X + r.Width / 2, r.Bottom + S(1));
                g.DrawLine(pen, r.X - S(1), r.Y + r.Height / 2, r.X + S(1), r.Y + r.Height / 2);
                g.DrawLine(pen, r.Right - S(1), r.Y + r.Height / 2, r.Right + S(1), r.Y + r.Height / 2);
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        for (int i = 0; i < _items.Count; i++)
            if (ItemRect(i).Contains(e.X, e.Y)) hover = i;
        if (hover != _hover) { _hover = hover; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        for (int i = 0; i < _items.Count; i++)
        {
            if (ItemRect(i).Contains(e.X, e.Y))
            {
                PageSelected?.Invoke(this, _items[i].Id);
                return;
            }
        }
    }
}

public sealed class DarkMenuRenderer : ToolStripRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Theme.Surface);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(4, 2, e.Item.Width - 8, e.Item.Height - 4);
        if (e.Item.Selected || e.Item.Pressed)
            Theme.FillRound(g, r, 5, Theme.Hover);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
    }

    protected override void OnRenderToolStripStatusLabelBackground(ToolStripItemRenderEventArgs e) { }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
}

public static class DarkMenus
{
    public static ContextMenuStrip Create()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.Ui(9f),
            ShowImageMargin = false,
            Padding = new Padding(4),
        };
        return menu;
    }

    public static ToolStripMenuItem Item(string text, EventHandler? onClick = null, bool check = false)
    {
        var item = new ToolStripMenuItem(text) { Checked = check, Padding = new Padding(6, 4, 6, 4) };
        if (onClick is not null) item.Click += onClick;
        return item;
    }
}
