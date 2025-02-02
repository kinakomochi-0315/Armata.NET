using System.Runtime.CompilerServices;

namespace Armata.NET;

[Flags]
public enum LogLevel
{
    Error = 1,
    Warn = 2 | Error,
    Info = 4 | Warn,
    Debug = 8 | Info
}

public static class DebugConsole
{
    public static LogLevel Level { get; set; } = LogLevel.Debug;

    public static void Debug(object message, [CallerMemberName] string memberName = "")
    {
        if ((Level & LogLevel.Debug) == 0) return;

        Console.ForegroundColor = ConsoleColor.Cyan;
        WriteLog("DEBUG", memberName, Environment.CurrentManagedThreadId, message);
    }

    public static void Info(object message, [CallerMemberName] string memberName = "")
    {
        if ((Level & LogLevel.Info) == 0) return;

        Console.ResetColor();
        WriteLog("INFO", memberName, Environment.CurrentManagedThreadId, message);
    }

    public static void Warn(object message, [CallerMemberName] string memberName = "")
    {
        if ((Level & LogLevel.Warn) == 0) return;

        Console.ForegroundColor = ConsoleColor.Yellow;
        WriteLog("WARN", memberName, Environment.CurrentManagedThreadId, message);
    }

    public static void Error(object message, [CallerMemberName] string memberName = "")
    {
        if ((Level & LogLevel.Error) == 0) return;

        Console.ForegroundColor = ConsoleColor.Red;
        WriteLog("ERROR", memberName, Environment.CurrentManagedThreadId, message);
    }

    private static void WriteLog(string level, string memberName, int threadId, object message)
    {
        var now = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine($"[{level}] {now} {memberName}[{threadId}]: {message}");
    }
}