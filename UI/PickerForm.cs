using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Clipped.Core;

namespace Clipped.UI;

/// <summary>The Win+V replacement, drawn to match the Windows clipboard flyout.</summary>
public sealed class PickerForm : Form
{
    private enum Mode { Recent, Tab, Search, Dates, Sequence, Emoji, Gif }

    private sealed class Item
    {
        public string Title = "";
        public string Sub = "";
        public string Tag = "";
        public string? Paste;
        public Block? Block;
        public string? TabName;
    }

    private sealed class EmojiCategory
    {
        public string Label = "";
        public string Icon = "";
        public bool Recent;
        public EmojiEntry[] Entries = Array.Empty<EmojiEntry>();
    }

    private const int BaseW = 508;
    private const int BaseH = 572;
    private const int BasePad = 24;
    private const int BaseFooterH = 48;
    private const int BaseTabsY = 24;
    private const int BaseTabsBox = 36;
    private const int BaseSearchY = 78;
    private const int BaseSearchH = 34;
    private const int BaseTitleY = 121;
    private const int BaseBodyTop = 156;
    private const int BaseCardH = 64;
    private const int BaseCardGap = 8;
    private const int BaseEmojiCell = 48;
    private const int BaseGifChipY = 152;
    private const int BaseGifGridTop = 190;
    private const int BaseGifCellH = 138;
    private const int BaseGifGap = 8;

    private readonly AppController _app;
    private readonly TextBox _search;
    private readonly List<Item> _items = new();
    private readonly List<(RectangleF Rect, int Index)> _cardHits = new();
    private readonly List<(RectangleF Rect, string Action, string Id)> _barHits = new();
    private readonly List<(RectangleF Rect, int Index)> _emojiHits = new();
    private readonly List<(RectangleF Rect, int Index)> _gifHits = new();
    private readonly List<(RectangleF Rect, int Index)> _gifChipHits = new();
    private readonly List<GifItem> _gifItems = new();
    private readonly Dictionary<string, GifPreview> _gifPreviews = new(StringComparer.Ordinal);
    private readonly HashSet<string> _gifBusy = new(StringComparer.Ordinal);
    private readonly List<(RectangleF Rect, string Id)> _tabHits = new();
    private readonly List<(RectangleF Rect, int Index)> _catHits = new();
    private RectangleF _closeHit;
    private RectangleF _gearHit;
    private RectangleF _handleHit;
    private RectangleF _backHit;
    private RectangleF _gifFileHit;
    private int _openedAt;

    private Mode _mode = Mode.Recent;
    private string _tabId = "recent";
    private Block? _sequence;
    private string _dateFormat = "dd MMM yyyy";
    private int _sel;
    private int _scroll;
    private int _emojiScroll;
    private int _emojiCat;
    private int _hoverCard = -1;
    private int _hoverEmoji = -1;
    private int _hoverGif = -1;
    private string _hoverTab = "";
    private int _hoverCat = -1;
    private int _hoverChip = -1;
    private bool _hoverBack;
    private bool _hoverBar;
    private string _hoverBarKey = "";
    private bool _hoverClose;
    private bool _hoverGear;
    private bool _dragging;
    private Point _dragStart;
    private Point _dragOrigin;
    private List<EmojiEntry> _emojiView = new();
    private List<EmojiCategory> _emojiCats = new();
    private static Dictionary<string, EmojiEntry>? _emojiByName;

    private bool _suppress;
    private bool _busyClosing;
    private float _s = 1f;
    private float _iconSize = 34f;
    private IntPtr _target;
    private int _appliedDpi = -1;

    private GifProvider? _gifProvider;
    private int _gifSel;
    private int _gifScroll;
    private string _gifCat = "";
    private string _gifError = "";
    private bool _gifLoading;
    private CancellationTokenSource? _gifCts;
    private readonly System.Windows.Forms.Timer _gifTimer;
    private readonly System.Windows.Forms.Timer _gifSearchTimer;

    private Font _fPanelTitle = Theme.Display(15f, FontStyle.Bold);
    private Font _fTitle = Theme.Ui(9.8f);
    private Font _fTitleBold = Theme.Ui(9.8f, FontStyle.Bold);
    private Font _fSub = Theme.Ui(8.4f);
    private Font _fChip = Theme.Ui(8.4f, FontStyle.Bold);
    private Font _fTag = Theme.Ui(7.6f);
    private Font _fHint = Theme.Ui(8.2f);
    private Font _fTab = Theme.Ui(10f, FontStyle.Bold);
    private Font _fEmpty = Theme.Ui(9.6f);
    private Font _fEmoji = new("Segoe UI Emoji", 30f, FontStyle.Regular, GraphicsUnit.Pixel);

    public PickerForm(AppController app)
    {
        _app = app;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Theme.Bg;
        Font = Theme.Ui(9f);
        Text = "Clipped";
        Icon = AppIcon.Get();
        Opacity = _app.Headless ? 1.0 : 0.95;

        _search = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            Font = Theme.Ui(10.5f),
            PlaceholderText = "Search clips and snippets",
        };
        Controls.Add(_search);

        _search.TextChanged += (_, _) => OnQueryChanged();
        _search.KeyDown += OnSearchKeyDown;
        _search.KeyPress += OnSearchKeyPress;
        KeyDown += OnFormKeyDown;

        _gifTimer = new System.Windows.Forms.Timer { Interval = 90 };
        _gifTimer.Tick += (_, _) =>
        {
            if (_mode != Mode.Gif || _gifPreviews.Count == 0) return;
            foreach (var preview in _gifPreviews.Values) preview.Advance();
            Invalidate();
        };

        _gifSearchTimer = new System.Windows.Forms.Timer { Interval = 260 };
        _gifSearchTimer.Tick += (_, _) =>
        {
            _gifSearchTimer.Stop();
            LoadGifs(_search.Text.Trim());
        };

        MouseWheel += (_, e) =>
        {
            if (_mode == Mode.Emoji) EmojiScrollBy(-Math.Sign(e.Delta) * EmojiColumns());
            else if (_mode == Mode.Gif) GifScrollBy(-Math.Sign(e.Delta));
            else ScrollBy(-Math.Sign(e.Delta));
        };
        MouseMove += OnFormMouseMove;
        MouseLeave += (_, _) =>
        {
            _hoverCard = -1;
            _hoverEmoji = -1;
            _hoverGif = -1;
            _hoverTab = "";
            _hoverCat = -1;
            _hoverBack = false;
            _hoverBar = false;
            _hoverBarKey = "";
            _hoverClose = false;
            _hoverGear = false;
            Invalidate();
        };
        MouseDown += OnFormMouseDown;
        MouseUp += (_, _) => _dragging = false;
        DpiChanged += (_, _) => ApplyMetrics();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    public bool IsOpen => Visible;

    public void Open(IntPtr target)
    {
        _target = target;
        _mode = Mode.Recent;
        _tabId = "recent";
        _sequence = null;
        _sel = 0;
        _scroll = 0;
        _emojiScroll = 0;
        _emojiCat = 0;
        _hoverCard = -1;
        _hoverEmoji = -1;
        _hoverGif = -1;
        _hoverTab = "";
        _hoverCat = -1;
        _hoverBack = false;
        _hoverBar = false;
        _hoverBarKey = "";
        _dragging = false;
        _suppress = true;
        _search.Text = "";
        _search.PlaceholderText = "Search clips and snippets";
        _suppress = false;
        ResetGifState();

        PositionOnCursorMonitor();
        ApplyMetrics();
        Build();
        Show();
        _openedAt = Environment.TickCount;
        Native.ForceForeground(Handle);
        _search.Focus();
        _search.Select(_search.TextLength, 0);
        Invalidate();
    }

    internal void PreviewTab(string tabId) => SwitchTab(tabId);

    internal void PreviewEmoji() => OpenEmojiMode();

    internal void PreviewGif() => OpenGifMode();

    internal void PreviewDates(string format)
    {
        _dateFormat = format;
        OpenDateMode();
    }

