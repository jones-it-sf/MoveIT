using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace MoveIT.Core.Security;

/// <summary>
/// A Windows security identifier that can be parsed, compared and serialized on any OS,
/// so the permission-rewriting logic can be unit tested off Windows.
/// </summary>
public sealed class Sid : IEquatable<Sid>
{
    public const int MaxSubAuthorities = 15;
    private const int HeaderLength = 8;
    private const ulong NtAuthority = 5;
    private const ulong EntraAuthority = 12;

    private readonly byte[] _binary;

    private Sid(byte[] binary)
    {
        _binary = binary;
        Value = Format(binary);
    }

    public static Sid LocalSystem { get; } = Parse("S-1-5-18");
    public static Sid BuiltinAdministrators { get; } = Parse("S-1-5-32-544");

    public string Value { get; }

    public int BinaryLength => _binary.Length;

    public int SubAuthorityCount => _binary[1];

    public ulong IdentifierAuthority
    {
        get
        {
            ulong value = 0;
            for (int i = 2; i < HeaderLength; i++)
            {
                value = (value << 8) | _binary[i];
            }
            return value;
        }
    }

    public uint Rid => SubAuthorityCount == 0 ? 0 : GetSubAuthority(SubAuthorityCount - 1);

    /// <summary>True for Entra ID (Azure AD) user SIDs: S-1-12-1-a-b-c-d.</summary>
    public bool IsEntraUser => IdentifierAuthority == EntraAuthority && SubAuthorityCount == 5 && GetSubAuthority(0) == 1;

    /// <summary>For S-1-5-21-a-b-c-RID returns S-1-5-21-a-b-c, otherwise null.</summary>
    public Sid? AccountDomainSid
    {
        get
        {
            if (IdentifierAuthority != NtAuthority || SubAuthorityCount != 5 || GetSubAuthority(0) != 21)
            {
                return null;
            }

            var binary = new byte[HeaderLength + 4 * 4];
            _binary.AsSpan(0, binary.Length).CopyTo(binary);
            binary[1] = 4;
            return new Sid(binary);
        }
    }

    public uint GetSubAuthority(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, SubAuthorityCount);
        return BinaryPrimitives.ReadUInt32LittleEndian(_binary.AsSpan(HeaderLength + 4 * index));
    }

    public ReadOnlySpan<byte> AsSpan() => _binary;

    public byte[] ToArray() => (byte[])_binary.Clone();

    public static Sid Parse(string value) =>
        TryParse(value, out var sid) ? sid : throw new FormatException($"'{value}' is not a valid SID.");

    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Sid? sid)
    {
        sid = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('-');
        if (parts.Length < 3 || !parts[0].Equals("S", StringComparison.OrdinalIgnoreCase) || parts[1] != "1")
        {
            return false;
        }

        ulong authority;
        if (parts[2].StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!ulong.TryParse(parts[2].AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out authority))
            {
                return false;
            }
        }
        else if (!ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out authority))
        {
            return false;
        }

        int subAuthorityCount = parts.Length - 3;
        if (authority > 0xFFFF_FFFF_FFFF || subAuthorityCount > MaxSubAuthorities)
        {
            return false;
        }

        var binary = new byte[HeaderLength + 4 * subAuthorityCount];
        binary[0] = 1;
        binary[1] = (byte)subAuthorityCount;
        for (int i = 0; i < 6; i++)
        {
            binary[2 + i] = (byte)(authority >> (8 * (5 - i)));
        }

        for (int i = 0; i < subAuthorityCount; i++)
        {
            if (!uint.TryParse(parts[3 + i], NumberStyles.None, CultureInfo.InvariantCulture, out var subAuthority))
            {
                return false;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(binary.AsSpan(HeaderLength + 4 * i), subAuthority);
        }

        sid = new Sid(binary);
        return true;
    }

    public static Sid FromBinary(ReadOnlySpan<byte> data) =>
        TryReadBinary(data, out var sid, out _) ? sid : throw new FormatException("Invalid binary SID.");

    public static bool TryReadBinary(ReadOnlySpan<byte> data, [NotNullWhen(true)] out Sid? sid, out int length)
    {
        sid = null;
        length = 0;
        if (data.Length < HeaderLength || data[0] != 1 || data[1] > MaxSubAuthorities)
        {
            return false;
        }

        int required = HeaderLength + 4 * data[1];
        if (data.Length < required)
        {
            return false;
        }

        length = required;
        sid = new Sid(data[..required].ToArray());
        return true;
    }

    /// <summary>
    /// Entra ID user SIDs are derived from the user's object ID: the 16 GUID bytes become
    /// the last four sub-authorities of S-1-12-1.
    /// </summary>
    public static Sid FromEntraObjectId(Guid objectId)
    {
        var binary = new byte[HeaderLength + 4 * 5];
        binary[0] = 1;
        binary[1] = 5;
        binary[7] = (byte)EntraAuthority;
        BinaryPrimitives.WriteUInt32LittleEndian(binary.AsSpan(HeaderLength), 1);
        objectId.TryWriteBytes(binary.AsSpan(HeaderLength + 4));
        return new Sid(binary);
    }

    public Guid? ToEntraObjectId() => IsEntraUser ? new Guid(_binary.AsSpan(HeaderLength + 4, 16)) : null;

    public bool Equals(Sid? other) => other is not null && _binary.AsSpan().SequenceEqual(other._binary);

    public override bool Equals(object? obj) => obj is Sid other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_binary);
        return hash.ToHashCode();
    }

    public override string ToString() => Value;

    public static bool operator ==(Sid? left, Sid? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Sid? left, Sid? right) => !(left == right);

    private static string Format(byte[] binary)
    {
        ulong authority = 0;
        for (int i = 2; i < HeaderLength; i++)
        {
            authority = (authority << 8) | binary[i];
        }

        var builder = new StringBuilder("S-1-");
        if (authority >> 32 == 0)
        {
            builder.Append(authority.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            builder.Append("0x").Append(authority.ToString("X12", CultureInfo.InvariantCulture));
        }

        for (int i = 0; i < binary[1]; i++)
        {
            uint subAuthority = BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(HeaderLength + 4 * i));
            builder.Append('-').Append(subAuthority.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
