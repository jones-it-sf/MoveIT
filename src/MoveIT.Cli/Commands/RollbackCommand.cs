using System.Runtime.Versioning;
using MoveIT.Core;
using MoveIT.Core.Migration;

namespace MoveIT.Cli.Commands;

[SupportedOSPlatform("windows")]
internal static class RollbackCommand
{
    public static int Run(ParsedCommand command)
    {
        var backupDirectory = command.Get("backup") ?? throw new CommandLineException("--backup <folder> is required.");

        using var log = LogSetup.Create(command.Get("log"), null, "rollback", command.Has("verbose"));
        log.Info($"MoveIT {ToolInfo.Version} rollback on {Environment.MachineName}");
        log.Info($"Log file: {log.FilePath}");

        if (!command.Has("yes"))
        {
            if (!ConsoleInput.IsInteractive)
            {
                log.Error("This session is not interactive. Add --yes to confirm the rollback. Nothing was changed.");
                return ExitCodes.NothingChanged;
            }

            if (!ConsoleInput.Confirm($"Roll back the migration recorded in {backupDirectory}?"))
            {
                log.Warn("Cancelled. Nothing was changed.");
                return ExitCodes.NothingChanged;
            }
        }

        try
        {
            RollbackRunner.Run(backupDirectory, command.Has("delete-created-account"), log);
            return log.WarningCount > 0 ? ExitCodes.CompletedWithWarnings : ExitCodes.Success;
        }
        catch (Exception e)
        {
            log.Error(e.Message);
            log.Debug(e.ToString());
            return ExitCodes.Failed;
        }
    }
}