    public void ClosePicker()
    {
        if (!Visible) return;
        _busyClosing = false;
        Hide();
        _search.Text = "";
        _target = IntPtr.Zero;
        ResetGifState();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_app.Headless) return;
        // Ignore focus churn from the window itself appearing; only an actual click-away closes.
        if (!_busyClosing && Visible && !ContainsFocus && Environment.TickCount - _openedAt > 350)
        {
            BeginInvoke(() => { if (Visible && !ContainsFocus) ClosePicker(); });
        }
    }

    private void ApplyMetrics()
    {
        if (DeviceDpi == _appliedDpi) return;
        _appliedDpi = DeviceDpi;
        _s = Math.Clamp(DeviceDpi / 96f, 0.75f, 2f);
        _iconSize = Math.Clamp(_app.Store.Data.Settings.PickerIconSize, 24, 48) * _s;

        SuspendLayout();
        ClientSize = new Size((int)(BaseW * _s), (int)(BaseH * _s));
        ResumeLayout(false);

        _fPanelTitle = Theme.Display(15f, FontStyle.Bold);
        _fTitle = Theme.Ui(9.8f);
        _fTitleBold = Theme.Ui(9.8f, FontStyle.Bold);
        _fSub = Theme.Ui(8.4f);
        _fChip = Theme.Ui(8.4f, FontStyle.Bold);
        _fTag = Theme.Ui(7.6f);
        _fHint = Theme.Ui(8.2f);
        _fTab = Theme.Ui(10f, FontStyle.Bold);
        _fEmpty = Theme.Ui(9.6f);
        _fEmoji = new Font("Segoe UI Emoji", 30 * _s, FontStyle.Regular, GraphicsUnit.Pixel);
        _search.Font = Theme.Ui(10.5f);

        RoundCorners();
        LayoutSearch();
        Invalidate();
    }

    private void RoundCorners()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using var path = Theme.RoundRect(
            new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), S(8));
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    private RectangleF SearchFieldRect() =>
        new(S(BasePad), S(BaseSearchY), ClientSize.Width - S(BasePad * 2), S(BaseSearchH));

    private void LayoutSearch()
    {
        var f = SearchFieldRect();
        int h = _search.PreferredHeight;
        _search.SetBounds(
            (int)(f.X + S(30)),
            (int)(f.Y + (f.Height - h) / 2f),
            (int)(f.Width - S(30) - S(12)),
            h);
    }

    private void PositionOnCursorMonitor()
    {
        int w = (int)(BaseW * _s);
        int h = (int)(BaseH * _s);
        int gap = (int)(8 * _s);
        var anchor = _target != IntPtr.Zero ? _target : Native.GetForegroundWindow();

        int x, y;
        Rectangle work;
        if (Native.TryGetCaretRect(anchor, out var caret))
        {
            // Windows draws the flyout beside the text caret: below when it fits, otherwise above.
            work = Native.WorkAreaForPoint(new Native.POINT { X = caret.Left, Y = caret.Top });
            x = caret.Left;
            y = caret.Bottom + gap;
            if (y + h > work.Bottom - gap) y = caret.Top - h - gap;
        }
        else
        {
            Native.GetCursorPos(out var cur);
            work = Native.WorkAreaForPoint(cur);
            x = cur.X + gap;
            y = cur.Y + gap;
            if (y + h > work.Bottom - gap) y = cur.Y - h - gap;
            if (x + w > work.Right - gap) x = cur.X - w - gap;
        }

        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - w - gap));
        y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - h - gap));
        Bounds = new Rectangle(x, y, w, h);
    }

    private float S(float v) => v * _s;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, !Theme.IsLight);
        Native.SetBorderColor(Handle, Theme.Border);
    }

    // ---------- data ----------

    private void OnQueryChanged()
    {
        if (_suppress) return;
        if (_mode == Mode.Emoji)
        {
            BuildEmojiView();
            _sel = 0;
            _emojiScroll = 0;
            Invalidate();
            return;
        }
        if (_mode == Mode.Gif)
        {
            _gifCat = "";
            _gifSel = 0;
            _gifScroll = 0;
            _gifSearchTimer.Stop();
            _gifSearchTimer.Start();
            Invalidate();
            return;
        }
        if (_search.Text.Length > 0) _mode = Mode.Search;
        else _mode = _tabId == "recent" ? Mode.Recent : Mode.Tab;
        _sel = 0;
        _scroll = 0;
        Build();
    }

    private void Build()
    {
        _items.Clear();
        var store = _app.Store;

        switch (_mode)
        {
            case Mode.Emoji:
                break;

            case Mode.Recent:
                foreach (var h in store.Data.History)
                {
                    _items.Add(new Item
                    {
                        Title = Trim(Block.OneLine(h.Text), 110),
                        Sub = Ago(h.CopiedAt) + "  ·  " + h.Text.Length + " chars",
                        Tag = "clip",
                        Paste = h.Text,
                    });
                }
                break;

            case Mode.Tab:
            {
                var tab = store.FindTab(_tabId);
                if (tab is null) { SwitchTab("recent"); return; }
                foreach (var b in tab.Blocks)
                {
                    _items.Add(new Item
                    {
                        Title = string.IsNullOrWhiteSpace(b.Label) ? Block.OneLine(b.Preview()) : b.Label,
                        Sub = Trim(Block.OneLine(b.Preview()), 110),
                        Tag = KindTag(b.Kind),
                        Block = b,
                        TabName = tab.Name,
                    });
                }
                break;
            }

            case Mode.Search:
            {
                var q = _search.Text.Trim();
                if (q.Length == 0) { _mode = Mode.Recent; Build(); return; }
                var scored = new List<(int Score, Item Item)>();

                foreach (var tab in store.Data.Tabs)
                {
                    foreach (var b in tab.Blocks)
                    {
                        int score = ScoreHit(q, b.Label, b.Value, tab.Name);
                        if (score < 0) continue;
                        scored.Add((score, new Item
                        {
                            Title = string.IsNullOrWhiteSpace(b.Label) ? Block.OneLine(b.Preview()) : b.Label,
                            Sub = Trim(Block.OneLine(b.Preview()), 100),
                            Tag = tab.Name,
                            Block = b,
                            TabName = tab.Name,
                        }));
                    }
                }

                foreach (var h in store.Data.History)
                {
                    if (!h.Text.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                    scored.Add((6, new Item
                    {
                        Title = Trim(Block.OneLine(h.Text), 110),
                        Sub = Ago(h.CopiedAt) + "  ·  clipboard",
                        Tag = "clip",
                        Paste = h.Text,
                    }));
                }

                foreach (var entry in scored
                             .OrderBy(t => t.Score)
                             .ThenBy(t => t.Item.Title, StringComparer.OrdinalIgnoreCase)
                             .Take(60))
                {
                    if (_items.Any(i => i.Paste is not null && i.Paste == entry.Item.Paste)) continue;
                    _items.Add(entry.Item);
                }
                break;
            }

            case Mode.Dates:
            {
                var now = DateTime.Now;
                for (int offset = -3; offset <= 14; offset++)
                {
                    var d = now.AddDays(offset);
                    string title = offset switch
                    {
                        0 => "Today",
                        1 => "Tomorrow",
                        -1 => "Yesterday",
                        _ => d.ToString("dddd"),
                    };
                    _items.Add(new Item
                    {
                        Title = title,
                        Sub = d.ToString("dddd, dd MMM yyyy"),
                        Tag = offset == 0 ? "0 d" : (offset > 0 ? "+" + offset + " d" : offset + " d"),
                        Paste = d.ToString(_dateFormat),
                    });
                }
                if (_search.TextLength > 0)
                {
                    var q = _search.Text.Trim();
                    _items.RemoveAll(i => !i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                                          !i.Sub.Contains(q, StringComparison.OrdinalIgnoreCase));
                }
                break;
            }

            case Mode.Sequence:
            {
                if (_sequence is null) { SwitchTab(_tabId); return; }
                var values = _sequence.ExpandSequence().ToList();
                for (int i = 0; i < values.Count; i++)
                {
                    _items.Add(new Item
                    {
                        Title = values[i],
                        Sub = _sequence.Label,
                        Tag = (i + 1) + " / " + values.Count,
                        Paste = values[i],
                    });
                }
                break;
            }
        }

        if (_sel >= _items.Count) _sel = _items.Count - 1;
        if (_sel < 0) _sel = 0;
        Invalidate();
    }

    private static int ScoreHit(string q, string label, string value, string tabName)
    {
        if (label.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 0;
        if (label.Contains(q, StringComparison.OrdinalIgnoreCase)) return 1;
        if (value.Contains(q, StringComparison.OrdinalIgnoreCase)) return 2;
        if (tabName.Contains(q, StringComparison.OrdinalIgnoreCase)) return 4;
        return -1;
    }

    private static string KindTag(BlockKind kind) => kind switch
    {
        BlockKind.Template => "fill-in",
        BlockKind.Sequence => "sequence",
        BlockKind.Date => "date",
        _ => "text",
    };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";

    private static string Ago(DateTime t)
    {
        var span = DateTime.Now - t;
        if (span.TotalSeconds < 45) return "just now";
        if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " min ago";
        if (span.TotalHours < 24) return (int)span.TotalHours + " h ago";
        if (span.TotalDays < 7) return (int)span.TotalDays + " d ago";
        return t.ToString("dd MMM");
    }

    private void SwitchTab(string tabId)
    {
        _tabId = tabId;
        _sequence = null;
        _mode = tabId == "recent" ? Mode.Recent : Mode.Tab;
        _suppress = true;
        _search.Text = "";
        _search.PlaceholderText = "Search clips and snippets";
        _suppress = false;
        _sel = 0;
        _scroll = 0;
        _emojiScroll = 0;
        Build();
    }

    private void Back()
    {
        if (_mode == Mode.Gif)
        {
            if (_search.TextLength > 0)
            {
                _suppress = true;
                _search.Text = "";
                _suppress = false;
                _gifCat = "";
                LoadGifs("");
                return;
            }
            SwitchTab(_tabId);
            return;
        }
        if (_mode == Mode.Emoji)
        {
            if (_search.TextLength > 0)
            {
                _suppress = true;
                _search.Text = "";
                _suppress = false;
                BuildEmojiView();
                _sel = 0;
                _emojiScroll = 0;
                Invalidate();
                return;
            }
            SwitchTab(_tabId);
            return;
        }
        if (_mode == Mode.Sequence || _mode == Mode.Dates)
        {
            SwitchTab(_tabId);
            return;
        }
        if (_mode == Mode.Tab)
        {
            SwitchTab("recent");
            return;
        }
        if (_mode == Mode.Search)
        {
            _suppress = true;
            _search.Text = "";
            _suppress = false;
            OnQueryChanged();
        }
    }

    private void CycleTab(int dir)
    {
        var ids = new List<string> { "recent" };
        ids.AddRange(_app.Store.Data.Tabs.Select(t => t.Id));
        int idx = ids.IndexOf(_tabId);
        if (idx < 0) idx = 0;
        idx = (idx + dir + ids.Count) % ids.Count;
        SwitchTab(ids[idx]);
    }

    // ---------- emoji ----------

    private static Dictionary<string, EmojiEntry> EmojiByName()
    {
        if (_emojiByName is not null) return _emojiByName;
        var map = new Dictionary<string, EmojiEntry>(StringComparer.Ordinal);
        foreach (var section in EmojiCatalog.Sections)
            foreach (var entry in section.Emojis)
                map[entry.Char] = entry;
        return _emojiByName = map;
    }

    private void BuildEmojiCats()
    {
        _emojiCats = new List<EmojiCategory>();
        var recents = _app.Store.Data.Settings.RecentEmoji;
        if (recents.Count > 0)
        {
            var byChar = EmojiByName();
            var entries = new List<EmojiEntry>();
            foreach (var ch in recents)
                if (byChar.TryGetValue(ch, out var entry)) entries.Add(entry);
            if (entries.Count > 0)
            {
                _emojiCats.Add(new EmojiCategory
                {
                    Label = "Recently used",
                    Recent = true,
                    Entries = entries.ToArray(),
                });
            }
        }

        foreach (var section in EmojiCatalog.Sections)
        {
            _emojiCats.Add(new EmojiCategory
            {
                Label = section.Name,
                Icon = section.Icon,
                Entries = section.Emojis,
            });
        }
    }

    private void BuildEmojiView()
    {
        var view = new List<EmojiEntry>();
        var q = _search.Text.Trim();
        if (q.Length > 0)
        {
            foreach (var section in EmojiCatalog.Sections)
                foreach (var entry in section.Emojis)
                    if (entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                        view.Add(entry);
            _emojiCat = -1;
        }
        else
        {
            if (_emojiCats.Count == 0) BuildEmojiCats();
            _emojiCat = Math.Clamp(_emojiCat, 0, _emojiCats.Count - 1);
            view.AddRange(_emojiCats[_emojiCat].Entries);
        }
        _emojiView = view;
    }

    private void OpenEmojiMode()
    {
        _mode = Mode.Emoji;
        _sel = 0;
        _scroll = 0;
        _emojiScroll = 0;
        _emojiCat = 0;
        _hoverCard = -1;
        _hoverEmoji = -1;
        _hoverTab = "";
        _hoverCat = -1;
        _suppress = true;
        _search.Text = "";
        _search.PlaceholderText = "Search emoji by name";
        _suppress = false;
        BuildEmojiCats();
        BuildEmojiView();
        Build();
        Invalidate();
    }

    private void ChooseEmoji(int index)
    {
        if (index < 0 || index >= _emojiView.Count) return;
        var ch = _emojiView[index].Char;

        var st = _app.Store.Data.Settings;
        st.RecentEmoji.Remove(ch);
        st.RecentEmoji.Insert(0, ch);
        if (st.RecentEmoji.Count > 24) st.RecentEmoji.RemoveRange(24, st.RecentEmoji.Count - 24);
        _app.Store.Save();

        _busyClosing = true;
        var target = _target;
        Hide();
        _search.Text = "";
        _target = IntPtr.Zero;
        _busyClosing = false;
        _app.CommitText(ch, target);
    }

    // ---------- GIF ----------

    private static readonly (string Label, string Query)[] GifCats =
    {
        ("Trending", ""),
        ("Funny", "funny"),
        ("Reactions", "reaction"),
        ("Animals", "animals"),
        ("Food", "food"),
        ("Love", "love"),
        ("Party", "party"),
    };

    private void ResetGifState()
    {
        _gifCts?.Cancel();
        _gifCts = null;
        _gifSearchTimer.Stop();
        _gifTimer.Stop();
        _gifItems.Clear();
        _gifBusy.Clear();
        foreach (var preview in _gifPreviews.Values) preview.Dispose();
        _gifPreviews.Clear();
        _gifLoading = false;
        _gifError = "";
        _gifSel = 0;
        _gifScroll = 0;
        _gifCat = "";
        _hoverGif = -1;
        _hoverChip = -1;
    }

    private void OpenGifMode()
    {
        _mode = Mode.Gif;
        _sel = 0;
        _scroll = 0;
        _emojiScroll = 0;
        _emojiCat = 0;
        _hoverCard = -1;
        _hoverEmoji = -1;
        _hoverGif = -1;
        _hoverTab = "";
        _hoverCat = -1;
        _hoverChip = -1;
        _suppress = true;
        _search.Text = "";
        _search.PlaceholderText = "Search GIFs";
        _suppress = false;

        _gifProvider = GifProvider.FromSettings(_app.Store.Data.Settings);
        _gifCat = "";
        _gifSel = 0;
        _gifScroll = 0;
        _gifError = "";
        Build();
        _gifTimer.Start();

        if (!_gifProvider.HasKey)
        {
            _gifError = "no-key";
            Invalidate();
            return;
        }

        LoadGifs("");
    }

    private async void LoadGifs(string query)
    {
        var provider = _gifProvider;
        if (provider is null) return;
        if (!provider.HasKey)
        {
            _gifError = "no-key";
            Invalidate();
            return;
        }

        _gifCts?.Cancel();
        var cts = new CancellationTokenSource();
        _gifCts = cts;
        _gifLoading = true;
        _gifError = "";
        Invalidate();

        try
        {
            var results = query.Length == 0
                ? await provider.TrendingAsync(24, cts.Token)
                : await provider.SearchAsync(query, 24, cts.Token);
            if (cts.IsCancellationRequested || _mode != Mode.Gif) return;
            SetGifItems(results);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("GIF search failed", ex);
            if (_mode != Mode.Gif) return;
            _gifItems.Clear();
            _gifError = "offline";
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                _gifLoading = false;
                Invalidate();
            }
        }
    }

    private void SetGifItems(IReadOnlyList<GifItem> items)
    {
        ClearGifPreviews();
        _gifItems.Clear();
        foreach (var item in items) _gifItems.Add(item);
        _gifSel = 0;
        _gifScroll = 0;
        _gifLoading = false;
        Invalidate();
        _ = LoadVisibleGifsAsync();
    }

    private void ClearGifPreviews()
    {
        foreach (var preview in _gifPreviews.Values) preview.Dispose();
        _gifPreviews.Clear();
        _gifBusy.Clear();
    }

    private async Task LoadVisibleGifsAsync()
    {
        var provider = _gifProvider;
        if (provider is null) return;
        var visible = GifVisibleIndexes();

        // stills first so the grid fills in quickly
        foreach (var index in visible)
        {
            if (_mode != Mode.Gif) return;
            var item = _gifItems[index];
            if (_gifPreviews.ContainsKey(item.Id)) continue;
            if (item.PreviewUrl.Length == 0 || item.PreviewUrl == item.GifUrl) continue;
            await LoadGifStillAsync(item);
        }

        // then the animated originals
        foreach (var index in visible)
        {
            if (_mode != Mode.Gif) return;
            await LoadGifFullAsync(_gifItems[index]);
        }
    }

    private async Task LoadGifStillAsync(GifItem item)
    {
        var provider = _gifProvider;
        if (provider is null) return;
        if (!_gifBusy.Add(item.Id)) return;
        try
        {
            var bytes = await provider.DownloadAsync(item.PreviewUrl);
            if (_mode != Mode.Gif) return;
            SetGifPreview(item.Id, GifPreview.FromBytes(bytes));
        }
        catch (Exception ex) { Log.Error("GIF still download failed", ex); }
        finally { _gifBusy.Remove(item.Id); }
    }

    private async Task LoadGifFullAsync(GifItem item)
    {
        var provider = _gifProvider;
        if (provider is null) return;
        if (!_gifBusy.Add(item.Id)) return;
        try
        {
            var bytes = await provider.DownloadAsync(item.GifUrl);
            if (_mode != Mode.Gif) return;
            SetGifPreview(item.Id, GifPreview.FromBytes(bytes));
        }
        catch (Exception ex) { Log.Error("GIF download failed", ex); }
        finally { _gifBusy.Remove(item.Id); }
    }

    private void SetGifPreview(string id, GifPreview preview)
    {
        if (_gifPreviews.TryGetValue(id, out var old)) old.Dispose();
        _gifPreviews[id] = preview;
        Invalidate();
    }

    private (float Top, float CellW, float CellH, float Gap, int Cols, int Rows) GifGrid()
    {
        float footerTop = ClientSize.Height - S(BaseFooterH);
        float top = S(BaseGifGridTop);
        float gap = S(BaseGifGap);
        float width = ClientSize.Width - S(BasePad * 2);
        const int cols = 3;
        float cellW = (width - gap * (cols - 1)) / cols;
        float cellH = S(BaseGifCellH);
        int rows = Math.Max(1, (int)((footerTop - top - S(4) + gap) / (cellH + gap)));
        return (top, cellW, cellH, gap, cols, rows);
    }

    private List<int> GifVisibleIndexes()
    {
        var (_, _, _, _, cols, rows) = GifGrid();
        var list = new List<int>();
        int start = _gifScroll * cols;
        int end = Math.Min(_gifItems.Count, (_gifScroll + rows) * cols);
        for (int i = start; i < end; i++) list.Add(i);
        return list;
    }

    private void GifScrollBy(int rows)
    {
        var (_, _, _, _, cols, visible) = GifGrid();
        int total = (_gifItems.Count + cols - 1) / cols;
        int next = Math.Clamp(_gifScroll + rows, 0, Math.Max(0, total - visible));
        if (next == _gifScroll) return;
        _gifScroll = next;
        Invalidate();
        _ = LoadVisibleGifsAsync();
    }

    private void GifMoveTo(int index)
    {
        if (_gifItems.Count == 0) return;
        _gifSel = Math.Clamp(index, 0, _gifItems.Count - 1);
        var (_, _, _, _, cols, visible) = GifGrid();
        int row = _gifSel / cols;
        if (row < _gifScroll) _gifScroll = row;
        if (row >= _gifScroll + visible) _gifScroll = row - visible + 1;
        Invalidate();
    }

    private async void ChooseGif(int index)
    {
        if (index < 0 || index >= _gifItems.Count) return;
        var provider = _gifProvider;
        if (provider is null) return;

        _gifSel = index;
        Invalidate();

        byte[] bytes;
        try
        {
            bytes = await provider.DownloadAsync(_gifItems[index].GifUrl);
        }
        catch (Exception ex)
        {
            Log.Error("GIF download failed", ex);
            _gifError = "download";
            Invalidate();
            return;
        }

        _busyClosing = true;
        var target = _target;
        Hide();
        _search.Text = "";
        _target = IntPtr.Zero;
        _busyClosing = false;
        ResetGifState();
        _app.CommitGif(bytes, target);
    }

    private void PickGifFromDisk()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose a GIF",
            Filter = "GIF images|*.gif|Images|*.gif;*.png;*.jpg;*.jpeg;*.bmp;*.webp",
            CheckFileExists = true,
        };
        _busyClosing = true;
        var target = _target;
        try
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            Hide();
            _search.Text = "";
            _target = IntPtr.Zero;
            _app.CommitImage(dlg.FileName, target);
        }
        finally
        {
            _busyClosing = false;
        }
    }

    // ---------- input ----------

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_mode == Mode.Gif)
        {
            var (_, _, _, _, cols, visible) = GifGrid();
            switch (e.KeyCode)
            {
                case Keys.Left: GifMoveTo(_gifSel - 1); break;
                case Keys.Right: GifMoveTo(_gifSel + 1); break;
                case Keys.Up: GifMoveTo(_gifSel - cols); break;
                case Keys.Down: GifMoveTo(_gifSel + cols); break;
                case Keys.PageUp: GifMoveTo(_gifSel - cols * visible); break;
                case Keys.PageDown: GifMoveTo(_gifSel + cols * visible); break;
                case Keys.Home: GifMoveTo(0); break;
                case Keys.End: GifMoveTo(_gifItems.Count - 1); break;
                case Keys.Enter: ChooseGif(_gifSel); break;
                case Keys.Back:
                    if (_search.TextLength == 0) { Back(); break; }
                    return;
                case Keys.Tab when e.Control: CycleTab(e.Shift ? -1 : 1); break;
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        if (_mode == Mode.Emoji)
        {
            int cols = EmojiColumns();
            switch (e.KeyCode)
            {
                case Keys.Left: EmojiMove(-1); break;
                case Keys.Right: EmojiMove(1); break;
                case Keys.Up: EmojiMove(-cols); break;
                case Keys.Down: EmojiMove(cols); break;
                case Keys.PageUp: EmojiMove(-cols * 4); break;
                case Keys.PageDown: EmojiMove(cols * 4); break;
                case Keys.Home: EmojiMoveTo(0); break;
                case Keys.End: EmojiMoveTo(_emojiView.Count - 1); break;
                case Keys.Enter: ChooseEmoji(_sel); break;
                case Keys.Back:
                    if (_search.TextLength == 0) { Back(); break; }
                    return;
                case Keys.Tab when e.Control: CycleTab(e.Shift ? -1 : 1); break;
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Tab)
        {
            CycleTab(e.Shift ? -1 : 1);
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode is >= Keys.D0 and <= Keys.D9)
        {
            int idx = e.KeyCode - Keys.D0;
            var tabs = _app.Store.Data.Tabs;
            if (idx == 0) SwitchTab("recent");
            else if (idx - 1 < tabs.Count) SwitchTab(tabs[idx - 1].Id);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Down:
                MoveSel(1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Up:
                MoveSel(-1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.PageDown:
                MoveSel(VisibleRows() - 1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.PageUp:
                MoveSel(-(VisibleRows() - 1));
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Home:
                _sel = 0;
                Invalidate();
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.End:
                _sel = Math.Max(0, _items.Count - 1);
                Invalidate();
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                Choose(_sel);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Back:
                if (_search.TextLength == 0)
                {
                    Back();
                    e.Handled = e.SuppressKeyPress = true;
                }
                break;
        }
    }

    private void OnSearchKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (_mode == Mode.Emoji) return;
        if (_mode == Mode.Gif) return;
        if (_search.TextLength > 0) return;
        if (char.IsControl(e.KeyChar)) return;

        if (char.IsDigit(e.KeyChar))
        {
            int idx = e.KeyChar == '0' ? 9 : e.KeyChar - '1';
            if (idx >= 0 && idx < _items.Count)
            {
                e.Handled = true;
                Choose(idx);
            }
            return;
        }

        if (char.IsLetter(e.KeyChar))
        {
            var tab = _app.Store.Data.Tabs.FirstOrDefault(
                t => string.Equals(t.Key, e.KeyChar.ToString(), StringComparison.OrdinalIgnoreCase));
            if (tab is not null)
            {
                e.Handled = true;
                SwitchTab(tab.Id);
            }
        }
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            if (_search.TextLength > 0)
            {
                _suppress = true;
                _search.Text = "";
                _suppress = false;
                OnQueryChanged();
            }
            else if (_mode is Mode.Sequence or Mode.Dates or Mode.Emoji or Mode.Gif)
            {
                Back();
            }
            else
            {
                ClosePicker();
            }
            return;
        }

        if (e.Control && e.KeyCode == Keys.D)
        {
            e.Handled = true;
            OpenDateMode();
            return;
        }

        if (e.Control && (e.KeyCode == Keys.Oemcomma || e.KeyCode == Keys.OemPeriod))
        {
            e.Handled = true;
            _busyClosing = true;
            ClosePicker();
            _app.OpenDashboard("library");
        }
    }

    private void OpenDateMode()
    {
        _mode = Mode.Dates;
        _tabId = "recent";
        _sel = 0;
        _scroll = 0;
        _sequence = null;
        _suppress = true;
        _search.Text = "";
        _search.PlaceholderText = "Search clips and snippets";
        _suppress = false;
        Build();
    }

    private void MoveSel(int delta)
    {
        if (_items.Count == 0) return;
        _sel = Math.Clamp(_sel + delta, 0, _items.Count - 1);
        Invalidate();
    }

    private (float Top, float Cell, int Cols, int Rows) EmojiGrid()
    {
        float footerTop = ClientSize.Height - S(BaseFooterH);
        float top = S(BaseBodyTop);
        float cell = S(BaseEmojiCell);
        float step = cell + S(2);
        int cols = Math.Max(1, (int)((ClientSize.Width - S(BasePad * 2) + S(2)) / step));
        int rows = Math.Max(1, (int)((footerTop - top - S(4)) / step));
        return (top, cell, cols, rows);
    }

    private int EmojiColumns() => EmojiGrid().Cols;

    private void EmojiScrollBy(int rows)
    {
        var (_, _, cols, visible) = EmojiGrid();
        int total = (_emojiView.Count + cols - 1) / cols;
        _emojiScroll = Math.Clamp(_emojiScroll + rows, 0, Math.Max(0, total - visible));
        Invalidate();
    }

    private void EmojiMove(int delta) => EmojiMoveTo(_sel + delta);

    private void EmojiMoveTo(int index)
    {
        if (_emojiView.Count == 0) return;
        _sel = Math.Clamp(index, 0, _emojiView.Count - 1);
        var (_, _, cols, visible) = EmojiGrid();
        int row = _sel / cols;
        if (row < _emojiScroll) _emojiScroll = row;
        if (row >= _emojiScroll + visible) _emojiScroll = row - visible + 1;
        Invalidate();
    }

    private (float Top, float Width, float Height, float Gap, int Rows) CardGrid()
    {
        float footerTop = ClientSize.Height - S(BaseFooterH);
        float top = S(BaseBodyTop);
        float w = ClientSize.Width - S(BasePad * 2);
        float h = S(BaseCardH);
        float gap = S(BaseCardGap);
        int rows = Math.Max(1, (int)((footerTop - top - S(6) + gap) / (h + gap)));
        return (top, w, h, gap, rows);
    }

    private int VisibleRows() => CardGrid().Rows;

    private void ScrollBy(int rows)
    {
        var (_, _, _, _, visible) = CardGrid();
        int max = Math.Max(0, _items.Count - visible);
        _scroll = Math.Clamp(_scroll + rows, 0, max);
        Invalidate();
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        var item = _items[index];

        if (item.Block is { Kind: BlockKind.Sequence } seq && _mode != Mode.Sequence)
        {
            _sequence = seq;
            _mode = Mode.Sequence;
            _sel = 0;
            _scroll = 0;
            Build();
            return;
        }

        string? text = item.Block is not null ? item.Block.Resolve() : item.Paste;
        if (string.IsNullOrEmpty(text)) return;

        if (item.Block is { Kind: BlockKind.Template })
            _app.Store.NotifyChanged();

        _busyClosing = true;
        var target = _target;
        Hide();
        _search.Text = "";
        _target = IntPtr.Zero;
        _busyClosing = false;
        _app.CommitText(text, target);
    }

    // ---------- mouse ----------

    private void OnFormMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            Location = new Point(
                _dragOrigin.X + Cursor.Position.X - _dragStart.X,
                _dragOrigin.Y + Cursor.Position.Y - _dragStart.Y);
            return;
        }

        int card = _mode == Mode.Emoji ? -1 : HitCard(e.Location);
        int emoji = _mode == Mode.Emoji ? HitEmoji(e.Location) : -1;
        int gif = _mode == Mode.Gif ? HitGif(e.Location) : -1;
        int chip = _mode == Mode.Gif ? HitGifChip(e.Location) : -1;
        string tab = HitTab(e.Location);
        int cat = HitCat(e.Location);
        bool back = ( _mode == Mode.Emoji || _mode == Mode.Gif) && _backHit.Contains(e.Location);
        bool close = _closeHit.Contains(e.Location);
        bool gear = _gearHit.Contains(e.Location);
        bool overHandle = _handleHit.Contains(e.Location);

        bool bar = false;
        string barKey = "";
        foreach (var (rect, action, id) in _barHits)
            if (rect.Contains(e.Location)) { bar = true; barKey = action + ":" + id; break; }
        if (!bar && _mode == Mode.Gif && _gifFileHit.Contains(e.Location))
        {
            bar = true;
            barKey = "giffile:";
        }

        Cursor = overHandle ? Cursors.SizeAll
            : card >= 0 || emoji >= 0 || gif >= 0 || chip >= 0 || tab.Length > 0 || cat >= 0 || back || bar || close || gear
                ? Cursors.Hand
                : Cursors.Default;

        if (card != _hoverCard || emoji != _hoverEmoji || gif != _hoverGif || chip != _hoverChip ||
            tab != _hoverTab || cat != _hoverCat ||
            back != _hoverBack || bar != _hoverBar || barKey != _hoverBarKey ||
            close != _hoverClose || gear != _hoverGear)
        {
            _hoverCard = card;
            _hoverEmoji = emoji;
            _hoverGif = gif;
            _hoverChip = chip;
            _hoverTab = tab;
            _hoverCat = cat;
            _hoverBack = back;
            _hoverBar = bar;
            _hoverBarKey = barKey;
            _hoverClose = close;
            _hoverGear = gear;
            Invalidate();
        }
    }

    private void OnFormMouseDown(object? sender, MouseEventArgs e)
    {
        if (_handleHit.Contains(e.Location))
        {
            _dragging = true;
            _dragStart = Cursor.Position;
            _dragOrigin = Location;
            return;
        }

        if (_closeHit.Contains(e.Location))
        {
            ClosePicker();
            return;
        }

        if (_gearHit.Contains(e.Location))
        {
            _busyClosing = true;
            ClosePicker();
            _app.OpenDashboard("settings");
            return;
        }

        if (_mode == Mode.Gif)
        {
            int gif = HitGif(e.Location);
            if (gif >= 0)
            {
                _gifSel = gif;
                ChooseGif(gif);
                return;
            }

            int chip = HitGifChip(e.Location);
            if (chip >= 0)
            {
                _gifCat = GifCats[chip].Query;
                _suppress = true;
                _search.Text = "";
                _suppress = false;
                _gifSel = 0;
                _gifScroll = 0;
                LoadGifs(_gifCat);
                return;
            }

            if (_gifFileHit.Contains(e.Location))
            {
                PickGifFromDisk();
                return;
            }

            if (_backHit.Contains(e.Location))
            {
                Back();
                return;
            }
        }
        else if (_mode == Mode.Emoji)
        {
            int emoji = HitEmoji(e.Location);
            if (emoji >= 0)
            {
                _sel = emoji;
                ChooseEmoji(emoji);
                return;
            }

            int cat = HitCat(e.Location);
            if (cat >= 0)
            {
                if (_search.TextLength > 0)
                {
                    _suppress = true;
                    _search.Text = "";
                    _suppress = false;
                }
                _emojiCat = cat;
                _emojiScroll = 0;
                _sel = 0;
                BuildEmojiView();
                Invalidate();
                return;
            }

            if (_backHit.Contains(e.Location))
            {
                Back();
                return;
            }
        }
        else
        {
            int card = HitCard(e.Location);
            if (card >= 0)
            {
                _sel = card;
                Choose(card);
                return;
            }
        }

        foreach (var (rect, action, id) in _barHits)
        {
            if (!rect.Contains(e.Location)) continue;
            if (action == "emoji") { OpenEmojiMode(); return; }
            if (action == "gif") { OpenGifMode(); return; }
            if (action == "winv")
            {
                _app.SetTakeOverWinV(!_app.Store.Data.Settings.TakeOverWinV);
                Invalidate();
                return;
            }
        }

        foreach (var (rect, id) in _tabHits)
        {
            if (!rect.Contains(e.Location)) continue;
            if (id == "add")
            {
                _busyClosing = true;
                ClosePicker();
                _app.OpenDashboard("library");
                return;
            }
            SwitchTab(id);
            return;
        }
    }

    private int HitCard(Point p)
    {
        foreach (var (rect, index) in _cardHits)
            if (rect.Contains(p)) return index;
        return -1;
    }

    private int HitEmoji(Point p)
    {
        foreach (var (rect, index) in _emojiHits)
            if (rect.Contains(p)) return index;
        return -1;
    }

    private int HitGif(Point p)
    {
        foreach (var (rect, index) in _gifHits)
            if (rect.Contains(p)) return index;
        return -1;
    }

    private int HitGifChip(Point p)
    {
        foreach (var (rect, index) in _gifChipHits)
            if (rect.Contains(p)) return index;
        return -1;
    }

    private int HitCat(Point p)
    {
        foreach (var (rect, index) in _catHits)
            if (rect.Contains(p)) return index;
        return -1;
    }

    private string HitTab(Point p)
    {
        foreach (var (rect, id) in _tabHits)
            if (rect.Contains(p)) return id;
        return "";
    }

    // ---------- painting ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        _cardHits.Clear();
        _barHits.Clear();
        _emojiHits.Clear();
        _gifHits.Clear();
        _gifChipHits.Clear();
        _tabHits.Clear();
        _catHits.Clear();
        _closeHit = RectangleF.Empty;
        _gearHit = RectangleF.Empty;
        _handleHit = RectangleF.Empty;
        _backHit = RectangleF.Empty;
        _gifFileHit = RectangleF.Empty;

        Theme.PaintCanvasGlass(g, ClientSize);

        DrawDragHandle(g);
        DrawTopButtons(g);
        DrawTabs(g);
        DrawSearchField(g);
        if (_mode == Mode.Emoji)
        {
            DrawEmojiBody(g);
        }
        else if (_mode == Mode.Gif)
        {
            DrawGifBody(g);
        }
        else
        {
            DrawBody(g);
        }
        DrawFooter(g);
    }

    private void DrawDragHandle(Graphics g)
    {
        _handleHit = new RectangleF((ClientSize.Width - S(36)) / 2f, S(4), S(36), S(12));
        Theme.FillRound(g, new RectangleF(_handleHit.X, S(8), S(36), S(4)), S(2), Theme.BorderHi);
    }

    private void DrawTopButtons(Graphics g)
    {
        _closeHit = new RectangleF(ClientSize.Width - S(40), S(12), S(24), S(24));
        _gearHit = new RectangleF(ClientSize.Width - S(70), S(12), S(24), S(24));

        foreach (var (rect, glyph, hover) in new[]
                 {
                     (_gearHit, "\uE713", _hoverGear),
                     (_closeHit, "\uE8BB", _hoverClose),
                 })
        {
            if (hover) Theme.FillRound(g, rect, S(4), Theme.Hover);
            Theme.TextIn(g, glyph, Theme.Icon(S(11)), hover ? Theme.Text : Theme.Faint, rect,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // Icon tab row: the Windows clipboard's top strip, with an accent underline on the active tab.
    private void DrawTabs(Graphics g)
    {
        float box = S(BaseTabsBox);
        float y = S(BaseTabsY);
        float x = S(BasePad) - S(8);
        float maxX = ClientSize.Width - S(86);
        float step = S(44);
        var tabs = _app.Store.Data.Tabs;

        DrawTabIcon(g, ref x, y, box, "recent", "Recent", null);

        foreach (var tab in tabs)
        {
            if (x + box > maxX) break;
            DrawTabIcon(g, ref x, y, box, tab.Id, tab.Name, tab);
        }

        // new tab
        if (x + box <= maxX)
        {
            var r = new RectangleF(x, y, box, box);
            _tabHits.Add((r, "add"));
            bool hover = _hoverTab == "add";
            if (hover) Theme.FillRound(g, r, S(6), Theme.Hover);
            Theme.TextIn(g, "\uE710", Theme.Icon(S(13)), hover ? Theme.Text : Theme.Faint, r,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawTabIcon(Graphics g, ref float x, float y, float box, string id, string label, Tab? tab)
    {
        var r = new RectangleF(x, y, box, box);
        _tabHits.Add((r, id));

        bool selected = _tabId == id && _mode != Mode.Emoji;
        bool hover = _hoverTab == id;
        if (hover) Theme.FillRound(g, r, S(6), Theme.Hover);

        var iconBox = new RectangleF(r.X + (box - S(20)) / 2f, r.Y + (box - S(20)) / 2f, S(20), S(20));
        var ink = selected ? Theme.Text : (hover ? Theme.Text : Theme.Muted);

        if (tab is null)
        {
            Theme.TextIn(g, "\uE823", Theme.Icon(S(17)), selected ? Theme.Accent : ink, iconBox,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else if (!string.IsNullOrEmpty(tab.Icon))
        {
            var tint = string.IsNullOrEmpty(tab.Color) ? ink : TabStyle.Parse(tab.Color);
            TabStyle.DrawIcon(g, iconBox, tab.Icon, tint);
        }
        else
        {
            Theme.TextIn(g, tab.Key.Length > 0 ? tab.Key : "?", _fTab, selected ? Theme.Text : ink, iconBox,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (selected)
        {
            var underline = new RectangleF(r.X + (box - S(18)) / 2f, r.Bottom - S(3), S(18), S(2.5f));
            Theme.FillRound(g, underline, S(1.25f), Theme.Accent);
        }

        x += S(44);
    }

    private void DrawSearchField(Graphics g)
    {
        var field = SearchFieldRect();
        Theme.FillRound(g, field, S(4), Theme.Field);
        if (Theme.IsLight) Theme.StrokeRound(g, field, S(4), Theme.Border, 1f);

        float mx = field.X + S(12);
        float my = field.Y + field.Height / 2f - S(6);
        using (var pen = new Pen(_search.Focused ? Theme.Muted : Theme.Faint, Math.Max(1.1f, S(1.2f))))
        {
            g.DrawEllipse(pen, mx, my - S(1), S(8), S(8));
            g.DrawLine(pen, mx + S(6.5f), my + S(5.5f), mx + S(9.5f), my + S(9));
        }

        if (_search.Focused)
            Theme.FillRound(g, new RectangleF(field.X + S(2), field.Bottom - S(2), field.Width - S(4), S(2)),
                S(1), Theme.Accent);
    }

    private void DrawBody(Graphics g)
    {
        float footerTop = ClientSize.Height - S(BaseFooterH);
        var (top, cardW, cardH, gap, visible) = CardGrid();

        string label = _mode switch
        {
            Mode.Recent => "Recent",
            Mode.Tab => _app.Store.FindTab(_tabId)?.Name ?? "Tab",
            Mode.Search => "Search",
            Mode.Dates => "Dates",
            Mode.Sequence => _sequence is null ? "Sequence" : "Sequence · " + _sequence.Label,
            _ => "",
        };
        Theme.TextAt(g, label, _fPanelTitle, Theme.Text, S(BasePad), S(BaseTitleY));

        string hint = _items.Count == 0
            ? ""
            : _items.Count + (_items.Count == 1 ? " item" : " items") + "  ·  enter to paste";
        Theme.TextIn(g, hint, _fHint, Theme.Faint,
            new RectangleF(S(BasePad), S(BaseTitleY), cardW, S(26)),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        if (_items.Count == 0)
        {
            string empty = _mode switch
            {
                Mode.Recent => "Nothing copied yet. Copy something and it lands here.",
                Mode.Search => "No matches for \u201c" + _search.Text + "\u201d",
                Mode.Tab => "This tab is empty. Add snippets from the dashboard.",
                Mode.Dates => "No dates match.",
                _ => "Nothing to show.",
            };
            var area = new RectangleF(S(BasePad), top, cardW, S(120));
            Theme.TextIn(g, empty, _fEmpty, Theme.Faint, area,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        int maxScroll = Math.Max(0, _items.Count - visible);
        if (_sel < _scroll) _scroll = _sel;
        if (_sel >= _scroll + visible) _scroll = _sel - visible + 1;
        _scroll = Math.Clamp(_scroll, 0, maxScroll);

        g.SetClip(new RectangleF(0, top - S(2), ClientSize.Width, footerTop - top + S(2)));
        for (int i = _scroll; i < _items.Count && i < _scroll + visible; i++)
        {
            var r = new RectangleF(S(BasePad), top + (i - _scroll) * (cardH + gap), cardW, cardH);
            _cardHits.Add((r, i));
            DrawCard(g, i, r);
        }
        g.ResetClip();

        if (maxScroll > 0)
        {
            float trackTop = top;
            float trackH = footerTop - top - S(8);
            float thumbH = Math.Max(S(28), trackH * visible / _items.Count);
            float thumbY = trackTop + (trackH - thumbH) * _scroll / maxScroll;
            Theme.FillRound(g, new RectangleF(ClientSize.Width - S(7), thumbY, S(3), thumbH), S(1.5f), Theme.BorderHi);
        }
    }

    private void DrawCard(Graphics g, int index, RectangleF r)
    {
        var item = _items[index];
        bool selected = index == _sel;
        bool hover = index == _hoverCard;

        var fill = selected ? Theme.SurfaceAlt : hover ? Theme.Hover : Theme.Surface;
        Theme.FillRound(g, r, S(4), fill);
        if (selected)
            Theme.StrokeRound(g, r, S(4), Theme.WithAlpha(Theme.Text, 120), 1.4f);
        else
            Theme.StrokeRound(g, r, S(4), Theme.WithAlpha(Theme.Border, 150), 1f);

        string number = index < 9 ? (index + 1).ToString() : "·";
        Theme.TextIn(g, number, _fTag, selected ? Theme.Text : Theme.Faint,
            new RectangleF(r.X + S(11), r.Y + S(8), S(16), S(14)),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);

        string tag = _mode is Mode.Search or Mode.Dates ? item.Tag : "";
        float textX = r.X + S(32);
        float tagW = tag.Length > 0 ? Theme.Measure(tag, _fTag).Width : 0;

        Theme.TextIn(g, item.Title, selected ? _fTitleBold : _fTitle, Theme.Text,
            new RectangleF(textX, r.Y + S(6), r.Right - textX - S(12), S(18)),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        float subW = r.Width - (textX - r.X) - S(12) - (tagW > 0 ? tagW + S(10) : 0);
        TextWrap(g, item.Sub, _fSub, Theme.Faint,
            new RectangleF(textX, r.Y + S(24), subW, r.Height - S(24) - S(8)), 2);

        if (tagW > 0)
        {
            Theme.TextIn(g, tag, _fTag, Theme.Faint,
                new RectangleF(r.Right - tagW - S(10), r.Bottom - S(21), tagW, S(14)),
                TextFormatFlags.Right | TextFormatFlags.Top);
        }
    }

    private void DrawEmojiBody(Graphics g)
    {
        float footerTop = ClientSize.Height - S(BaseFooterH);

        Theme.TextAt(g, "Emoji", _fPanelTitle, Theme.Text, S(BasePad), S(BaseTitleY));

        string status = _search.TextLength > 0
            ? _emojiView.Count + (_emojiView.Count == 1 ? " match" : " matches")
            : _emojiCat >= 0 && _emojiCat < _emojiCats.Count
                ? _emojiCats[_emojiCat].Label + "  ·  " + _emojiView.Count + " emoji"
                : _emojiView.Count + " emoji";
        Theme.TextIn(g, status, _fHint, Theme.Faint,
            new RectangleF(S(BasePad), S(BaseTitleY), ClientSize.Width - S(BasePad * 2), S(26)),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        if (_emojiView.Count == 0)
        {
            var area = new RectangleF(S(BasePad), S(BaseBodyTop), ClientSize.Width - S(BasePad * 2), S(120));
            Theme.TextIn(g, "No emoji match \u201c" + _search.Text.Trim() + "\u201d", _fEmpty, Theme.Faint, area,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var (top, cell, cols, rows) = EmojiGrid();
        int totalRows = (_emojiView.Count + cols - 1) / cols;
        int selRow = _sel / cols;
        if (selRow < _emojiScroll) _emojiScroll = selRow;
        if (selRow >= _emojiScroll + rows) _emojiScroll = selRow - rows + 1;
        _emojiScroll = Math.Clamp(_emojiScroll, 0, Math.Max(0, totalRows - rows));

        bool colored = !string.Equals(_app.Store.Data.Settings.EmojiStyle, "outline", StringComparison.OrdinalIgnoreCase);
        int artPx = (int)Math.Max(12, cell - S(12));

        g.SetClip(new RectangleF(0, top - S(2), ClientSize.Width, footerTop - top + S(2)));
        for (int i = 0; i < _emojiView.Count; i++)
        {
            int col = i % cols, row = i / cols;
            if (row < _emojiScroll || row >= _emojiScroll + rows) continue;
            var r = new RectangleF(
                S(BasePad) + col * (cell + S(2)),
                top + (row - _emojiScroll) * (cell + S(2)),
                cell,
                cell);

            bool selected = i == _sel;
            bool hover = i == _hoverEmoji;
            if (selected)
            {
                Theme.FillRound(g, r, S(6), Theme.Selected);
                Theme.StrokeRound(g, r, S(6), Theme.WithAlpha(Theme.Text, 110), 1.2f);
            }
            else if (hover)
            {
                Theme.FillRound(g, r, S(6), Theme.Hover);
            }

            var entry = _emojiView[i];
            var art = colored ? EmojiArt.Get(entry.Char, artPx, Theme.Text) : null;
            if (art is not null)
            {
                float ax = r.X + (r.Width - art.Width) / 2f;
                float ay = r.Y + (r.Height - art.Height) / 2f;
                g.DrawImage(art, ax, ay, art.Width, art.Height);
            }
            else
            {
                TextRenderer.DrawText(g, entry.Char, _fEmoji, Rectangle.Round(r), Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            _emojiHits.Add((r, i));
        }
        g.ResetClip();

        if (totalRows > rows)
        {
            float trackTop = top;
            float trackH = footerTop - top - S(8);
            float thumbH = Math.Max(S(28), trackH * rows / totalRows);
            float maxScroll = Math.Max(1, totalRows - rows);
            float thumbY = trackTop + (trackH - thumbH) * _emojiScroll / maxScroll;
            Theme.FillRound(g, new RectangleF(ClientSize.Width - S(7), thumbY, S(3), thumbH), S(1.5f), Theme.BorderHi);
        }
    }

    private void DrawGifBody(Graphics g)
    {
        float footerTop = ClientSize.Height - S(BaseFooterH);

        Theme.TextAt(g, "GIFs", _fPanelTitle, Theme.Text, S(BasePad), S(BaseTitleY));

        string status = _gifError switch
        {
            "no-key" => "API key needed",
            "offline" => "Service unreachable",
            "download" => "Download failed",
            _ => _gifLoading ? "Searching…" : _gifItems.Count + (_gifItems.Count == 1 ? " result" : " results"),
        };
        Theme.TextIn(g, status, _fHint, Theme.Faint,
            new RectangleF(S(BasePad), S(BaseTitleY), ClientSize.Width - S(BasePad * 2), S(26)),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        DrawGifChips(g);

        if (_gifError == "no-key")
        {
            var setup = new RectangleF(S(BasePad), S(BaseGifGridTop) + S(24), ClientSize.Width - S(BasePad * 2), S(60));
            Theme.TextIn(g, "GIF search needs a free KLIPY API key.", _fEmpty, Theme.Text, setup,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var hint = new RectangleF(S(BasePad), setup.Bottom + S(6), ClientSize.Width - S(BasePad * 2), S(40));
            Theme.TextIn(g, "Add it in Settings → GIF, or set CLIPPED_GIF_API_KEY.\nPick a file from disk in the meantime.", _fHint, Theme.Faint, hint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        if (_gifItems.Count == 0)
        {
            string empty = _gifError switch
            {
                "offline" => "Could not reach the GIF service.",
                "download" => "That GIF could not be downloaded.",
                _ => _gifLoading ? "Looking for GIFs…" : "No GIFs found.",
            };
            var area = new RectangleF(S(BasePad), S(BaseGifGridTop), ClientSize.Width - S(BasePad * 2), S(120));
            Theme.TextIn(g, empty, _fEmpty, Theme.Faint, area,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var (top, cellW, cellH, gap, cols, rows) = GifGrid();
        int totalRows = (_gifItems.Count + cols - 1) / cols;
        int selRow = _gifSel / cols;
        if (selRow < _gifScroll) _gifScroll = selRow;
        if (selRow >= _gifScroll + rows) _gifScroll = selRow - rows + 1;
        _gifScroll = Math.Clamp(_gifScroll, 0, Math.Max(0, totalRows - rows));

        g.SetClip(new RectangleF(0, top - S(2), ClientSize.Width, footerTop - top + S(2)));
        for (int i = _gifScroll * cols; i < _gifItems.Count && i < (_gifScroll + rows) * cols; i++)
        {
            int col = i % cols, row = i / cols;
            var r = new RectangleF(
                S(BasePad) + col * (cellW + gap),
                top + (row - _gifScroll) * (cellH + gap),
                cellW,
                cellH);
            _gifHits.Add((r, i));
            DrawGifTile(g, i, r);
        }
        g.ResetClip();

        if (totalRows > rows)
        {
            float trackTop = top;
            float trackH = footerTop - top - S(8);
            float thumbH = Math.Max(S(28), trackH * rows / totalRows);
            float maxScroll = Math.Max(1, totalRows - rows);
            float thumbY = trackTop + (trackH - thumbH) * _gifScroll / maxScroll;
            Theme.FillRound(g, new RectangleF(ClientSize.Width - S(7), thumbY, S(3), thumbH), S(1.5f), Theme.BorderHi);
        }
    }

    private void DrawGifChips(Graphics g)
    {
        float x = S(BasePad);
        float y = S(BaseGifChipY);
        float h = S(24);

        for (int i = 0; i < GifCats.Length; i++)
        {
            var (label, query) = GifCats[i];
            float w = Theme.Measure(label, _fChip).Width + S(16);
            if (x + w > ClientSize.Width - S(BasePad)) break;

            var r = new RectangleF(x, y, w, h);
            _gifChipHits.Add((r, i));

            bool selected = _gifCat == query && _search.TextLength == 0;
            bool hover = i == _hoverChip;

            if (selected)
            {
                Theme.FillRound(g, r, S(12), Theme.WithAlpha(Theme.Accent, Theme.IsLight ? 30 : 46));
                Theme.StrokeRound(g, r, S(12), Theme.WithAlpha(Theme.Accent, 160), 1f);
            }
            else if (hover)
            {
                Theme.FillRound(g, r, S(12), Theme.Hover);
            }

            Theme.TextIn(g, label, _fChip, selected ? Theme.Accent : Theme.Muted, r,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            x += w + S(6);
        }
    }

    private void DrawGifTile(Graphics g, int index, RectangleF r)
    {
        var item = _gifItems[index];
        bool selected = index == _gifSel;
        bool hover = index == _hoverGif;

        Theme.FillRound(g, r, S(6), selected ? Theme.SurfaceAlt : Theme.Surface);
        if (selected) Theme.StrokeRound(g, r, S(6), Theme.WithAlpha(Theme.Text, 120), 1.4f);
        else Theme.StrokeRound(g, r, S(6), Theme.WithAlpha(Theme.Border, 150), 1f);

        var media = new RectangleF(r.X + S(3), r.Y + S(3), r.Width - S(6), r.Height - S(24));
        Theme.FillRound(g, media, S(4), Theme.Field);

        if (_gifPreviews.TryGetValue(item.Id, out var preview) && preview.Image is not null)
        {
            using var path = Theme.RoundRect(media, S(4));
            g.SetClip(path);
            preview.Draw(g, media);
            g.ResetClip();
        }
        else
        {
            Theme.TextIn(g, "· · ·", _fTag, Theme.Faint, media,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        string caption = item.Title.Length > 0 ? item.Title : "GIF";
        if (caption.Length > 34) caption = caption[..33].TrimEnd() + "…";
        Theme.TextIn(g, caption, _fTag, selected ? Theme.Text : Theme.Faint,
            new RectangleF(r.X + S(7), r.Bottom - S(20), r.Width - S(14), S(14)),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void DrawFooter(Graphics g)
    {
        float footerH = S(BaseFooterH);
        float top = ClientSize.Height - footerH;
        using (var pen = new Pen(Theme.WithAlpha(Theme.Border, 190)))
            g.DrawLine(pen, 0, top, ClientSize.Width, top);

        float size = _iconSize;
        float gap = S(4);
        float x = S(BasePad);
        float y = top + (footerH - size) / 2f;

        if (_mode == Mode.Emoji)
        {
            DrawEmojiStrip(g, ref x, top, footerH);
        }
        else if (_mode == Mode.Gif)
        {
            DrawGifStrip(g, ref x, top, footerH);
        }
        else
        {
            DrawEmojiButton(g, ref x, y, size, gap);
            DrawGifButton(g, ref x, y, size, gap);

            x += S(4);
            using (var pen = new Pen(Theme.WithAlpha(Theme.Border, 190)))
                g.DrawLine(pen, x, top + S(12), x, top + footerH - S(12));
            x += S(10);
            DrawWinVButton(g, ref x, y, size, gap);
        }

        string hints = _mode switch
        {
            Mode.Emoji => "type to search  ·  esc back",
            Mode.Gif => "Powered by KLIPY",
            _ when _hoverBarKey == "winv:" => _app.Store.Data.Settings.TakeOverWinV
                ? "give Win+V back to Windows"
                : "take Win+V from Windows",
            Mode.Dates => "↑↓ move  ·  1–9 paste",
            Mode.Sequence => "↑↓ move  ·  1–9 paste",
            _ => "1–9 paste  ·  esc close",
        };

        float hintW = Theme.Measure(hints, _fHint).Width;
        Theme.TextAt(g, hints, _fHint, Theme.Faint,
            ClientSize.Width - S(BasePad) - hintW, top + (footerH - _fHint.Height) / 2f);
    }

    private void DrawGifStrip(Graphics g, ref float x, float top, float footerH)
    {
        float h = S(26);
        float cy = top + (footerH - h) / 2f;

        _backHit = new RectangleF(S(BasePad), cy, S(22), h);
        if (_hoverBack) Theme.FillRound(g, _backHit, S(4), Theme.Hover);
        Theme.TextIn(g, "\uE72B", Theme.Icon(S(10)), Theme.Muted, _backHit,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        x = _backHit.Right + S(10);

        string label = "From file…";
        float w = Theme.Measure(label, _fHint).Width + S(18);
        _gifFileHit = new RectangleF(x, cy, w, h);
        bool hover = _hoverBarKey == "giffile:";
        if (hover) Theme.FillRound(g, _gifFileHit, S(13), Theme.Hover);
        Theme.TextIn(g, label, _fHint, hover ? Theme.Text : Theme.Muted, _gifFileHit,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawEmojiStrip(Graphics g, ref float x, float top, float footerH)
    {
        float h = S(26);
        float cy = top + (footerH - h) / 2f;

        _backHit = new RectangleF(S(BasePad), cy, S(22), h);
        if (_hoverBack) Theme.FillRound(g, _backHit, S(4), Theme.Hover);
        Theme.TextIn(g, "\uE72B", Theme.Icon(S(10)), Theme.Muted, _backHit,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        x = _backHit.Right + S(10);

        if (_search.TextLength > 0)
        {
            Theme.TextIn(g, _emojiView.Count + (_emojiView.Count == 1 ? " result" : " results"), _fHint, Theme.Faint,
                new RectangleF(x, cy, S(120), h), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        float step = h + S(4);
        for (int i = 0; i < _emojiCats.Count; i++)
        {
            var cat = _emojiCats[i];
            var r = new RectangleF(x + i * step, cy, h, h);
            _catHits.Add((r, i));

            bool selected = i == _emojiCat;
            bool hover = i == _hoverCat;
            if (selected)
            {
                Theme.FillRound(g, r, S(4), Theme.WithAlpha(Theme.Accent, Theme.IsLight ? 30 : 46));
                Theme.StrokeRound(g, r, S(4), Theme.WithAlpha(Theme.Accent, 160), 1f);
            }
            else if (hover)
            {
                Theme.FillRound(g, r, S(4), Theme.Hover);
            }

            if (cat.Recent)
            {
                Theme.TextIn(g, "\uE81C", Theme.Icon(S(13)), selected ? Theme.Accent : Theme.Muted,
                    r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                int iconPx = (int)S(15);
                var art = string.Equals(_app.Store.Data.Settings.EmojiStyle, "outline", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : EmojiArt.Get(cat.Icon, iconPx, Theme.Text);
                if (art is not null)
                {
                    g.DrawImage(art, r.X + (r.Width - art.Width) / 2f, r.Y + (r.Height - art.Height) / 2f,
                        art.Width, art.Height);
                }
                else
                {
                    TextRenderer.DrawText(g, cat.Icon, Theme.Icon(S(13)), Rectangle.Round(r),
                        selected ? Theme.Accent : Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }
            }
        }

        x += _emojiCats.Count * step;
    }

    private void DrawEmojiButton(Graphics g, ref float x, float y, float size, float gap)
    {
        var r = new RectangleF(x, y, size, size);
        bool active = _mode == Mode.Emoji;
        bool hover = _hoverBarKey == "emoji:";

        if (active) Theme.FillRound(g, r, S(4), Theme.WithAlpha(Theme.Accent, Theme.IsLight ? 26 : 36));
        else if (hover) Theme.FillRound(g, r, S(4), Theme.Hover);

        Theme.TextIn(g, "\uE76E", Theme.Icon(size * 0.58f), active ? Theme.Text : Theme.Muted,
            new RectangleF(r.X, r.Y, r.Width, r.Height),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        _barHits.Add((r, "emoji", ""));
        x += size + gap;
    }

    private void DrawWinVButton(Graphics g, ref float x, float y, float size, float gap)
    {
        var r = new RectangleF(x, y, size, size);
        bool on = _app.Store.Data.Settings.TakeOverWinV;
        bool hover = _hoverBarKey == "winv:";

        if (on) Theme.FillRound(g, r, S(4), Theme.WithAlpha(Theme.Accent, Theme.IsLight ? 26 : 36));
        else if (hover) Theme.FillRound(g, r, S(4), Theme.Hover);

        var ink = on ? Theme.Accent : (hover ? Theme.Text : Theme.Muted);
        float cell = size * 0.19f;
        float sep = size * 0.06f;
        float left = r.X + (size - cell * 2 - sep) / 2f;
        float top = r.Y + (size - cell * 2 - sep) / 2f;
        using (var brush = new SolidBrush(ink))
        {
            for (int i = 0; i < 4; i++)
            {
                var pane = new RectangleF(left + (i % 2) * (cell + sep), top + (i / 2) * (cell + sep), cell, cell);
                using var path = Theme.RoundRect(pane, size * 0.03f);
                g.FillPath(brush, path);
            }
        }
        _barHits.Add((r, "winv", ""));
        x += size + gap;
    }

    private void DrawGifButton(Graphics g, ref float x, float y, float size, float gap)
    {
        var r = new RectangleF(x, y, size, size);
        bool active = _mode == Mode.Gif;
        bool hover = _hoverBarKey == "gif:";
        if (active) Theme.FillRound(g, r, S(4), Theme.WithAlpha(Theme.Accent, Theme.IsLight ? 26 : 36));
        else if (hover) Theme.FillRound(g, r, S(4), Theme.Hover);

        DrawPixelGif(g, new RectangleF(r.X + size * 0.16f, r.Y + size * 0.25f, size * 0.68f, size * 0.5f),
            active || hover ? Theme.Text : Theme.Muted);
        _barHits.Add((r, "gif", ""));
        x += size + gap;
    }

    // 3×5 pixel glyphs for G, I, F — the GIF wordmark stays unmistakably "GIF".
    private static readonly byte[][] GifGlyphs =
    {
        new byte[] { 7, 4, 5, 5, 7 },
        new byte[] { 7, 2, 2, 2, 7 },
        new byte[] { 7, 4, 6, 4, 4 },
    };

    private void DrawPixelGif(Graphics g, RectangleF box, Color ink)
    {
        float unit = Math.Min(box.Width / 11f, box.Height / 5f);
        float w = 11 * unit, h = 5 * unit;
        float ox = box.X + (box.Width - w) / 2f;
        float oy = box.Y + (box.Height - h) / 2f;

        using var brush = new SolidBrush(ink);
        for (int l = 0; l < GifGlyphs.Length; l++)
        {
            var pattern = GifGlyphs[l];
            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    if ((pattern[row] & (1 << (2 - col))) == 0) continue;
                    g.FillRectangle(brush, ox + (l * 4 + col) * unit, oy + row * unit, unit * 0.86f, unit * 0.86f);
                }
            }
        }
    }

    private static void TextWrap(Graphics g, string text, Font font, Color color, RectangleF bounds, int lines)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1) return;
        var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        if (lines <= 1) flags |= TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, text ?? "", font, Rectangle.Round(bounds), color, flags);
    }
}
