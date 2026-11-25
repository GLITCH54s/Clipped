using System.Drawing;
using System.Runtime.InteropServices;

namespace Clipped.Core;

/// <summary>Watches the clipboard using AddClipboardFormatListener.</summary>
public sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    public event Action<string>? TextCopied;
    public event Action<Image>? ImageCopied;

    private readonly System.Windows.Forms.Timer? _poll;
    private uint _lastSeq;

    public ClipboardWatcher(bool poll = true)
    {
        var cp = new CreateParams
        {
            Caption = "ClippedClipboardWatcher",
            Style = 0,
        };
        CreateHandle(cp);
        if (!Native.AddClipboardFormatListener(Handle))
            Log.Error("AddClipboardFormatListener failed");
        _lastSeq = Native.GetClipboardSequenceNumber();

        // WM_CLIPBOARDUPDATE is the primary signal, but a clip set by a higher
        // integrity process can be filtered from our window by UIPI. The sequence
        // number is readable regardless, so poll it as a fallback.
        if (poll)
        {
            _poll = new System.Windows.Forms.Timer { Interval = 700 };
            _poll.Tick += (_, _) => ReadClipboard();
            _poll.Start();
        }
    }

    /// <summary>Reads whatever is on the clipboard now, even if unchanged since the last read.</summary>
    public void Sync() => ReadClipboard(force: true);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_CLIPBOARDUPDATE) ReadClipboard();
        base.WndProc(ref m);
    }

    private void ReadClipboard(bool force = false)
    {
        var seq = Native.GetClipboardSequenceNumber();
        if (!force && seq == _lastSeq) return;

        // Text first: Office and browsers put a bitmap next to the text on an
        // ordinary copy. Only when there is no text is the bitmap the actual clip.
        var text = TryReadText(out bool textFailed);
        if (textFailed) return; // clipboard stayed locked — leave _lastSeq so we retry
        if (!string.IsNullOrWhiteSpace(text))
        {
            _lastSeq = seq;
            try { TextCopied?.Invoke(text); }
            catch (Exception ex) { Log.Error("Clipboard handler crashed", ex); }
            return;
        }

        var image = TryReadImage(out bool imageFailed);
        if (imageFailed) return;
        _lastSeq = seq;
        if (image is null) return;
        try { ImageCopied?.Invoke(image); }
        catch (Exception ex)
        {
            image.Dispose();
            Log.Error("Image clipboard handler crashed", ex);
        }
    }

    private static Image? TryReadImage(out bool failed)
    {
        failed = false;
        for (int i = 0; i < 8; i++)
        {
            try
            {
                if (!Clipboard.ContainsImage()) return null;
                return Clipboard.GetImage();
            }
            catch (ExternalException)
            {
                Thread.Sleep(15);
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard image read failed", ex);
                return null;
            }
        }
        failed = true;
        return null;
    }

    private static string? TryReadText(out bool failed)
    {
        failed = false;
        for (int i = 0; i < 8; i++)
        {
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                    return Clipboard.GetText(TextDataFormat.UnicodeText);
                return null;
            }
            catch (ExternalException)
            {
                Thread.Sleep(15);
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard read failed", ex);
                return null;
            }
        }
        failed = true;
        return null;
    }

    public void Dispose()
    {
        try
        {
            _poll?.Stop();
            _poll?.Dispose();
            if (Handle != IntPtr.Zero)
            {
                Native.RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
        }
        catch { }
    }
}
