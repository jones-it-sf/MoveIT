using MoveIT.Core.Profiles;
using MoveIT.Core.Security;

namespace MoveIT.Core.Tests;

public class ProfileResolverTests
{
    private static readonly Guid JohnObjectId = Guid.Parse("6f9c2d41-3b7e-4a55-9a10-2c4e8b7d1f03");

    private static readonly ProfileEntry John = Entry(Sid.FromEntraObjectId(JohnObjectId), @"C:\Users\JohnSmith", @"AzureAD\JohnSmith", SidKind.Entra);
    private static readonly ProfileEntry LocalJ = Entry(Sid.Parse("S-1-5-21-1-2-3-1001"), @"C:\Users\jsmith", @"PC01\jsmith", SidKind.Local);
    private static readonly ProfileEntry Jane = Entry(Sid.FromEntraObjectId(Guid.NewGuid()), @"C:\Users\JaneDoe", null, SidKind.Entra);
    private static readonly ProfileEntry DomainJohn = Entry(Sid.Parse("S-1-5-21-7-8-9-1105"), @"C:\Users\JohnSmith.CONTOSO", @"CONTOSO\JohnSmith", SidKind.Domain);
    private static readonly ProfileEntry StaleBackup = Entry(LocalJ.Sid, @"C:\Users\TEMP", @"PC01\jsmith", SidKind.Local, isBackup: true);

    private static readonly IReadOnlyList<ProfileEntry> Profiles = [John, LocalJ, Jane, DomainJohn, StaleBackup];

    private static ProfileEntry Entry(Sid sid, string path, string? account, SidKind kind, bool isBackup = false) =>
        new(sid, path, account, kind, IsLoaded: false, isBackup, ProfileGuid: null, LastUsed: null);

    [Theory]
    [InlineData(@"AzureAD\JohnSmith")]
    [InlineData(@"azuread\johnsmith")]
    [InlineData(@"C:\Users\JohnSmith")]
    [InlineData(@"c:/users/johnsmith/")]
    [InlineData("6f9c2d41-3b7e-4a55-9a10-2c4e8b7d1f03")]
    public void FindsTheEntraProfile(string spec)
    {
        Assert.Same(John, ProfileResolver.Resolve(spec, Profiles, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void FindsBySid()
    {
        Assert.Same(LocalJ, ProfileResolver.Resolve(LocalJ.Sid.Value, Profiles, out _));
    }

    [Fact]
    public void FindsByShortAccountName()
    {
        Assert.Same(LocalJ, ProfileResolver.Resolve("jsmith", Profiles, out _));
    }

    [Fact]
    public void FallsBackToFolderNameWhenTheAccountCannotBeResolved()
    {
        Assert.Same(Jane, ProfileResolver.Resolve("JaneDoe", Profiles, out _));
    }

    [Fact]
    public void ReportsAmbiguousShortNames()
    {
        Assert.Null(ProfileResolver.Resolve("JohnSmith", Profiles, out var error));
        Assert.Contains("more than one profile", error);
        Assert.Contains(@"AzureAD\JohnSmith", error);
        Assert.Contains(@"CONTOSO\JohnSmith", error);
    }

    [Fact]
    public void ReportsUnknownProfiles()
    {
        Assert.Null(ProfileResolver.Resolve(@"AzureAD\Nobody", Profiles, out var error));
        Assert.Contains("moveit list", error);
    }

    [Fact]
    public void IgnoresBakEntries()
    {
        Assert.Null(ProfileResolver.Resolve(@"C:\Users\TEMP", Profiles, out _));
    }

    [Theory]
    [InlineData(@"C:\Users\x", true)]
    [InlineData("D:/Profiles/x", true)]
    [InlineData(@"\\server\share\x", true)]
    [InlineData(@"%SystemDrive%\Users\x", true)]
    [InlineData(@"AzureAD\x", false)]
    [InlineData("jsmith", false)]
    public void RecognisesPaths(string spec, bool expected)
    {
        Assert.Equal(expected, ProfileResolver.LooksLikePath(spec));
    }
}
