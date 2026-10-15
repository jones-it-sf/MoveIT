namespace MoveIT.Core.Util;

/// <summary>Works out how to launch a hook script based on its file extension. Pure, so it is unit tested.</summary>
public sealed record ScriptCommand(string FileName, IReadOnlyList<string> Arguments)
{
    public static ScriptCommand For(string scriptPath)
    {
        var fullPath = Path.GetFullPath(scriptPath);
        var system = Environment.SystemDirectory;

        return Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".ps1" => new ScriptCommand(
                Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", fullPath]),
            ".cmd" or ".bat" => new ScriptCommand(Path.Combine(system, "cmd.exe"), ["/d", "/c", fullPath]),
            ".exe" => new ScriptCommand(fullPath, []),
            var other => throw new ArgumentException(
                $"MoveIT can only run .ps1, .cmd, .bat or .exe hook scripts, not '{other}'.", nameof(scriptPath)),
        };
    }
}
