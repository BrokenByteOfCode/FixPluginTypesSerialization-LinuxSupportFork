using FixPluginTypesSerialization.UnityPlayer.Structs.Default;
using FixPluginTypesSerialization.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace FixPluginTypesSerialization.UnityPlayer
{
    // https://github.com/BepInEx/BepInEx/blob/master/BepInEx.IL2CPP/Preloader.cs#L93

    // https://github.com/knah/Il2CppAssemblyUnhollower/blob/master/UnhollowerBaseLib/Runtime/UnityVersionHandler.cs

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    internal class ApplicableToUnityVersionsSinceAttribute : Attribute
    {
        public string StartVersion { get; }

        public ApplicableToUnityVersionsSinceAttribute(string startVersion)
        {
            StartVersion = startVersion;
        }
    }

    public static class UseRightStructs
    {
        private static readonly Type[] InterfacesOfInterest;
        private static readonly Dictionary<Type, List<VersionedHandler>> VersionedHandlers = new();
        private static readonly Dictionary<Type, object> CurrentHandlers = new();

        private static Version _unityVersion;
        public static Version UnityVersion
        {
            get
            {
                if (_unityVersion == null)
                {
                    InitializeUnityVersion();
                }

                return _unityVersion;
            }
        }

        public static int LabelMemStringId { get; private set; }

        private static void InitializeUnityVersion()
        {
            // 1. Check if user provided an override in the config file
            if (!Polyfills.StringIsNullOrWhiteSpace(Config.UnityVersionOverride.Value))
            {
                if (TryInitializeUnityVersion(Config.UnityVersionOverride.Value))
                {
                    Log.Debug($"Unity version obtained from config file");
                    return;
                }
                Log.Error($"Unity version {Config.UnityVersionOverride.Value} has incorrect format.");
            }

            // 2. Try to get version from the module (works mostly on Windows PE files)
            static bool IsUnityPlayer(ProcessModule p)
            {
                return p.ModuleName.ToLowerInvariant().Contains("unityplayer");
            }

            //exe doesn't always have correct version, trying to use UnityPlayer when available
            var module = Process.GetCurrentProcess().Modules
                .Cast<ProcessModule>()
                .FirstOrDefault(IsUnityPlayer) ?? Process.GetCurrentProcess().MainModule;

            if (module?.FileVersionInfo?.FileVersion != null && TryInitializeUnityVersion(module.FileVersionInfo.FileVersion))
            {
                Log.Debug($"Unity version obtained from main application module.");
                return;
            }

            // 3. Linux/Fallback: Read the version directly from the game data files
            var ggmVersion = TryGetVersionFromGlobalGameManagers();
            if (!string.IsNullOrEmpty(ggmVersion) && TryInitializeUnityVersion(ggmVersion))
            {
                Log.Debug($"Unity version obtained from globalgamemanagers file.");
                return;
            }

            Log.Error($"Running under default Unity version. UnityVersionHandler is not initialized.");
        }

        // Added this method to extract Unity version from binary data files on Linux
        private static string TryGetVersionFromGlobalGameManagers()
        {
            try
            {
                // Find the _Data folder (e.g., Baldi_Data)
                var dataFolder = Directory.GetDirectories(BepInEx.Paths.GameRootPath, "*_Data").FirstOrDefault();
                if (dataFolder == null) return null;

                // globalgamemanagers contains the Unity version string at the beginning
                var ggmPath = Path.Combine(dataFolder, "globalgamemanagers");
                if (!File.Exists(ggmPath)) return null;

                using var fs = File.OpenRead(ggmPath);
                using var br = new BinaryReader(fs);
                
                // Read first 256 bytes which usually contains the version string
                var buffer = br.ReadBytes(256);
                var rawData = Encoding.ASCII.GetString(buffer);
                
                // Search for version pattern like 2020.3.49
                var match = Regex.Match(rawData, @"(\d{4})\.(\d{1,2})\.(\d{1,2})");
                if (match.Success)
                {
                    return match.Value;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to read globalgamemanagers: {ex.Message}");
            }
            return null;
        }

        private static bool TryInitializeUnityVersion(string version)
        {
            try
            {
                if (Polyfills.StringIsNullOrWhiteSpace(version))
                    return false;

                var parts = version.Split('.');
                var major = 0;
                var minor = 0;
                var build = 0;
                var revision = 0;

                // Issue #229 - Don't use Version.Parse("2019.4.16.14703470L&ProductVersion")
                bool success = int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out major);
                if (success && parts.Length > 1)
                    success = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minor);
                if (success && parts.Length > 2)
                    success = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out build);
                if (success && parts.Length > 3)
                    success = int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out revision);

                if (!success)
                {
                    Log.Error($"Failed to parse Unity version: {version}");
                    return false;
                }

                _unityVersion = new Version(major, minor, build, revision);
                Log.Info($"Running under Unity v{UnityVersion}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to parse Unity version: {ex}");
                return false;
            }
        }

        static UseRightStructs()
        {
            var allTypes = GetAllTypesSafe();
            InterfacesOfInterest = allTypes.Where(t => t.IsInterface && typeof(INativeStruct).IsAssignableFrom(t) && t != typeof(INativeStruct)).ToArray();

            foreach (var i in InterfacesOfInterest)
            {
                VersionedHandlers[i] = new();
            }

            foreach (var handlerImpl in allTypes.Where(t => !t.IsAbstract && InterfacesOfInterest.Any(i => i.IsAssignableFrom(t))))
            {
                foreach (var startVersion in handlerImpl.GetCustomAttributes<ApplicableToUnityVersionsSinceAttribute>())
                {
                    var instance = Activator.CreateInstance(handlerImpl);
                    foreach (var i in handlerImpl.GetInterfaces())
                    {
                        if (InterfacesOfInterest.Contains(i))
                        {
                            VersionedHandlers[i].Add(new VersionedHandler(Polyfills.VersionParse(startVersion.StartVersion), instance));
                        }
                    }
                }
            }

            foreach (var handlerList in VersionedHandlers.Values)
            {
                handlerList.Sort((a, b) => -a.version.CompareTo(b.version));
            }

            GatherUnityVersionSpecificHandlers();
            SetUnityVersionSpecificMemStringId();
        }

        private static void GatherUnityVersionSpecificHandlers()
        {
            CurrentHandlers.Clear();
            
            // Critical fix: prevent comparing null version which causes ArgumentNullException
            if (UnityVersion == null)
            {
                return;
            }

            foreach (var type in InterfacesOfInterest)
            {
                foreach (var (version, handler) in VersionedHandlers[type])
                {
                    if (version > UnityVersion) continue;

                    CurrentHandlers[type] = handler;
                    break;
                }
            }
        }

        private static T GetHandler<T>()
        {
            if (CurrentHandlers.TryGetValue(typeof(T), out var result))
            {
                return (T)result;
            }

            Log.Error($"No direct for {typeof(T).FullName} found for Unity {UnityVersion}; this likely indicates a severe error somewhere");

            throw new ApplicationException("No handler");
        }

        private static Type[] GetAllTypesSafe()
        {
            try
            {
                return typeof(UseRightStructs).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException re)
            {
                return re.Types.Where(t => t != null).ToArray();
            }
        }

        internal static T GetStruct<T>(IntPtr ptr) where T : INativeStruct
        {
            if (ptr == IntPtr.Zero)
            {
                throw new ArgumentNullException("ptr");
            }

            var @struct = GetHandler<T>();

            @struct.Pointer = ptr;

            return @struct;
        }

        private static void SetUnityVersionSpecificMemStringId()
        {
            // Default to legacy ID if version is unknown
            if (UnityVersion == null)
            {
                LabelMemStringId = 0x3a;
                return;
            }

            if (UnityVersion >= new Version(2023, 1))
            {
                LabelMemStringId = 0x9;
            }
            else if (UnityVersion >= new Version(2021, 1))
            {
                LabelMemStringId = 0x49;
            }
            else if (UnityVersion >= new Version(2020, 2))
            {
                LabelMemStringId = 0x2B;
            }
            else if (UnityVersion >= new Version(2020, 1))
            {
                LabelMemStringId = 0x2A;
            }
            else if (UnityVersion >= new Version(2019, 4))
            {
                LabelMemStringId = 0x2B;
            }
            else if (UnityVersion >= new Version(2019, 3))
            {
                LabelMemStringId = 0x2A;
            }
            else if (UnityVersion >= new Version(2019, 1))
            {
                LabelMemStringId = 0x2B;
            }
            else if (UnityVersion >= new Version(2018, 3))
            {
                LabelMemStringId = 0x45;
            }
            else if (UnityVersion >= new Version(2017, 2))
            {
                LabelMemStringId = 0x44;
            }
            else if (UnityVersion >= new Version(2017, 1))
            {
                LabelMemStringId = 0x42;
            }
            else 
            {
                LabelMemStringId = 0x3a;
            }
        }
    }
}
