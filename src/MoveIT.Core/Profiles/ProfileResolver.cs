using MoveIT.Core.Security;

namespace MoveIT.Core.Profiles;

/// <summary>
/// Finds the profile a technician means. Accepts a SID, an Entra object ID, a profile folder path,
/// an account name ("AzureAD\JohnSmith" or "JohnSmith") or, as a last resort, a profile folder name.
/// </summary>
public static class ProfileResolver
{
    public static ProfileEntry? Resolve(string spec, IReadOnlyList<ProfileEntry> profiles, out string? error)
    {
        spec = spec.Trim();
        var candidates = profiles.Where(p => !p.IsBackupEntry).ToList();

        if (Sid.TryParse(spec, out var sid))
        {
            return Single(candidates.Where(p => p.Sid == sid), spec, out error);
        }

        if (Guid.TryParse(spec, out var objectId))
        {
            var entraSid = Sid.FromEntraObjectId(objectId);
            return Single(candidates.Where(p => p.Sid == entraSid), spec, out error);
        }

        if (LooksLikePath(spec))
        {
            var normalized = NormalizePath(spec);
            return Single(
                candidates.Where(p => string.Equals(NormalizePath(p.ProfileImagePath), normalized, StringComparison.OrdinalIgnoreCase)),
                spec,
                out error);
        }

        List<ProfileEntry> matches = spec.Contains('\\')
            ? candidates.Where(p => string.Equals(p.AccountName, spec, StringComparison.OrdinalIgnoreCase)).ToList()
            : candidates.Where(p => string.Equals(AccountPart(p.AccountName), spec, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0 && !spec.Contains('\\'))
        {
            matches = candidates.Where(p => string.Equals(FolderName(p.ProfileImagePath), spec, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Single(matches, spec, out error);
    }

    public static bool LooksLikePath(string spec) =>
        (spec.Length >= 3 && char.IsAsciiLetter(spec[0]) && spec[1] == ':' && spec[2] is '\\' or '/')
        || spec.StartsWith(@"\\", StringComparison.Ordinal)
        || spec.Contains('%');

    public static string NormalizePath(string path) => path.Trim().Replace('/', '\\').TrimEnd('\\');

    private static string? AccountPart(string? accountName)
    {
        if (accountName is null)
        {
            return null;
        }
        int separator = accountName.IndexOf('\\');
        return separator >= 0 ? accountName[(separator + 1)..] : accountName;
    }

    private static string FolderName(string path)
    {
        var normalized = NormalizePath(path);
        int separator = normalized.LastIndexOf('\\');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private static ProfileEntry? Single(IEnumerable<ProfileEntry> matches, string spec, out string? error)
    {
        var list = matches.ToList();
        switch (list.Count)
        {
            case 1:
                error = null;
                return list[0];
            case 0:
                error = $"No user profile matches '{spec}'. Run 'moveit list' to see the profiles on this computer.";
                return null;
            default:
                var names = string.Join(", ", list.Select(p => $"{p.DisplayName} ({p.ProfileImagePath})"));
                error = $"'{spec}' matches more than one profile: {names}. Use the SID or the profile path instead.";
                return null;
        }
    }
}
