using MoveIT.Core.Security;

namespace MoveIT.Cli.Commands;

/// <summary>Converts between Entra object IDs and the S-1-12-1 SIDs Windows uses for Entra accounts.</summary>
internal static class SidCommand
{
    public static int Run(ParsedCommand command)
    {
        if (command.Get("object-id") is { } objectIdText)
        {
            if (!Guid.TryParse(objectIdText, out var objectId))
            {
                throw new CommandLineException($"'{objectIdText}' is not a valid object ID (GUID).");
            }
            Console.WriteLine(Sid.FromEntraObjectId(objectId));
            return ExitCodes.Success;
        }

        if (command.Get("sid") is { } sidText)
        {
            if (!Sid.TryParse(sidText, out var sid) || sid.ToEntraObjectId() is not { } entraObjectId)
            {
                throw new CommandLineException($"'{sidText}' is not an Entra ID user SID (S-1-12-1-...).");
            }
            Console.WriteLine(entraObjectId);
            return ExitCodes.Success;
        }

        throw new CommandLineException("Use --object-id <GUID> or --sid <S-1-12-1-...>.");
    }
}
