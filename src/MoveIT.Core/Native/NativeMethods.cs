using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MoveIT.Core.Native;

internal static class NativeMethods
{
    internal const int ERROR_SUCCESS = 0;
    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_SHARING_VIOLATION = 32;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int ERROR_NO_MORE_ITEMS = 259;
    internal const int ERROR_NOT_ALL_ASSIGNED = 1300;
    internal const int ERROR_MEMBER_NOT_IN_ALIAS = 1377;
    internal const int ERROR_MEMBER_IN_ALIAS = 1378;
    internal const int NERR_BadUsername = 2202;
    internal const int NERR_GroupNotFound = 2220;
    internal const int NERR_UserNotFound = 2221;
    internal const int NERR_UserExists = 2224;
    internal const int NERR_PasswordTooShort = 2245;

    internal const uint READ_CONTROL = 0x00020000;
    internal const uint WRITE_DAC = 0x00040000;
    internal const uint WRITE_OWNER = 0x00080000;
    internal const uint KEY_ENUMERATE_SUB_KEYS = 0x0008;

    internal const uint FILE_SHARE_ALL = 0x7;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    internal const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    internal const int FileAttributeTagInfo = 9;
    internal const uint IO_REPARSE_TAG_NAME_SURROGATE = 0x20000000;

    internal const uint TOKEN_QUERY = 0x0008;
    internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    internal const uint SE_PRIVILEGE_ENABLED = 0x2;

    internal const uint REG_OPTION_BACKUP_RESTORE = 0x4;
    internal static readonly IntPtr HKEY_USERS = new(unchecked((int)0x80000003));

    internal const uint USER_PRIV_USER = 1;
    internal const uint UF_SCRIPT = 0x1;
    internal const uint UF_ACCOUNTDISABLE = 0x2;
    internal const uint UF_NORMAL_ACCOUNT = 0x200;
    internal const int MAX_PREFERRED_LENGTH = -1;

    internal const int SidTypeUser = 1;
    internal const int SidTypeDomain = 3;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_PRIVILEGES_SINGLE
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_ATTRIBUTE_TAG_INFO
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct USER_INFO_1
    {
        public string usri1_name;
        public string? usri1_password;
        public uint usri1_password_age;
        public uint usri1_priv;
        public string? usri1_home_dir;
        public string? usri1_comment;
        public uint usri1_flags;
        public string? usri1_script_path;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct USER_INFO_1011
    {
        public string usri1011_full_name;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LOCALGROUP_MEMBERS_INFO_0
    {
        public IntPtr lgrmi0_sid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LOCALGROUP_MEMBERS_INFO_2
    {
        public IntPtr lgrmi2_sid;
        public int lgrmi2_sidusage;
        public IntPtr lgrmi2_domainandname;
    }

    internal sealed class SafeTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeTokenHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FILE_ATTRIBUTE_TAG_INFO fileInformation,
        uint bufferSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeTokenHandle tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupPrivilegeValue(string? systemName, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustTokenPrivileges(
        SafeTokenHandle tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES_SINGLE newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetKernelObjectSecurity(
        SafeHandle handle,
        uint requestedInformation,
        byte[]? securityDescriptor,
        uint length,
        out uint lengthNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetKernelObjectSecurity(SafeHandle handle, uint securityInformation, byte[] securityDescriptor);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupAccountSid(
        string? systemName,
        byte[] sid,
        char[]? name,
        ref uint nameLength,
        char[]? domainName,
        ref uint domainNameLength,
        out int sidUse);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupAccountName(
        string? systemName,
        string accountName,
        byte[]? sid,
        ref uint sidLength,
        char[]? domainName,
        ref uint domainNameLength,
        out int sidUse);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegLoadKey(IntPtr key, string subKey, string file);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegUnLoadKey(IntPtr key, string subKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegCreateKeyEx(
        IntPtr key,
        string subKey,
        uint reserved,
        string? keyClass,
        uint options,
        uint desiredAccess,
        IntPtr securityAttributes,
        out SafeRegistryHandle result,
        out uint disposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegOpenKeyEx(IntPtr key, string subKey, uint options, uint desiredAccess, out SafeRegistryHandle result);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegEnumKeyEx(
        SafeRegistryHandle key,
        uint index,
        char[] name,
        ref uint nameLength,
        IntPtr reserved,
        IntPtr keyClass,
        IntPtr keyClassLength,
        IntPtr lastWriteTime);

    [DllImport("advapi32.dll")]
    internal static extern int RegGetKeySecurity(SafeRegistryHandle key, uint securityInformation, byte[]? securityDescriptor, ref uint length);

    [DllImport("advapi32.dll")]
    internal static extern int RegSetKeySecurity(SafeRegistryHandle key, uint securityInformation, byte[] securityDescriptor);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetUserAdd(string? serverName, uint level, ref USER_INFO_1 buffer, out uint parameterError);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetUserDel(string? serverName, string userName);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetUserGetInfo(string? serverName, string userName, uint level, out IntPtr buffer);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetUserSetInfo(string? serverName, string userName, uint level, ref USER_INFO_1011 buffer, out uint parameterError);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetLocalGroupAddMembers(string? serverName, string groupName, uint level, ref LOCALGROUP_MEMBERS_INFO_0 buffer, uint totalEntries);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetLocalGroupDelMembers(string? serverName, string groupName, uint level, ref LOCALGROUP_MEMBERS_INFO_0 buffer, uint totalEntries);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetLocalGroupGetMembers(
        string? serverName,
        string localGroupName,
        uint level,
        out IntPtr buffer,
        int preferredMaximumLength,
        out uint entriesRead,
        out uint totalEntries,
        IntPtr resumeHandle);

    [DllImport("netapi32.dll")]
    internal static extern int NetApiBufferFree(IntPtr buffer);
}
