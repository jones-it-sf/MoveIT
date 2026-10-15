namespace MoveIT.Core.Migration;

public sealed class MigrationException(string message, IReadOnlyList<string>? details = null) : Exception(message)
{
    public IReadOnlyList<string> Details { get; } = details ?? [];
}
