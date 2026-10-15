namespace MoveIT.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Writes to the console and, when configured, to a log file. Safe to use from multiple threads.</summary>
public sealed class Logger : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter? _file;
    private int _warnings;
    private int _errors;

    public Logger(string? filePath, bool verbose)
    {
        Verbose = verbose;
        FilePath = filePath;
        if (filePath is not null)
        {
            var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _file = new StreamWriter(stream) { AutoFlush = true };
        }
    }

    public bool Verbose { get; }

    public string? FilePath { get; }

    /// <summary>Raised for every message (including debug). Used by the GUI to mirror output on screen.</summary>
    public event Action<LogLevel, string>? MessageLogged;

    public int WarningCount => Volatile.Read(ref _warnings);

    public int ErrorCount => Volatile.Read(ref _errors);

    public void Debug(string message) => Write(LogLevel.Debug, message);

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Success(string message) => Write(LogLevel.Success, message);

    public void Warn(string message) => Write(LogLevel.Warning, message);

    public void Error(string message) => Write(LogLevel.Error, message);

    public void Write(LogLevel level, string message)
    {
        if (level == LogLevel.Warning)
        {
            Interlocked.Increment(ref _warnings);
        }
        else if (level == LogLevel.Error)
        {
            Interlocked.Increment(ref _errors);
        }

        MessageLogged?.Invoke(level, message);

        lock (_gate)
        {
            _file?.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}");

            if (level == LogLevel.Debug && !Verbose)
            {
                return;
            }

            var (prefix, color) = level switch
            {
                LogLevel.Debug => ("  ", ConsoleColor.DarkGray),
                LogLevel.Success => ("OK ", ConsoleColor.Green),
                LogLevel.Warning => ("WARNING: ", ConsoleColor.Yellow),
                LogLevel.Error => ("ERROR: ", ConsoleColor.Red),
                _ => ("", Console.ForegroundColor),
            };

            var writer = level >= LogLevel.Warning ? Console.Error : Console.Out;
            var original = Console.ForegroundColor;
            Console.ForegroundColor = color;
            writer.WriteLine(prefix + message);
            Console.ForegroundColor = original;
        }
    }

    public void Dispose() => _file?.Dispose();
}
