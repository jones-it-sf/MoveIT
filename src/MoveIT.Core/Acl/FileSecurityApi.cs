using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using MoveIT.Core.Security;
using static MoveIT.Core.Native.NativeMethods;

namespace MoveIT.Core.Acl;

[SupportedOSPlatform("windows")]
internal static class FileSecurityApi
{
    private const uint RequestedInformation = (uint)(SecurityInformation.Owner | SecurityInformation.Group | SecurityInformation.Dacl);

    /// <summary>
    /// Opens a file or folder for its security descriptor only. Backup semantics plus the backup and
    /// restore privileges bypass the object's own permissions; reparse points are opened, not followed.
    /// </summary>
    public static SafeFileHandle Open(string path, bool forWrite, out int error)
    {
        uint access = forWrite ? READ_CONTROL | WRITE_DAC | WRITE_OWNER : READ_CONTROL;
        var handle = CreateFile(
            ToExtendedPath(path),
            access,
            FILE_SHARE_ALL,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);
        error = handle.IsInvalid ? Marshal.GetLastPInvokeError() : ERROR_SUCCESS;
        return handle;
    }

    public static byte[] GetSecurity(SafeFileHandle handle)
    {
        var buffer = new byte[1024];
        while (true)
        {
            if (GetKernelObjectSecurity(handle, RequestedInformation, buffer, (uint)buffer.Length, out var needed))
            {
                return buffer;
            }

            int error = Marshal.GetLastPInvokeError();
            if (error != ERROR_INSUFFICIENT_BUFFER || needed <= buffer.Length)
            {
                throw new Win32Exception(error);
            }
            buffer = new byte[needed];
        }
    }

    public static void SetSecurity(SafeFileHandle handle, SecurityDescriptorChange change)
    {
        if (!SetKernelObjectSecurity(handle, (uint)change.Changed, change.Descriptor))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public static byte[] ReadDescriptor(string path)
    {
        using var handle = Open(path, forWrite: false, out var error);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(error, $"Cannot open {path}: {new Win32Exception(error).Message}");
        }
        return GetSecurity(handle);
    }

    public static uint? GetReparseTag(SafeFileHandle handle) =>
        GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out var info, (uint)Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>())
            ? info.ReparseTag
            : null;

    public static string ToExtendedPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }
}
