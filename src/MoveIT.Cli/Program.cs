using MoveIT.Cli;
using MoveIT.Cli.Commands;
using MoveIT.Core;

ParsedCommand command;
try
{
    command = CommandLine.Parse(args);
}
catch (CommandLineException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine("Run 'moveit help' for usage.");
    return ExitCodes.Usage;
}

switch (command.Name)
{
    case "help":
        Console.WriteLine(HelpText.Full);
        return ExitCodes.Success;
    case "version":
        Console.WriteLine($"MoveIT {ToolInfo.Version}");
        return ExitCodes.Success;
    case "sid":
        return SidCommand.Run(command);
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine($"'{command.Name}' only runs on Windows.");
    return ExitCodes.Usage;
}

try
{
    return command.Name switch
    {
        "list" => ListCommand.Run(command),
        "migrate" => MigrateCommand.Run(command),
        "rollback" => RollbackCommand.Run(command),
        _ => throw new CommandLineException($"Unknown command '{command.Name}'."),
    };
}
catch (CommandLineException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine("Run 'moveit help' for usage.");
    return ExitCodes.Usage;
}
catch (Exception e)
{
    Console.Error.WriteLine($"Unexpected error: {e.Message}");
    Console.Error.WriteLine(e);
    return ExitCodes.Failed;
}
