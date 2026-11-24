using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

internal static class BlitzBlockTaskbar
{
    private const string AppId = "LiamMorganDev.BlitzBlock";
    private static readonly Guid AppModel = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        internal Guid Format;
        internal uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyValue
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal IntPtr Text;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface PropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropertyValue value);
        void SetValue(ref PropertyKey key, ref PropertyValue value);
        void Commit();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink {}

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out PropertyStore store);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIcon
    {
        internal uint Size;
        internal IntPtr Window;
        internal uint Id, Flags, Callback;
        internal IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid Guid;
        internal IntPtr BalloonIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropertyValue value);

    internal static bool ActivateExistingWindow()
    {
        IntPtr window = IntPtr.Zero;
        while ((window = FindWindowEx(IntPtr.Zero, window, "Chrome_WidgetWin_1", null)) != IntPtr.Zero)
        {
            PropertyStore store;
            Guid id = typeof(PropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(window, ref id, out store) < 0) continue;
            try
            {
                var key = new PropertyKey { Format = AppModel, Id = 5 };
                PropertyValue value;
                store.GetValue(ref key, out value);
                try
                {
                    if (value.Type != 31 || Marshal.PtrToStringUni(value.Text) != AppId) continue;
                }
                finally { PropVariantClear(ref value); }
            }
            catch (COMException) { continue; }
            finally { Marshal.ReleaseComObject(store); }

            if (IsIconic(window)) ShowWindowAsync(window, 9); // SW_RESTORE
            else if (!IsWindowVisible(window)) ShowWindowAsync(window, 5); // SW_SHOW
            SetForegroundWindow(window);
            return true;
        }
        return false;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIcon icon);

    internal static bool SetTrayPresentation(int processId, IntPtr icon)
    {
        IntPtr host = IntPtr.Zero;
        while ((host = FindWindowEx(IntPtr.Zero, host, "Electron_NotifyIconHostWindow", null)) != IntPtr.Zero)
        {
            uint owner;
            GetWindowThreadProcessId(host, out owner);
            if (owner != processId) continue;
            // shortcut: Blitz 3.0.29 uses tray ID 3. Recheck if Electron changes it.
            var data = new NotifyIcon {
                Size = (uint)Marshal.SizeOf(typeof(NotifyIcon)), Window = host, Id = 3,
                Flags = 2 | 4 | 128, Icon = icon, Tip = "BlitzBlock" // NIF_ICON | NIF_TIP | NIF_SHOWTIP
            };
            return Shell_NotifyIcon(1, ref data);
        }
        return false;
    }

    internal static bool SetWindow(IntPtr window, string launcher, string icon)
    {
        PropertyStore store = null;
        try
        {
            Guid id = typeof(PropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(window, ref id, out store) < 0)
                return false;
            Write(store, 2, "\"" + launcher + "\""); // RelaunchCommand
            Write(store, 3, icon + ",0"); // RelaunchIconResource
            Write(store, 4, "BlitzBlock"); // RelaunchDisplayNameResource
            Write(store, 5, AppId); // Set identity last so Explorer refreshes with the relaunch properties.
            return true;
        }
        catch (COMException) { return false; }
        finally { if (store != null) Marshal.ReleaseComObject(store); }
    }

    internal static void SetShortcut(string path)
    {
        var link = new ShellLink();
        try
        {
            var file = (IPersistFile)link;
            file.Load(path, 2);
            var store = (PropertyStore)link;
            Write(store, 5, AppId);
            store.Commit();
            file.Save(path, true);
        }
        catch (COMException) {} // The ordinary shortcut still works.
        finally { Marshal.ReleaseComObject(link); }
    }

    private static void Write(PropertyStore store, uint id, string text)
    {
        var key = new PropertyKey { Format = AppModel, Id = id };
        var value = new PropertyValue { Type = 31, Text = Marshal.StringToCoTaskMemUni(text) };
        try { store.SetValue(ref key, ref value); }
        finally { Marshal.FreeCoTaskMem(value.Text); }
    }
}
