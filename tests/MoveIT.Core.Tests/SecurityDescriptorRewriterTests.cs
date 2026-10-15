using MoveIT.Core.Security;
using static MoveIT.Core.Tests.SecurityDescriptorBuilder;

namespace MoveIT.Core.Tests;

public class SecurityDescriptorRewriterTests
{
    private static readonly Sid EntraUser = Sid.FromEntraObjectId(Guid.Parse("6f9c2d41-3b7e-4a55-9a10-2c4e8b7d1f03"));
    private static readonly Sid LocalUser = Sid.Parse("S-1-5-21-111-222-333-1001");
    private static readonly Sid Admins = Sid.BuiltinAdministrators;
    private static readonly Sid System = Sid.LocalSystem;

    private static byte[] TypicalProfileFolder(byte userFlags = ObjectInherit | ContainerInherit) => Descriptor(
        owner: EntraUser,
        group: System,
        dacl: AclOf(
            Ace(Allowed, ObjectInherit | ContainerInherit, FullControl, System),
            Ace(Allowed, ObjectInherit | ContainerInherit, FullControl, Admins),
            Ace(Allowed, userFlags, FullControl, EntraUser)));

    [Fact]
    public void AddGrantsTargetTheSameEntryRightAfterTheSource()
    {
        var change = SecurityDescriptorRewriter.Rewrite(TypicalProfileFolder(), EntraUser, LocalUser, AclMode.Add);

        Assert.NotNull(change);
        Assert.Equal(SecurityInformation.Owner | SecurityInformation.Dacl, change.Changed);
        Assert.Equal(1, change.AcesAdded);
        Assert.Equal(0, change.AcesRemoved);

        var result = SecurityDescriptorRewriter.Summarize(change.Descriptor);
        Assert.Equal(LocalUser, result.Owner);
        Assert.Equal(System, result.Group);
        Assert.Equal(
            [System, Admins, EntraUser, LocalUser],
            result.Dacl!.Select(a => a.Sid));
        Assert.Equal(new AceSummary(Allowed, ObjectInherit | ContainerInherit, FullControl, LocalUser), result.Dacl![3]);
    }

    [Fact]
    public void InheritedEntriesStayInherited()
    {
        var change = SecurityDescriptorRewriter.Rewrite(TypicalProfileFolder(Inherited | ObjectInherit), EntraUser, LocalUser, AclMode.Add);

        var added = SecurityDescriptorRewriter.Summarize(change!.Descriptor).Dacl!.Single(a => a.Sid == LocalUser);
        Assert.Equal(Inherited | ObjectInherit, added.Flags);
    }

    [Fact]
    public void RunningTwiceChangesNothingTheSecondTime()
    {
        var first = SecurityDescriptorRewriter.Rewrite(TypicalProfileFolder(), EntraUser, LocalUser, AclMode.Add);

        Assert.Null(SecurityDescriptorRewriter.Rewrite(first!.Descriptor, EntraUser, LocalUser, AclMode.Add));
    }

    [Fact]
    public void ReplaceMovesEveryEntryToTheTarget()
    {
        var change = SecurityDescriptorRewriter.Rewrite(TypicalProfileFolder(), EntraUser, LocalUser, AclMode.Replace);

        Assert.Equal(1, change!.AcesAdded);
        Assert.Equal(1, change.AcesRemoved);
        Assert.False(SecurityDescriptorRewriter.References(change.Descriptor, EntraUser));
        Assert.Equal([System, Admins, LocalUser], SecurityDescriptorRewriter.Summarize(change.Descriptor).Dacl!.Select(a => a.Sid));
    }

    [Fact]
    public void ReplaceAfterAddOnlyRemovesTheSource()
    {
        var added = SecurityDescriptorRewriter.Rewrite(TypicalProfileFolder(), EntraUser, LocalUser, AclMode.Add)!;

        var replaced = SecurityDescriptorRewriter.Rewrite(added.Descriptor, EntraUser, LocalUser, AclMode.Replace)!;

        Assert.Equal(0, replaced.AcesAdded);
        Assert.Equal(1, replaced.AcesRemoved);
        Assert.Equal(SecurityInformation.Dacl, replaced.Changed);
        Assert.Equal([System, Admins, LocalUser], SecurityDescriptorRewriter.Summarize(replaced.Descriptor).Dacl!.Select(a => a.Sid));
    }

    [Fact]
    public void KeepsCanonicalOrderWithDenyEntries()
    {
        var descriptor = Descriptor(System, System, AclOf(
            Ace(Denied, 0, 0x00010000, EntraUser),
            Ace(Allowed, 0, FullControl, Admins),
            Ace(Allowed, Inherited, ReadExecute, EntraUser)));

        var change = SecurityDescriptorRewriter.Rewrite(descriptor, EntraUser, LocalUser, AclMode.Add);

        var dacl = SecurityDescriptorRewriter.Summarize(change!.Descriptor).Dacl!;
        Assert.Equal(
            [(Denied, EntraUser), (Denied, LocalUser), (Allowed, Admins), (Allowed, EntraUser), (Allowed, LocalUser)],
            dacl.Select(a => (a.Type, a.Sid!)));
        Assert.Equal(ReadExecute, dacl[4].Mask);
    }

