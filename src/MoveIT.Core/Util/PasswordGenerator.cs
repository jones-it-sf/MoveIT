using System.Security.Cryptography;

namespace MoveIT.Core.Util;

/// <summary>
/// Generates passwords that satisfy the default Windows complexity policy: at least one lowercase,
/// uppercase, digit and symbol, with no ambiguous characters.
/// </summary>
public static class PasswordGenerator
{
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%&*+-=?@";
    private const int MinLength = 12;

    private static readonly string[] Classes = [Lower, Upper, Digits, Symbols];
    private static readonly string All = string.Concat(Classes);

    public static string Generate(int length)
    {
        if (length < MinLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"A generated password must be at least {MinLength} characters.");
        }

        var chars = new char[length];
        for (int i = 0; i < Classes.Length; i++)
        {
            chars[i] = Pick(Classes[i]);
        }
        for (int i = Classes.Length; i < length; i++)
        {
            chars[i] = Pick(All);
        }

        Shuffle(chars);
        return new string(chars);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];

    private static void Shuffle(char[] chars)
    {
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}
