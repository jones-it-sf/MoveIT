namespace MoveIT.Cli;

internal sealed class CommandLineException(string message) : Exception(message);

internal sealed class ParsedCommand(string name, IReadOnlyDictionary<string, string?> options)
{
    public string Name { get; } = name;

    public IEnumerable<string> OptionNames => options.Keys;

    public bool Has(string option) => options.ContainsKey(option);

    public string? Get(string option) => options.TryGetValue(option, out var value) ? value : null;
}

internal static class CommandLine
{
    private sealed record CommandSpec(string[] Flags, string[] Values);

    private static readonly Dictionary<string, CommandSpec> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["list"] = new(["all"], []),
        ["migrate"] = new(
            ["create", "admin", "must-change-password", "overwrite-target-profile", "leave-entra", "reboot", "what-if", "force", "yes", "verbose", "all", "generate-passwords", "disable-source", "delete-source"],
            ["source", "target", "full-name", "password-env", "acl-mode", "config", "log", "backup-root", "map", "exclude", "name-prefix", "credentials-out", "after-each", "after-all"]),
        ["rollback"] = new(["delete-created-account", "yes", "verbose"], ["backup", "log"]),
        ["sid"] = new([], ["object-id", "sid"]),
        ["help"] = new([], []),
        ["version"] = new([], []),
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["-h"] = "help",
        ["--help"] = "help",
        ["-?"] = "help",
        ["/?"] = "help",
        ["--version"] = "version",
    };

    public static ParsedCommand Parse(IReadOnlyList<string> args)
    {
        var empty = new Dictionary<string, string?>();
        if (args.Count == 0)
        {
            return new ParsedCommand("help", empty);
        }

        var name = Aliases.GetValueOrDefault(args[0], args[0]).ToLowerInvariant();
        if (!Commands.TryGetValue(name, out var spec))
        {
            throw new CommandLineException($"Unknown command '{args[0]}'.");
        }

        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Count; i++)
        {
            var argument = args[i];
            if (Aliases.TryGetValue(argument, out var alias) && alias == "help")
            {
                return new ParsedCommand("help", empty);
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
            {
                throw new CommandLineException($"Unexpected argument '{argument}'.");
            }

            var option = argument[2..];
            string? inlineValue = null;
            int equals = option.IndexOf('=');
            if (equals >= 0)
            {
                inlineValue = option[(equals + 1)..];
                option = option[..equals];
            }
            option = option.ToLowerInvariant();

            if (options.ContainsKey(option))
            {
                throw new CommandLineException($"--{option} was given more than once.");
            }

            if (spec.Flags.Contains(option))
            {
                if (inlineValue is not null)
                {
                    throw new CommandLineException($"--{option} does not take a value.");
                }
                options[option] = null;
            }
            else if (spec.Values.Contains(option))
            {
                var value = inlineValue;
                if (value is null && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new CommandLineException($"--{option} needs a value.");
                }
                options[option] = value;
            }
            else
            {
                throw new CommandLineException($"Unknown option '--{option}' for '{name}'.");
            }
        }

        return new ParsedCommand(name, options);
    }
}
