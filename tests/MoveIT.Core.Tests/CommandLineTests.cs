using MoveIT.Cli;

namespace MoveIT.Core.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("/?")]
    [InlineData("migrate", "--source", "x", "--help")]
    public void ShowsHelp(params string[] args)
    {
        Assert.Equal("help", CommandLine.Parse(args).Name);
    }

    [Fact]
    public void ParsesMigrateOptions()
    {
        var command = CommandLine.Parse(["migrate", "--source", @"AzureAD\JohnSmith", "--TARGET=jsmith", "--create", "--admin", "--what-if"]);

        Assert.Equal("migrate", command.Name);
        Assert.Equal(@"AzureAD\JohnSmith", command.Get("source"));
        Assert.Equal("jsmith", command.Get("target"));
        Assert.True(command.Has("create"));
        Assert.True(command.Has("admin"));
        Assert.True(command.Has("what-if"));
        Assert.False(command.Has("leave-entra"));
    }

    [Theory]
    [InlineData("Unknown command", "frobnicate")]
    [InlineData("Unknown option", "migrate", "--sauce", "x")]
    [InlineData("needs a value", "migrate", "--source")]
    [InlineData("needs a value", "migrate", "--source", "--target", "x")]
    [InlineData("does not take a value", "migrate", "--create=yes")]
    [InlineData("more than once", "migrate", "--yes", "--yes")]
    [InlineData("Unexpected argument", "migrate", "jsmith")]
    [InlineData("Unknown option", "list", "--source", "x")]
    public void RejectsBadArguments(string expectedMessage, params string[] args)
    {
        var error = Assert.Throws<CommandLineException>(() => CommandLine.Parse(args));

        Assert.Contains(expectedMessage, error.Message);
    }
}
