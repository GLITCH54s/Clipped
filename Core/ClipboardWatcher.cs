using System.Drawing;
using System.Runtime.InteropServices;

namespace Clipped.Core;

/// <summary>Watches the clipboard using AddClipboardFormatListener.</summary>
public sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    public event Action<string>? TextCopied;
    public event Action<Image>? ImageCopied;

    public ClipboardWatcher()
    {
        var cp = new CreateParams
        {
            Caption = "ClippedClipboardWatcher",
            Style = 0,
        };
        CreateHandle(cp);
        if (!Native.AddClipboardFormatListener(Handle))
            Log.Error("AddClipboardFormatListener failed");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_CLIPBOARDUPDATE)
        {
            // Text first: Office and browsers put a bitmap next to the text on an
            // ordinary copy. Only when there is no text is the bitmap the actual clip.
            var text = TryReadText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { TextCopied?.Invoke(text); }
                catch (Exception ex) { Log.Error("Clipboard handler crashed", ex); }
            }
            else
            {
                var image = TryReadImage();
                if (image is null) return;
                try { ImageCopied?.Invoke(image); }
                catch (Exception ex) { Log.Error("Image clipboard handler crashed", ex); }
            }
        }
        base.WndProc(ref m);
    }

    private static Image? TryReadImage()
    {
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
        return null;
    }

    private static string? TryReadText()
    {
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
        return null;
    }

    public void Dispose()
    {
        try
        {
            if (Handle != IntPtr.Zero)
            {
                Native.RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
        }
        catch { }
    }
}
