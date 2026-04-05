using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace DeployPaladin.Core;

public class RegistryService
{
    private RegistryKey? GetRoot(string hive)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

        return hive.ToUpper() switch
        {
            "HKCU" => Registry.CurrentUser,
            "HKLM" => Registry.LocalMachine,
            _ => null
        };
    }

    public void CreateKey(string hive, string key, string value, string data)
    {
        var root = GetRoot(hive);
        if (root == null) return;

        key = key.Replace('/', '\\');

        try
        {
            using var subkey = root.CreateSubKey(key, true);
            subkey.SetValue(value, data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Registry Error: {ex.Message}");
        }
    }

    public bool KeyExists(string hive, string key)
    {
        var root = GetRoot(hive);
        if (root == null) return false;

        key = key.Replace('/', '\\');
        using var subkey = root.OpenSubKey(key, false);
        return subkey != null;
    }

    public string? ReadValue(string hive, string key, string value)
    {
        var root = GetRoot(hive);
        if (root == null) return null;

        key = key.Replace('/', '\\');
        try
        {
            using var subkey = root.OpenSubKey(key, false);
            return subkey?.GetValue(value)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public void DeleteKey(string hive, string key)
    {
        var root = GetRoot(hive);
        if (root == null) return;

        key = key.Replace('/', '\\');
        try
        {
            root.DeleteSubKeyTree(key, false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Registry Delete Error: {ex.Message}");
        }
    }
}
