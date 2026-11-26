using System.Drawing.Drawing2D;
using Clipped.Core;

namespace Clipped.UI;

public sealed class DashboardForm : Form
{
    private readonly AppController _app;
    private readonly NavBar _nav;
    private readonly Panel _host;
    private readonly LibraryPage _library;
    private readonly HistoryPage _history;
    private readonly SettingsPage _settings;
    private readonly PillButton _min;
    private readonly PillButton _close;
    private readonly WheelRouter _wheel;
    private string _page = "library";
    private float _s = 1f;

    public DashboardForm(AppController app)
    {
        _app = app;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor = Theme.Bg;
        Text = "Clipped";
        Icon = AppIcon.Get();
        ShowInTaskbar = true;
        KeyPreview = true;

        _s = DeviceDpi / 96f;
        var size = FitToWorkArea((int)(1020 * _s), (int)(680 * _s));
        Size = size;
        MinimumSize = new Size(Math.Min((int)(880 * _s), size.Width), Math.Min((int)(560 * _s), size.Height));

        _nav = new NavBar();
        _nav.SetItems(new[]
        {
            new NavBar.NavItem { Id = "library", Label = "Library", Glyph = "grid" },
            new NavBar.NavItem { Id = "history", Label = "History", Glyph = "clock" },
            new NavBar.NavItem { Id = "settings", Label = "Settings", Glyph = "gear" },
        }, "library");
        _nav.PageSelected += (_, id) => ShowPage(id);
        Controls.Add(_nav);

        _host = new Panel { BackColor = Theme.Bg };
        Controls.Add(_host);

        _library = new LibraryPage(this);
        _history = new HistoryPage(this);
        _settings = new SettingsPage(this);
        _host.Controls.Add(_library);
        _host.Controls.Add(_history);
        _host.Controls.Add(_settings);

        _min = new PillButton { Text = "–", Kind = PillButton.Style.Ghost, Font = Theme.Ui(11f), Size = new Size(34, 28) };
        _min.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _close = new PillButton { Text = "✕", Kind = PillButton.Style.Ghost, Font = Theme.Ui(9f), Size = new Size(34, 28) };
        _close.Click += (_, _) => Hide();
        Controls.Add(_min);
        Controls.Add(_close);
        _min.BringToFront();
        _close.BringToFront();

        Resize += (_, _) => LayoutAll();
        DpiChanged += (_, _) => LayoutAll();
        MouseDown += OnDragArea;
        _nav.MouseDown += OnDragArea;
        FormClosing += (_, e) =>
        {
            if (_app.Quitting) return;
            e.Cancel = true;
            Hide();
        };

        _app.Store.Changed += (_, _) => RefreshContents();

        // Wheel messages go to the focused control, so route them to the hand-scrolled
        // settings page whenever the pointer is over it.
        _wheel = new WheelRouter(this);
        Application.AddMessageFilter(_wheel);

        ShowPage("library");
    }

    private bool RouteWheel(int delta)
    {
        if (delta == 0 || _page != "settings" || !_settings.Visible) return false;
        if (_settings.FullHeight <= _settings.ClientSize.Height) return false;
        if (!_settings.RectangleToScreen(_settings.ClientRectangle).Contains(Cursor.Position)) return false;
        _settings.ScrollBy(delta);
        return true;
    }

    private Size FitToWorkArea(int width, int height)
    {
        var work = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        int margin = (int)(24 * _s);
        return new Size(Math.Min(width, work.Width - margin), Math.Min(height, work.Height - margin));
    }

