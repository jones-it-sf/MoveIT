namespace MoveIT.Cli;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int CompletedWithWarnings = 2;
    public const int Usage = 3;

    /// <summary>Pre-flight checks failed or the user cancelled; nothing on the computer was changed.</summary>
    public const int NothingChanged = 4;
}
