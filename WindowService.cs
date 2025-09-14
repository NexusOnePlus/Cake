using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cake;

public class WindowService
{
    public List<WindowItem> EnumerateWindowsWithIcons()
    {
        var list = new List<WindowItem>();
        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (!IsAppWindow(hWnd)) return true;

            string title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            var wi = new WindowItem { Hwnd = hWnd, Title = title };

            var (type, identifier) = GetIdentifierForWindow(hWnd);
            if (!string.IsNullOrEmpty(identifier))
            {
                wi.Identifier = identifier;
                wi.Icon = (type == AppType.Aumid)
                    ? GetIconFromAumid(identifier, out _)
                    : GetIconFromPath(identifier);
            }

            wi.Icon ??= GetSystemIcon(hWnd);
            list.Add(wi);

            return true;
        }, IntPtr.Zero);

        return list;
    }

    private (AppType, string?) GetIdentifierForWindow(IntPtr hWnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return (AppType.Path, null);

            using var proc = Process.GetProcessById((int)pid);
            string? path = proc.MainModule?.FileName;

            IntPtr handle = NativeMethods.OpenProcess(NativeMethods.ProcessAccessFlags.QueryLimitedInformation, false, (int)pid);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    int len = 1024;
                    var sb = new StringBuilder(len);
                    if (NativeMethods.GetApplicationUserModelId(handle, ref len, sb) == 0)
                        return (AppType.Aumid, sb.ToString());
                }
                finally { NativeMethods.CloseHandle(handle); }
            }

            return (AppType.Path, path);
        }
        catch { return (AppType.Path, null); }
    }

    private bool IsAppWindow(IntPtr hWnd)
    {

        if (!NativeMethods.IsWindowVisible(hWnd)) return false;
        if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return false;

        long exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;

        int cloaked = 0;
        NativeMethods.DwmGetWindowAttribute(
            hWnd,
            (int)NativeMethods.DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
            ref cloaked,
            Marshal.SizeOf<int>());

        if (cloaked != 0) return false;

        StringBuilder className = new(256);
        NativeMethods.GetClassName(hWnd, className, className.Capacity);
        if (className.ToString().Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            return false;

        string title = GetWindowTitle(hWnd);
        if (string.IsNullOrWhiteSpace(title)) return false;

        return true;
    }


    private string GetWindowTitle(IntPtr hWnd)
    {
        int len = NativeMethods.GetWindowTextLength(hWnd);
        if (len == 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private ImageSource? GetIconFromAumid(string aumid, out string? title)
    {
        title = null;
        NativeMethods.IShellItem2? shellItem = null;
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            if (NativeMethods.SHCreateItemInKnownFolder(NativeMethods.AppsFolder, 0, aumid,
                    typeof(NativeMethods.IShellItem2).GUID, out shellItem) != 0 || shellItem == null)
                return null;

            var pkey = NativeMethods.PKEY_ItemNameDisplay;
            if (shellItem.GetString(ref pkey, out string displayName) == 0)
                title = displayName;

            var imageFactory = (NativeMethods.IShellItemImageFactory)shellItem;
            var size = new NativeMethods.SIZE { cx = 256, cy = 256 };
            if (imageFactory.GetImage(size, NativeMethods.SIIGBF.ICONONLY, out hBitmap) == 0 && hBitmap != IntPtr.Zero)
            {
                var bmp = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                return bmp;
            }
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (shellItem != null) Marshal.ReleaseComObject(shellItem);
        }
        return null;
    }

    private BitmapSource? GetIconFromPath(string path, int size = 256)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            Guid iid = typeof(NativeMethods.IShellItem2).GUID;
            NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var shellItem);
            if (shellItem is NativeMethods.IShellItemImageFactory factory)
            {
                var sz = new NativeMethods.SIZE { cx = size, cy = size };
                if (factory.GetImage(sz, NativeMethods.SIIGBF.ICONONLY, out hBitmap) == 0 && hBitmap != IntPtr.Zero)
                {
                    var bmp = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    return bmp;
                }
            }
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
        }
        return null;
    }

    private BitmapSource? GetSystemIcon(IntPtr hWnd)
    {
        IntPtr hIcon = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_BIG, IntPtr.Zero);
        if (hIcon == IntPtr.Zero)
            hIcon = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL2, IntPtr.Zero);

        if (hIcon != IntPtr.Zero)
        {
            var bmp = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            NativeMethods.DestroyIcon(hIcon);
            return bmp;
        }
        return null;
    }
}
