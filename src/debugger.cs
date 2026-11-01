using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MineTogether
{
    /// <summary>
    /// Everything needed to answer "why did that not work" without a debugger attached: one log
    /// file per launch, the environment, which Harmony patches actually applied, which reflection
    /// bindings resolved, the settings in force, and a counter per module.
    ///
    /// The file is truncated at launch, so it always describes the build that is installed. It
    /// lives beside the config folder rather than in the game root, and the whole thing is behind
    /// the debug switch except for errors, which are always written.
    /// </summary>
    internal static class Diag
    {
        private const string FolderName = "BSBLogs";
        private const string FileName = "MineTogether.log";

        private static ConfigEntry<bool> _enabled;
        private static StreamWriter _writer;
        private static readonly HashSet<string> Once = new HashSet<string>();

        // Always collected: a report is still useful when the switch went on mid-session.
        internal static int SessionsStarted;
        internal static int SessionsCompleted;
        internal static int SessionsStopped;
        internal static int DropsScaled;
        internal static int Collapses;
        internal static int AoeSwings;
        internal static int SyncApplied;
        internal static int SyncRejected;
        internal static int Errors;

        internal static bool Enabled => _enabled != null && _enabled.Value;

        internal static string Path
        {
            get { return System.IO.Path.Combine(Paths.BepInExRootPath, FolderName, FileName); }
        }

        internal static void Bind(ConfigFile config)
        {
            _enabled = config.Bind("Debug", "Enable Debug", false,
                "Write diagnostics to BepInEx/BSBLogs/MineTogether.log: environment, applied patches, " +
                "resolved bindings, settings and what each module did. Errors are written even when this is off.");
        }

        internal static void Open()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.Combine(Paths.BepInExRootPath, FolderName));
                FileStream stream = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                Write($"--- Mine Together v{Plugin.Version} [{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] ---");
                Write($"[Env] assembly {AssemblyLocation()} sha256 {AssemblyHash()}");
            Plugin.Instance?.Log.LogInfo($"[env] {AssemblyLocation()} sha256 {AssemblyHash()}");
                Write($"[Env] unity {Application.unityVersion} | bepinex {typeof(Paths).Assembly.GetName().Version} | " +
                      $"os {Environment.OSVersion} | pid {System.Diagnostics.Process.GetCurrentProcess().Id}");
            }
            catch (Exception e)
            {
                Plugin.Instance?.Log.LogError("[diag] cannot open " + FileName + ": " + e.Message);
            }
        }

        /// <summary>Written after binding; before that every switch legitimately reads false.</summary>
        internal static void State()
        {
            if (!Enabled) return;
            Write($"[Env] {Switches.Describe()}");
        }

        internal static void Close()
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch (Exception)
            {
                // Nothing useful to do while the game is closing.
            }
            _writer = null;
        }

        internal static void Trace(string tag, string message)
        {
            if (!Enabled) return;
            Write($"{tag} {message}");
        }

        /// <summary>Log once per process, for facts that would repeat on every object.</summary>
        internal static void TraceOnce(string key, string tag, string message)
        {
            if (!Enabled || !Once.Add(key)) return;
            Write($"{tag} {message}");
        }

        internal static void Error(string tag, string message)
        {
            Errors++;
            Write($"[Error] {tag} {message}");
        }

        internal static void Error(string tag, Exception e)
        {
            Errors++;
            Write($"[Error] {tag} {e.GetType().Name}: {e.Message}");
        }

        /// <summary>Which of this mod's patches are actually attached, and to what.</summary>
        internal static void PatchInventory(Harmony harmony)
        {
            if (!Enabled) return;
            int methods = 0, ours = 0;
            foreach (MethodBase method in harmony.GetPatchedMethods())
            {
                methods++;
                Patches info = Harmony.GetPatchInfo(method);
                if (info == null) continue;
                ours += Report(info.Prefixes, "prefix", method);
                ours += Report(info.Postfixes, "postfix", method);
                ours += Report(info.Transpilers, "transpiler", method);
                ours += Report(info.Finalizers, "finalizer", method);
            }
            Write($"[Patches] {ours} of ours across {methods} patched methods");
        }

        /// <summary>Whether every by-name game member resolved. A MISSING line explains silence.</summary>
        internal static void Bindings()
        {
            if (!Enabled) return;
            int resolved = 0, missing = 0;
            foreach (FieldInfo field in typeof(GameRefs).GetFields(
                         BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (field.GetValue(null) != null)
                {
                    resolved++;
                    continue;
                }
                missing++;
                Write($"[Bindings] MISSING {field.Name}");
            }
            Write($"[Bindings] {resolved} resolved, {missing} missing");
        }

        internal static void ConfigSnapshot(ConfigFile config)
        {
            if (!Enabled || config == null) return;
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> entry in config)
            {
                Write($"[Config] {entry.Key.Section}/{entry.Key.Key} = {entry.Value.BoxedValue}");
            }
        }

        /// <summary>
        /// Run every startup dump, each isolated: a defect in the reporting must not stop the mod
        /// from loading. Errors land in the log file even when the debug switch is off.
        /// </summary>
        internal static void StartupDumps(Harmony harmony, ConfigFile config)
        {
            try { Bindings(); } catch (Exception e) { Error("[Bindings]", e); }
            try { PatchInventory(harmony); } catch (Exception e) { Error("[Patches]", e); }
            try { ConfigSnapshot(config); } catch (Exception e) { Error("[Config]", e); }
            Summary();
        }

        /// <summary>Counters and switch state; safe to call on every world load.</summary>
        internal static void Summary()
        {
            if (!Enabled) return;
            Write($"[State] {Switches.Describe()}");
            Write($"[Stats] sessions={SessionsStarted} completed={SessionsCompleted} stopped={SessionsStopped} " +
                  $"drops={DropsScaled} collapses={Collapses} aoe={AoeSwings} " +
                  $"sync={SyncApplied}/{SyncRejected} errors={Errors}");
        }

        private static string AssemblyLocation()
        {
            try
            {
                return typeof(Plugin).Assembly.Location;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        /// <summary>SHA-256 of the loaded DLL, so build-info.txt can be matched without guessing.</summary>
        private static string AssemblyHash()
        {
            try
            {
                string path = typeof(Plugin).Assembly.Location;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "unavailable";
                using (FileStream stream = File.OpenRead(path))
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream);
                    StringBuilder text = new StringBuilder(16);
                    for (int i = 0; i < 8; i++) text.Append(hash[i].ToString("x2"));
                    return text.ToString();
                }
            }
            catch (Exception)
            {
                return "unavailable";
            }
        }

        private static int Report(IList<Patch> patches, string kind, MethodBase target)
        {
            int count = 0;
            for (int i = 0; i < patches.Count; i++)
            {
                Patch patch = patches[i];
                if (patch == null || patch.owner != Plugin.Id) continue;
                count++;
                Write($"[Patch] {kind} {Method(patch.PatchMethod)} -> {Method(target)}");
            }
            return count;
        }

        private static string Method(MethodBase method)
        {
            if (method == null) return "?";
            ParameterInfo[] parameters = method.GetParameters();
            StringBuilder text = new StringBuilder(method.Name).Append('(');
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(parameters[i].ParameterType.Name);
            }
            return text.Append(')').ToString();
        }

        private static void Write(string message)
        {
            try
            {
                _writer?.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + message);
            }
            catch (Exception)
            {
                // A full disk must not break gameplay.
            }
            if (message.StartsWith("[Error]", StringComparison.Ordinal))
            {
                Plugin.Instance?.Log.LogError(message);
            }
            else if (Enabled)
            {
                Plugin.Instance?.Log.LogInfo(message);
            }
        }
    }
}
