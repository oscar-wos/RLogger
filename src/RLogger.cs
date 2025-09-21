using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RLogger.Enums;

namespace RLogger;

public class Logger : IDisposable
{
    private readonly string _path;
    private readonly ILogger _logger;
    private readonly long _updateInterval;

    private readonly object _logLock = new();
    private readonly Channel<LogEntry> _logChannel = Channel.CreateUnbounded<LogEntry>();
    private readonly ConcurrentDictionary<string, StreamWriter> _logWriters = [];

    private static DateTime _cachedTime = DateTime.Now;
    private static long _cachedTimestamp = Stopwatch.GetTimestamp();

    private volatile string _currentDate = _cachedTime.ToString("yyyy-MM-dd");
    private volatile int _lastDay = _cachedTime.Day;

    public Logger(string path, ILogger logger, uint accuracy = 1)
    {
        _path = path;
        _logger = logger;
        _updateInterval = Stopwatch.Frequency / (1000 / accuracy);

        _ = Task.Run(WriteLogs);
    }

    public void Debug(string message, Exception? exception = null)
    {
        _logger.LogDebug(exception, "{message}", message);

        _ = _logChannel.Writer.TryWrite(
            new LogEntry(GetTimestamp(), LogType.Debug, message, exception)
        );
    }

    public void Information(string message, Exception? exception = null)
    {
        _logger.LogInformation(exception, "{message}", message);

        _ = _logChannel.Writer.TryWrite(
            new LogEntry(GetTimestamp(), LogType.Information, message, exception)
        );
    }

    public void Warning(string message, Exception? exception = null)
    {
        _logger.LogWarning(exception, "{message}", message);

        _ = _logChannel.Writer.TryWrite(
            new LogEntry(GetTimestamp(), LogType.Warning, message, exception)
        );
    }

    public void Error(string message, Exception? exception = null)
    {
        _logger.LogError(exception, "{message}", message);

        _ = _logChannel.Writer.TryWrite(
            new LogEntry(GetTimestamp(), LogType.Error, message, exception)
        );
    }

    public Exception Critical(string message, Exception? exception = null)
    {
        _ = _logChannel.Writer.TryWrite(
            new LogEntry(GetTimestamp(), LogType.Critical, message, exception)
        );

        lock (_logLock)
        {
            foreach (StreamWriter writer in _logWriters.Values)
            {
                writer.Flush();
            }
        }

        return new Exception(message, exception);
    }

    public void Dispose()
    {
        _logChannel.Writer.Complete();

        lock (_logLock)
        {
            foreach (StreamWriter streamWriter in _logWriters.Values)
            {
                streamWriter.Flush();
                streamWriter.Dispose();
            }

            _logWriters.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private DateTime GetTimestamp()
    {
        long currentTimestamp = Stopwatch.GetTimestamp();

        if (currentTimestamp - _cachedTimestamp > _updateInterval)
        {
            _cachedTime = DateTime.Now;
            _cachedTimestamp = currentTimestamp;
        }

        return _cachedTime;
    }

    private async Task WriteLogs()
    {
        await foreach (LogEntry entry in _logChannel.Reader.ReadAllAsync())
        {
            WriteToFile(entry.Timestamp, entry.Type, entry.Message, entry.Exception);
        }
    }

    private void WriteToFile(DateTime timestamp, LogType type, string message, Exception? exception)
    {
        try
        {
            lock (_logLock)
            {
                string dateString = FormatDate(timestamp);
                string logString = FormatLog(timestamp, type, message, exception);

                string specificFile = $"{dateString}_{type}.log";
                StreamWriter specificWriter = GetWriter(specificFile);

                string globalFile = $"{dateString}_All.log";
                StreamWriter globalWriter = GetWriter(globalFile);

                specificWriter.WriteLine(logString);
                globalWriter.WriteLine(logString);
            }
        }
        catch (Exception ex)
        {
            throw new Exception("RLogger.WriteToFile()", ex);
        }
    }

    private StreamWriter GetWriter(string fileName) =>
        _logWriters.GetOrAdd(
            fileName,
            key =>
            {
                if (!Directory.Exists(_path))
                {
                    _ = Directory.CreateDirectory(_path);
                }

                string filePath = Path.Combine(_path, key);
                return new StreamWriter(filePath, append: true);
            }
        );

    private string FormatDate(DateTime timestamp)
    {
        if (timestamp.Day != _lastDay)
        {
            _currentDate = timestamp.ToString("yyyy-MM-dd");
            _lastDay = timestamp.Day;
        }

        return _currentDate;
    }

    private static string FormatLog(
        DateTime timestamp,
        LogType type,
        string message,
        Exception? exception
    ) =>
        $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{type}] {message}{(exception is { } ? $"\n{exception}" : "")}";

    private record LogEntry(DateTime Timestamp, LogType Type, string Message, Exception? Exception);
}