    [Fact]
    public void ReturnsNullWhenTheSourceIsNotReferenced()
    {
        var descriptor = Descriptor(Admins, System, AclOf(Ace(Allowed, 0, FullControl, System)));

        Assert.Null(SecurityDescriptorRewriter.Rewrite(descriptor, EntraUser, LocalUser, AclMode.Add));
    }

    [Fact]
    public void RewritesPrimaryGroup()
    {
        var descriptor = Descriptor(Admins, EntraUser, AclOf(Ace(Allowed, 0, FullControl, System)));

        var change = SecurityDescriptorRewriter.Rewrite(descriptor, EntraUser, LocalUser, AclMode.Add);

        Assert.Equal(SecurityInformation.Group, change!.Changed);
        Assert.Equal(LocalUser, SecurityDescriptorRewriter.Summarize(change.Descriptor).Group);
    }

    [Fact]
    public void CallbackEntriesKeepTheirApplicationData()
    {
        byte[] applicationData = [0xAA, 0xBB, 0xCC, 0xDD];
        var descriptor = Descriptor(Admins, System, AclOf(Ace(AllowedCallback, 0, FullControl, EntraUser, applicationData)));

        var change = SecurityDescriptorRewriter.Rewrite(descriptor, EntraUser, LocalUser, AclMode.Replace);

        var expected = Descriptor(Admins, System, AclOf(Ace(AllowedCallback, 0, FullControl, LocalUser, applicationData)));
        Assert.Equal(expected, change!.Descriptor);
    }

    [Fact]
    public void FindsTheTrusteeInObjectEntries()
    {
        var objectType = Guid.Parse("bf967aba-0de6-11d0-a285-00aa003049e2");
        var descriptor = Descriptor(Admins, System, AclOf(ObjectAce(AllowedObject, 0, 0x100, objectType, EntraUser)));

        var change = SecurityDescriptorRewriter.Rewrite(descriptor, EntraUser, LocalUser, AclMode.Replace);

        Assert.Equal(Descriptor(Admins, System, AclOf(ObjectAce(AllowedObject, 0, 0x100, objectType, LocalUser))), change!.Descriptor);
    }

    [Fact]
    public void KeepsANullDaclNull()
    {
        var descriptor = Descriptor(EntraUser, System, dacl: null, nullDacl: true);

        var change = SecurityDescriptorRewriter.Rewrite(descriptor, EntraUser, LocalUser, AclMode.Add);

        Assert.Equal(SecurityInformation.Owner, change!.Changed);
        Assert.Equal(Descriptor(LocalUser, System, dacl: null, nullDacl: true), change.Descriptor);
    }

    [Fact]
    public void IgnoresBytesAfterTheDescriptor()
    {
        var descriptor = TypicalProfileFolder();
        var buffer = new byte[descriptor.Length + 100];
        descriptor.CopyTo(buffer, 0);
        Array.Fill(buffer, (byte)0xFF, descriptor.Length, 100);

        var fromBuffer = SecurityDescriptorRewriter.Rewrite(buffer, EntraUser, LocalUser, AclMode.Add);
        var exact = SecurityDescriptorRewriter.Rewrite(descriptor, EntraUser, LocalUser, AclMode.Add);

        Assert.Equal(exact!.Descriptor, fromBuffer!.Descriptor);
    }

    [Fact]
    public void ReferencesDetectsOwnerGroupAndEntries()
    {
        Assert.True(SecurityDescriptorRewriter.References(TypicalProfileFolder(), EntraUser));
        Assert.True(SecurityDescriptorRewriter.References(Descriptor(Admins, LocalUser, AclOf()), LocalUser));
        Assert.False(SecurityDescriptorRewriter.References(TypicalProfileFolder(), LocalUser));
    }

    [Fact]
    public void OwnerOnlyDescriptorCarriesJustTheOwner()
    {
        var summary = SecurityDescriptorRewriter.Summarize(SecurityDescriptorRewriter.CreateOwnerOnly(Admins));

        Assert.Equal(Admins, summary.Owner);
        Assert.Null(summary.Group);
        Assert.Null(summary.Dacl);
    }

    [Fact]
    public void RejectsMalformedDescriptors()
    {
        var valid = TypicalProfileFolder();
        var notSelfRelative = (byte[])valid.Clone();
        notSelfRelative[3] = 0;
        var badAclSize = (byte[])valid.Clone();
        int daclOffset = BitConverter.ToInt32(valid, 16);
        badAclSize[daclOffset + 2] = 0xFF;
        badAclSize[daclOffset + 3] = 0xFF;

        Assert.Throws<FormatException>(() => SecurityDescriptorRewriter.Rewrite(valid.AsSpan(0, 10), EntraUser, LocalUser, AclMode.Add));
        Assert.Throws<FormatException>(() => SecurityDescriptorRewriter.Rewrite(notSelfRelative, EntraUser, LocalUser, AclMode.Add));
        Assert.Throws<FormatException>(() => SecurityDescriptorRewriter.Rewrite(badAclSize, EntraUser, LocalUser, AclMode.Add));
    }

    [Fact]
    public void RefusesIdenticalSourceAndTarget()
    {
        Assert.Throws<ArgumentException>(() => SecurityDescriptorRewriter.Rewrite(TypicalProfileFolder(), EntraUser, EntraUser, AclMode.Add));
    }
}
