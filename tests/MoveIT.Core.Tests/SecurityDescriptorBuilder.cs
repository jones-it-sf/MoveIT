using System.Buffers.Binary;
using MoveIT.Core.Security;

namespace MoveIT.Core.Tests;

/// <summary>Builds self-relative security descriptors byte by byte, the way Windows returns them.</summary>
internal static class SecurityDescriptorBuilder
{
    public const byte Allowed = 0x00;
    public const byte Denied = 0x01;
    public const byte AllowedObject = 0x05;
    public const byte AllowedCallback = 0x09;

    public const byte ObjectInherit = 0x01;
    public const byte ContainerInherit = 0x02;
    public const byte Inherited = 0x10;

    public const uint FullControl = 0x001F01FF;
    public const uint ReadExecute = 0x001200A9;

    private const ushort SelfRelative = 0x8000;
    private const ushort DaclPresent = 0x0004;
    private const ushort DaclAutoInherited = 0x0400;

    public static byte[] Ace(byte type, byte flags, uint mask, Sid sid, byte[]? applicationData = null)
    {
        var data = applicationData ?? [];
        var ace = new byte[8 + sid.BinaryLength + data.Length];
        ace[0] = type;
        ace[1] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(ace.AsSpan(2), (ushort)ace.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(ace.AsSpan(4), mask);
        sid.AsSpan().CopyTo(ace.AsSpan(8));
        data.CopyTo(ace, 8 + sid.BinaryLength);
        return ace;
    }

    public static byte[] ObjectAce(byte type, byte flags, uint mask, Guid objectType, Sid sid)
    {
        var ace = new byte[12 + 16 + sid.BinaryLength];
        ace[0] = type;
        ace[1] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(ace.AsSpan(2), (ushort)ace.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(ace.AsSpan(4), mask);
        BinaryPrimitives.WriteUInt32LittleEndian(ace.AsSpan(8), 1);
        objectType.TryWriteBytes(ace.AsSpan(12));
        sid.AsSpan().CopyTo(ace.AsSpan(28));
        return ace;
    }

    public static byte[] AclOf(params byte[][] aces)
    {
        int length = 8 + aces.Sum(a => a.Length);
        var acl = new byte[length];
        acl[0] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(2), (ushort)length);
        BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(4), (ushort)aces.Length);
        int position = 8;
        foreach (var ace in aces)
        {
            ace.CopyTo(acl, position);
            position += ace.Length;
        }
        return acl;
    }

    /// <summary>A null <paramref name="dacl"/> with <paramref name="nullDacl"/> set produces a NULL DACL (present, offset 0).</summary>
    public static byte[] Descriptor(Sid? owner, Sid? group, byte[]? dacl, bool nullDacl = false)
    {
        ushort control = SelfRelative | DaclAutoInherited;
        if (dacl is not null || nullDacl)
        {
            control |= DaclPresent;
        }

        var parts = new List<(int OffsetField, byte[] Bytes)>();
        if (owner is not null)
        {
            parts.Add((4, owner.ToArray()));
        }
        if (group is not null)
        {
            parts.Add((8, group.ToArray()));
        }
        if (dacl is not null)
        {
            parts.Add((16, dacl));
        }

        var descriptor = new byte[20 + parts.Sum(p => p.Bytes.Length)];
        descriptor[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(2), control);
        int position = 20;
        foreach (var (offsetField, bytes) in parts)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(offsetField), (uint)position);
            bytes.CopyTo(descriptor, position);
            position += bytes.Length;
        }
        return descriptor;
    }
}
