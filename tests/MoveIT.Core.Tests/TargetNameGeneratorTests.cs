using MoveIT.Core.Migration;
using MoveIT.Core.Profiles;
using MoveIT.Core.Security;

namespace MoveIT.Core.Tests;

public class TargetNameGeneratorTests
{
    private static ProfileEntry Entra(string? account, string path) =>
        new(Sid.FromEntraObjectId(Guid.NewGuid()), path, account, SidKind.Entra, IsLoaded: false, IsBackupEntry: false, ProfileGuid: null, LastUsed: null);

    private static string Generate(ProfileEntry source, NamingOptions? options = null, params string[] taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        return new TargetNameGenerator(options ?? new NamingOptions(), name => used.Contains(name)).Generate(source);
    }

    [Theory]
    [InlineData(@"AzureAD\JohnSmith", @"C:\Users\JohnSmith", "johnsmith")]
    [InlineData(@"AzureAD\john.smith@contoso.com", @"C:\Users\john.smi", "john.smith")]
    [InlineData(@"AzureAD\Jane-Doe", @"C:\Users\JaneDoe", "jane-doe")]
    public void DerivesNameFromAccount(string account, string path, string expected)
    {
        Assert.Equal(expected, Generate(Entra(account, path)));
    }

    [Fact]
    public void FallsBackToFolderWhenAccountUnknown()
    {
        Assert.Equal("janedoe", Generate(Entra(null, @"C:\Users\JaneDoe")));
    }

    [Fact]
    public void UsesFolderNameWhenAsked()
    {
        var options = new NamingOptions { Basis = NameBasis.FolderName };

        Assert.Equal("john.smi", Generate(Entra(@"AzureAD\john.smith@contoso.com", @"C:\Users\john.smi"), options));
    }

    [Fact]
    public void AppendsNumberOnCollision()
    {
        Assert.Equal("johnsmith2", Generate(Entra(@"AzureAD\JohnSmith", @"C:\Users\JohnSmith"), null, "johnsmith"));
        Assert.Equal("johnsmith3", Generate(Entra(@"AzureAD\JohnSmith", @"C:\Users\JohnSmith"), null, "johnsmith", "johnsmith2"));
    }

    [Fact]
    public void AppliesPrefixAndKeepsCase()
    {
        var options = new NamingOptions { Prefix = "loc-", Lowercase = false };

        Assert.Equal("loc-JohnSmith", Generate(Entra(@"AzureAD\JohnSmith", @"C:\Users\JohnSmith"), options));
    }

    [Fact]
    public void TruncatesToTwentyCharactersAndStillDedupes()
    {
        var longName = Entra(@"AzureAD\AbcdefghijklmnopqrstuvwxyzExtra", @"C:\Users\x");

        var first = Generate(longName);
        Assert.Equal("abcdefghijklmnopqrst", first);
        Assert.Equal(20, first.Length);

        var second = Generate(longName, null, first);
        Assert.Equal("abcdefghijklmnopqrs2", second);
        Assert.Equal(20, second.Length);
    }

    [Fact]
    public void ProducesUsableNameFromUnusableBasis()
    {
        var result = Generate(Entra(@"AzureAD\...", @"C:\Users\$$$"));

        Assert.Equal("user", result);
    }
}
