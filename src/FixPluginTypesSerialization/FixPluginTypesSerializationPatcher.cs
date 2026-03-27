using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using FixPluginTypesSerialization.Patchers;
using FixPluginTypesSerialization.Util;
using Mono.Cecil;

namespace FixPluginTypesSerialization
{
    internal static class FixPluginTypesSerializationPatcher
    {
        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        public static List<string> PluginPaths = 
            Directory.GetFiles(BepInEx.Paths.PluginPath, "*.dll", SearchOption.AllDirectories)
            .Where(f => IsNetAssembly(f))
            .ToList();
        public static List<string> PluginNames = PluginPaths.Select(p => Path.GetFileName(p)).ToList();

        public static bool IsNetAssembly(string fileName)
        {
            try
            {
                AssemblyName.GetAssemblyName(fileName);
            }
            catch (BadImageFormatException)
            {
                return false;
            }

            return true;
        }

        public static void Patch(AssemblyDefinition ass)
        {
        }

        public static void Initialize()
        {
            Log.Init();

            try
            {
                InitializeInternal();
            }
            catch (Exception e)
            {
                Log.Error($"Failed to initialize plugin types serialization fix: ({e.GetType()}) {e.Message}. Some plugins may not work properly.");
                Log.Error(e);
            }
        }

        private static void InitializeInternal()
        {
            DetourUnityPlayer();
        }

        private static IntPtr GetUnityPlayerBaseAddress()
        {
            // Parsing /proc/self/maps for find libs
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                try
                {
                    var maps = File.ReadAllLines("/proc/self/maps");
                    foreach (var line in maps)
                    {
                        // Finding default loading UnityPlayer.so (offset is 00000000)
                        if (line.Contains("UnityPlayer.so") && line.Contains(" 00000000 "))
                        {
                            var addrStr = line.Split('-')[0];
                            IntPtr baseAddr = new IntPtr(Convert.ToInt64(addrStr, 16));
                            Log.Info($"Linux UnityPlayer.so Base Address found: {baseAddr.ToString("X")}");
                            return baseAddr;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error reading /proc/self/maps: {ex.Message}");
                }
            }

            // Fallback for Windows
            static bool IsUnityPlayer(ProcessModule p)
            {
                return p.ModuleName.ToLowerInvariant().Contains("unityplayer");
            }

            var proc = Process.GetCurrentProcess().Modules
                .Cast<ProcessModule>()
                .FirstOrDefault(IsUnityPlayer) ?? Process.GetCurrentProcess().MainModule;

            Log.Info($"UnityPlayer Base Address found via Process Modules: {proc.BaseAddress.ToString("X")}");
            return proc.BaseAddress;
        }

        private static unsafe void DetourUnityPlayer()
        {
            var unityDllPath = Path.Combine(BepInEx.Paths.GameRootPath, "UnityPlayer.dll");
            
            // Older Unity builds had all functionality in .exe instead of UnityPlayer.dll
            if (!File.Exists(unityDllPath))
            {
                unityDllPath = BepInEx.Paths.ExecutablePath;
            }

            IntPtr baseAddress = GetUnityPlayerBaseAddress();
            
            if (baseAddress == IntPtr.Zero)
            {
                Log.Error("Could not find UnityPlayer Base Address. Aborting detour.");
                return;
            }

            var patternDiscoverer = new PatternDiscoverer(baseAddress, unityDllPath);
            CommonUnityFunctions.Init(patternDiscoverer);

            var awakeFromLoadPatcher = new AwakeFromLoad();
            var isAssemblyCreatedPatcher = new IsAssemblyCreated();
            var isFileCreatedPatcher = new IsFileCreated();
            var scriptingManagerDeconstructorPatcher = new ScriptingManagerDeconstructor();
            var convertSeparatorsToPlatformPatcher = new ConvertSeparatorsToPlatform();
            
            awakeFromLoadPatcher.Patch(patternDiscoverer, Config.MonoManagerAwakeFromLoadOffset);
            isAssemblyCreatedPatcher.Patch(patternDiscoverer, Config.MonoManagerIsAssemblyCreatedOffset);
            if (!IsAssemblyCreated.IsApplied)
            {
                isFileCreatedPatcher.Patch(patternDiscoverer, Config.IsFileCreatedOffset);
            }
            convertSeparatorsToPlatformPatcher.Patch(patternDiscoverer, Config.ConvertSeparatorsToPlatformOffset);
            scriptingManagerDeconstructorPatcher.Patch(patternDiscoverer, Config.ScriptingManagerDeconstructorOffset);
        }
    }
}