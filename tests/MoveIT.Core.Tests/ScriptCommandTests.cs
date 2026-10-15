using MoveIT.Core.Util;

namespace MoveIT.Core.Tests;

public class ScriptCommandTests
{
    [Fact]
    public void BuildsPowerShellInvocation()
    {
        var command = ScriptCommand.For(@"C:\scripts\post.ps1");

        Assert.EndsWith("powershell.exe", command.FileName);
        Assert.Contains("-ExecutionPolicy", command.Arguments);
        Assert.Contains("Bypass", command.Arguments);
        Assert.Equal("-File", command.Arguments[^2]);
        Assert.EndsWith("post.ps1", command.Arguments[^1]);
    }

    [Theory]
    [InlineData(@"C:\s\go.cmd", "cmd.exe")]
    [InlineData(@"C:\s\go.bat", "cmd.exe")]
    public void BuildsCmdInvocation(string path, string expectedExe)
    {
        var command = ScriptCommand.For(path);

        Assert.EndsWith(expectedExe, command.FileName);
        Assert.Contains("/c", command.Arguments);
    }

    [Fact]
    public void RunsExeDirectly()
    {
        var command = ScriptCommand.For(@"C:\tools\agent.exe");

        Assert.EndsWith("agent.exe", command.FileName);
        Assert.Empty(command.Arguments);
    }

    [Fact]
    public void RejectsUnsupportedExtensions()
    {
        Assert.Throws<ArgumentException>(() => ScriptCommand.For(@"C:\s\thing.py"));
        Assert.Throws<ArgumentException>(() => ScriptCommand.For(@"C:\s\thing"));
    }
}