    public void ShowPage(string page)
    {
        if (page is not ("library" or "history" or "settings")) page = "library";
        _page = page;
        _nav.SetActive(page);
        _library.Visible = page == "library";
        _history.Visible = page == "history";
        _settings.Visible = page == "settings";

        if (page == "library") _library.RefreshFromStore();
        if (page == "history") _history.RefreshFromStore();
        if (page == "settings") _settings.SyncFromStore();

        if (!Visible)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }
        Native.ForceForeground(Handle);
    }

    public void RefreshContents()
    {
        if (!Visible) return;
        if (_page == "library") _library.RefreshFromStore();
        else if (_page == "history") _history.RefreshFromStore();
    }

    public void SyncVisibleSettings()
    {
        if (_page == "settings") _settings.SyncFromStore();
    }

    public Bitmap RenderFullSettingsShot()
    {
        _settings.Show();
        _settings.PerformLayout();
        int fullH = Math.Max(_settings.FullHeight, _settings.ClientSize.Height);
        int scroll = _settings.ScrollOffset;

        var oldSize = _settings.Size;
        _settings.Size = new Size(oldSize.Width, fullH);
        _settings.PerformLayout();

        using var page = new Bitmap(_settings.Width, _settings.Height);
        _settings.DrawToBitmap(page, new Rectangle(0, 0, page.Width, page.Height));

        _settings.Size = oldSize;
        _settings.PerformLayout();
        _settings.ScrollTo(scroll);

        var shot = new Bitmap(Width, (int)S(56) + page.Height);
        using (var g = Graphics.FromImage(shot))
        {
            Theme.PaintCanvas(g, shot.Size);
            using (var win = new Bitmap(ClientSize.Width, (int)S(56)))
            {
                DrawToBitmap(win, new Rectangle(0, 0, win.Width, win.Height));
                g.DrawImage(win, 0, 0);
            }
            g.DrawImage(page, _host.Left, (int)S(56));
            using (var nav = new Bitmap(_nav.Width, _nav.Height))
            {
                _nav.DrawToBitmap(nav, new Rectangle(0, 0, nav.Width, nav.Height));
                g.DrawImage(nav, 0, 0);
            }
            using var pen = new Pen(Theme.Border);
            g.DrawLine(pen, _nav.Right - 1, 0, _nav.Right - 1, shot.Height);
        }
        return shot;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, !Theme.IsLight);
        Native.SetBorderColor(Handle, Theme.Border);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.PaintCanvas(e.Graphics, ClientSize);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, _nav.Right - 1, 0, _nav.Right - 1, Height);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (RouteWheel(e.Delta))
        {
            if (e is HandledMouseEventArgs h) h.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
    }

    private void LayoutAll()
    {
        _s = DeviceDpi / 96f;
        int navW = (int)S(224);
        _nav.Bounds = new Rectangle(0, 0, navW, ClientSize.Height);
        _host.Bounds = new Rectangle(navW, 0, Math.Max(10, ClientSize.Width - navW), ClientSize.Height);
        foreach (Control page in new Control[] { _library, _history, _settings })
        {
            page.Bounds = new Rectangle(0, (int)S(56), _host.Width, Math.Max(10, _host.Height - (int)S(56)));
        }
        _close.Bounds = new Rectangle(ClientSize.Width - (int)S(48), (int)S(14), (int)S(34), (int)S(28));
        _min.Bounds = new Rectangle(ClientSize.Width - (int)S(86), (int)S(14), (int)S(34), (int)S(28));
        _library.ApplyScale();
        _history.ApplyScale();
        _settings.ApplyScale();
        Invalidate();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutAll();
    }

    private void OnDragArea(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        bool inNavTop = sender == _nav && e.Y < S(64);
        bool inContentTop = sender == this && e.Y < S(56) && e.X > _nav.Width;
        if (!inNavTop && !inContentTop) return;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
    }

    internal float S(float v) => v * _s;
    internal AppController App => _app;

    private sealed class WheelRouter : IMessageFilter
    {
        private const int WM_MOUSEWHEEL = 0x020A;
        private readonly DashboardForm _form;
        public WheelRouter(DashboardForm form) => _form = form;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;
            int delta = (short)((long)m.WParam >> 16);
            return _form.RouteWheel(delta);
        }
    }

    // =====================================================================
    internal sealed class LibraryPage : Panel
    {
        private readonly DashboardForm _owner;
        private readonly RoundPanel _tabsCard, _midCard, _editorCard;
        private readonly ItemList _tabs, _blocks;
        private readonly PillButton _addTab, _deleteTab;
        private readonly FieldBox _tabName, _tabKey;
        private readonly TextLabel _tabCount, _iconCaption, _colorCaption;
        private readonly IconPicker _iconPicker;
        private readonly ColorPicker _colorPicker;
        private readonly PillButton _addText, _addTpl, _addSeq, _addDate;
        private readonly TextLabel _labelCaption, _contentCaption, _typeCaption, _saved, _emptyHint, _seqPreview, _datePreview, _tplNext, _tplCaption;
        private readonly FieldBox _labelField, _valueField, _seqStart, _seqEnd, _seqStep, _dateFormat, _tplStart, _tplStep;
        private readonly PillButton _kindText, _kindTpl, _kindSeq, _kindDate, _tplReset, _deleteBlock;
        private readonly System.Windows.Forms.Timer _saveTimer;

        private string _tabId = "";
        private string _blockId = "";
        private bool _updating;

        public LibraryPage(DashboardForm owner)
        {
            _owner = owner;
            DoubleBuffered = true;
            AutoScroll = false;
            BackColor = Theme.Bg;

            _tabsCard = new RoundPanel();
            _midCard = new RoundPanel();
            _editorCard = new RoundPanel();
            Controls.Add(_tabsCard);
            Controls.Add(_midCard);
            Controls.Add(_editorCard);

            _tabs = new ItemList { ShowBadges = true, RowHeight = 44, BackColor = Theme.Surface };
            _tabsCard.Controls.Add(_tabs);
            _addTab = new PillButton { Text = "+  New tab", Kind = PillButton.Style.Outline };
            _tabsCard.Controls.Add(_addTab);

            _tabName = new FieldBox();
            _tabName.Input.PlaceholderText = "Tab name";
            _tabName.Input.MaxLength = 40;
            _midCard.Controls.Add(_tabName);

            _tabKey = new FieldBox();
            _tabKey.Input.MaxLength = 1;
            _tabKey.Input.CharacterCasing = CharacterCasing.Upper;
            _tabKey.Input.TextAlign = HorizontalAlignment.Center;
            _tabKey.Input.Font = Theme.Ui(10f, FontStyle.Bold);
            _midCard.Controls.Add(_tabKey);

            _iconCaption = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(7.8f, FontStyle.Bold), Text = "TAB ICON" };
            _colorCaption = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(7.8f, FontStyle.Bold), Text = "TAB COLOR" };
            _iconPicker = new IconPicker();
            _colorPicker = new ColorPicker();
            _midCard.Controls.Add(_iconCaption);
            _midCard.Controls.Add(_colorCaption);
            _midCard.Controls.Add(_iconPicker);
            _midCard.Controls.Add(_colorPicker);

            _iconPicker.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                var tab = _owner.App.Store.FindTab(_tabId);
                if (tab is null) return;
                tab.Icon = _iconPicker.Value;
                RefreshTabEntries();
                ScheduleSave();
            };

            _colorPicker.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                var tab = _owner.App.Store.FindTab(_tabId);
                if (tab is null) return;
                tab.Color = _colorPicker.Value;
                RefreshTabEntries();
                ScheduleSave();
            };

            _deleteTab = new PillButton { Text = "Delete", Kind = PillButton.Style.Danger };
            _midCard.Controls.Add(_deleteTab);

            _blocks = new ItemList { RowHeight = 44, BackColor = Theme.Surface };
            _midCard.Controls.Add(_blocks);
            _tabCount = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(8.2f) };
            _midCard.Controls.Add(_tabCount);

            _addText = new PillButton { Text = "+  Text", Kind = PillButton.Style.Subtle };
            _addTpl = new PillButton { Text = "+  Fill-in", Kind = PillButton.Style.Subtle };
            _addSeq = new PillButton { Text = "+  Numbers", Kind = PillButton.Style.Subtle };
            _addDate = new PillButton { Text = "+  Date", Kind = PillButton.Style.Subtle };
            _midCard.Controls.Add(_addText);
            _midCard.Controls.Add(_addTpl);
            _midCard.Controls.Add(_addSeq);
            _midCard.Controls.Add(_addDate);

            _typeCaption = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(7.8f, FontStyle.Bold) };
            _labelCaption = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(7.8f, FontStyle.Bold), Text = "LABEL" };
            _contentCaption = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(7.8f, FontStyle.Bold), Text = "CONTENT" };
            _saved = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(8f), Text = "", Alignment = ContentAlignment.MiddleRight };
            _emptyHint = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(9.6f),
                Text = "Select a block to edit, or add one on the left.",
                Alignment = ContentAlignment.MiddleCenter,
            };
            _editorCard.Controls.Add(_typeCaption);
            _editorCard.Controls.Add(_labelCaption);
            _editorCard.Controls.Add(_contentCaption);
            _editorCard.Controls.Add(_saved);
            _editorCard.Controls.Add(_emptyHint);

            _labelField = new FieldBox();
            _valueField = new FieldBox(true);
            _seqStart = new FieldBox();
            _seqEnd = new FieldBox();
            _seqStep = new FieldBox();
            _dateFormat = new FieldBox();
            _tplStart = new FieldBox();
            _tplStep = new FieldBox();
            _editorCard.Controls.Add(_labelField);
            _editorCard.Controls.Add(_valueField);
            _editorCard.Controls.Add(_seqStart);
            _editorCard.Controls.Add(_seqEnd);
            _editorCard.Controls.Add(_seqStep);
            _editorCard.Controls.Add(_dateFormat);
            _editorCard.Controls.Add(_tplStart);
            _editorCard.Controls.Add(_tplStep);

            _seqStart.Input.PlaceholderText = "Start";
            _seqEnd.Input.PlaceholderText = "End";
            _seqStep.Input.PlaceholderText = "Step";
            _dateFormat.Input.PlaceholderText = "dd MMM yyyy";
            _tplStart.Input.PlaceholderText = "Start";
            _tplStep.Input.PlaceholderText = "Step";

            _seqPreview = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(8.6f) };
            _datePreview = new TextLabel { ForeColor = Theme.Muted, Font = Theme.Ui(9.2f) };
            _tplNext = new TextLabel { ForeColor = Theme.Muted, Font = Theme.Ui(9.2f) };
            _tplCaption = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(7.8f, FontStyle.Bold), Text = "COUNTER" };
            _editorCard.Controls.Add(_seqPreview);
            _editorCard.Controls.Add(_datePreview);
            _editorCard.Controls.Add(_tplNext);
            _editorCard.Controls.Add(_tplCaption);

            _kindText = new PillButton { Text = "Text" };
            _kindTpl = new PillButton { Text = "Fill-in" };
            _kindSeq = new PillButton { Text = "Numbers" };
            _kindDate = new PillButton { Text = "Date" };
            _editorCard.Controls.Add(_kindText);
            _editorCard.Controls.Add(_kindTpl);
            _editorCard.Controls.Add(_kindSeq);
            _editorCard.Controls.Add(_kindDate);

            _tplReset = new PillButton { Text = "Reset counter", Kind = PillButton.Style.Outline };
            _deleteBlock = new PillButton { Text = "Delete block", Kind = PillButton.Style.Danger };
            _editorCard.Controls.Add(_tplReset);
            _editorCard.Controls.Add(_deleteBlock);

            _saveTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                _saved.Text = "Saved";
                _owner.App.Store.Save();
                var toast = new System.Windows.Forms.Timer { Interval = 1600 };
                toast.Tick += (_, _) => { toast.Stop(); toast.Dispose(); _saved.Text = ""; };
                toast.Start();
            };

            WireEvents();
        }

        private void WireEvents()
        {
            _tabs.SelectionChanged += (_, _) =>
            {
                if (_updating) return;
                if (_tabs.Selected?.Payload is string id && id != _tabId) SelectTab(id);
            };

            _blocks.SelectionChanged += (_, _) =>
            {
                if (_updating) return;
                if (_blocks.Selected?.Payload is string id && id != _blockId) SelectBlock(id);
            };

            _addTab.Click += (_, _) =>
            {
                Flush();
                var tab = _owner.App.Store.AddTab("New tab");
                SelectTab(tab.Id, rebuild: true);
                _tabName.Input.Focus();
                _tabName.Input.SelectAll();
            };

            _deleteTab.Click += (_, _) =>
            {
                var tab = _owner.App.Store.FindTab(_tabId);
                if (tab is null) return;
                if (MessageBox.Show(this, "Delete tab \"" + tab.Name + "\" and its " + tab.Blocks.Count + " blocks?",
                        "Clipped", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                int index = _owner.App.Store.Data.Tabs.FindIndex(t => t.Id == _tabId);
                _owner.App.Store.RemoveTab(_tabId);
                var tabs = _owner.App.Store.Data.Tabs;
                SelectTab(tabs.Count == 0 ? "" : tabs[Math.Clamp(index, 0, tabs.Count - 1)].Id, rebuild: true);
            };

            _tabName.Input.TextChanged += (_, _) => ApplyTabEdits();
            _tabKey.Input.TextChanged += (_, _) => ApplyTabEdits();

            _addText.Click += (_, _) => AddBlock(BlockKind.Text);
            _addTpl.Click += (_, _) => AddBlock(BlockKind.Template);
            _addSeq.Click += (_, _) => AddBlock(BlockKind.Sequence);
            _addDate.Click += (_, _) => AddBlock(BlockKind.Date);

            _labelField.Input.TextChanged += (_, _) => ApplyBlockEdits();
            _valueField.Input.TextChanged += (_, _) => ApplyBlockEdits();
            _dateFormat.Input.TextChanged += (_, _) => ApplyBlockEdits();
            foreach (var box in new[] { _seqStart, _seqEnd, _seqStep, _tplStart, _tplStep })
                box.Input.TextChanged += (_, _) => ApplyBlockEdits();

            _kindText.Click += (_, _) => SetKind(BlockKind.Text);
            _kindTpl.Click += (_, _) => SetKind(BlockKind.Template);
            _kindSeq.Click += (_, _) => SetKind(BlockKind.Sequence);
            _kindDate.Click += (_, _) => SetKind(BlockKind.Date);

            _tplReset.Click += (_, _) =>
            {
                var b = CurrentBlock();
                if (b is null) return;
                b.Counter = 0;
                UpdateTemplatePreview(b);
                ScheduleSave();
            };

            _deleteBlock.Click += (_, _) =>
            {
                var b = CurrentBlock();
                if (b is null) return;
                if (MessageBox.Show(this, "Delete this block?", "Clipped",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                _owner.App.Store.RemoveBlock(_tabId, b.Id);
                _blockId = "";
                RefreshFromStore();
            };
        }

        private void SetKind(BlockKind kind)
        {
            var b = CurrentBlock();
            if (b is null) return;
            b.Kind = kind;
            UpdateKindButtons(kind);
            LayoutEditor();
            ScheduleSave();
        }

        private void AddBlock(BlockKind kind)
        {
            if (string.IsNullOrEmpty(_tabId)) return;
            var block = new Block
            {
                Kind = kind,
                Label = kind switch
                {
                    BlockKind.Sequence => "Numbers",
                    BlockKind.Date => "Today",
                    BlockKind.Template => "New fill-in",
                    _ => "New block",
                },
                Value = kind == BlockKind.Template ? "REF-{n}" : "",
            };
            _owner.App.Store.AddBlock(_tabId, block);
            SelectBlock(block.Id);
            RefreshLists();
            _labelField.Input.Focus();
            _labelField.Input.SelectAll();
        }

        private Block? CurrentBlock()
        {
            var tab = _owner.App.Store.FindTab(_tabId);
            return tab?.Blocks.FirstOrDefault(b => b.Id == _blockId);
        }

        private void SelectTab(string id, bool rebuild = false)
        {
            Flush();
            _tabId = id;
            _blockId = "";
            var tab = _owner.App.Store.FindTab(_tabId);
            _updating = true;
            _tabName.Input.Text = tab?.Name ?? "";
            _tabKey.Input.Text = tab?.Key ?? "";
            _iconPicker.Value = tab?.Icon ?? "";
            _colorPicker.Value = tab?.Color ?? "";
            _updating = false;
            _deleteTab.Enabled = tab is not null;
            _tabName.Enabled = tab is not null;
            _tabKey.Enabled = tab is not null;
            _iconPicker.Enabled = tab is not null;
            _colorPicker.Enabled = tab is not null;
            RefreshLists(rebuild);
            if (tab is { Blocks.Count: > 0 }) SelectBlock(tab.Blocks[0].Id);
            else SelectBlock("");
        }

        private void SelectBlock(string id)
        {
            Flush();
            _blockId = id;
            var b = CurrentBlock();
            _updating = true;
            if (b is null)
            {
                _labelField.Input.Text = "";
                _valueField.Input.Text = "";
                _dateFormat.Input.Text = "";
                _seqStart.Input.Text = "";
                _seqEnd.Input.Text = "";
                _seqStep.Input.Text = "";
                _tplStart.Input.Text = "";
                _tplStep.Input.Text = "";
            }
            else
            {
                _labelField.Input.Text = b.Label;
                _valueField.Input.Text = b.Value;
                _dateFormat.Input.Text = b.DateFormat;
                _seqStart.Input.Text = b.Start.ToString();
                _seqEnd.Input.Text = b.End.ToString();
                _seqStep.Input.Text = b.Step.ToString();
                _tplStart.Input.Text = b.CounterStart.ToString();
                _tplStep.Input.Text = b.CounterStep.ToString();
            }
            _updating = false;

            if (b is not null)
            {
                UpdateKindButtons(b.Kind);
                UpdateTemplatePreview(b);
                UpdateDatePreview(b);
                UpdateSequencePreview(b);
            }
            LayoutEditor();
            if (b is not null && _blocks.Selected?.Payload as string != b.Id)
            {
                int index = _blocks.Items.ToList().FindIndex(e => (string?)e.Payload == b.Id);
                if (index >= 0)
                {
                    _updating = true;
                    _blocks.Select(index);
                    _updating = false;
                }
            }
        }

        private void ApplyTabEdits()
        {
            if (_updating) return;
            var tab = _owner.App.Store.FindTab(_tabId);
            if (tab is null) return;

            var name = _tabName.Input.Text.Trim();
            if (name.Length > 0) tab.Name = name;

            var key = _tabKey.Input.Text.Trim().ToUpperInvariant();
            if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
            {
                bool taken = _owner.App.Store.Data.Tabs.Any(t => t.Id != tab.Id &&
                    string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
                if (!taken) tab.Key = key;
                else _tabKey.Input.Text = tab.Key;
            }
            else if (key.Length == 0)
            {
                tab.Key = "";
            }

            RefreshTabEntries();
            ScheduleSave();
        }

        private void ApplyBlockEdits()
        {
            if (_updating) return;
            var b = CurrentBlock();
            if (b is null) return;

            b.Label = _labelField.Input.Text.Trim();
            b.Value = _valueField.Input.Text;
            b.DateFormat = string.IsNullOrWhiteSpace(_dateFormat.Input.Text) ? "dd MMM yyyy" : _dateFormat.Input.Text;
            b.Start = ParseInt(_seqStart.Input.Text, b.Start);
            b.End = ParseInt(_seqEnd.Input.Text, b.End);
            b.Step = ParseInt(_seqStep.Input.Text, b.Step == 0 ? 1 : b.Step);
            b.CounterStart = ParseInt(_tplStart.Input.Text, b.CounterStart);
            b.CounterStep = ParseInt(_tplStep.Input.Text, b.CounterStep == 0 ? 1 : b.CounterStep);

            UpdateSequencePreview(b);
            UpdateDatePreview(b);
            UpdateTemplatePreview(b);
            RefreshBlockEntries();
            ScheduleSave();
        }

        private static int ParseInt(string text, int fallback) =>
            int.TryParse(text.Trim(), out var v) ? v : fallback;

        private void UpdateKindButtons(BlockKind kind)
        {
            foreach (var (btn, k) in new[] { (_kindText, BlockKind.Text), (_kindTpl, BlockKind.Template),
                         (_kindSeq, BlockKind.Sequence), (_kindDate, BlockKind.Date) })
            {
                btn.Kind = k == kind ? PillButton.Style.Primary : PillButton.Style.Subtle;
                btn.Invalidate();
            }
        }

        private void UpdateSequencePreview(Block b)
        {
            int step = Math.Max(1, Math.Abs(b.Step));
            var values = b.ExpandSequence().ToList();
            string sample = values.Count > 3
                ? values[0] + ", " + values[1] + ", " + values[2] + " … " + values[^1]
                : string.Join(", ", values);
            _seqPreview.Text = values.Count + " values  ·  " + sample;
        }

        private void UpdateDatePreview(Block b)
        {
            try { _datePreview.Text = "Preview:  " + DateTime.Now.ToString(b.DateFormat); }
            catch { _datePreview.Text = "Preview:  invalid format"; }
        }

        private void UpdateTemplatePreview(Block b)
        {
            _tplNext.Text = "Next:  " + b.Value.Replace("{n}", b.NextCounter().ToString());
        }

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void Flush()
        {
            if (!_saveTimer.Enabled) return;
            _saveTimer.Stop();
            _owner.App.Store.Save();
        }

        public void RefreshFromStore()
        {
            var tab = _owner.App.Store.FindTab(_tabId);
            if (tab is null)
            {
                var first = _owner.App.Store.Data.Tabs.FirstOrDefault();
                _tabId = first?.Id ?? "";
                tab = first;
            }
            _updating = true;
            _tabName.Input.Text = tab?.Name ?? "";
            _tabKey.Input.Text = tab?.Key ?? "";
            _iconPicker.Value = tab?.Icon ?? "";
            _colorPicker.Value = tab?.Color ?? "";
            _updating = false;
            _deleteTab.Enabled = tab is not null;
            RefreshLists();
            if (tab is not null && tab.Blocks.Count > 0 && CurrentBlock() is null)
                SelectBlock(tab.Blocks[0].Id);
            if (tab is null) SelectBlock("");
            LayoutEditor();
        }

        private void RefreshLists(bool rebuildTabs = true)
        {
            RefreshTabEntries();
            RefreshBlockEntries();
        }

        private void RefreshTabEntries()
        {
            var entries = _owner.App.Store.Data.Tabs.Select(t => new ItemList.Entry
            {
                Title = t.Name,
                Sub = t.Blocks.Count + (t.Blocks.Count == 1 ? " block" : " blocks"),
                Badge = t.Key,
                Icon = t.Icon,
                Payload = t.Id,
            }).ToList();
            int select = entries.FindIndex(e => (string?)e.Payload == _tabId);
            _updating = true;
            _tabs.SetItems(entries, Math.Max(0, select));
            _updating = false;
        }

        private void RefreshBlockEntries()
        {
            var tab = _owner.App.Store.FindTab(_tabId);
            var entries = (tab?.Blocks ?? new List<Block>()).Select(b => new ItemList.Entry
            {
                Title = string.IsNullOrWhiteSpace(b.Label) ? Block.OneLine(b.Preview()) : b.Label,
                Sub = b.Preview(),
                Tag = KindName(b.Kind),
                Payload = b.Id,
            }).ToList();
            int select = entries.FindIndex(e => (string?)e.Payload == _blockId);
            _updating = true;
            _blocks.SetItems(entries, Math.Max(0, select));
            _updating = false;
            _tabCount.Text = tab is null ? "" : tab.Name + "  ·  " + tab.Blocks.Count + " blocks";
            _addText.Enabled = tab is not null;
            _addTpl.Enabled = tab is not null;
            _addSeq.Enabled = tab is not null;
            _addDate.Enabled = tab is not null;
        }

        private static string KindName(BlockKind kind) => kind switch
        {
            BlockKind.Template => "fill-in",
            BlockKind.Sequence => "numbers",
            BlockKind.Date => "date",
            _ => "text",
        };

        public void ApplyScale()
        {
            LayoutPage();
            LayoutEditor();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutPage();
            LayoutEditor();
        }

        private void LayoutPage()
        {
            float s = DeviceDpi / 96f;
            int W = ClientSize.Width, H = ClientSize.Height;
            int pad = (int)(26 * s);
            int top = (int)(92 * s);
            int col1 = (int)(202 * s);
            int col2 = (int)(300 * s);
            int gap = (int)(18 * s);
            int h = H - top - pad;

            _tabsCard.Bounds = new Rectangle(pad, top, col1, h);
            _tabs.SetBounds((int)(8 * s), (int)(38 * s), col1 - (int)(16 * s), h - (int)(38 * s) - (int)(46 * s));
            _addTab.SetBounds((int)(8 * s), h - (int)(40 * s), col1 - (int)(16 * s), (int)(32 * s));

            int midX = pad + col1 + gap;
            _midCard.Bounds = new Rectangle(midX, top, col2, h);

            _tabName.SetBounds((int)(12 * s), (int)(30 * s), (int)(148 * s), (int)(34 * s));
            _tabKey.SetBounds((int)(168 * s), (int)(30 * s), (int)(38 * s), (int)(34 * s));
            _deleteTab.SetBounds((int)(214 * s), (int)(30 * s), (int)(74 * s), (int)(34 * s));
            _tabCount.SetBounds((int)(12 * s), (int)(68 * s), col2 - (int)(24 * s), (int)(16 * s));

            int iconW = col2 - (int)(24 * s);
            _iconCaption.SetBounds((int)(12 * s), (int)(86 * s), iconW, (int)(12 * s));
            int iconH = _iconPicker.HeightForWidth(iconW);
            _iconPicker.SetBounds((int)(12 * s), (int)(100 * s), iconW, iconH);
            int colorCaptionY = (int)(100 * s) + iconH + (int)(10 * s);
            _colorCaption.SetBounds((int)(12 * s), colorCaptionY, iconW, (int)(12 * s));
            int colorY = colorCaptionY + (int)(15 * s);
            _colorPicker.SetBounds((int)(12 * s), colorY, iconW, (int)(28 * s));
            int listY = colorY + (int)(28 * s) + (int)(10 * s);

            int btnW = (col2 - (int)(32 * s)) / 2;
            int btnY = h - (int)(86 * s);
            _addText.SetBounds((int)(10 * s), btnY, btnW, (int)(30 * s));
            _addTpl.SetBounds((int)(12 * s) + btnW, btnY, btnW, (int)(30 * s));
            _addSeq.SetBounds((int)(10 * s), btnY + (int)(36 * s), btnW, (int)(30 * s));
            _addDate.SetBounds((int)(12 * s) + btnW, btnY + (int)(36 * s), btnW, (int)(30 * s));

            _blocks.SetBounds((int)(8 * s), listY, col2 - (int)(16 * s), h - listY - (int)(96 * s));

            int edX = midX + col2 + gap;
            int edW = W - edX - pad;
            _editorCard.Bounds = new Rectangle(edX, top, Math.Max((int)(320 * s), edW), h);
        }

        private void LayoutEditor()
        {
            float s = DeviceDpi / 96f;
            int w = _editorCard.ClientSize.Width;
            int h = _editorCard.ClientSize.Height;
            int cw = w - (int)(36 * s);
            var b = CurrentBlock();
            bool has = b is not null;

            _emptyHint.Visible = !has;
            _emptyHint.SetBounds((int)(18 * s), 0, cw, h);

            foreach (Control c in new Control[] { _labelCaption, _labelField, _typeCaption, _kindText, _kindTpl,
                         _kindSeq, _kindDate, _contentCaption, _saved })
                c.Visible = has;

            _labelCaption.SetBounds((int)(18 * s), (int)(46 * s), cw, (int)(16 * s));
            _labelField.SetBounds((int)(18 * s), (int)(62 * s), cw, (int)(36 * s));
            _typeCaption.SetBounds((int)(18 * s), (int)(110 * s), cw, (int)(16 * s));
            _typeCaption.Text = "TYPE";

            int bx = (int)(18 * s);
            int by = (int)(126 * s);
            int bw = Math.Max((int)(60 * s), Math.Min((int)(86 * s), (cw - (int)(24 * s)) / 4));
            _kindText.SetBounds(bx, by, bw, (int)(30 * s));
            _kindTpl.SetBounds(bx + bw + (int)(8 * s), by, bw, (int)(30 * s));
            _kindSeq.SetBounds(bx + (bw + (int)(8 * s)) * 2, by, bw, (int)(30 * s));
            _kindDate.SetBounds(bx + (bw + (int)(8 * s)) * 3, by, bw, (int)(30 * s));

            _contentCaption.SetBounds((int)(18 * s), (int)(172 * s), cw, (int)(16 * s));

            bool showValue = has && b!.Kind is BlockKind.Text or BlockKind.Template;
            bool showSeq = has && b!.Kind == BlockKind.Sequence;
            bool showDate = has && b!.Kind == BlockKind.Date;
            bool showTpl = has && b!.Kind == BlockKind.Template;

            _valueField.Visible = showValue;
            _valueField.SetBounds((int)(18 * s), (int)(190 * s), cw, (int)(180 * s));

            _seqStart.Visible = _seqEnd.Visible = _seqStep.Visible = showSeq;
            int sw = (cw - (int)(16 * s)) / 3;
            _seqStart.SetBounds((int)(18 * s), (int)(190 * s), sw, (int)(36 * s));
            _seqEnd.SetBounds((int)(18 * s) + sw + (int)(8 * s), (int)(190 * s), sw, (int)(36 * s));
            _seqStep.SetBounds((int)(18 * s) + (sw + (int)(8 * s)) * 2, (int)(190 * s), sw, (int)(36 * s));
            _seqPreview.SetBounds((int)(18 * s), (int)(234 * s), cw, (int)(20 * s));

            _dateFormat.Visible = showDate;
            _dateFormat.SetBounds((int)(18 * s), (int)(190 * s), Math.Min(cw, (int)(340 * s)), (int)(36 * s));
            _datePreview.SetBounds((int)(18 * s), (int)(234 * s), cw, (int)(20 * s));

            _tplCaption.Visible = _tplStart.Visible = _tplStep.Visible = _tplNext.Visible = _tplReset.Visible = showTpl;
            _tplCaption.SetBounds((int)(18 * s), (int)(384 * s), cw, (int)(16 * s));
            _tplStart.SetBounds((int)(18 * s), (int)(402 * s), (int)(110 * s), (int)(36 * s));
            _tplStep.SetBounds((int)(136 * s), (int)(402 * s), (int)(110 * s), (int)(36 * s));
            _tplNext.SetBounds((int)(262 * s), (int)(410 * s), Math.Max(80, cw - (int)(250 * s)), (int)(20 * s));
            _tplReset.SetBounds(w - (int)(140 * s), (int)(402 * s), (int)(122 * s), (int)(32 * s));

            _contentCaption.Text = showSeq ? "RANGE" : showDate ? "FORMAT" : "CONTENT";

            _seqPreview.Visible = showSeq;
            _datePreview.Visible = showDate;

            _deleteBlock.Visible = has;
            _deleteBlock.SetBounds((int)(18 * s), h - (int)(50 * s), (int)(120 * s), (int)(32 * s));
            _saved.SetBounds(w - (int)(120 * s), h - (int)(50 * s), (int)(100 * s), (int)(32 * s));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.PaintCanvas(e.Graphics, ClientSize);
            float s = DeviceDpi / 96f;
            Theme.TextAt(e.Graphics, "Library", Theme.Display(20f, FontStyle.Bold), Theme.Text, 26 * s, 16 * s);
            Theme.TextAt(e.Graphics, "Saved blocks, one keypress away.", Theme.Ui(9.8f), Theme.Faint, 27 * s, 54 * s);
        }
    }

    // =====================================================================
    internal sealed class HistoryPage : Panel
    {
        private readonly DashboardForm _owner;
        private readonly RoundPanel _listCard, _detailCard;
        private readonly ItemList _list;
        private readonly FieldBox _text;
        private readonly TextLabel _meta;
        private readonly PillButton _copy, _save, _delete, _clear;
        private int _index = -1;

        public HistoryPage(DashboardForm owner)
        {
            _owner = owner;
            DoubleBuffered = true;
            BackColor = Theme.Bg;

            _listCard = new RoundPanel();
            _detailCard = new RoundPanel();
            Controls.Add(_listCard);
            Controls.Add(_detailCard);

            _list = new ItemList { RowHeight = 52, BackColor = Theme.Surface, Compact = false };
            _listCard.Controls.Add(_list);

            _text = new FieldBox(true);
            _text.Input.ReadOnly = true;
            _text.Input.Font = Theme.Ui(9.8f);
            _detailCard.Controls.Add(_text);

            _meta = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(8.4f) };
            _detailCard.Controls.Add(_meta);

            _copy = new PillButton { Text = "Copy", Kind = PillButton.Style.Primary };
            _save = new PillButton { Text = "Save to tab…", Kind = PillButton.Style.Outline };
            _delete = new PillButton { Text = "Delete", Kind = PillButton.Style.Danger };
            _clear = new PillButton { Text = "Clear history", Kind = PillButton.Style.Danger };
            _detailCard.Controls.Add(_copy);
            _detailCard.Controls.Add(_save);
            _detailCard.Controls.Add(_delete);
            Controls.Add(_clear);

            _list.SelectionChanged += (_, _) =>
            {
                _index = _list.SelectedIndex;
                ShowSelected();
            };

            _copy.Click += (_, _) =>
            {
                if (_index < 0 || _index >= _owner.App.Store.Data.History.Count) return;
                var entry = _owner.App.Store.Data.History[_index];
                if (entry.ImagePath is { Length: > 0 } path)
                    Paster.PasteImageInto(IntPtr.Zero, path, false);
                else
                    Paster.Copy(entry.Text);
            };

            _save.Click += (_, _) => ShowSaveMenu();

            _delete.Click += (_, _) =>
            {
                if (_index < 0 || _index >= _owner.App.Store.Data.History.Count) return;
                _owner.App.Store.RemoveHistoryAt(_index);
                RefreshFromStore();
            };

            _clear.Click += (_, _) =>
            {
                if (_owner.App.Store.Data.History.Count == 0) return;
                if (MessageBox.Show(this, "Clear all clipboard history?", "Clipped",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                _owner.App.Store.ClearHistory();
                RefreshFromStore();
            };
        }

        private void ShowSaveMenu()
        {
            if (_index < 0 || _index >= _owner.App.Store.Data.History.Count) return;
            var entry = _owner.App.Store.Data.History[_index];
            if (entry.ImagePath is { Length: > 0 }) return;
            var menu = DarkMenus.Create();
            foreach (var tab in _owner.App.Store.Data.Tabs)
            {
                var t = tab;
                menu.Items.Add(DarkMenus.Item("Save to  " + tab.Name, (_, _) =>
                {
                    _owner.App.Store.AddBlock(t.Id, new Block
                    {
                        Label = Shorten(_owner.App.Store.Data.History[_index].Text, 32),
                        Value = _owner.App.Store.Data.History[_index].Text,
                    });
                }));
            }
            if (menu.Items.Count == 0)
                menu.Items.Add(DarkMenus.Item("No tabs yet"));
            menu.Show(_save, new Point(0, _save.Height));
        }

        private static string Shorten(string text, int max)
        {
            var one = Block.OneLine(text);
            return one.Length <= max ? one : one[..max] + "…";
        }

        public void RefreshFromStore()
        {
            var history = _owner.App.Store.Data.History;
            var entries = history.Select(h => new ItemList.Entry
            {
                Title = h.ImagePath is { Length: > 0 }
                    ? "Image"
                    : Block.OneLine(h.Text).Length == 0 ? "(whitespace)" : Shorten(h.Text, 64),
                Sub = h.CopiedAt.ToString("HH:mm") + "  ·  " + h.CopiedAt.ToString("dd MMM"),
                Tag = h.ImagePath is { Length: > 0 } ? "image" : h.Text.Length + " ch",
            }).ToList();
            _list.SetItems(entries, Math.Max(0, Math.Min(_index, entries.Count - 1)));
            _index = _list.SelectedIndex;
            ShowSelected();
        }

        private void ShowSelected()
        {
            var history = _owner.App.Store.Data.History;
            bool valid = _index >= 0 && _index < history.Count;
            _copy.Enabled = _save.Enabled = _delete.Enabled = valid;
            if (!valid)
            {
                _text.Input.Text = "";
                _meta.Text = "Nothing selected.";
                return;
            }
            var h = history[_index];
            bool image = h.ImagePath is { Length: > 0 };
            _save.Enabled = !image;
            _text.Input.Text = image ? "Image" : h.Text;
            _meta.Text = h.CopiedAt.ToString("dddd, dd MMM yyyy  ·  HH:mm:ss") +
                         (image ? "  ·  image clip" : "  ·  " + h.Text.Length + " characters");
        }

        public void ApplyScale() => LayoutPage();

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutPage();
        }

        private void LayoutPage()
        {
            float s = DeviceDpi / 96f;
            int W = ClientSize.Width, H = ClientSize.Height;
            int pad = (int)(26 * s);
            int top = (int)(92 * s);
            int h = H - top - pad;
            int listW = (int)(380 * s);

            _listCard.Bounds = new Rectangle(pad, top, listW, h);
            _list.SetBounds((int)(8 * s), (int)(12 * s), listW - (int)(16 * s), h - (int)(24 * s));

            int dx = pad + listW + (int)(18 * s);
            _detailCard.Bounds = new Rectangle(dx, top, W - dx - pad, h);
            int dw = _detailCard.ClientSize.Width;
            _meta.SetBounds((int)(18 * s), (int)(16 * s), dw - (int)(36 * s), (int)(18 * s));
            _text.SetBounds((int)(18 * s), (int)(44 * s), dw - (int)(36 * s), h - (int)(44 * s) - (int)(60 * s));
            _copy.SetBounds((int)(18 * s), h - (int)(50 * s), (int)(96 * s), (int)(32 * s));
            _save.SetBounds((int)(122 * s), h - (int)(50 * s), (int)(130 * s), (int)(32 * s));
            _delete.SetBounds((int)(260 * s), h - (int)(50 * s), (int)(96 * s), (int)(32 * s));
            _clear.SetBounds(W - pad - (int)(120 * s), (int)(46 * s), (int)(120 * s), (int)(32 * s));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.PaintCanvas(e.Graphics, ClientSize);
            float s = DeviceDpi / 96f;
            Theme.TextAt(e.Graphics, "History", Theme.Display(20f, FontStyle.Bold), Theme.Text, 26 * s, 16 * s);
            Theme.TextAt(e.Graphics, "Everything you copy, ready to reuse.", Theme.Ui(9.8f), Theme.Faint, 27 * s, 54 * s);
        }
    }

    // =====================================================================
    internal sealed class SettingsPage : Panel
    {
        private readonly DashboardForm _owner;
        private readonly Card _appearanceCard, _hotkeyCard, _behaviorCard, _gifCard, _windowsCard, _installCard, _aboutCard;
        private readonly PillButton _themeSystem, _themeLight, _themeDark;
        private readonly PillButton _emojiWindows, _emojiOutline;
        private readonly HotkeyBox _hotkey;
        private readonly SwitchToggle _winV, _paste, _startup, _integration;
        private readonly SwitchToggle _trayIcon;
        private readonly TextLabel _winVLabel, _pasteLabel, _startupLabel, _integrationLabel, _trayLabel;
        private readonly TextLabel _themeHint, _accentHint, _emojiStyleLabel, _emojiStyleHint;
        private readonly TextLabel _hotkeyHint, _winVHint, _limitLabel, _winHint, _iconSizeLabel;
        private readonly FieldBox _limit, _iconSize, _gifKey;
        private readonly PillButton _openApps, _openDefaults, _openData, _openLog, _quit;
        private readonly PillButton _uninstall;
        private readonly TextLabel _quitHint, _version, _gifKeyLabel, _gifHint;
        private readonly TextLabel _installVersion, _installPath, _installSize, _installHint;
        private readonly ViewportPanel _viewport;
        private readonly Card[] _cards;
        private int _scroll;
        private int _contentH;
        private bool _syncing;

        public SettingsPage(DashboardForm owner)
        {
            _owner = owner;
            DoubleBuffered = true;
            BackColor = Theme.Bg;
            // WinForms AutoScroll moves children behind a LayoutPage that pins them back,
            // so scroll the cards by hand inside a clipping viewport below the header.
            AutoScroll = false;

            _viewport = new ViewportPanel { BackColor = Theme.Bg };
            Controls.Add(_viewport);

            _appearanceCard = new Card { Caption = "Appearance" };
            _hotkeyCard = new Card { Caption = "Shortcut" };
            _behaviorCard = new Card { Caption = "Behavior" };
            _gifCard = new Card { Caption = "GIF search" };
            _windowsCard = new Card { Caption = "Windows integration" };
            _installCard = new Card { Caption = "Installation" };
            _aboutCard = new Card { Caption = "About" };
            _cards = new[] { _appearanceCard, _hotkeyCard, _behaviorCard, _gifCard, _windowsCard, _installCard, _aboutCard };
            foreach (var card in _cards) _viewport.Controls.Add(card);

            _themeSystem = new PillButton { Text = "System", Kind = PillButton.Style.Outline };
            _themeLight = new PillButton { Text = "Light", Kind = PillButton.Style.Outline };
            _themeDark = new PillButton { Text = "Dark", Kind = PillButton.Style.Outline };
            _appearanceCard.Controls.Add(_themeSystem);
            _appearanceCard.Controls.Add(_themeLight);
            _appearanceCard.Controls.Add(_themeDark);

            _themeHint = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.4f),
                Text = "Follows Windows by default. Pick Light or Dark to keep a fixed theme.",
            };
            _accentHint = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.4f),
                Text = "The accent color always comes from Windows.",
            };
            _appearanceCard.Controls.Add(_themeHint);
            _appearanceCard.Controls.Add(_accentHint);

            _emojiWindows = new PillButton { Text = "Windows color", Kind = PillButton.Style.Primary };
            _emojiOutline = new PillButton { Text = "Outline", Kind = PillButton.Style.Outline };
            _appearanceCard.Controls.Add(_emojiWindows);
            _appearanceCard.Controls.Add(_emojiOutline);

            _emojiStyleLabel = new TextLabel
            {
                ForeColor = Theme.Muted,
                Font = Theme.Ui(8.8f, FontStyle.Bold),
                Text = "Emoji style",
            };
            _emojiStyleHint = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.4f),
                Text = "Windows color matches the built-in emoji picker. Outline keeps a monochrome look in the flyout.",
            };
            _appearanceCard.Controls.Add(_emojiStyleLabel);
            _appearanceCard.Controls.Add(_emojiStyleHint);

            _hotkey = new HotkeyBox();
            _hotkeyCard.Controls.Add(_hotkey);

            _winV = new SwitchToggle();
            _winVLabel = new TextLabel { Text = "Replace Win+V with Clipped", Font = Theme.Ui(9.2f) };
            _hotkeyCard.Controls.Add(_winV);
            _hotkeyCard.Controls.Add(_winVLabel);

            _hotkeyHint = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.4f),
                Text = "Click the box, then press your combo. Clipped swallows it before apps or Windows see it.",
            };
            _winVHint = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.4f),
                Text = "On: Clipped replaces the built-in clipboard history. Off hands Win+V back to Windows.",
            };
            _hotkeyCard.Controls.Add(_hotkeyHint);
            _hotkeyCard.Controls.Add(_winVHint);

            _paste = new SwitchToggle();
            _pasteLabel = new TextLabel { Text = "Paste automatically after choosing an item", Font = Theme.Ui(9.2f) };
            _behaviorCard.Controls.Add(_paste);
            _behaviorCard.Controls.Add(_pasteLabel);

            _trayIcon = new SwitchToggle();
            _trayLabel = new TextLabel { Text = "Show Clipped in the system tray", Font = Theme.Ui(9.2f) };
            _behaviorCard.Controls.Add(_trayIcon);
            _behaviorCard.Controls.Add(_trayLabel);

            _limitLabel = new TextLabel
            {
                ForeColor = Theme.Muted,
                Font = Theme.Ui(8.8f),
                Text = "items kept in history",
            };
            _behaviorCard.Controls.Add(_limitLabel);

            _limit = new FieldBox();
            _limit.Input.TextAlign = HorizontalAlignment.Center;
            _behaviorCard.Controls.Add(_limit);

            _iconSizeLabel = new TextLabel
            {
                ForeColor = Theme.Muted,
                Font = Theme.Ui(8.8f),
                Text = "px  ·  size of the bottom icons in the picker",
            };
            _behaviorCard.Controls.Add(_iconSizeLabel);

            _iconSize = new FieldBox();
            _iconSize.Input.TextAlign = HorizontalAlignment.Center;
            _behaviorCard.Controls.Add(_iconSize);

            _gifKey = new FieldBox();
            _gifCard.Controls.Add(_gifKey);

            _gifKeyLabel = new TextLabel
            {
                ForeColor = Theme.Muted,
                Font = Theme.Ui(8.8f, FontStyle.Bold),
                Text = "KLIPY API key",
            };
            _gifCard.Controls.Add(_gifKeyLabel);

            _gifHint = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.4f),
                Text = "Powers the GIF tab in the picker — the Tenor-style search. Get a free key at klipy.com, then paste it here. Leave it empty to keep the tab off.",
            };
            _gifCard.Controls.Add(_gifHint);

            _startup = new SwitchToggle();            _startupLabel = new TextLabel
            {
                Text = "Launch at startup  (shows in Settings → Apps → Startup)",
                Font = Theme.Ui(9.2f),
            };
            _windowsCard.Controls.Add(_startup);
            _windowsCard.Controls.Add(_startupLabel);

            _integration = new SwitchToggle();
            _integrationLabel = new TextLabel
            {
                Text = "Show Clipped in Installed apps and Default apps",
                Font = Theme.Ui(9.2f),
            };
            _windowsCard.Controls.Add(_integration);
            _windowsCard.Controls.Add(_integrationLabel);

            _winHint = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.4f),
                Text = "Shows up in Settings natively — no injection, survives Windows updates.",
            };
            _windowsCard.Controls.Add(_winHint);

            _openApps = new PillButton { Text = "Open Installed apps", Kind = PillButton.Style.Outline };
            _openDefaults = new PillButton { Text = "Open Default apps", Kind = PillButton.Style.Outline };
            _windowsCard.Controls.Add(_openApps);
            _windowsCard.Controls.Add(_openDefaults);

            _installVersion = new TextLabel { ForeColor = Theme.Muted, Font = Theme.Ui(9f) };
            _installPath = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(8.4f) };
            _installSize = new TextLabel { ForeColor = Theme.Faint, Font = Theme.Ui(8.4f) };
            _uninstall = new PillButton { Text = "Uninstall Clipped", Kind = PillButton.Style.Danger };
            _installHint = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.4f),
                Text = "Settings → Apps → Installed apps → Clipped → Modify lands on this page.",
            };
            _installCard.Controls.Add(_installVersion);
            _installCard.Controls.Add(_installPath);
            _installCard.Controls.Add(_installSize);
            _installCard.Controls.Add(_uninstall);
            _installCard.Controls.Add(_installHint);

            _openData = new PillButton { Text = "Open data folder", Kind = PillButton.Style.Outline };
            _openLog = new PillButton { Text = "View log", Kind = PillButton.Style.Outline };
            _quit = new PillButton { Text = "Quit Clipped", Kind = PillButton.Style.Outline };
            _quitHint = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.4f),
                Text = "Clipped keeps running in the background so the shortcut still works. Quit here or from the tray.",
            };
            _version = new TextLabel
            {
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.4f),
                Alignment = ContentAlignment.MiddleRight,
                Text = "v" + WindowsIntegration.AppVersion + "  ·  MIT license  ·  github.com/GLITCH54s/clipped",
            };
            _aboutCard.Controls.Add(_openData);
            _aboutCard.Controls.Add(_openLog);
            _aboutCard.Controls.Add(_quit);
            _aboutCard.Controls.Add(_quitHint);
            _aboutCard.Controls.Add(_version);

            _hotkey.ValueChanged += (_, _) =>
            {
                if (_syncing) return;
                _owner.App.Store.Data.Settings.Hotkey = _hotkey.Value;
                _owner.App.Store.Save();
                // Windows reserves Win+V, so that shortcut only exists as the takeover.
                if (_hotkey.Value.Trim().Equals("Win+V", StringComparison.OrdinalIgnoreCase) &&
                    !_owner.App.Store.Data.Settings.TakeOverWinV)
                    _owner.App.SetTakeOverWinV(true);
                _owner.App.ApplyHotkey();
            };

            _winV.CheckedChanged += (_, _) =>
            {
                if (_syncing) return;
                _owner.App.SetTakeOverWinV(_winV.Checked);
            };

            _paste.CheckedChanged += (_, _) =>
            {
                if (_syncing) return;
                _owner.App.Store.Data.Settings.PasteAfterSelect = _paste.Checked;
                _owner.App.Store.Save();
            };

            _trayIcon.CheckedChanged += (_, _) =>
            {
                if (_syncing) return;
                _owner.App.SetShowTrayIcon(_trayIcon.Checked);
            };

            _startup.CheckedChanged += (_, _) =>
            {
                if (_syncing) return;
                _owner.App.Store.Data.Settings.LaunchAtStartup = _startup.Checked;
                WindowsIntegration.SetStartup(_startup.Checked);
                _owner.App.Store.Save();
            };

            _integration.CheckedChanged += (_, _) =>
            {
                if (_syncing) return;
                _owner.App.Store.Data.Settings.WindowsIntegration = _integration.Checked;
                WindowsIntegration.RegisterIntegration(_integration.Checked);
                _owner.App.Store.Save();
            };

            _limit.Input.TextChanged += (_, _) =>
            {
                if (_syncing) return;
                if (int.TryParse(_limit.Input.Text, out var n))
                {
                    n = Math.Clamp(n, 10, 500);
                    _owner.App.Store.Data.Settings.HistoryLimit = n;
                    _owner.App.Store.Save();
                }
            };
            _limit.Input.Leave += (_, _) =>
            {
                _syncing = true;
                _limit.Input.Text = _owner.App.Store.Data.Settings.HistoryLimit.ToString();
                _syncing = false;
            };

            _iconSize.Input.TextChanged += (_, _) =>
            {
                if (_syncing) return;
                if (int.TryParse(_iconSize.Input.Text, out var n))
                {
                    n = Math.Clamp(n, 24, 48);
                    _owner.App.Store.Data.Settings.PickerIconSize = n;
                    _owner.App.Store.Save();
                }
            };
            _iconSize.Input.Leave += (_, _) =>
            {
                _syncing = true;
                _iconSize.Input.Text = _owner.App.Store.Data.Settings.PickerIconSize.ToString();
                _syncing = false;
            };

            _gifKey.Input.TextChanged += (_, _) =>
            {
                if (_syncing) return;
                _owner.App.Store.Data.Settings.GifApiKey = _gifKey.Input.Text.Trim();
                _owner.App.Store.Save();
            };

            _openApps.Click += (_, _) => WindowsIntegration.OpenSettingsPage("appsfeatures");
            _openDefaults.Click += (_, _) => WindowsIntegration.OpenSettingsPage("defaultapps");
            _uninstall.Click += (_, _) =>
            {
                if (WindowsIntegration.IsPackaged) WindowsIntegration.OpenSettingsPage("appsfeatures");
                else WindowsIntegration.LaunchUninstall();
            };
            _openData.Click += (_, _) => OpenFolder(Paths.DataDir);
            _openLog.Click += (_, _) => OpenFile(Path.Combine(Paths.DataDir, "clipped.log"));
            _quit.Click += (_, _) => _owner.App.Quit();

            _themeSystem.Click += (_, _) => ChooseTheme(ThemeMode.System);
            _themeLight.Click += (_, _) => ChooseTheme(ThemeMode.Light);
            _themeDark.Click += (_, _) => ChooseTheme(ThemeMode.Dark);
            _emojiWindows.Click += (_, _) => ChooseEmojiStyle("windows");
            _emojiOutline.Click += (_, _) => ChooseEmojiStyle("outline");
        }

        private void ChooseEmojiStyle(string style)
        {
            _owner.App.Store.Data.Settings.EmojiStyle = style;
            _owner.App.Store.Save();
            UpdateEmojiButtons();
        }

        private void UpdateEmojiButtons()
        {
            var style = _owner.App.Store.Data.Settings.EmojiStyle;
            foreach (var (button, value) in new[]
                     {
                         (_emojiWindows, "windows"),
                         (_emojiOutline, "outline"),
                     })
            {
                button.Kind = string.Equals(style, value, StringComparison.OrdinalIgnoreCase)
                    ? PillButton.Style.Primary
                    : PillButton.Style.Outline;
                button.Invalidate();
            }
        }

        private void ChooseTheme(ThemeMode mode)
        {
            _owner.App.SetThemeMode(mode);
            UpdateThemeButtons();
        }

        private void UpdateThemeButtons()
        {
            foreach (var (button, mode) in new[]
                     {
                         (_themeSystem, ThemeMode.System),
                         (_themeLight, ThemeMode.Light),
                         (_themeDark, ThemeMode.Dark),
                     })
            {
                button.Kind = Theme.Mode == mode ? PillButton.Style.Primary : PillButton.Style.Outline;
                button.Invalidate();
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + path + "\"")
                { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("open folder failed", ex); }
        }

        private void OpenFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("open file failed", ex); }
        }

        public void SyncFromStore()
        {
            _syncing = true;
            var st = _owner.App.Store.Data.Settings;
            _hotkey.SetValue(st.Hotkey);
            _winV.Checked = st.TakeOverWinV;
            _paste.Checked = st.PasteAfterSelect;
            _trayIcon.Checked = st.ShowTrayIcon;
            st.LaunchAtStartup = WindowsIntegration.StartupEnabled;
            _startup.Checked = st.LaunchAtStartup;
            _integration.Checked = st.WindowsIntegration;
            _limit.Input.Text = st.HistoryLimit.ToString();
            _iconSize.Input.Text = st.PickerIconSize.ToString();
            _gifKey.Input.Text = st.GifApiKey;
            _installVersion.Text = "Version " + WindowsIntegration.AppVersion;
            _installPath.Text = "Installed in " + WindowsIntegration.InstallFolder;
            _installSize.Text = WindowsIntegration.IsPackaged
                ? "Installed by Microsoft Store"
                : WindowsIntegration.InstallSizeText + " on disk";
            if (WindowsIntegration.IsPackaged)
            {
                _startup.Checked = false;
                _startup.Enabled = false;
                _startupLabel.Text = "Launch at startup  (managed in Settings → Apps → Startup)";
                _integration.Checked = true;
                _integration.Enabled = false;
                _winHint.Text = "Installed from the Microsoft Store — Windows owns startup, default apps and uninstall.";
            }
            _syncing = false;
            UpdateThemeButtons();
            UpdateEmojiButtons();
            LayoutPage();
        }

        public void ApplyScale() => LayoutPage();

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.ApplyControlTheme(this, Theme.IsLight, "ScrollBar");
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutPage();
        }

        private void LayoutPage()
        {
            float s = DeviceDpi / 96f;
            int W = ClientSize.Width;
            int pad = (int)(26 * s);
            int cardW = W - pad * 2;
            int head = (int)(84 * s);
            _viewport.Bounds = new Rectangle(0, head, Math.Max(10, W - (int)(12 * s)), Math.Max(10, ClientSize.Height - head));
            int y = (int)(8 * s);
            int gap = (int)(12 * s);

            int appH = (int)(226 * s);
            _appearanceCard.Bounds = new Rectangle(pad, y, cardW, appH);
            int themeW = (int)(96 * s);
            _themeSystem.SetBounds((int)(18 * s), (int)(44 * s), themeW, (int)(32 * s));
            _themeLight.SetBounds((int)(18 * s) + themeW + (int)(8 * s), (int)(44 * s), themeW, (int)(32 * s));
            _themeDark.SetBounds((int)(18 * s) + (themeW + (int)(8 * s)) * 2, (int)(44 * s), themeW, (int)(32 * s));
            _themeHint.SetBounds((int)(18 * s), (int)(88 * s), cardW - (int)(36 * s), (int)(14 * s));
            _accentHint.SetBounds((int)(18 * s), (int)(106 * s), cardW - (int)(36 * s), (int)(14 * s));
            _emojiStyleLabel.SetBounds((int)(18 * s), (int)(136 * s), cardW - (int)(36 * s), (int)(18 * s));
            _emojiWindows.SetBounds((int)(18 * s), (int)(158 * s), (int)(126 * s), (int)(32 * s));
            _emojiOutline.SetBounds((int)(18 * s) + (int)(134 * s), (int)(158 * s), (int)(96 * s), (int)(32 * s));
            _emojiStyleHint.SetBounds((int)(18 * s), (int)(200 * s), cardW - (int)(36 * s), (int)(14 * s));
            y += appH + gap;

            _hotkeyCard.Bounds = new Rectangle(pad, y, cardW, (int)(150 * s));
            _hotkey.SetBounds((int)(18 * s), (int)(44 * s), (int)(280 * s), (int)(38 * s));
            _winV.Bounds = new Rectangle((int)(322 * s), (int)(50 * s), (int)(42 * s), (int)(24 * s));
            _winVLabel.SetBounds((int)(374 * s), (int)(50 * s), cardW - (int)(400 * s), (int)(24 * s));
            _hotkeyHint.SetBounds((int)(18 * s), (int)(96 * s), cardW - (int)(36 * s), (int)(14 * s));
            _winVHint.SetBounds((int)(18 * s), (int)(114 * s), cardW - (int)(36 * s), (int)(14 * s));
            y += (int)(150 * s) + gap;

            _behaviorCard.Bounds = new Rectangle(pad, y, cardW, (int)(218 * s));
            _paste.Bounds = new Rectangle((int)(18 * s), (int)(48 * s), (int)(42 * s), (int)(24 * s));
            _pasteLabel.SetBounds((int)(70 * s), (int)(48 * s), cardW - (int)(100 * s), (int)(24 * s));
            _trayIcon.Bounds = new Rectangle((int)(18 * s), (int)(84 * s), (int)(42 * s), (int)(24 * s));
            _trayLabel.SetBounds((int)(70 * s), (int)(84 * s), cardW - (int)(100 * s), (int)(24 * s));
            _limit.SetBounds((int)(18 * s), (int)(124 * s), (int)(80 * s), (int)(34 * s));
            _limitLabel.SetBounds((int)(112 * s), (int)(133 * s), cardW - (int)(130 * s), (int)(18 * s));
            _iconSize.SetBounds((int)(18 * s), (int)(168 * s), (int)(80 * s), (int)(34 * s));
            _iconSizeLabel.SetBounds((int)(112 * s), (int)(177 * s), cardW - (int)(130 * s), (int)(18 * s));
            y += (int)(218 * s) + gap;

            _gifCard.Bounds = new Rectangle(pad, y, cardW, (int)(140 * s));
            _gifKey.SetBounds((int)(18 * s), (int)(44 * s), (int)(280 * s), (int)(38 * s));
            _gifKeyLabel.SetBounds((int)(312 * s), (int)(50 * s), cardW - (int)(330 * s), (int)(24 * s));
            _gifHint.SetBounds((int)(18 * s), (int)(96 * s), cardW - (int)(36 * s), (int)(30 * s));
            y += (int)(140 * s) + gap;

            _windowsCard.Bounds = new Rectangle(pad, y, cardW, (int)(228 * s));
            _startup.Bounds = new Rectangle((int)(18 * s), (int)(44 * s), (int)(42 * s), (int)(24 * s));
            _startupLabel.SetBounds((int)(70 * s), (int)(44 * s), cardW - (int)(100 * s), (int)(24 * s));
            _integration.Bounds = new Rectangle((int)(18 * s), (int)(82 * s), (int)(42 * s), (int)(24 * s));
            _integrationLabel.SetBounds((int)(70 * s), (int)(82 * s), cardW - (int)(100 * s), (int)(24 * s));
            _openApps.SetBounds((int)(18 * s), (int)(124 * s), (int)(150 * s), (int)(32 * s));
            _openDefaults.SetBounds((int)(178 * s), (int)(124 * s), (int)(146 * s), (int)(32 * s));
            _winHint.SetBounds((int)(18 * s), (int)(170 * s), cardW - (int)(36 * s), (int)(16 * s));
            y += (int)(228 * s) + gap;

            _installCard.Bounds = new Rectangle(pad, y, cardW, (int)(196 * s));
            _installVersion.SetBounds((int)(18 * s), (int)(44 * s), cardW - (int)(36 * s), (int)(18 * s));
            _installPath.SetBounds((int)(18 * s), (int)(64 * s), cardW - (int)(36 * s), (int)(16 * s));
            _installSize.SetBounds((int)(18 * s), (int)(84 * s), cardW - (int)(36 * s), (int)(16 * s));
            _uninstall.SetBounds((int)(18 * s), (int)(112 * s), (int)(150 * s), (int)(32 * s));
            _installHint.SetBounds((int)(18 * s), (int)(156 * s), cardW - (int)(36 * s), (int)(16 * s));
            y += (int)(196 * s) + gap;

            _aboutCard.Bounds = new Rectangle(pad, y, cardW, (int)(150 * s));
            _openData.SetBounds((int)(18 * s), (int)(60 * s), (int)(140 * s), (int)(32 * s));
            _openLog.SetBounds((int)(168 * s), (int)(60 * s), (int)(100 * s), (int)(32 * s));
            _quit.SetBounds((int)(278 * s), (int)(60 * s), (int)(112 * s), (int)(32 * s));
            _quitHint.SetBounds((int)(18 * s), (int)(104 * s), cardW - (int)(36 * s), (int)(16 * s));
            _version.SetBounds(cardW / 2, (int)(12 * s), cardW / 2 - (int)(36 * s), (int)(16 * s));
            y += (int)(150 * s) + gap;

            _contentH = y + (int)(8 * s);
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _contentH - _viewport.Height));
            foreach (var card in _cards) card.Top -= _scroll;
        }

        internal int FullHeight => _viewport.Top + _contentH;

        internal int ScrollOffset => _scroll;

        internal void ScrollTo(int value) => SetScroll(value);

        private void SetScroll(int value)
        {
            value = Math.Clamp(value, 0, Math.Max(0, _contentH - _viewport.Height));
            if (value == _scroll) return;
            _scroll = value;
            LayoutPage();
            Invalidate();
            _viewport.Invalidate(true);
        }

        public void ScrollBy(int delta)
        {
            if (delta == 0) return;
            int lines = SystemInformation.MouseWheelScrollLines;
            float s = DeviceDpi / 96f;
            int step = lines < 0
                ? (int)Math.Round(-delta / 120f * _viewport.Height)
                : (int)Math.Round(-delta / 120f * lines * 34 * s);
            SetScroll(_scroll + step);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(e.Delta);
            if (e is HandledMouseEventArgs h) h.Handled = true;
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.PaintCanvas(e.Graphics, ClientSize);
            float s = DeviceDpi / 96f;
            var g = e.Graphics;
            Theme.TextAt(g, "Settings", Theme.Display(20f, FontStyle.Bold), Theme.Text, 26 * s, 16 * s);
            Theme.TextAt(g, "Appearance, shortcut, behavior and Windows integration.", Theme.Ui(9.8f), Theme.Faint, 27 * s, 54 * s);

            int viewH = _viewport.Height;
            int range = _contentH - viewH;
            if (range > 0)
            {
                float trackTop = _viewport.Top + 8 * s;
                float trackH = viewH - 16 * s;
                float thumbH = Math.Max(28 * s, trackH * viewH / Math.Max(1f, _contentH));
                float offset = Math.Clamp(_scroll / (float)range, 0f, 1f);
                var thumb = new RectangleF(Width - 7 * s, trackTop + (trackH - thumbH) * offset, 3 * s, thumbH);
                Theme.FillRound(g, thumb, 1.5f * s, Theme.BorderHi);
            }
        }
    }
}

/// <summary>Clipping surface for the hand-scrolled settings cards.</summary>
internal sealed class ViewportPanel : Panel
{
    public ViewportPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}
