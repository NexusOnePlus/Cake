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
    static WindowService()
    {
        NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        Debug.WriteLine("[HISTORY] WinEventHook windows history init.");
    }

    private static List<IntPtr> _windowHistory = new List<IntPtr>();
    private static NativeMethods.WinEventDelegate _winEventProc = new NativeMethods.WinEventDelegate(WinEventProc);

    private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND && hwnd != IntPtr.Zero)
        {
            UpdateWindowHistory(hwnd);
        }
    }

    public static void CleanupHistory()
    {
        int originalCount = _windowHistory.Count;
        _windowHistory = _windowHistory.Where(hwnd => NativeMethods.IsWindow(hwnd)).ToList();
        Debug.WriteLine($"[HISTORY] Cleaning completed. {originalCount - _windowHistory.Count} no valid windows deleted.");
    }


    private static void UpdateWindowHistory(IntPtr hwnd)
    {
        if (!IsAppWindow(hwnd)) return;

        var title = GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title)) return;

        _windowHistory.Remove(hwnd);

        _windowHistory.Insert(0, hwnd);

        Debug.WriteLine($"[HISTORY] Active window: '{title}' (HWND: {hwnd}). History: {_windowHistory.Count} windows.");

        if (_windowHistory.Count > 20)
        {
            _windowHistory.RemoveRange(20, _windowHistory.Count - 20);
        }
    }

    public List<WindowItem> EnumerateWindowsWithIcons()
    {
        var allWindows = new List<WindowItem>();
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
            allWindows.Add(wi);

            return true;
        }, IntPtr.Zero);

        return OrderWindowsByHistory(allWindows);
    }
    public static IntPtr GetLastActiveWindow()
    {
        CleanupHistory();

        if (_windowHistory.Count > 1)
        {
            Debug.WriteLine($"[HISTORY] Latest window active. Returning: {GetWindowTitle(_windowHistory[1])}");
            return _windowHistory[1];
        }

        Debug.WriteLine("[HISTORY] Not found latest active window.");
        return IntPtr.Zero;
    }
    private List<WindowItem> OrderWindowsByHistory(List<WindowItem> windows)
    {
        var orderedWindows = new List<WindowItem>();
        var usedHandles = new HashSet<IntPtr>();
        

        foreach (var hwnd in _windowHistory)
        {
            var window = windows.FirstOrDefault(w => w.Hwnd == hwnd);
            if (window != null && !usedHandles.Contains(hwnd))
            {
                if (NativeMethods.IsWindow(hwnd))
                {
                    orderedWindows.Add(window);
                    usedHandles.Add(hwnd);
                }
            }
        }

        var remainingWindows = windows.Where(w => !usedHandles.Contains(w.Hwnd)).ToList();
        orderedWindows.AddRange(remainingWindows);

        Debug.WriteLine($"[ORDER] Completed. {orderedWindows.Count} total windows ({usedHandles.Count} by history, {remainingWindows.Count} remaining).");

        return orderedWindows;
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

    public static bool IsAppWindow(IntPtr hWnd)
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


    private static string GetWindowTitle(IntPtr hWnd)
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
