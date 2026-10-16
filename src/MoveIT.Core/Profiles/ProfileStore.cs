using System.Runtime.Versioning;
using Microsoft.Win32;
using MoveIT.Core.Accounts;
using MoveIT.Core.Migration;
using MoveIT.Core.Security;

namespace MoveIT.Core.Profiles;

/// <summary>Reads and updates the profile registrations Windows uses to pick a user's profile folder at sign-in.</summary>
[SupportedOSPlatform("windows")]
public static class ProfileStore
{
    public const string ProfileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    public const string ProfileGuidPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileGuid";

    public static string ProfileListKey(Sid sid) => $@"{ProfileListPath}\{sid.Value}";

    public static string ProfileGuidKey(Guid guid) => $@"{ProfileGuidPath}\{guid:B}";

    public static IEnumerable<string> GetHivePaths(string profilePath) =>
    [
        Path.Combine(profilePath, "NTUSER.DAT"),
        Path.Combine(profilePath, "AppData", "Local", "Microsoft", "Windows", "UsrClass.dat"),
    ];

    public static IReadOnlyList<ProfileEntry> GetProfiles(bool includeSystem)
    {
        var machineSid = AccountResolver.GetMachineSid();
        using var hklm = OpenLocalMachine();
        using var list = hklm.OpenSubKey(ProfileListPath);
        if (list is null)
        {
            return [];
        }

        var result = new List<ProfileEntry>();
        foreach (var name in list.GetSubKeyNames())
        {
            bool isBackup = name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
            if (!Sid.TryParse(isBackup ? name[..^4] : name, out var sid))
            {
                continue;
            }

            var kind = SidClassifier.Classify(sid, machineSid);
            if (kind == SidKind.System && !includeSystem)
            {
                continue;
            }

            using var key = list.OpenSubKey(name);
            if (key?.GetValue("ProfileImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string rawPath)
            {
                continue;
            }

            var path = Environment.ExpandEnvironmentVariables(rawPath);
            Guid? profileGuid = key.GetValue("Guid") is string guidText && Guid.TryParse(guidText, out var guid) ? guid : null;
            var hive = Path.Combine(path, "NTUSER.DAT");
            DateTime? lastUsed = File.Exists(hive) ? File.GetLastWriteTime(hive) : null;

            result.Add(new ProfileEntry(sid, path, AccountResolver.TryGetAccountName(sid), kind, IsLoaded(sid), isBackup, profileGuid, lastUsed));
        }

        return [.. result.OrderBy(p => p.ProfileImagePath, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A loaded hive under HKEY_USERS means the user is signed in or something still holds their profile.</summary>
    public static bool IsLoaded(Sid sid)
    {
        using var key = Registry.Users.OpenSubKey(sid.Value);
        return key is not null;
    }

    /// <summary>
    /// Verifies that Windows is not holding a profile hive open. This catches loaded profiles that
    /// are not visible under the expected HKEY_USERS SID, including stale or unusual Entra sessions.
    /// </summary>
    public static bool IsHiveInUse(string hivePath)
    {
        try
        {
            using var stream = new FileStream(
                hivePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static bool ProfileGuidExists(Guid guid)
    {
        using var hklm = OpenLocalMachine();
        using var key = hklm.OpenSubKey(ProfileGuidKey(guid));
        return key is not null;
    }

    /// <summary>
    /// Points the target account at the source's profile folder and removes the source registration,
    /// so the next sign-in by the target loads the existing profile instead of creating a new one.
    /// </summary>
    public static void Reassign(Sid source, Sid target, bool replaceExistingTarget)
    {
        using var hklm = OpenLocalMachine();
        using var list = hklm.OpenSubKey(ProfileListPath, writable: true)
            ?? throw new MigrationException("The ProfileList registry key is missing.");

        IReadOnlyList<RegistryValueData> sourceValues;
        using (var sourceKey = list.OpenSubKey(source.Value) ?? throw new MigrationException($"The profile registration for {source} disappeared during the migration."))
        {
            sourceValues = [.. sourceKey.GetValueNames().Select(name => new RegistryValueData(
                name,
                sourceKey.GetValueKind(name),
                sourceKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!))];
        }

        var targetValues = ProfileListValues.ForLocalTarget(sourceValues, target);

        using (var existing = list.OpenSubKey(target.Value))
        {
            if (existing is not null && !replaceExistingTarget)
            {
                throw new MigrationException($"{target} already has a profile registration.");
            }
        }
        list.DeleteSubKeyTree(target.Value, throwOnMissingSubKey: false);

        using (var targetKey = list.CreateSubKey(target.Value, writable: true))
        {
            foreach (var value in targetValues)
            {
                targetKey.SetValue(value.Name, value.Data, value.Kind);
            }
        }

        list.DeleteSubKeyTree(source.Value);
    }

    /// <summary>Deletes a registration only if it still points at the expected folder.</summary>
    public static bool DeleteEntryIfPathMatches(Sid sid, string expectedPath)
    {
        using var hklm = OpenLocalMachine();
        using var list = hklm.OpenSubKey(ProfileListPath, writable: true);
        using var key = list?.OpenSubKey(sid.Value);
        if (list is null || key?.GetValue("ProfileImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string path)
        {
            return false;
        }

        if (!string.Equals(
            ProfileResolver.NormalizePath(Environment.ExpandEnvironmentVariables(path)),
            ProfileResolver.NormalizePath(expectedPath),
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        list.DeleteSubKeyTree(sid.Value);
        return true;
    }

    public static void DeleteProfileGuid(Guid guid, Sid owner)
    {
        using var hklm = OpenLocalMachine();
        using var guids = hklm.OpenSubKey(ProfileGuidPath, writable: true);
        using var key = guids?.OpenSubKey(guid.ToString("B"));
        if (guids is not null && key?.GetValue("SidString") is string sidString && sidString.Equals(owner.Value, StringComparison.OrdinalIgnoreCase))
        {
            guids.DeleteSubKeyTree(guid.ToString("B"));
        }
    }

    private static RegistryKey OpenLocalMachine() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
}
