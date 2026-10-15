using System.Buffers;

namespace MoveIT.Core.Accounts;

public static class LocalAccountName
{
    public const int MaxLength = 20;

    private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create("\"/\\[]:;|=,+*?<>");

    /// <summary>
    /// Accepts "name", ".\name" or "COMPUTER\name" and returns the bare local account name.
    /// </summary>
    public static bool TryNormalize(string input, string machineName, out string name, out string? error)
    {
        name = input.Trim();
        error = null;

        int separator = name.IndexOf('\\');
        if (separator >= 0)
        {
            var prefix = name[..separator];
            if (prefix != "." && !prefix.Equals(machineName, StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{input}' is not a local account on {machineName}. The target must be a local account.";
                return false;
            }
            name = name[(separator + 1)..];
        }

        error = Validate(name);
        return error is null;
    }

    public static string? Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "The account name is empty.";
        }

        if (name.Length > MaxLength)
        {
            return $"'{name}' is longer than {MaxLength} characters, the Windows limit for local account names.";
        }

        if (name.AsSpan().IndexOfAny(InvalidCharacters) >= 0 || name.Any(char.IsControl))
        {
            return $"'{name}' contains characters Windows does not allow in account names (\" / \\ [ ] : ; | = , + * ? < >).";
        }

        if (name.All(c => c is '.' or ' '))
        {
            return "An account name cannot consist only of periods and spaces.";
        }

        return null;
    }
}
