using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RLogger.Enums;

namespace RLogger;

public class Logger : IDisposable
{
    private readonly string _path;
    private readonly ILogger? _logger;

    private readonly long _updateInterval;
    private readonly Timer _flushTimer;
    private readonly Func<TimeOnly, string> _timeFormatter;

    private readonly object _logLock = new();
    private readonly Channel<LogEntry> _logChannel = Channel.CreateUnbounded<LogEntry>();
    private readonly Dictionary<string, StreamWriter> _logWriters = [];
    private bool _hasDirtyWriters = false;

    private DateOnly _cachedDate = DateOnly.FromDateTime(DateTime.Now);
    private string _cachedDateString = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
    private string _cachedTimeString = TimeOnly.FromDateTime(DateTime.Now).ToString("HH:mm:ss");
    private long _cachedTimestamp = Stopwatch.GetTimestamp();

    public Logger(string path, ILogger? logger = null, uint accuracy = 1000)
    {
        _path = path;
        _logger = logger;

        _updateInterval = Stopwatch.Frequency / 1000 * accuracy;
        _flushTimer = new Timer(FlushLogs, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        _timeFormatter = GetTimeFormatter(accuracy);

        _ = Task.Run(WriteLogs);
    }

    public void Debug(string message, Exception? exception = null)
    {
        _logger?.LogDebug(exception, "{message}", message);

        _ = _logChannel.Writer.TryWrite(
            new LogEntry(GetTimestamp(), LogType.Debug, message, exception)
        );
    }

    public void Information(string message, Exception? exception = null)
    {
        _logger?.LogInformation(exception, "{message}", message);

        _ = _logChannel.Writer.TryWrite(
            new LogEntry(GetTimestamp(), LogType.Information, message, exception)
        );
    }

    public void Warning(string message, Exception? exception = null)
    {
        _logger?.LogWarning(exception, "{message}", message);

        _ = _logChannel.Writer.TryWrite(
            new LogEntry(GetTimestamp(), LogType.Warning, message, exception)
        );
    }

    public void Error(string message, Exception? exception = null)
    {
        _logger?.LogError(exception, "{message}", message);

        _ = _logChannel.Writer.TryWrite(
            new LogEntry(GetTimestamp(), LogType.Error, message, exception)
        );
    }

    public Exception Critical(string message, Exception? exception = null)
    {
        _ = _logChannel.Writer.TryWrite(
            new LogEntry(GetTimestamp(), LogType.Critical, message, exception)
        );

        FlushLogs(null);
        return new Exception(message, exception);
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        _logChannel.Writer.Complete();

        lock (_logLock)
        {
            foreach (StreamWriter streamWriter in _logWriters.Values)
            {
                streamWriter.Flush();
                streamWriter.Dispose();
            }

            _logWriters.Clear();
            _hasDirtyWriters = false;
        }

        GC.SuppressFinalize(this);
    }

    private string GetTimestamp()
    {
        long currentTimestamp = Stopwatch.GetTimestamp();

        if (currentTimestamp - _cachedTimestamp > _updateInterval)
        {
            DateTime dateTime = DateTime.Now;
            DateOnly date = DateOnly.FromDateTime(dateTime);

            if (_cachedDate != date)
            {
                _cachedDate = date;
                _cachedDateString = date.ToString("yyyy-MM-dd");
            }

            _cachedTimeString = _timeFormatter(TimeOnly.FromDateTime(dateTime));
            _cachedTimestamp = currentTimestamp;
        }

        return _cachedTimeString;
    }

    private async Task WriteLogs()
    {
        await foreach (LogEntry entry in _logChannel.Reader.ReadAllAsync())
        {
            WriteLog(entry.Timestamp, entry.Type, entry.Message, entry.Exception);
        }
    }

    private void WriteLog(string timestamp, LogType type, string message, Exception? exception)
    {
        try
        {
            lock (_logLock)
            {
                string logString = FormatLog(timestamp, type, message, exception);

                string specificFile = $"{_cachedDateString}_{type}.log";
                StreamWriter specificWriter = GetWriter(specificFile);

                string globalFile = $"{_cachedDateString}_All.log";
                StreamWriter globalWriter = GetWriter(globalFile);

                specificWriter.WriteLine(logString);
                globalWriter.WriteLine(logString);

                _hasDirtyWriters = true;
            }
        }
        catch (Exception ex)
        {
            throw new Exception("RLogger.WriteToFile()", ex);
        }
    }

    private void FlushLogs(object? state)
    {
        if (!_hasDirtyWriters)
        {
            return;
        }

        lock (_logLock)
        {
            foreach (StreamWriter writer in _logWriters.Values)
            {
                writer.Flush();
            }

            _hasDirtyWriters = false;
        }
    }

    private StreamWriter GetWriter(string fileName)
    {
        if (_logWriters.TryGetValue(fileName, out StreamWriter? writer))
        {
            return writer;
        }

        if (!Directory.Exists(_path))
        {
            _ = Directory.CreateDirectory(_path);
        }

        string filePath = Path.Combine(_path, fileName);
        _logWriters[fileName] = new StreamWriter(filePath, append: true);

        return _logWriters[fileName];
    }

    private string FormatLog(
        string timestamp,
        LogType type,
        string message,
        Exception? exception
    ) =>
        $"[{_cachedDateString} {timestamp}] [{type}] {message}{(exception is { } ? $"\n{exception}" : "")}";

    private static Func<TimeOnly, string> GetTimeFormatter(uint accuracy) =>
        accuracy switch
        {
            <= 9 => time => time.ToString("HH:mm:ss.fff"),
            <= 99 => time => time.ToString("HH:mm:ss.ff"),
            <= 999 => time => time.ToString("HH:mm:ss.f"),
            _ => time => time.ToString("HH:mm:ss"),
        };

    private record LogEntry(string Timestamp, LogType Type, string Message, Exception? Exception);
}
