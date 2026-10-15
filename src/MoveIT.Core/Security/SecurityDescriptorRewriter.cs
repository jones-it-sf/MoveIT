using System.Buffers.Binary;

namespace MoveIT.Core.Security;

public enum AclMode
{
    /// <summary>Grant the target everything the source has; the source keeps its entries.</summary>
    Add,

    /// <summary>Move the source's entries to the target.</summary>
    Replace,
}

[Flags]
public enum SecurityInformation : uint
{
    None = 0,
    Owner = 0x1,
    Group = 0x2,
    Dacl = 0x4,
}

public sealed record SecurityDescriptorChange(byte[] Descriptor, SecurityInformation Changed, int AcesAdded, int AcesRemoved);

public sealed record AceSummary(byte Type, byte Flags, uint Mask, Sid? Sid);

public sealed record SecurityDescriptorSummary(ushort Control, Sid? Owner, Sid? Group, IReadOnlyList<AceSummary>? Dacl);

/// <summary>
/// Rewrites self-relative security descriptors (the format returned by GetKernelObjectSecurity
/// and RegGetKeySecurity) so that every reference to one account is also given to, or moved to,
/// another account. Inherited entries are rewritten in place, so no inheritance propagation is needed.
/// </summary>
public static class SecurityDescriptorRewriter
{
    private const byte DescriptorRevision = 1;
    private const ushort DaclPresent = 0x0004;
    private const ushort SaclPresent = 0x0010;
    private const ushort SelfRelative = 0x8000;
    private const int HeaderLength = 20;
    private const int AclHeaderLength = 8;
    private const int AceHeaderLength = 4;

    private const byte AccessAllowedAceType = 0x00;
    private const byte AccessDeniedAceType = 0x01;
    private const byte AccessAllowedObjectAceType = 0x05;
    private const byte AccessDeniedObjectAceType = 0x06;
    private const byte AccessAllowedCallbackAceType = 0x09;
    private const byte AccessDeniedCallbackAceType = 0x0A;
    private const byte AccessAllowedCallbackObjectAceType = 0x0B;
    private const byte AccessDeniedCallbackObjectAceType = 0x0C;
    private const uint ObjectTypePresent = 0x1;
    private const uint InheritedObjectTypePresent = 0x2;

    /// <summary>Returns the rewritten descriptor, or null when nothing references <paramref name="source"/>.</summary>
    public static SecurityDescriptorChange? Rewrite(ReadOnlySpan<byte> descriptor, Sid source, Sid target, AclMode mode)
    {
        if (source == target)
        {
            throw new ArgumentException("Source and target accounts must be different.", nameof(target));
        }

        var parsed = ParsedDescriptor.Parse(descriptor);
        var changed = SecurityInformation.None;

        if (parsed.Owner == source)
        {
            parsed.Owner = target;
            changed |= SecurityInformation.Owner;
        }

        if (parsed.Group == source)
        {
            parsed.Group = target;
            changed |= SecurityInformation.Group;
        }

        int added = 0, removed = 0;
        if (parsed.DaclAces is { } aces && RewriteAces(aces, source, target, mode, out added, out removed) is { } rewritten)
        {
            parsed.DaclAces = rewritten;
            changed |= SecurityInformation.Dacl;
        }

        return changed == SecurityInformation.None ? null : new SecurityDescriptorChange(parsed.ToBinary(), changed, added, removed);
    }

    public static bool References(ReadOnlySpan<byte> descriptor, Sid sid)
    {
        var parsed = ParsedDescriptor.Parse(descriptor);
        return parsed.Owner == sid || parsed.Group == sid || parsed.DaclAces?.Any(ace => AceHasSid(ace, sid)) == true;
    }

    public static SecurityDescriptorSummary Summarize(ReadOnlySpan<byte> descriptor)
    {
        var parsed = ParsedDescriptor.Parse(descriptor);
        var aces = parsed.DaclAces?.Select(ace =>
        {
            Sid? sid = GetSidOffset(ace) is int offset && Sid.TryReadBinary(ace.AsSpan(offset), out var s, out _) ? s : null;
            uint mask = ace.Length >= AceHeaderLength + 4 ? ReadUInt32(ace, AceHeaderLength) : 0;
            return new AceSummary(ace[0], ace[1], mask, sid);
        }).ToList();
        return new SecurityDescriptorSummary(parsed.Control, parsed.Owner, parsed.Group, aces);
    }

