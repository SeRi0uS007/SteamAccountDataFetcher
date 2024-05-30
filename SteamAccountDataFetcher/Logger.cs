using System.Runtime.CompilerServices;

namespace SteamAccountDataFetcher;

static class Logger
{
    public enum Level
    {
        Debug,
        Info,
        Warning,
        Error
    }

    internal static void Log(string message, Level level = Level.Info, [CallerMemberName] string callerName = "")
    {
        string timeString = DateTime.Now.ToString("dd.MM.yy - HH:mm:ss.ff");
        Console.WriteLine($"|{level}| [{timeString}] ({callerName}) -> {message}");
    }
}
