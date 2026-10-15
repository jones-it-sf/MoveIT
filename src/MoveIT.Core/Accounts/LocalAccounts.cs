using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MoveIT.Core.Migration;
using MoveIT.Core.Security;
using MoveIT.Core.Util;
using static MoveIT.Core.Native.NativeMethods;

namespace MoveIT.Core.Accounts;

[SupportedOSPlatform("windows")]
public static class LocalAccounts
{
    /// <summary>Returns the account's SID, or null if no local account has that name.</summary>
    public static Sid? TryGetSid(string name)
    {
        if (!Exists(name))
        {
            return null;
        }

        if (AccountResolver.TryLookupName($"{Environment.MachineName}\\{name}", out var sid, out var use) && use == SidTypeUser)
        {
            return sid;
        }

        throw new MigrationException($"Local account '{name}' exists but its SID could not be resolved.");
    }

    public static bool Exists(string name)
    {
        int rc = NetUserGetInfo(null, name, 0, out var buffer);
        if (buffer != IntPtr.Zero)
        {
            NetApiBufferFree(buffer);
        }

        return rc switch
        {
            ERROR_SUCCESS => true,
            NERR_UserNotFound => false,
            _ => throw new MigrationException($"Could not look up local account '{name}': {Describe(rc)}"),
        };
    }

    public static Sid Create(string name, string password, string? fullName, string comment)
    {
        var info = new USER_INFO_1
        {
            usri1_name = name,
            usri1_password = password,
            usri1_priv = USER_PRIV_USER,
            usri1_comment = comment,
            usri1_flags = UF_SCRIPT | UF_NORMAL_ACCOUNT,
        };

        int rc = NetUserAdd(null, 1, ref info, out _);
        if (rc != ERROR_SUCCESS)
        {
            throw new MigrationException($"Could not create local account '{name}': {Describe(rc)}");
        }

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            SetFullName(name, fullName);
        }

