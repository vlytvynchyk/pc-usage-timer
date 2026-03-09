using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PcUsageTimer;

public static class PinManager
{
    private static readonly string PinFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PcUsageTimer", "pin.hash");

    private static string _currentHash = "";

    public static bool HasPin => !string.IsNullOrEmpty(_currentHash);

    public static void Load()
    {
        try
        {
            if (File.Exists(PinFilePath))
                _currentHash = File.ReadAllText(PinFilePath).Trim();
        }
        catch { }
    }

    public static void Set(string pin)
    {
        _currentHash = Hash(pin);
        try
        {
            var dir = Path.GetDirectoryName(PinFilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(PinFilePath, _currentHash);
        }
        catch { }
    }

    public static bool Validate(string pin)
    {
        if (!HasPin) return false;
        return Hash(pin) == _currentHash;
    }

    private static string Hash(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin + "PcUsageTimer"));
        return Convert.ToHexString(bytes);
    }
}
