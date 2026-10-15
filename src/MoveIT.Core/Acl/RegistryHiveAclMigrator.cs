using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using MoveIT.Core.Logging;
using MoveIT.Core.Migration;
using MoveIT.Core.Security;
using static MoveIT.Core.Native.NativeMethods;

namespace MoveIT.Core.Acl;

/// <summary>
/// Temporarily loads a user's registry hive (NTUSER.DAT or UsrClass.dat) and rewrites the
/// security descriptor of every key that references the source account.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryHiveAclMigrator(Sid source, Sid target, AclMode mode, bool dryRun, Logger log)
{
    private const uint RequestedInformation = (uint)(SecurityInformation.Owner | SecurityInformation.Group | SecurityInformation.Dacl);
    private const int MaxKeyNameLength = 256;

    private long _scanned;
    private long _changed;

    public AclMigrationStats Run(string hivePath)
    {
        var stopwatch = Stopwatch.StartNew();
        var mountName = "MoveIT_" + Guid.NewGuid().ToString("N")[..12];

        int rc = RegLoadKey(HKEY_USERS, mountName, hivePath);
        if (rc == ERROR_SHARING_VIOLATION)
        {
            throw new MigrationException($"{hivePath} is in use. The user is probably still signed in; sign them out or restart, then try again.");
        }
        if (rc != ERROR_SUCCESS)
        {
            throw new MigrationException($"Could not load the registry hive {hivePath}: {new Win32Exception(rc).Message}");
        }

        var failures = new FailureRecorder(log, Path.GetFileName(hivePath));
        try
        {
            Walk(mountName, failures);
        }
        finally
        {
            Unload(mountName);
        }

        failures.Summarize();
        return new AclMigrationStats(_scanned, _changed, failures.Count, 0, stopwatch.Elapsed);
    }

    private void Walk(string mountName, FailureRecorder failures)
    {
        var pending = new Stack<string>();
        pending.Push(mountName);
        var nameBuffer = new char[MaxKeyNameLength];

        while (pending.Count > 0)
        {
            var path = pending.Pop();
            _scanned++;

            if (OpenForRead(path, out var key) is var openError && openError != ERROR_SUCCESS)
            {
                failures.Record(DisplayPath(path, mountName), new Win32Exception(openError).Message);
                continue;
            }

            using (key)
            {
                try
                {
                    var change = SecurityDescriptorRewriter.Rewrite(GetSecurity(key), source, target, mode);
                    if (change is not null)
                    {
                        if (!dryRun)
                        {
                            WriteSecurity(path, change);
                        }
                        _changed++;
                    }
                }
                catch (Exception e) when (e is Win32Exception or FormatException or InvalidOperationException)
                {
                    failures.Record(DisplayPath(path, mountName), e.Message);
                }

                for (uint index = 0; ; index++)
                {
                    uint length = (uint)nameBuffer.Length;
                    int rc = RegEnumKeyEx(key, index, nameBuffer, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (rc == ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }
                    if (rc != ERROR_SUCCESS)
                    {
                        failures.Record(DisplayPath(path, mountName), $"cannot list subkeys: {new Win32Exception(rc).Message}");
                        break;
                    }
                    pending.Push(path + "\\" + new string(nameBuffer, 0, (int)length));
                }
            }
        }
    }

    private static int OpenForRead(string path, out SafeRegistryHandle key)
    {
        int rc = RegCreateKeyEx(HKEY_USERS, path, 0, null, REG_OPTION_BACKUP_RESTORE, 0, IntPtr.Zero, out key, out _);
        if (rc == ERROR_SUCCESS)
        {
            return rc;
        }

        key.Dispose();
        return RegOpenKeyEx(HKEY_USERS, path, 0, READ_CONTROL | KEY_ENUMERATE_SUB_KEYS, out key);
    }

    private static byte[] GetSecurity(SafeRegistryHandle key)
    {
        uint length = 1024;
        while (true)
        {
            var buffer = new byte[length];
            int rc = RegGetKeySecurity(key, RequestedInformation, buffer, ref length);
            if (rc == ERROR_SUCCESS)
            {
                return buffer;
            }
            if (rc != ERROR_INSUFFICIENT_BUFFER || length <= buffer.Length)
            {
                throw new Win32Exception(rc);
            }
        }
    }

    /// <summary>
    /// Backup/restore opens do not guarantee WRITE_DAC on registry keys, so write through a normal
    /// handle. If the key's DACL locks administrators out, take ownership first (owners can always
    /// change the DACL) and put the intended owner back in the same final write.
    /// </summary>
    private static void WriteSecurity(string path, SecurityDescriptorChange change)
    {
        uint access = 0;
        if (change.Changed.HasFlag(SecurityInformation.Dacl))
        {
            access |= WRITE_DAC;
        }
        if ((change.Changed & (SecurityInformation.Owner | SecurityInformation.Group)) != 0)
        {
            access |= WRITE_OWNER;
        }

        int rc = TrySet(path, access, change.Changed, change.Descriptor);
        if (rc == ERROR_SUCCESS)
        {
            return;
        }
        if (rc != ERROR_ACCESS_DENIED)
        {
            throw new Win32Exception(rc);
        }

        rc = TrySet(path, WRITE_OWNER, SecurityInformation.Owner, SecurityDescriptorRewriter.CreateOwnerOnly(Sid.BuiltinAdministrators));
        if (rc != ERROR_SUCCESS)
        {
            throw new Win32Exception(rc);
        }

        rc = TrySet(path, WRITE_DAC | WRITE_OWNER, change.Changed | SecurityInformation.Owner, change.Descriptor);
        if (rc != ERROR_SUCCESS)
        {
            throw new Win32Exception(rc);
        }
    }

    private static int TrySet(string path, uint access, SecurityInformation information, byte[] descriptor)
    {
        int rc = RegOpenKeyEx(HKEY_USERS, path, 0, access, out var key);
        using (key)
        {
            return rc != ERROR_SUCCESS ? rc : RegSetKeySecurity(key, (uint)information, descriptor);
        }
    }

    private void Unload(string mountName)
    {
        for (int attempt = 1; ; attempt++)
        {
            int rc = RegUnLoadKey(HKEY_USERS, mountName);
            if (rc == ERROR_SUCCESS)
            {
                return;
            }

            if (attempt == 5)
            {
                log.Error($"Could not unload the temporary registry hive HKEY_USERS\\{mountName} ({new Win32Exception(rc).Message}). Restart the computer before anyone signs in to this profile.");
                return;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(TimeSpan.FromMilliseconds(500 * attempt));
        }
    }

    private static string DisplayPath(string path, string mountName) =>
        path.Length > mountName.Length ? path[(mountName.Length + 1)..] : "(root)";
}
