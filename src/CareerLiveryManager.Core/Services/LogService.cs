namespace CareerLiveryManager.Core.Services;

/// <summary>
/// Append-only text log in %APPDATA%\CareerLiveryManager\logs. The log is the support channel, so it
/// must never crash the app and must never grow without bound: <c>app.log</c> rolls over at about
/// 1 MB into <c>app.1.log</c> and <c>app.2.log</c> (three files, so roughly 3 MB at most).
/// </summary>
public sealed class LogService
{
    /// <summary>Size at which app.log is rolled into app.1.log.</summary>
    public const long DefaultMaxBytes = 1_000_000;

    /// <summary>How many rolled-over files are kept next to app.log (app.1.log, app.2.log).</summary>
    public const int RolledFilesToKeep = 2;

    private static readonly string DefaultLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CareerLiveryManager", "logs");

    public static readonly string LogDirectory = DefaultLogDir;

    public static readonly string LogFilePath = Path.Combine(DefaultLogDir, "app.log");

    private static readonly object Lock = new();

    private readonly string _logDir;
    private readonly string _logPath;
    private readonly long _maxBytes;
    private readonly Dictionary<string, string> _lastMessageByKey = new();

    public LogService() : this(DefaultLogDir, DefaultMaxBytes)
    {
    }

    internal LogService(string logDir, long maxBytes)
    {
        _logDir = logDir;
        _logPath = Path.Combine(logDir, "app.log");
        _maxBytes = maxBytes;
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    /// <summary>
    /// Writes <paramref name="message"/> unless the previous message logged under the same
    /// <paramref name="key"/> was identical. For lines a screen repeats on every visit ("Scanned N
    /// aircraft...") that would otherwise be most of the log.
    /// </summary>
    public void InfoIfChanged(string key, string message)
    {
        lock (Lock)
        {
            if (_lastMessageByKey.TryGetValue(key, out var last) && last == message)
            {
                return;
            }

            _lastMessageByKey[key] = message;
        }

        Info(message);
    }

    /// <summary>The path of the n-th rolled-over file (1 = most recent), e.g. app.1.log.</summary>
    public static string RolledFileName(int index) => $"app.{index}.log";

    private void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(_logDir);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

            lock (Lock)
            {
                RollOverIfNeeded();
                File.AppendAllText(_logPath, line);
            }
        }
        catch
        {
            // Logging must never crash the app - swallow any IO failure here.
        }
    }

    private void RollOverIfNeeded()
    {
        try
        {
            var info = new FileInfo(_logPath);
            if (!info.Exists || info.Length < _maxBytes)
            {
                return;
            }

            // app.1.log -> app.2.log (replacing it), then app.log -> app.1.log.
            for (var i = RolledFilesToKeep; i >= 1; i--)
            {
                var source = i == 1 ? _logPath : Path.Combine(_logDir, RolledFileName(i - 1));
                var target = Path.Combine(_logDir, RolledFileName(i));
                if (File.Exists(source))
                {
                    File.Move(source, target, overwrite: true);
                }
            }
        }
        catch
        {
            // If another program holds the file open the roll-over is simply retried on the next line.
        }
    }
}
