using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SteamAccountDataFetcher;

internal static class Logger
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
