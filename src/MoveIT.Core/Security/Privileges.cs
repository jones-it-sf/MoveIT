using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static MoveIT.Core.Native.NativeMethods;

namespace MoveIT.Core.Security;

[SupportedOSPlatform("windows")]
public static class Privileges
{
    public const string Backup = "SeBackupPrivilege";
    public const string Restore = "SeRestorePrivilege";
    public const string TakeOwnership = "SeTakeOwnershipPrivilege";

    /// <summary>Backup, restore and take-ownership let MoveIT read and rewrite permissions it is not granted.</summary>
    public static void EnableMigrationPrivileges() => Enable(Backup, Restore, TakeOwnership);

    public static void Enable(params string[] names)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not open the process token.");
        }

        using (token)
        {
            foreach (var name in names)
            {
                if (!LookupPrivilegeValue(null, name, out var luid))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Unknown privilege {name}.");
                }

                var privileges = new TOKEN_PRIVILEGES_SINGLE { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not enable {name}.");
                }

                if (Marshal.GetLastPInvokeError() == ERROR_NOT_ALL_ASSIGNED)
                {
                    throw new UnauthorizedAccessException(
                        $"This process does not hold {name}. Run MoveIT from an elevated prompt (Run as administrator) or as SYSTEM.");
                }
            }
        }
    }
}
