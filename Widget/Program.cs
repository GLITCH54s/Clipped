using System.Runtime.InteropServices;
using Clipped.Widget;
using Microsoft.Windows.Widgets.Providers;

namespace Clipped.Widget;

internal static class Program
{
    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    [STAThread]
    private static void Main()
    {
        Log("start " + Environment.CommandLine);
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            var factory = new WidgetProviderFactory<WidgetProvider>();
            Marshal.ThrowExceptionForHR(
                CoRegisterClassObject(WidgetProviderFactory<WidgetProvider>.ClassId, factory, 0x4, 0x1, out uint cookie));
            Log("class registered");

            try
            {
                WidgetProvider.EmptyList.WaitOne();
            }
            finally
            {
                CoRevokeClassObject(cookie);
                GC.KeepAlive(factory);
            }
            Log("exit");
        }
        catch (Exception ex)
        {
            Log("fatal " + ex);
            throw;
        }
    }

    private static void Log(string message)
    {
        try
        {
            var dir = ClipStore.DataDir;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "widget.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
        }
        catch { }
    }
}