    /// <summary>Builds a descriptor that only carries an owner, for use with SecurityInformation.Owner.</summary>
    public static byte[] CreateOwnerOnly(Sid owner) =>
        new ParsedDescriptor { Control = SelfRelative, Owner = owner }.ToBinary();

    private static List<byte[]>? RewriteAces(List<byte[]> aces, Sid source, Sid target, AclMode mode, out int added, out int removed)
    {
        added = 0;
        removed = 0;
        var present = new HashSet<byte[]>(aces, ByteArrayComparer.Instance);
        var result = new List<byte[]>(aces.Count + 4);

        foreach (var ace in aces)
        {
            var clone = CloneWithSid(ace, source, target);
            if (clone is null)
            {
                result.Add(ace);
                continue;
            }

            if (mode == AclMode.Replace)
            {
                removed++;
            }
            else
            {
                result.Add(ace);
            }

            // Inserting directly after the original keeps the ACL in canonical order.
            if (present.Add(clone))
            {
                result.Add(clone);
                added++;
            }
        }

        return added == 0 && removed == 0 ? null : result;
    }

    private static byte[]? CloneWithSid(byte[] ace, Sid source, Sid target)
    {
        if (GetSidOffset(ace) is not int offset || !ace.AsSpan(offset).StartsWith(source.AsSpan()))
        {
            return null;
        }

        int newLength = ace.Length - source.BinaryLength + target.BinaryLength;
        if (newLength > ushort.MaxValue)
        {
            throw new InvalidOperationException("The rewritten access control entry is too large.");
        }

        var clone = new byte[newLength];
        ace.AsSpan(0, offset).CopyTo(clone);
        target.AsSpan().CopyTo(clone.AsSpan(offset));
        ace.AsSpan(offset + source.BinaryLength).CopyTo(clone.AsSpan(offset + target.BinaryLength));
        BinaryPrimitives.WriteUInt16LittleEndian(clone.AsSpan(2), (ushort)newLength);
        return clone;
    }

    private static bool AceHasSid(byte[] ace, Sid sid) =>
        GetSidOffset(ace) is int offset && ace.AsSpan(offset).StartsWith(sid.AsSpan());

