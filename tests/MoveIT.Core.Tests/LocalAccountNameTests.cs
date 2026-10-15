using MoveIT.Core.Accounts;

namespace MoveIT.Core.Tests;

public class LocalAccountNameTests
{
    [Theory]
    [InlineData("jsmith", "jsmith")]
    [InlineData(@".\jsmith", "jsmith")]
    [InlineData(@"PC01\jsmith", "jsmith")]
    [InlineData(@"pc01\jsmith", "jsmith")]
    [InlineData("  john.smith  ", "john.smith")]
    public void AcceptsLocalNames(string input, string expected)
    {
        Assert.True(LocalAccountName.TryNormalize(input, "PC01", out var name, out var error));
        Assert.Equal(expected, name);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(@"CONTOSO\jsmith", "not a local account")]
    [InlineData(@"AzureAD\jsmith", "not a local account")]
    [InlineData("", "empty")]
    [InlineData("averyveryverylongname1", "longer than 20")]
    [InlineData("j:smith", "characters")]
    [InlineData("j*smith", "characters")]
    [InlineData("...", "only of periods")]
    public void RejectsInvalidNames(string input, string expectedMessage)
    {
        Assert.False(LocalAccountName.TryNormalize(input, "PC01", out _, out var error));
        Assert.Contains(expectedMessage, error);
    }
}
