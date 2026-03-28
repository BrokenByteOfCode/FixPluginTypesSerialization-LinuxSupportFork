using System;
using System.Text;
using FixPluginTypesSerialization.Util;

namespace FixPluginTypesSerialization
{
    internal static class DebugLogs
    {
        static DebugLogs()
        {
            System.Console.WriteLine(">>> DEBUG_LOGS_BOOTED <<<");
        }

        public static void Log(int opcode)
        {
            System.Console.WriteLine($">>> [OPCode] {opcode}");
        }

        public static unsafe void LogDiscovery(string name, IntPtr address)
        {
            if (address == IntPtr.Zero)
            {
                FixPluginTypesSerialization.Log.Warning($"[Discovery] {name} NOT FOUND");
                return;
            }

            FixPluginTypesSerialization.Log.Info($"[Discovery] {name} found at {address.ToString("X")}");
            
            try
            {
                HexDump(address, 16, $"Header of {name}");
            }
            catch (Exception ex)
            {
                FixPluginTypesSerialization.Log.Debug($"Could not hex dump {name}: {ex.Message}");
            }
        }

        public static unsafe void HexDump(IntPtr ptr, int size, string label = "HexDump")
        {
            if (ptr == IntPtr.Zero) return;

            byte* b = (byte*)ptr;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"--- {label} at {ptr.ToString("X")} ({size} bytes) ---");

            for (int i = 0; i < size; i += 16)
            {
                sb.Append($"{i:X4}: ");
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < size)
                        sb.Append($"{b[i + j]:X2} ");
                    else
                        sb.Append("   ");
                }
                sb.Append(" | ");
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < size)
                    {
                        char c = (char)b[i + j];
                        sb.Append(char.IsControl(c) ? '.' : c);
                    }
                }
                sb.AppendLine();
            }
            System.Console.WriteLine(sb.ToString());
        }
    }
}
