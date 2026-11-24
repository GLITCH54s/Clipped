using System.Security.Cryptography;
using System.Text;
using Clipped.Core;
using Clipped.UI;

namespace Clipped;

internal static class Program
{
    public static readonly string DataKey = ComputeKey();

    private static string ComputeKey()
    {
        var path = Paths.DataDir.ToLowerInvariant();
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(path));
        return Convert.ToHexString(hash)[..10];
    }

    private static string LinkPath => Path.Combine(Path.GetTempPath(), "clipped-deeplink-" + DataKey + ".txt");

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            if (WindowsIntegration.IsPackaged)
            {
                WindowsIntegration.OpenSettingsPage("appsfeatures");
                return;
            }

            var installed = WindowsIntegration.InstallerOwnsUninstallEntry();
            WindowsIntegration.RegisterIntegration(false);
            WindowsIntegration.SetStartup(false);
            MessageBox.Show(installed
                    ? "Clipped was removed from Windows Settings and startup.\n\n" +
                      "To remove the application files, uninstall Clipped from Apps & features."
                    : "Clipped was removed from Windows Settings and startup.\n\n" +
                      "You can delete this folder to finish uninstalling.",
                "Clipped", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var link = args.FirstOrDefault(a => a.StartsWith("clipped:", StringComparison.OrdinalIgnoreCase))
                   ?? args.FirstOrDefault(a => a.StartsWith("--picker") || a.StartsWith("--settings") || a.StartsWith("--modify") || a.StartsWith("--dashboard") || a.StartsWith("--dates") || a.StartsWith("--gif"));

        var shotIndex = Array.FindIndex(args, a => string.Equals(a, "--shot", StringComparison.OrdinalIgnoreCase));
        if (shotIndex >= 0)
        {
            var dir = shotIndex + 1 < args.Length && !args[shotIndex + 1].StartsWith("--")
                ? args[shotIndex + 1]
                : Path.Combine(Path.GetTempPath(), "clipped-shots");
            var app = new AppController(headless: true);
            app.TakeScreenshots(dir);
            app.Quit();
            return;
        }

        using var mutex = new Mutex(true, "Clipped_Mutex_" + DataKey, out bool isFirst);
        if (!isFirst)
        {
            // Launching the app while it runs is the way back in if the hotkey and tray are off.
            if (string.IsNullOrEmpty(link)) link = "--dashboard";
            if (!string.IsNullOrEmpty(link))
            {
                try { File.WriteAllText(LinkPath, link); } catch { }
                try
                {
                    using var ev = EventWaitHandle.OpenExisting("Clipped_Activate_" + DataKey);
                    ev.Set();
                }
                catch { }
            }
            return;
        }

        var controller = new AppController();
        controller.Start(link);
        Application.Run(controller);

        GC.KeepAlive(mutex);
    }

    public static string? ReadDeepLink()
    {
        try
        {
            if (!File.Exists(LinkPath)) return null;
            var text = File.ReadAllText(LinkPath).Trim();
            File.Delete(LinkPath);
            return text.Length == 0 ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
