using Clipped.Core;
using Clipped.UI;

namespace Clipped;

public sealed class AppController : ApplicationContext
{
    public Store Store { get; }
    public PickerForm Picker { get; }
    public DashboardForm? Dashboard { get; private set; }
    public IntPtr LastExternalWindow { get; private set; }
    public bool Quitting { get; private set; }

    private readonly ClipboardWatcher _watcher;
    private readonly HotkeyHook _hook;
    private readonly bool _headless;
    public bool Headless => _headless;
    private NotifyIcon? _tray;
    private const string FallbackHotkey = "Ctrl+Shift+Space";
    private readonly EventWaitHandle? _activate;
    private readonly SynchronizationContext? _ui;
    private readonly System.Windows.Forms.Timer _pasteTimer;

    public AppController(bool headless = false)
    {
        _headless = headless;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        Store = new Store();
        Theme.Init(Theme.ParseMode(Store.Data.Settings.ThemeMode));
        Theme.StartMonitoring();
        Theme.Changed += OnThemeChanged;
        Picker = new PickerForm(this);

        _hook = new HotkeyHook();
        _hook.Pressed += OnHotkey;

        _watcher = new ClipboardWatcher();
        _watcher.TextCopied += text => Store.AddHistory(text);
        _watcher.ImageCopied += image =>
        {
            try { Store.AddImageHistory(image); }
            finally { image.Dispose(); }
        };

        _pasteTimer = new System.Windows.Forms.Timer { Interval = 70 };
        _pasteTimer.Tick += (_, _) =>
        {
            _pasteTimer.Stop();
            var pending = _pendingPaste;
            var gif = _pendingGif;
            _pendingPaste = null;
            _pendingGif = null;
            if (gif is not null)
            {
                if (pending is null) return;
                Paster.PasteGifInto(pending.Value.Target, gif, pending.Value.Send);
                return;
            }
            if (pending is null) return;
            if (pending.Value.Image is { } image)
                Paster.PasteImageInto(pending.Value.Target, image, pending.Value.Send);
            else if (pending.Value.Text is { } text)
                Paster.PasteInto(pending.Value.Target, text, pending.Value.Send);
        };

        if (!_headless)
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Clipped_Activate_" + Program.DataKey);
            var listener = new Thread(() =>
            {
                while (true)
                {
                    if (!_activate.WaitOne()) continue;
                    var link = Program.ReadDeepLink();
                    if (string.IsNullOrEmpty(link)) continue;
                    _ui?.Post(_ => HandleLink(link), null);
                }
            })
            { IsBackground = true, Name = "Clipped deep links" };
            listener.Start();
        }
    }

    private (string? Text, string? Image, IntPtr Target, bool Send)? _pendingPaste;
    private byte[]? _pendingGif;

    public void Start(string? deepLink)
    {
        var settings = Store.Data.Settings;
        settings.LaunchAtStartup = WindowsIntegration.StartupEnabled;

        if (!_headless)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Clipped");
                key?.SetValue("ExePath", Paths.ExePath);
            }
            catch { }

            WindowsIntegration.RegisterIntegration(settings.WindowsIntegration);
            _hook.Install();
            ApplyHotkey();
            if (settings.ShowTrayIcon) SetupTray();
        }

        if (!string.IsNullOrEmpty(deepLink))
        {
            HandleLink(deepLink);
            return;
        }

        if (!settings.SeenWelcome && !_headless)
        {
            settings.SeenWelcome = true;
            Store.Save();
            OpenDashboard("library");
            _tray?.ShowBalloonTip(4000, "Clipped is running",
                "Press " + HotkeyLabel() + " anywhere to open the picker. Open Settings to change the shortcut.",
                ToolTipIcon.Info);
        }
    }

    public void SetTakeOverWinV(bool on)
    {
        var settings = Store.Data.Settings;
        if (settings.TakeOverWinV == on) return;
        settings.TakeOverWinV = on;
        var custom = HotkeyHook.Parse(settings.Hotkey);
        if (!on && custom is not null && IsWinV(custom))
        {
            // Win+V is reserved while we no longer hook it; a dead shortcut would leave
            // the tray as the only way in.
            settings.Hotkey = FallbackHotkey;
            _tray?.ShowBalloonTip(4000, "Win+V is Windows' again",
                "Clipped now opens with " + FallbackHotkey + ". Change it in Settings.", ToolTipIcon.Info);
        }
        else if (on && string.Equals(settings.Hotkey?.Trim(), FallbackHotkey, StringComparison.OrdinalIgnoreCase))
        {
            settings.Hotkey = "Win+V";
        }
        Store.Save();
        Log.Info("Win+V takeover " + (on ? "on" : "off"));
        if (!on && !settings.ShowTrayIcon && !HasOtherHotkey())
            SetShowTrayIcon(true);
        ApplyHotkey();
        Picker.Invalidate();
        if (Dashboard is { Visible: true }) Dashboard.SyncVisibleSettings();
    }

    private static bool IsWinV(HotkeyHook.Combo combo) =>
        combo.Win && !combo.Ctrl && !combo.Alt && !combo.Shift && combo.Key == (int)Keys.V;

    private bool HasOtherHotkey()
    {
        var custom = HotkeyHook.Parse(Store.Data.Settings.Hotkey);
        return custom is not null && (custom.HasModifier || custom.IsFunctionKey) && !IsWinV(custom);
    }

    public void ApplyHotkey()
    {
        if (_headless) return;
        var settings = Store.Data.Settings;

        // The custom field holds "Win+V" by default — it is the takeover, not an extra combo.
        // Stale data can leave Win+V configured with the takeover off, which registers nothing.
        var stale = HotkeyHook.Parse(settings.Hotkey);
        if (!settings.TakeOverWinV && stale is not null && IsWinV(stale))
        {
            settings.Hotkey = FallbackHotkey;
            Store.Save();
            Log.Info("Win+V is Windows' own while takeover is off; shortcut moved to " + FallbackHotkey);
        }

        var combos = new List<HotkeyHook.Combo>();
        var custom = HotkeyHook.Parse(settings.Hotkey);
        if (custom is not null && (custom.HasModifier || custom.IsFunctionKey) &&
            (!IsWinV(custom) || settings.TakeOverWinV))
            combos.Add(custom);

        if (settings.TakeOverWinV)
        {
            bool alreadyWinV = combos.Any(c => IsWinV(c));
            if (!alreadyWinV) combos.Add(new HotkeyHook.Combo { Win = true, Key = (int)Keys.V });
        }

        _hook.Set(combos);
        if (combos.Count == 0) Log.Info("No hotkey configured — only the tray opens Clipped.");
        else Log.Info("Hotkey active: " + string.Join(" / ", combos.Select(c => c.ToString())));

        if (_tray is not null)
            _tray.Text = Shorten("Clipped — " + string.Join(" / ", combos.Select(c => c.ToString())), 63);
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max];

    public string HotkeyLabel()
    {
        var settings = Store.Data.Settings;
        var custom = HotkeyHook.Parse(settings.Hotkey);
        if (custom is null) return "Win+V";
        if (settings.TakeOverWinV && !(custom.Win && custom.Key == (int)Keys.V && !custom.Ctrl && !custom.Alt && !custom.Shift))
            return "Win+V or " + custom;
        return custom.ToString();
    }

    public void SetShowTrayIcon(bool show)
    {
        Store.Data.Settings.ShowTrayIcon = show;
        Store.Save();
        if (_headless) return;

        if (show)
        {
            if (_tray is null) SetupTray();
        }
        else
        {
            _tray?.Dispose();
            _tray = null;
        }
    }

    private void SetupTray()
    {
        _tray?.Dispose();
        _tray = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "Clipped",
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowPicker();

        var menu = DarkMenus.Create();
        var open = DarkMenus.Item("Open Clipped", (_, _) => ShowPicker());
        var dashboard = DarkMenus.Item("Dashboard", (_, _) => OpenDashboard("library"));
        var startup = DarkMenus.Item("Launch at startup", (_, _) =>
        {
            bool next = !WindowsIntegration.StartupEnabled;
            WindowsIntegration.SetStartup(next);
            Store.Data.Settings.LaunchAtStartup = next;
            Store.Save();
        }, WindowsIntegration.StartupEnabled);
        var integration = DarkMenus.Item("Show in Windows Settings", (_, _) =>
        {
            bool next = !Store.Data.Settings.WindowsIntegration;
            Store.Data.Settings.WindowsIntegration = next;
            WindowsIntegration.RegisterIntegration(next);
            Store.Save();
        }, Store.Data.Settings.WindowsIntegration);
        var winv = DarkMenus.Item("Replace Win+V", (_, _) =>
            SetTakeOverWinV(!Store.Data.Settings.TakeOverWinV), Store.Data.Settings.TakeOverWinV);
        var settings = DarkMenus.Item("Settings…", (_, _) => OpenDashboard("settings"));
        var quit = DarkMenus.Item("Quit", (_, _) => Quit());

        menu.Items.Add(open);
        menu.Items.Add(dashboard);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startup);
        menu.Items.Add(integration);
        menu.Items.Add(winv);
        menu.Items.Add(settings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quit);
        menu.Opening += (_, _) =>
        {
            open.Text = "Open Clipped    " + HotkeyLabel();
            startup.Checked = WindowsIntegration.StartupEnabled;
            integration.Checked = Store.Data.Settings.WindowsIntegration;
            winv.Checked = Store.Data.Settings.TakeOverWinV;
        };
        _tray.ContextMenuStrip = menu;
    }

    private void OnHotkey()
    {
        // Runs inside the low-level keyboard hook callback. Do the work on the UI
        // thread: a slow callback can make Windows silently drop the hook, after
        // which the shortcut stops working until the app restarts.
        _ui?.Post(_ =>
        {
            try
            {
                Log.Info("Hotkey fired");
                TogglePicker();
            }
            catch (Exception ex)
            {
                Log.Error("Hotkey handler crashed", ex);
            }
        }, null);
    }

    public void TogglePicker()
    {
        if (Picker.Visible)
        {
            Picker.ClosePicker();
            return;
        }
        ShowPicker();
    }

    public void ShowPicker()
    {
        var fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != Picker.Handle && (Dashboard is null || fg != Dashboard.Handle))
            LastExternalWindow = fg;
        Picker.Open(LastExternalWindow);
        Log.Info("Picker opened visible=" + Picker.Visible);
    }

    public void CommitText(string text, IntPtr target)
    {
        Picker.ClosePicker();
        var t = target != IntPtr.Zero ? target : LastExternalWindow;
        bool send = Store.Data.Settings.PasteAfterSelect && t != IntPtr.Zero;
        _pendingPaste = (text, null, t, send);
        _pasteTimer.Stop();
        _pasteTimer.Start();
    }

    public void CommitImage(string path, IntPtr target)
    {
        Picker.ClosePicker();
        var t = target != IntPtr.Zero ? target : LastExternalWindow;
        bool send = Store.Data.Settings.PasteAfterSelect && t != IntPtr.Zero;
        _pendingPaste = (null, path, t, send);
        _pasteTimer.Stop();
        _pasteTimer.Start();
    }

    public void CommitGif(byte[] bytes, IntPtr target)
    {
        Picker.ClosePicker();
        var t = target != IntPtr.Zero ? target : LastExternalWindow;
        bool send = Store.Data.Settings.PasteAfterSelect && t != IntPtr.Zero;
        _pendingPaste = (null, null, t, send);
        _pendingGif = bytes;
        _pasteTimer.Stop();
        _pasteTimer.Start();
    }

    public void OpenDashboard(string page)
    {
        Dashboard ??= new DashboardForm(this);
        Dashboard.ShowPage(page);
    }

    public void SetThemeMode(ThemeMode mode)
    {
        Store.Data.Settings.ThemeMode = mode.ToString().ToLowerInvariant();
        Store.Save();
        Theme.SetMode(mode);
    }

    private void OnThemeChanged(Palette old, Palette now)
    {
        void Apply()
        {
            Theme.ApplyTo(Picker, old);
            if (Picker.IsHandleCreated)
            {
                Native.DarkTitleBar(Picker.Handle, !Theme.IsLight);
                Native.SetBorderColor(Picker.Handle, Theme.Border);
            }
            Picker.Invalidate(true);

            if (Dashboard is not null)
            {
                Theme.ApplyTo(Dashboard, old);
                if (Dashboard.IsHandleCreated)
                {
                    Native.DarkTitleBar(Dashboard.Handle, !Theme.IsLight);
                    Native.SetBorderColor(Dashboard.Handle, Theme.Border);
                }
                Dashboard.Invalidate(true);
            }

            if (_tray?.ContextMenuStrip is ContextMenuStrip menu)
            {
                menu.BackColor = Theme.Surface;
                menu.ForeColor = Theme.Text;
            }
        }

        if (_ui is not null) _ui.Post(_ => Apply(), null);
        else Apply();
    }

    private void HandleLink(string link)
    {
        var lower = link.Trim().ToLowerInvariant();
        if (lower.StartsWith("--settings") || lower.StartsWith("--modify") || lower.Contains("dashboard/settings")) OpenDashboard("settings");
        else if (lower.Contains("dashboard/history")) OpenDashboard("history");
        else if (lower.StartsWith("--dashboard") || lower.Contains("dashboard")) OpenDashboard("library");
        else if (lower.StartsWith("--dates"))
        {
            ShowPicker();
            Picker.PreviewDates("dd MMM yyyy");
        }
        else if (lower.StartsWith("--gif"))
        {
            ShowPicker();
            Picker.PreviewGif();
        }
        else if (lower.StartsWith("--picker") || lower.Contains("open") || lower.StartsWith("clipped://")) ShowPicker();
    }

    public void Quit()
    {
        Quitting = true;
        Store.Save();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _hook.Dispose();
        _watcher.Dispose();
        Dashboard?.Dispose();
        Picker.Dispose();
        ExitThread();
    }

    // ---- screenshots (development aid) ----

    public void TakeScreenshots(string dir)
    {
        Directory.CreateDirectory(dir);
        if (Store.Data.History.Count == 0)
        {
            Store.AddHistory("thanks — that works perfectly!");
            Store.AddHistory("12 Nour Street, Apt 7\nCairo 11511");
            Store.AddHistory("https://github.com/GLITCH54s/clipped");
            Store.AddHistory("Option+Shift is taken, try Ctrl+Shift+Space");
        }

        ShowPicker();
        Application.DoEvents();
        Thread.Sleep(250);
        SaveShot(Picker, Path.Combine(dir, "picker-recent.png"));

        var firstTab = Store.Data.Tabs.FirstOrDefault();
        if (firstTab is not null)
        {
            Picker.PreviewTab(firstTab.Id);
            Application.DoEvents();
            Thread.Sleep(120);
            SaveShot(Picker, Path.Combine(dir, "picker-tab.png"));
        }
        Picker.PreviewDates("ddd, dd MMM yyyy");
        Application.DoEvents();
        Thread.Sleep(120);
        SaveShot(Picker, Path.Combine(dir, "picker-dates.png"));

        Picker.PreviewEmoji();
        Application.DoEvents();
        Thread.Sleep(120);
        SaveShot(Picker, Path.Combine(dir, "picker-emoji.png"));

        Picker.PreviewGif();
        for (int i = 0; i < 40; i++)
        {
            Application.DoEvents();
            Thread.Sleep(100);
        }
        SaveShot(Picker, Path.Combine(dir, "picker-gif.png"));
        Picker.ClosePicker();

        OpenDashboard("library");
        Application.DoEvents();
        Thread.Sleep(350);
        SaveShot(Dashboard!, Path.Combine(dir, "dashboard-library.png"));

        OpenDashboard("history");
        Application.DoEvents();
        Thread.Sleep(200);
        SaveShot(Dashboard!, Path.Combine(dir, "dashboard-history.png"));

        OpenDashboard("settings");
        Application.DoEvents();
        Thread.Sleep(200);
        SaveShot(Dashboard!, Path.Combine(dir, "dashboard-settings.png"));

        using (var full = Dashboard!.RenderFullSettingsShot())
            SaveShot(full, Path.Combine(dir, "dashboard-settings-full.png"));
        Application.DoEvents();

        // Flip the mode at runtime so the Changed→ApplyTo repaint path is exercised too.
        SetThemeMode(Theme.IsLight ? ThemeMode.Dark : ThemeMode.Light);
        Application.DoEvents();
        Thread.Sleep(300);

        ShowPicker();
        Application.DoEvents();
        Thread.Sleep(200);
        SaveShot(Picker, Path.Combine(dir, "picker-recent-switched.png"));
        Picker.ClosePicker();

        OpenDashboard("library");
        Application.DoEvents();
        Thread.Sleep(200);
        SaveShot(Dashboard!, Path.Combine(dir, "dashboard-library-switched.png"));

        OpenDashboard("settings");
        Application.DoEvents();
        Thread.Sleep(200);
        SaveShot(Dashboard!, Path.Combine(dir, "dashboard-settings-switched.png"));
    }

    private static void SaveShot(Form form, string path)
    {
        using var bmp = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
        SaveShot(bmp, path);
    }

    private static void SaveShot(Bitmap bmp, string path)
    {
        try
        {
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Log.Info("Screenshot: " + path);
        }
        catch (Exception ex)
        {
            Log.Error("Screenshot failed: " + path, ex);
        }
    }
}
