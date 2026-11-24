using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using Windows.ApplicationModel.DataTransfer;

namespace Clipped.Widget;

internal static class ClipStore
{
    // The provider runs with package identity, where SpecialFolder.ApplicationData is redirected
    // into the MSIX container; %APPDATA% still points at the real Clipped data folder.
    public static string DataDir =>
        Environment.GetEnvironmentVariable("CLIPPED_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetEnvironmentVariable("APPDATA")
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Clipped");

    public static IReadOnlyList<Clip> Recent(int max)
    {
        try
        {
            var path = Path.Combine(DataDir, "clipped.json");
            if (!File.Exists(path)) return Array.Empty<Clip>();

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("history", out var history)) return Array.Empty<Clip>();

            var clips = new List<Clip>();
            foreach (var item in history.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;

                var at = DateTime.Now;
                if (item.TryGetProperty("copiedAt", out var c) && c.TryGetDateTime(out var parsed)) at = parsed;

                clips.Add(new Clip(text, at));
                if (clips.Count >= max) break;
            }
            return clips;
        }
        catch
        {
            return Array.Empty<Clip>();
        }
    }

    public static void Copy(string text)
    {
        // Clipboard APIs need an STA thread; provider callbacks arrive on RPC threads.
        var thread = new Thread(() =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
                Clipboard.Flush();
            }
            catch { }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(2000);
    }

    public static void OpenDashboard()
    {
        // The protocol works for both the unpackaged install and the MSIX package.
        if (TryStart("clipped://dashboard")) return;

        var exe = FindExe();
        if (exe is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--dashboard") { UseShellExecute = true });
        }
        catch
        {
            // Clipped was moved or uninstalled; the widget stays functional for copying.
        }
    }

    private static bool TryStart(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindExe()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Clipped");
            if (key?.GetValue("ExePath") is string registered && File.Exists(registered)) return registered;
        }
        catch { }

        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Clipped.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Clipped", "Clipped.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Clipped", "Clipped.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
