using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;


namespace Cake;
class KeyboardHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;

    private static LowLevelKeyboardProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;

    private static bool altPressed = false;
    private static bool selectorVisible = false;
    private static MainWindow? selector;
    private static int currentIndex = -1;
    private static bool isFirstTabInSequence = true;
    public static void Start()
    {
        _hookID = SetHook(_proc);
    }

    public static void SetSelectorVisibility(bool isVisible)
    {
        selectorVisible = isVisible;
    }

    public static void Stop()
    {
        UnhookWindowsHookEx(_hookID);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule)
        {
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    const int VK_MENU = 0x12;
    const int VK_LMENU = 0xA4;
    const int VK_RMENU = 0xA5;
    const int VK_TAB = 0x09;

    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;


    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);

            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
            {
               // Debug.WriteLine($"[KEYDOWN] vkCode={vkCode}");

                if (vkCode == VK_MENU || vkCode == VK_LMENU || vkCode == VK_RMENU)
                {
                    altPressed = true;
                    isFirstTabInSequence = true;
                    //   Debug.WriteLine("[STATE] ALT pressed");
                }

                if (altPressed && vkCode == VK_TAB)
                {
                    Debug.WriteLine("[ACTION] ALT+TAB detected");

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (selector == null)
                        {
                            selector = new MainWindow();
                            Debug.WriteLine("[UI] New Window");
                        }

                        if (!selectorVisible)
                        {
                            currentIndex = 1;
                            selector.PrepareAndShow(currentIndex);
                            Debug.WriteLine($"[UI] Selector opened, index ready in {currentIndex}");
                        }
                        else
                        {
                            currentIndex = (currentIndex + 1) % selector.WindowsCount;
                            selector.Highlight(currentIndex);
                            Debug.WriteLine($"[UI] Selector moved to index {currentIndex}");
                        }
                    });


                    return (IntPtr)1;
                }
            }
            else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
            {
            //    Debug.WriteLine($"[KEYUP] vkCode={vkCode}");

                if (vkCode == VK_MENU || vkCode == VK_LMENU || vkCode == VK_RMENU)
                {
                    altPressed = false;
                    Debug.WriteLine("[STATE] ALT released");

                    if (selectorVisible && selector != null)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            selector.ActivateWindow(currentIndex);
                            selector.Hide();
                            Debug.WriteLine("[UI] Selector closed, active window.");
                        });
                    }
                    isFirstTabInSequence = true;
                }
            }
        }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }


    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook,
        LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
        IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
