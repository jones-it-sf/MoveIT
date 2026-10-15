using MoveIT.Core.Remote;

namespace MoveIT.Core.Tests;

public class RemoteHostsTests
{
    [Fact]
    public void ParsesLinesCommentsAndSeparators()
    {
        var hosts = RemoteHosts.Parse("""
            # lab machines
            PC01
            PC02, PC03   PC04   # trailing comment
            \\PC05\

            pc01
            """);

        Assert.Equal(["PC01", "PC02", "PC03", "PC04", "PC05"], hosts);
    }

    [Theory]
    [InlineData("PC01", @"C:\Windows\Temp\MoveIT", @"\\PC01\C$\Windows\Temp\MoveIT")]
    [InlineData("PC01", @"D:\Stage", @"\\PC01\D$\Stage")]
    public void BuildsAdminSharePaths(string host, string local, string expected)
    {
        Assert.Equal(expected, RemoteHosts.AdminShare(host, local));
    }

    [Fact]
    public void RejectsNonDrivePaths()
    {
        Assert.Throws<ArgumentException>(() => RemoteHosts.AdminShare("PC01", @"\\server\share"));
    }
}

public class SchTasksTests
{
    private const string RunningQuery = """
        Folder: \
        HostName:                             PC01
        TaskName:                             \MoveIT-Migration
        Status:                               Running
        Last Run Time:                        10/15/2026 9:00:00 AM
        Last Result:                          267009
        """;

    private const string DoneQuery = """
        TaskName:                             \MoveIT-Migration
        Status:                               Ready
        Last Run Time:                        10/15/2026 9:02:00 AM
        Last Result:                          0
        """;

    [Fact]
    public void CreateArgsRunAsSystem()
    {
        var args = SchTasks.CreateArgs("PC01", "MoveIT-Migration", @"C:\Windows\Temp\MoveIT\run.cmd");

        Assert.Equal("PC01", args[1]);
        Assert.Contains("/RU", args);
        Assert.Equal("SYSTEM", args[Array.IndexOf(args, "/RU") + 1]);
        Assert.Equal(@"C:\Windows\Temp\MoveIT\run.cmd", args[Array.IndexOf(args, "/TR") + 1]);
    }

    [Fact]
    public void ReadsRunningState()
    {
        Assert.Equal(RemoteTaskState.Running, SchTasks.ParseState(RunningQuery));
        Assert.Null(SchTasks.ParseLastResult(RunningQuery.Replace("267009", SchTasks.NotYetRun.ToString())));
    }

    [Fact]
    public void ReadsCompletedResult()
    {
        Assert.Equal(RemoteTaskState.Ready, SchTasks.ParseState(DoneQuery));
        Assert.Equal(0, SchTasks.ParseLastResult(DoneQuery));
    }

    [Theory]
    [InlineData("Last Result: 3010", 3010)]
    [InlineData("Last Result: -2147024894", -2147024894)]
    [InlineData("Last Result: 0x0", 0)]
    public void ParsesVariousResultFormats(string line, int expected)
    {
        Assert.Equal(expected, SchTasks.ParseLastResult(line));
    }
}
