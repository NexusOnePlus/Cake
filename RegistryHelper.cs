﻿using Microsoft.Win32;
using System.Diagnostics;

namespace Cake;

public static class RegistryHelper
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string RegistryValueName = "AltTabSettings";
    private static object? _originalValue;
    private static bool _isHooked = false;

    public static void DisableSystemAltTab()
    {
        if (_isHooked) return;

        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
            {
                if (key != null)
                {
                    _originalValue = key.GetValue(RegistryValueName);
                    key.SetValue(RegistryValueName, 1, RegistryValueKind.DWord);
                    _isHooked = true;
                    Debug.WriteLine("[REGISTRY] System Alt+Tab DISABLED.");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"[REGISTRY] FAILED to disable Alt+Tab: {ex.Message}");
        }
    }

    public static void RestoreSystemAltTab()
    {
        if (!_isHooked) return;

        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
            {
                if (key != null)
                {
                    if (_originalValue != null)
                    {
                        key.SetValue(RegistryValueName, _originalValue);
                    }
                    else
                    {
                        key.DeleteValue(RegistryValueName, false);
                    }
                    _isHooked = false;
                    Debug.WriteLine("[REGISTRY] System Alt+Tab RESTORED.");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"[REGISTRY] FAILED to restore Alt+Tab: {ex.Message}");
        }
    }
}