using MoveIT.Core.Security;

namespace MoveIT.Core.Tests;

public class SidTests
{
    [Theory]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-32-544")]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-1001")]
    [InlineData("S-1-12-1-1-196610-117835012-185207048")]
    [InlineData("S-1-0x010000000000-7")]
    public void ParseAndFormatRoundTrip(string value)
    {
        var sid = Sid.Parse(value);

        Assert.Equal(value, sid.Value);
        Assert.Equal(sid, Sid.FromBinary(sid.ToArray()));
    }

    [Fact]
    public void BinaryLayoutMatchesWindows()
    {
        Assert.Equal(new byte[] { 1, 1, 0, 0, 0, 0, 0, 5, 18, 0, 0, 0 }, Sid.LocalSystem.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("S-1")]
    [InlineData("S-2-5-18")]
    [InlineData("X-1-5-18")]
    [InlineData("S-1-x-18")]
    [InlineData("S-1-5-4294967296")]
    [InlineData("S-1-5-1-2-3-4-5-6-7-8-9-10-11-12-13-14-15-16")]
    public void RejectsInvalidStrings(string value)
    {
        Assert.False(Sid.TryParse(value, out _));
    }

    [Fact]
    public void RejectsTruncatedBinary()
    {
        var binary = Sid.Parse("S-1-5-21-1-2-3-1001").ToArray();

        Assert.False(Sid.TryReadBinary(binary.AsSpan(0, binary.Length - 1), out _, out _));
    }

    [Fact]
    public void EntraObjectIdConvertsToSidAndBack()
    {
        // GUID bytes 01000000 0200 0300 0405060708090A0B read as four little-endian uint32 values.
        var objectId = Guid.Parse("00000001-0002-0003-0405-060708090a0b");

        var sid = Sid.FromEntraObjectId(objectId);

        Assert.Equal("S-1-12-1-1-196610-117835012-185207048", sid.Value);
        Assert.True(sid.IsEntraUser);
        Assert.Equal(objectId, sid.ToEntraObjectId());
    }

    [Fact]
    public void RandomEntraObjectIdsRoundTrip()
    {
        for (int i = 0; i < 100; i++)
        {
            var objectId = Guid.NewGuid();
            Assert.Equal(objectId, Sid.Parse(Sid.FromEntraObjectId(objectId).Value).ToEntraObjectId());
        }
    }

    [Fact]
    public void NonEntraSidHasNoObjectId()
    {
        var sid = Sid.Parse("S-1-5-21-1-2-3-1001");

        Assert.False(sid.IsEntraUser);
        Assert.Null(sid.ToEntraObjectId());
    }

    [Fact]
    public void AccountDomainSidStripsRid()
    {
        Assert.Equal(Sid.Parse("S-1-5-21-10-20-30"), Sid.Parse("S-1-5-21-10-20-30-1001").AccountDomainSid);
        Assert.Null(Sid.Parse("S-1-5-32-544").AccountDomainSid);
        Assert.Null(Sid.Parse("S-1-12-1-1-2-3-4").AccountDomainSid);
    }

    [Fact]
    public void EqualityIsByValue()
    {
        var a = Sid.Parse("S-1-5-21-1-2-3-1001");
        var b = Sid.Parse("s-1-5-21-1-2-3-1001");

        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, Sid.Parse("S-1-5-21-1-2-3-1002"));
    }

    [Theory]
    [InlineData("S-1-12-1-1-2-3-4", null, SidKind.Entra)]
    [InlineData("S-1-5-21-10-20-30-1001", "S-1-5-21-10-20-30", SidKind.Local)]
    [InlineData("S-1-5-21-40-50-60-1104", "S-1-5-21-10-20-30", SidKind.Domain)]
    [InlineData("S-1-5-21-40-50-60-1104", null, SidKind.Unknown)]
    [InlineData("S-1-5-18", "S-1-5-21-10-20-30", SidKind.System)]
    public void ClassifiesAccounts(string sid, string? machineSid, SidKind expected)
    {
        Assert.Equal(expected, SidClassifier.Classify(Sid.Parse(sid), machineSid is null ? null : Sid.Parse(machineSid)));
    }
}
