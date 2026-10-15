namespace MoveIT.Core;

public static class ToolInfo
{
    public static string Version { get; } = typeof(ToolInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
