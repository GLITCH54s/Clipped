using System.Runtime.InteropServices;

namespace Clipped.Core;

/// <summary>
/// Global hotkey via a low-level keyboard hook, so Clipped can take over
/// combos the OS shells out first (Win+V, Win+E, ...) and swallow them.
/// </summary>
public sealed class HotkeyHook : IDisposable
{
    public sealed class Combo
    {
        public bool Win, Ctrl, Alt, Shift;
        public int Key;

        public bool HasModifier => Win || Ctrl || Alt || Shift;
        public bool IsFunctionKey => Key >= (int)Keys.F1 && Key <= (int)Keys.F24;

        public override string ToString()
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            parts.Add(KeyName(Key));
            return string.Join("+", parts);
        }

        public bool SameAs(Combo other) =>
            Win == other.Win && Ctrl == other.Ctrl && Alt == other.Alt &&
            Shift == other.Shift && Key == other.Key;
    }

    private static readonly Dictionary<string, int> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = (int)Keys.Space,
        ["enter"] = (int)Keys.Enter,
        ["return"] = (int)Keys.Enter,
        ["tab"] = (int)Keys.Tab,
        ["esc"] = (int)Keys.Escape,
        ["escape"] = (int)Keys.Escape,
        ["backspace"] = (int)Keys.Back,
        ["delete"] = (int)Keys.Delete,
        ["del"] = (int)Keys.Delete,
        ["insert"] = (int)Keys.Insert,
        ["home"] = (int)Keys.Home,
        ["end"] = (int)Keys.End,
        ["pageup"] = (int)Keys.PageUp,
        ["pgup"] = (int)Keys.PageUp,
        ["pagedown"] = (int)Keys.PageDown,
        ["pgdn"] = (int)Keys.PageDown,
        ["up"] = (int)Keys.Up,
        ["down"] = (int)Keys.Down,
        ["left"] = (int)Keys.Left,
        ["right"] = (int)Keys.Right,
        ["printscreen"] = (int)Keys.PrintScreen,
        ["pause"] = (int)Keys.Pause,
        ["capslock"] = (int)Keys.CapsLock,
        ["numlock"] = (int)Keys.NumLock,
        ["scrolllock"] = (int)Keys.Scroll,
        ["`"] = (int)Keys.Oemtilde,
        ["~"] = (int)Keys.Oemtilde,
        ["-"] = (int)Keys.OemMinus,
        ["="] = (int)Keys.Oemplus,
        ["["] = (int)Keys.OemOpenBrackets,
        ["]"] = (int)Keys.OemCloseBrackets,
        ["\\"] = (int)Keys.OemPipe,
        [";"] = (int)Keys.OemSemicolon,
        ["'"] = (int)Keys.OemQuotes,
        [","] = (int)Keys.Oemcomma,
        ["."] = (int)Keys.OemPeriod,
        ["/"] = (int)Keys.OemQuestion,
    };

    private static readonly Dictionary<int, string> KeyNames = new();

    static HotkeyHook()
    {
        foreach (var pair in KeyAliases)
        {
            if (!KeyNames.ContainsKey(pair.Value) && pair.Key.Length > 1)
                KeyNames[pair.Value] = Title(pair.Key);
        }
        KeyNames[(int)Keys.Oemtilde] = "`";
        KeyNames[(int)Keys.OemMinus] = "-";
        KeyNames[(int)Keys.Oemplus] = "=";
        KeyNames[(int)Keys.OemOpenBrackets] = "[";
        KeyNames[(int)Keys.OemCloseBrackets] = "]";
        KeyNames[(int)Keys.OemPipe] = "\\";
        KeyNames[(int)Keys.OemSemicolon] = ";";
        KeyNames[(int)Keys.OemQuotes] = "'";
        KeyNames[(int)Keys.Oemcomma] = ",";
        KeyNames[(int)Keys.OemPeriod] = ".";
        KeyNames[(int)Keys.OemQuestion] = "/";
    }

    private static string Title(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    public static string KeyName(int key)
    {
        if (key >= (int)Keys.A && key <= (int)Keys.Z) return ((char)key).ToString();
        if (key >= (int)Keys.D0 && key <= (int)Keys.D9) return ((char)key).ToString();
        if (key >= (int)Keys.F1 && key <= (int)Keys.F24) return "F" + (key - (int)Keys.F1 + 1);
        if (key >= (int)Keys.NumPad0 && key <= (int)Keys.NumPad9) return "Num" + (key - (int)Keys.NumPad0);
        return KeyNames.TryGetValue(key, out var name) ? name : ((Keys)key).ToString();
    }

    public static Combo? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var combo = new Combo();
        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in tokens)
        {
            var t = raw.Trim();
            var lower = t.ToLowerInvariant();
            switch (lower)
            {
                case "win":
                case "windows":
                case "meta": combo.Win = true; continue;
                case "ctrl":
                case "control": combo.Ctrl = true; continue;
                case "alt": combo.Alt = true; continue;
                case "shift": combo.Shift = true; continue;
            }

            int key;
            if (KeyAliases.TryGetValue(lower, out var alias)) key = alias;
            else if (t.Length == 1 && char.IsLetterOrDigit(t[0]))
                key = char.IsDigit(t[0]) ? (int)Keys.D0 + (t[0] - '0') : char.ToUpperInvariant(t[0]);
            else if (lower.StartsWith('f') && int.TryParse(lower[1..], out var fn) && fn is >= 1 and <= 24)
                key = (int)Keys.F1 + fn - 1;
            else if (Enum.TryParse<Keys>(t, true, out var parsed)) key = (int)parsed;
            else return null;

            combo.Key = key;
        }

        if (combo.Key == 0) return null;
        return combo;
    }

    private readonly Native.LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private readonly HashSet<int> _held = new();
    private bool _swallowWinUp;

    private List<Combo> _combos = new();
    public event Action? Pressed;

    public HotkeyHook()
    {
        _proc = Callback;
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc,
            Native.GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            Log.Error("Failed to install keyboard hook: " + Marshal.GetLastWin32Error());
        else
            Log.Info("Keyboard hook installed");
    }

    public void Set(IEnumerable<Combo> combos) => _combos = combos.ToList();

    private bool Down(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        if ((info.flags & Native.LLKHF_INJECTED) != 0)
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        int msg = (int)wParam;
        bool isDown = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
        bool isUp = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;
        int vk = (int)info.vkCode;

        if (isUp)
        {
            _held.Remove(vk);
            if (_swallowWinUp && (vk == Native.VK_LWIN || vk == Native.VK_RWIN))
            {
                if (!Down(Native.VK_LWIN) && !Down(Native.VK_RWIN)) _swallowWinUp = false;
                return (IntPtr)1;
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (!isDown) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        if (vk == 0) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        foreach (var combo in _combos)
        {
            if (vk != combo.Key) continue;
            bool win = Down(Native.VK_LWIN) || Down(Native.VK_RWIN);
            bool ctrl = Down(Native.VK_CONTROL);
            bool alt = Down(Native.VK_MENU);
            bool shift = Down(Native.VK_SHIFT);

            if (win == combo.Win && ctrl == combo.Ctrl && alt == combo.Alt && shift == combo.Shift)
            {
                bool first = _held.Add(vk);
                if (combo.Win) _swallowWinUp = true;
                if (first) Pressed?.Invoke();
                return (IntPtr)1;
            }
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
