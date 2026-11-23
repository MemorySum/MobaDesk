using System.Security.Cryptography;
using System.Text;

namespace MobaDesk.Services;

/// <summary>
/// Encrypts secrets with Windows DPAPI (current user). The blob format matches the one
/// mstsc expects for "password 51:b:", so the same protector can feed RDP files.
/// </summary>
public static class CredentialProtector
{
    public static string? Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return null;
        }

        try
        {
            var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(blob);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to protect credential", ex);
            return null;
        }
    }

    public static string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return null;
        }

        try
        {
            var blob = Convert.FromBase64String(protectedText);
            var plain = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to unprotect credential: {ex.Message}");
            return null;
        }
    }

    public static string ProtectToHex(string plainText)
    {
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), null, DataProtectionScope.CurrentUser);
        return Convert.ToHexString(blob);
    }
}
