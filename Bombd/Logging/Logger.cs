using System.Collections.Concurrent;
using System.Diagnostics;
using Bombd.Core;

namespace Bombd.Logging;

public class Logger
{
    private static readonly ConcurrentQueue<LogEntry> LogQueue = new();
    private static LogLevel MaxLevel = LogLevel.Info;

    static Logger()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                if (LogQueue.TryDequeue(out LogEntry log) && log.Level <= MaxLevel)
                {
                    string now = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

                    Console.ForegroundColor = GetLogColor(log.Level);
                    Console.Write($"[{now}] [{log.Level}] [{log.Type}]: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(log.Message);
                }

                Thread.Sleep(25);
            }
        });

        thread.Start();
    }

    private static ConsoleColor GetLogColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Debug => ConsoleColor.Magenta,
            _ => ConsoleColor.White
        };
    }

    public static void SetLogMaxLevel(LogLevel level) => MaxLevel = level;

    public static void LogError<T>(string message) => Log<T>(LogLevel.Error, message);
    public static void LogWarning<T>(string message) => Log<T>(LogLevel.Warning, message);
    public static void LogInfo<T>(string message) => Log<T>(LogLevel.Info, message);
    
    [Conditional("DEBUG")]
    public static void LogDebug<T>(string message) => Log<T>(LogLevel.Debug, message);
    
    [Conditional("DEBUG")]
    public static void LogTrace<T>(string message) => Log<T>(LogLevel.Trace, message);
    
    public static void Log<T>(LogLevel level, string message)
    {
        LogQueue.Enqueue(new LogEntry { Level = level, Message = message, Type = typeof(T).Name });
    }

    public static void LogError(Type type, string message) => Log(type, LogLevel.Error, message);
    public static void LogWarning(Type type, string message) => Log(type, LogLevel.Warning, message);
    public static void LogInfo(Type type, string message) => Log(type, LogLevel.Info, message);
    
    [Conditional("DEBUG")]
    public static void LogDebug(Type type, string message) => Log(type, LogLevel.Debug, message);
    
    [Conditional("DEBUG")]
    public static void LogTrace(Type type, string message) => Log(type, LogLevel.Trace, message);

    public static void Log(Type type, LogLevel level, string message)
    {
        LogQueue.Enqueue(new LogEntry { Level = level, Message = message, Type = type.Name });
    }
}