        return TryGetSid(name) ?? throw new MigrationException($"Local account '{name}' was created but cannot be found.");
    }

    public static void SetFullName(string name, string fullName)
    {
        var info = new USER_INFO_1011 { usri1011_full_name = fullName };
        int rc = NetUserSetInfo(null, name, 1011, ref info, out _);
        if (rc != ERROR_SUCCESS)
        {
            throw new MigrationException($"Could not set the full name of '{name}': {Describe(rc)}");
        }
    }

    public static void Delete(string name)
    {
        int rc = NetUserDel(null, name);
        if (rc is not ERROR_SUCCESS and not NERR_UserNotFound)
        {
            throw new MigrationException($"Could not delete local account '{name}': {Describe(rc)}");
        }
    }

    public static void SetEnabled(string name, bool enabled)
    {
        int rc = NetUserGetInfo(null, name, 1, out var buffer);
        if (rc != ERROR_SUCCESS)
        {
            throw new MigrationException($"Could not read local account '{name}': {Describe(rc)}");
        }

        uint flags;
        try
        {
            flags = Marshal.PtrToStructure<USER_INFO_1>(buffer).usri1_flags;
        }
        finally
        {
            NetApiBufferFree(buffer);
        }

        flags = enabled ? flags & ~UF_ACCOUNTDISABLE : flags | UF_ACCOUNTDISABLE;
        var update = new USER_INFO_1008 { usri1008_flags = flags };
        rc = NetUserSetFlags(null, name, 1008, ref update, out _);
        if (rc != ERROR_SUCCESS)
        {
            throw new MigrationException($"Could not {(enabled ? "enable" : "disable")} local account '{name}': {Describe(rc)}");
        }
    }

    /// <summary>Returns false if the account was already a member.</summary>
    public static bool AddToAdministrators(Sid user)
    {
        int rc = ChangeAdministratorsMembership(user, add: true);
        if (rc == ERROR_MEMBER_IN_ALIAS)
        {
            return false;
        }

        if (rc != ERROR_SUCCESS)
        {
            throw new MigrationException($"Could not add {user} to the local Administrators group: {Describe(rc)}");
        }

        return true;
    }

    public static void RemoveFromAdministrators(Sid user)
    {
        int rc = ChangeAdministratorsMembership(user, add: false);
        if (rc is not ERROR_SUCCESS and not ERROR_MEMBER_NOT_IN_ALIAS)
        {
            throw new MigrationException($"Could not remove {user} from the local Administrators group: {Describe(rc)}");
        }
    }

    public static void RequirePasswordChange(string name)
    {
        var result = ProcessRunner.Run(
            Path.Combine(Environment.SystemDirectory, "net.exe"),
            ["user", name, "/logonpasswordchg:yes"],
            TimeSpan.FromSeconds(30));
        if (result.ExitCode != 0)
        {
            throw new MigrationException($"Could not require a password change for '{name}': {result.Output.Trim()}");
        }
    }

    /// <summary>Names of enabled local (not domain or Entra) accounts in the Administrators group.</summary>
    public static IReadOnlyList<string> GetEnabledLocalAdministrators()
    {
        int rc = NetLocalGroupGetMembers(null, AdministratorsGroupName(), 2, out var buffer, MAX_PREFERRED_LENGTH, out var entries, out _, IntPtr.Zero);
        if (rc != ERROR_SUCCESS)
        {
            throw new MigrationException($"Could not list the local Administrators group: {Describe(rc)}");
        }

        var result = new List<string>();
        try
        {
            int size = Marshal.SizeOf<LOCALGROUP_MEMBERS_INFO_2>();
            for (int i = 0; i < entries; i++)
            {
                var member = Marshal.PtrToStructure<LOCALGROUP_MEMBERS_INFO_2>(buffer + i * size);
                if (member.lgrmi2_sidusage != SidTypeUser)
                {
                    continue;
                }

                var fullName = Marshal.PtrToStringUni(member.lgrmi2_domainandname) ?? "";
                int separator = fullName.IndexOf('\\');
                if (separator < 0 || !fullName[..separator].Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var userName = fullName[(separator + 1)..];
                if (IsEnabled(userName))
                {
                    result.Add(userName);
                }
            }
        }
        finally
        {
            NetApiBufferFree(buffer);
        }

        return result;
    }

    internal static string Describe(int rc) => rc switch
    {
        NERR_UserExists => "the account already exists",
        NERR_UserNotFound => "the account does not exist",
        NERR_GroupNotFound => "the group does not exist",
        NERR_BadUsername => "the user name is not valid",
        NERR_PasswordTooShort => "the password does not meet this computer's password policy (length, complexity or history)",
        ERROR_ACCESS_DENIED => "access denied (run MoveIT elevated or as SYSTEM)",
        _ => $"{new Win32Exception(rc).Message} (error {rc})",
    };

    private static bool IsEnabled(string name)
    {
        int rc = NetUserGetInfo(null, name, 1, out var buffer);
        if (rc != ERROR_SUCCESS)
        {
            return false;
        }

        try
        {
            var info = Marshal.PtrToStructure<USER_INFO_1>(buffer);
            return (info.usri1_flags & UF_ACCOUNTDISABLE) == 0;
        }
        finally
        {
            NetApiBufferFree(buffer);
        }
    }

    private static int ChangeAdministratorsMembership(Sid user, bool add)
    {
        var binary = user.ToArray();
        var sidPointer = Marshal.AllocHGlobal(binary.Length);
        try
        {
            Marshal.Copy(binary, 0, sidPointer, binary.Length);
            var member = new LOCALGROUP_MEMBERS_INFO_0 { lgrmi0_sid = sidPointer };
            return add
                ? NetLocalGroupAddMembers(null, AdministratorsGroupName(), 0, ref member, 1)
                : NetLocalGroupDelMembers(null, AdministratorsGroupName(), 0, ref member, 1);
        }
        finally
        {
            Marshal.FreeHGlobal(sidPointer);
        }
    }

    // The group is renamed on non-English Windows, so resolve it from its well-known SID.
    private static string AdministratorsGroupName()
    {
        var name = AccountResolver.TryGetAccountName(Sid.BuiltinAdministrators) ?? "Administrators";
        int separator = name.IndexOf('\\');
        return separator >= 0 ? name[(separator + 1)..] : name;
    }
}
