using System.Collections.Specialized;
using System.Runtime.InteropServices;

namespace Clipped.Core;

public static class Paster
{
    public static bool Copy(string text)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var data = new DataObject();
                data.SetText(text, TextDataFormat.UnicodeText);
                Clipboard.SetDataObject(data, true, 5, 40);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(25);
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard write failed", ex);
                return false;
            }
        }
        Log.Error("Clipboard write failed after retries (clipboard locked)");
        return false;
    }

    public static bool CopyImage(Image image)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var data = new DataObject();
                data.SetImage(image);
                Clipboard.SetDataObject(data, true, 5, 40);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(25);
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard image write failed", ex);
                return false;
            }
        }
        Log.Error("Clipboard image write failed after retries (clipboard locked)");
        return false;
    }

    /// <summary>
    /// Copies a GIF file so the animation survives the paste: the file goes on the
    /// clipboard as a file drop, with a still frame as the fallback for apps that
    /// only accept bitmaps.
    /// </summary>
    public static bool CopyGifFile(string path)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var data = new DataObject();
                data.SetFileDropList(new StringCollection { path });

                try
                {
                    using var frame = Image.FromFile(path);
                    data.SetImage(frame);
                }
                catch (Exception ex)
                {
                    Log.Error("Could not read GIF frame for fallback", ex);
                }

                Clipboard.SetDataObject(data, true, 5, 40);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(25);
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard GIF write failed", ex);
                return false;
            }
        }
        Log.Error("Clipboard GIF write failed after retries (clipboard locked)");
        return false;
    }

    /// <summary>Writes the bytes to the cache folder and copies them as an animated GIF.</summary>
    public static bool CopyGifBytes(byte[] bytes)
    {
        try
        {
            return CopyGifFile(SaveGif(bytes));
        }
        catch (Exception ex)
        {
            Log.Error("Could not stage GIF for clipboard", ex);
            return false;
        }
    }

    /// <summary>Stages GIF bytes on disk so the clipboard keeps a live file reference.</summary>
    public static string SaveGif(byte[] bytes)
    {
        var dir = Path.Combine(Paths.DataDir, "gifs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clipped-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".gif");
        File.WriteAllBytes(path, bytes);
        PruneGifCache(dir);
        return path;
    }

    private static void PruneGifCache(string dir)
    {
        try
        {
            var cutoff = DateTime.Now.AddHours(-2);
            foreach (var file in Directory.GetFiles(dir, "clipped-*.gif"))
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
        }
        catch { }
    }

    public static void SendCtrlV()
    {
        const ushort VK_CONTROL = 0x11;
        const ushort VK_V = 0x56;
        var inputs = new Native.INPUT[4];
        inputs[0].type = Native.INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = VK_CONTROL;
        inputs[1].type = Native.INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = VK_V;
        inputs[2].type = Native.INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = VK_V;
        inputs[2].u.ki.dwFlags = Native.KEYEVENTF_KEYUP;
        inputs[3].type = Native.INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = VK_CONTROL;
        inputs[3].u.ki.dwFlags = Native.KEYEVENTF_KEYUP;

        var sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
        if (sent != inputs.Length) Log.Error("SendInput returned " + sent);
    }

    public static void PasteInto(IntPtr target, string text, bool sendPaste)
    {
        if (!Copy(text)) return;
        if (!sendPaste) return;
        if (target != IntPtr.Zero)
        {
            Native.ForceForeground(target);
            Thread.Sleep(45);
        }
        SendCtrlV();
    }

    public static void PasteImageInto(IntPtr target, string path, bool sendPaste)
    {
        if (string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            if (!CopyGifFile(path)) return;
        }
        else
        {
            try
            {
                using var image = new Bitmap(path);
                if (!CopyImage(image)) return;
            }
            catch (Exception ex)
            {
                Log.Error("Could not load image " + path, ex);
                return;
            }
        }

        if (!sendPaste) return;
        if (target != IntPtr.Zero)
        {
            Native.ForceForeground(target);
            Thread.Sleep(45);
        }
        SendCtrlV();
    }

    public static void PasteGifInto(IntPtr target, byte[] bytes, bool sendPaste)
    {
        if (!CopyGifBytes(bytes)) return;
        if (!sendPaste) return;
        if (target != IntPtr.Zero)
        {
            Native.ForceForeground(target);
            Thread.Sleep(45);
        }
        SendCtrlV();
    }
}
