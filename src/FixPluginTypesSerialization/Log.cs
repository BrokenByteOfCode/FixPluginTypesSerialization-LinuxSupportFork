using System;
using BepInEx.Logging;

namespace FixPluginTypesSerialization
{
    internal static class Log
    {
        internal static ManualLogSource _logSource;

        internal static void Init()
        {
            _logSource = Logger.CreateLogSource("FixPluginTypesSerialization");
        }

        internal static void Debug(object data)
        {
            if (_logSource == null) Console.WriteLine($"[EarlyDebug] {data}");
            else _logSource.LogDebug(data);
        }
        internal static void Error(object data)
        {
            if (_logSource == null) Console.WriteLine($"[EarlyError] {data}");
            else _logSource.LogError(data);
        }
        internal static void Fatal(object data)
        {
            if (_logSource == null) Console.WriteLine($"[EarlyFatal] {data}");
            else _logSource.LogFatal(data);
        }
        internal static void Info(object data)
        {
            if (_logSource == null) Console.WriteLine($"[EarlyInfo] {data}");
            else _logSource.LogInfo(data);
        }
        internal static void Message(object data)
        {
            if (_logSource == null) Console.WriteLine($"[EarlyMessage] {data}");
            else _logSource.LogMessage(data);
        }
        internal static void Warning(object data)
        {
            if (_logSource == null) Console.WriteLine($"[EarlyWarning] {data}");
            else _logSource.LogWarning(data);
        }
    }
}