using System.Runtime.Versioning;
using MoveIT.Core.Logging;
using MoveIT.Core.Util;

namespace MoveIT.Cli.Commands;

[SupportedOSPlatform("windows")]
internal static class LogSetup
{
    public static Logger Create(string? logFile, string? logDirectory, string commandName, bool verbose)
    {
        if (logFile is null)
        {
            var directory = AppDirectories.Ensure(logDirectory ?? AppDirectories.DefaultLogs);
            logFile = Path.Combine(directory, $"moveit-{commandName}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        }
        else if (Path.GetDirectoryName(Path.GetFullPath(logFile)) is { } parent)
        {
            AppDirectories.Ensure(parent);
        }

        return new Logger(logFile, verbose);
    }
}