    /// <summary>Offset of the trustee SID inside an access-allowed/denied ACE, or null for other ACE types.</summary>
    private static int? GetSidOffset(ReadOnlySpan<byte> ace)
    {
        switch (ace[0])
        {
            case AccessAllowedAceType:
            case AccessDeniedAceType:
            case AccessAllowedCallbackAceType:
            case AccessDeniedCallbackAceType:
                return ace.Length >= AceHeaderLength + 4 + 8 ? AceHeaderLength + 4 : null;

            case AccessAllowedObjectAceType:
            case AccessDeniedObjectAceType:
            case AccessAllowedCallbackObjectAceType:
            case AccessDeniedCallbackObjectAceType:
                if (ace.Length < AceHeaderLength + 8)
                {
                    return null;
                }
                uint flags = ReadUInt32(ace, AceHeaderLength + 4);
                int offset = AceHeaderLength + 8
                    + ((flags & ObjectTypePresent) != 0 ? 16 : 0)
                    + ((flags & InheritedObjectTypePresent) != 0 ? 16 : 0);
                return ace.Length >= offset + 8 ? offset : null;

            default:
                return null;
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    private sealed class ParsedDescriptor
    {
        public ushort Control;
        public Sid? Owner;
        public Sid? Group;
        public byte[]? Sacl;
        public byte DaclRevision = 2;

        /// <summary>Null when the descriptor has no DACL or a NULL DACL.</summary>
        public List<byte[]>? DaclAces;

        public static ParsedDescriptor Parse(ReadOnlySpan<byte> data)
        {
            if (data.Length < HeaderLength)
            {
                throw new FormatException("Security descriptor is truncated.");
            }

            if (data[0] != DescriptorRevision)
            {
                throw new FormatException($"Unsupported security descriptor revision {data[0]}.");
            }

            var parsed = new ParsedDescriptor { Control = ReadUInt16(data, 2) };
            if ((parsed.Control & SelfRelative) == 0)
            {
                throw new FormatException("Security descriptor is not self-relative.");
            }

            parsed.Owner = ReadSid(data, ReadUInt32(data, 4));
            parsed.Group = ReadSid(data, ReadUInt32(data, 8));

            if ((parsed.Control & SaclPresent) != 0 && TrySliceAcl(data, ReadUInt32(data, 12), out var sacl))
            {
                parsed.Sacl = sacl.ToArray();
            }

            if ((parsed.Control & DaclPresent) != 0 && TrySliceAcl(data, ReadUInt32(data, 16), out var dacl))
            {
                parsed.DaclRevision = dacl[0];
                parsed.DaclAces = ParseAces(dacl);
            }

            return parsed;
        }

        public byte[] ToBinary()
        {
            byte[]? dacl = DaclAces is null ? null : BuildAcl(DaclRevision, DaclAces);
            int length = HeaderLength
                + (Owner?.BinaryLength ?? 0)
                + (Group?.BinaryLength ?? 0)
                + (Sacl?.Length ?? 0)
                + (dacl?.Length ?? 0);

            var buffer = new byte[length];
            buffer[0] = DescriptorRevision;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), Control);

            int position = HeaderLength;
            Put(4, Owner?.ToArray());
            Put(8, Group?.ToArray());
            Put(12, Sacl);
            Put(16, dacl);
            return buffer;

            void Put(int offsetField, byte[]? bytes)
            {
                if (bytes is null)
                {
                    return;
                }
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offsetField), (uint)position);
                bytes.CopyTo(buffer, position);
                position += bytes.Length;
            }
        }

        private static Sid? ReadSid(ReadOnlySpan<byte> data, uint offset)
        {
            if (offset == 0)
            {
                return null;
            }

            if (offset >= data.Length || !Sid.TryReadBinary(data[(int)offset..], out var sid, out _))
            {
                throw new FormatException("Security descriptor contains an invalid SID.");
            }

            return sid;
        }

        private static bool TrySliceAcl(ReadOnlySpan<byte> data, uint offset, out ReadOnlySpan<byte> acl)
        {
            acl = default;
            if (offset == 0)
            {
                return false;
            }

            if (offset + AclHeaderLength > data.Length)
            {
                throw new FormatException("ACL header is out of range.");
            }

            int size = ReadUInt16(data, (int)offset + 2);
            if (size < AclHeaderLength || offset + size > data.Length)
            {
                throw new FormatException("ACL size is out of range.");
            }

            acl = data.Slice((int)offset, size);
            return true;
        }

        private static List<byte[]> ParseAces(ReadOnlySpan<byte> acl)
        {
            int count = ReadUInt16(acl, 4);
            var aces = new List<byte[]>(count + 4);
            int position = AclHeaderLength;
            for (int i = 0; i < count; i++)
            {
                if (position + AceHeaderLength > acl.Length)
                {
                    throw new FormatException("ACE header is out of range.");
                }

                int size = ReadUInt16(acl, position + 2);
                if (size < AceHeaderLength || position + size > acl.Length)
                {
                    throw new FormatException("ACE size is out of range.");
                }

                aces.Add(acl.Slice(position, size).ToArray());
                position += size;
            }
            return aces;
        }

        private static byte[] BuildAcl(byte revision, List<byte[]> aces)
        {
            int length = AclHeaderLength + aces.Sum(ace => ace.Length);
            if (length > ushort.MaxValue || aces.Count > ushort.MaxValue)
            {
                throw new InvalidOperationException("The rewritten ACL exceeds the 64 KB Windows limit.");
            }

            var acl = new byte[length];
            acl[0] = revision;
            BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(2), (ushort)length);
            BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(4), (ushort)aces.Count);
            int position = AclHeaderLength;
            foreach (var ace in aces)
            {
                ace.CopyTo(acl, position);
                position += ace.Length;
            }
            return acl;
        }
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y) =>
            x is null ? y is null : y is not null && x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj)
        {
            var hash = new HashCode();
            hash.AddBytes(obj);
            return hash.ToHashCode();
        }
    }
}
