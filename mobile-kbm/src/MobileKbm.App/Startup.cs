using Microsoft.Win32;

namespace MobileKbm.App;

/// <summary>
/// "Start with Windows": a value under the current user's Run key, so no admin rights, and it
/// shows up in Task Manager's Startup tab. Disabling it there does not remove the value but
/// marks it in StartupApproved, so that is read too — and cleared when the user turns it on here.
/// </summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Mobile KBM";

    private static string? Command =>
        Environment.ProcessPath is { } path ? $"\"{path}\"" : null;

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (run?.GetValue(ValueName) is not string) return false;

                // Task Manager's flag: an odd first byte means disabled there.
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } flag || (flag[0] & 1) == 0;
            }
            catch (Exception ex) when (IsRegistryError(ex))
            {
                return false;
            }
        }
    }

    /// <summary>Turns it on or off. Returns false when the registry refused.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
            {
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            if (!enabled)
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            if (Command is not { } command) return false;
            run.SetValue(ValueName, command);
            return true;
        }
        catch (Exception ex) when (IsRegistryError(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// The app is portable: if it is on and the folder has moved since, point the entry here.
    /// Called on every launch.
    /// </summary>
    public static void Refresh()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(ValueName) is not string current || Command is not { } command) return;
            if (!string.Equals(current, command, StringComparison.OrdinalIgnoreCase)) run.SetValue(ValueName, command);
        }
        catch (Exception ex) when (IsRegistryError(ex))
        {
            // Leave it as it is.
        }
    }

    private static bool IsRegistryError(Exception ex) =>
        ex is System.Security.SecurityException or UnauthorizedAccessException or IOException;
}
