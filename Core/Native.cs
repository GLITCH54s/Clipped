using System.Runtime.InteropServices;
using System.Text;

namespace Clipped.Core;

internal static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_APP = 0x8000;

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;

    public const uint LLKHF_INJECTED = 0x10;

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWCP_ROUND = 2;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    /// <summary>True when Windows itself currently treats either Win key as held.</summary>
    public static bool WinKeyDown() =>
        (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("user32.dll")]
    public static extern IntPtr GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    /// <summary>Caret position in screen pixels for the thread that owns <paramref name="target"/>.</summary>
    public static bool TryGetCaretRect(IntPtr target, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (target == IntPtr.Zero) return false;
        uint tid = GetWindowThreadProcessId(target, out _);
        if (tid == 0) return false;

        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(tid, ref info) || info.hwndCaret == IntPtr.Zero) return false;

        var r = info.rcCaret;
        if (r.Bottom <= r.Top) return false;

        var pt = new POINT { X = r.Left, Y = r.Bottom };
        if (!ClientToScreen(info.hwndCaret, ref pt)) return false;

        rect = new Rectangle(pt.X, pt.Y - (r.Bottom - r.Top), Math.Max(2, r.Right - r.Left), r.Bottom - r.Top);
        return true;
    }

    /// <summary>Screen rect of another process's window, in physical pixels.</summary>
    public static bool TryGetWindowRect(IntPtr hwnd, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return false;
        if (r.Right <= r.Left || r.Bottom <= r.Top) return false;
        rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return true;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public static void DarkTitleBar(IntPtr hwnd, bool dark = true)
    {
        try
        {
            int flag = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref flag, sizeof(int));
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }
        catch { }
    }

    public static void SetBorderColor(IntPtr hwnd, Color color)
    {
        try
        {
            int bgr = color.R | (color.G << 8) | (color.B << 16);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref bgr, sizeof(int));
        }
        catch { }
    }

    public static uint DpiForCursor(out Native.POINT pt)
    {
        GetCursorPos(out pt);
        try
        {
            var mon = MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out var dx, out _) == 0 && dx > 0)
                return dx;
        }
        catch { }
        return 96;
    }

    public static Rectangle WorkAreaForCursor(out Native.POINT pt)
    {
        GetCursorPos(out pt);
        return WorkAreaForPoint(pt);
    }

    public static Rectangle WorkAreaForPoint(Native.POINT pt)
    {
        var mon = MonitorFromPoint(pt, 2);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(mon, ref info))
        {
            var r = info.rcWork;
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
        return (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll")]
    public static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

    /// <summary>Switches native scrollbars and edit chrome between Explorer's light and dark themes.</summary>
    public static void ApplyControlTheme(Control control, bool light, string? subId = null)
    {
        if (!control.IsHandleCreated) return;
        try
        {
            SetWindowTheme(control.Handle, light ? null : "DarkMode_Explorer", subId);
        }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    public static void SetTopMost(IntPtr hwnd, bool on)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, on ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0,
            0x0001 | 0x0002 | 0x0010 | 0x0040); // NOSIZE | NOMOVE | NOACTIVATE | SHOWWINDOW
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// Makes a window refuse activation so clicks inside it leave the foreground app alone.
    /// Used to float the picker over the taskbar search without closing the search.
    /// </summary>
    public static void SetNoActivate(IntPtr hwnd, bool on)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            long ex = IntPtr.Size == 8
                ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64()
                : GetWindowLong32(hwnd, GWL_EXSTYLE);
            long updated = on ? ex | WS_EX_NOACTIVATE : ex & ~WS_EX_NOACTIVATE;
            if (updated == ex) return;
            if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(updated));
            else SetWindowLong32(hwnd, GWL_EXSTYLE, (int)updated);
        }
        catch (Exception ex)
        {
            Log.Error("Could not change window activation style", ex);
        }
    }

    public static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var fg = GetForegroundWindow();
        uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        uint curThread = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != curThread && AttachThreadInput(curThread, fgThread, true);
        try
        {
            ShowWindow(hwnd, SW_SHOW);
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(curThread, fgThread, false);
        }
    }

    public static bool IsAltDown() => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
}
