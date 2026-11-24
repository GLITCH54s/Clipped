using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace Clipped.Widget;

internal static class ComGuids
{
    public const string IClassFactory = "00000001-0000-0000-C000-000000000046";
}

[ComImport, ComVisible(false), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid(ComGuids.IClassFactory)]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer(bool fLock);
}

[ComVisible(true)]
internal sealed class WidgetProviderFactory<T> : IClassFactory where T : IWidgetProvider, new()
{
    public static readonly Guid ClassId = new("969db025-0b6a-4782-ba4c-c728327c41a4");

    private const int CLASS_E_NOAGGREGATION = -2147221232;

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        if (pUnkOuter != IntPtr.Zero) return CLASS_E_NOAGGREGATION;

        IntPtr provider = IntPtr.Zero;
        try
        {
            provider = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
            return Marshal.QueryInterface(provider, in riid, out ppvObject);
        }
        catch (Exception ex)
        {
            return Marshal.GetHRForException(ex);
        }
        finally
        {
            if (provider != IntPtr.Zero) Marshal.Release(provider);
        }
    }

    public int LockServer(bool fLock) => 0;
}
