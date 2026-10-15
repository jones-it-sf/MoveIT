using System.Runtime.Versioning;
using MoveIT.Core.Logging;

namespace MoveIT.Core.Util;

[SupportedOSPlatform("windows")]
public static class ScriptRunner
{
    /// <summary>Runs a hook script and returns its exit code. Output goes to the log. Does not throw on a non-zero exit.</summary>
    public static int Run(string scriptPath, string label, IReadOnlyDictionary<string, string> environment, TimeSpan timeout, Logger log)
    {
        var command = ScriptCommand.For(scriptPath);
        log.Info($"Running {label} script: {scriptPath}");

        var result = ProcessRunner.Run(command.FileName, command.Arguments, timeout, environment);
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            log.Debug($"  [{label}] {line.TrimEnd()}");
        }

        if (result.ExitCode == 0)
        {
            log.Success($"{label} script finished.");
        }
        else
        {
            log.Warn($"{label} script exited with code {result.ExitCode}.");
        }
        return result.ExitCode;
    }
}
