using MoveIT.Core.Logging;

namespace MoveIT.Core.Acl;

/// <summary>Shows the first few failures on screen and sends the rest to the log file only.</summary>
internal sealed class FailureRecorder(Logger log, string area)
{
    private const int MaxOnScreen = 25;
    private long _count;

    public long Count => Interlocked.Read(ref _count);

    public void Record(string path, string reason)
    {
        long count = Interlocked.Increment(ref _count);
        var message = $"{area}: {path}: {reason}";
        if (count <= MaxOnScreen)
        {
            log.Warn(message);
        }
        else
        {
            log.Debug(message);
        }
    }

    public void Summarize()
    {
        if (Count > MaxOnScreen)
        {
            log.Warn($"{area}: {Count - MaxOnScreen:N0} more failures are listed in the log file.");
        }
    }
}
