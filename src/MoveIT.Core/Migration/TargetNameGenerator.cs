using System.Text;
using MoveIT.Core.Accounts;
using MoveIT.Core.Profiles;

namespace MoveIT.Core.Migration;

/// <summary>Derives a valid, unique local account name from a source profile.</summary>
public sealed class TargetNameGenerator(NamingOptions options, Func<string, bool> isNameTaken)
{
    /// <summary>Returns a name that passes LocalAccountName validation and that <see cref="isNameTaken"/> reports as free.</summary>
    public string Generate(ProfileEntry source)
    {
        var basis = Clean(ChooseBasis(source));
        if (basis.Length == 0)
        {
            basis = "user";
        }

        int maxLength = Math.Clamp(options.MaxLength, 1, LocalAccountName.MaxLength);

        var baseName = Compose(basis, "", maxLength);
        if (IsUsable(baseName))
        {
            return baseName;
        }

        for (int suffix = 2; suffix < 100_000; suffix++)
        {
            var number = suffix.ToString();
            var candidate = Compose(basis, number, maxLength);
            if (IsUsable(candidate))
            {
                return candidate;
            }
        }

        throw new MigrationException($"Could not generate a unique local account name from '{source.DisplayName}'.");
    }

    // Cleans prefix and basis together so a separator between them (for example "loc-") is kept.
    private string Compose(string basis, string number, int maxLength) =>
        Fit(Clean(options.Prefix + basis), maxLength - number.Length) + number;

    private string ChooseBasis(ProfileEntry source)
    {
        var account = AccountNameBasis(source.AccountName);
        var folder = FolderBasis(source.ProfileImagePath);
        return options.Basis switch
        {
            NameBasis.AccountName => account ?? folder,
            NameBasis.FolderName => folder,
            _ => account ?? folder,
        };
    }

    private static string? AccountNameBasis(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return null;
        }

        int backslash = accountName.IndexOf('\\');
        var name = backslash >= 0 ? accountName[(backslash + 1)..] : accountName;
        int at = name.IndexOf('@');
        return at > 0 ? name[..at] : name;
    }

    private static string FolderBasis(string profilePath)
    {
        var normalized = ProfileResolver.NormalizePath(profilePath);
        int separator = normalized.LastIndexOf('\\');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            {
                builder.Append(options.Lowercase ? char.ToLowerInvariant(c) : c);
            }
        }
        return builder.ToString().Trim('.', '-', '_');
    }

    private static string Fit(string value, int maxLength) =>
        (value.Length <= maxLength ? value : value[..maxLength]).TrimEnd('.', '-', '_');

    private bool IsUsable(string name) =>
        name.Length > 0 && LocalAccountName.Validate(name) is null && !isNameTaken(name);
}
