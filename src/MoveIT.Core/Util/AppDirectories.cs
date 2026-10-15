using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace MoveIT.Core.Util;

[SupportedOSPlatform("windows")]
public static class AppDirectories
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MoveIT");

    public static string DefaultLogs => Path.Combine(Root, "Logs");

    public static string DefaultBackups => Path.Combine(Root, "Backups");

    /// <summary>Creates the directory. Anything under %ProgramData%\MoveIT is readable only by SYSTEM and Administrators.</summary>
    public static string Ensure(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(Root, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(Root))
        {
            CreateRestrictedRoot();
        }

        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private static void CreateRestrictedRoot()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sidType in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sidType, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        new DirectoryInfo(Root).Create(security);
    }
}
