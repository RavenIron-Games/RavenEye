using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RavenIron.RavenEye.Config;
using RavenIron.RavenEye.Core;

namespace RavenIron.RavenEye
{
    /// <summary>
    /// The entry point. Binds config, starts the tick, installs the patches one class at a
    /// tick.
    ///
    /// One role-aware DLL, as in the studio's other mods: a dedicated server sends the
    /// roster, an admin's client receives it and lets vanilla draw the map, a listen host
    /// does both. The roles are told apart at RUNTIME, never at build time.
    /// </summary>
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public class RavenEye : BaseUnityPlugin
    {
        public const string PluginId   = "com.raveniron.raveneye";
        public const string PluginName = "RavenEye";

        /// <summary>
        /// Generated at build time from the csproj Version property (GenerateVersionConst).
        /// Never edit or hardcode: two copies of a version drift.
        /// </summary>
        public const string PluginVersion = BuildVersion.Value;

        public static RavenEye Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        /// <summary>
        /// Can this process draw anything at all? Decided at Awake, before ZNet exists.
        /// GraphicsDeviceType.Null is the headless tell that survives compiling against the
        /// client's reference assembly, in which `ZNet.IsDedicated` is a hardcoded false.
        /// </summary>
        public static bool HasRenderer =>
            UnityEngine.SystemInfo.graphicsDeviceType !=
            UnityEngine.Rendering.GraphicsDeviceType.Null;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModConfig.Bind(base.Config);

            // A plain MonoBehaviour driven from Update — deliberately NOT a coroutine.
            // Added BEFORE patching, so the roster and the unlock survive a patch target
            // that a future Valheim update removes.
            gameObject.AddComponent<RavenEyeTick>();

            // One class at a time, each in its own try/catch: `PatchAll` stops at the first
            // target that no longer resolves and takes everything after it down with it.
            _harmony = new Harmony(PluginId);
            List<string> failed = PatchInstall.Each(
                typeof(RavenEye).Assembly.GetTypes()
                    .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0),
                t => _harmony.CreateClassProcessor(t).Patch(),
                (t, ex) => Log.LogError(
                    $"patch {t.Name} could not be installed ({ex.GetType().Name}: {ex.Message}); " +
                    "that part of the mod is off, the rest carries on. A Valheim update probably moved its target."));

            // Proof of life. A silent success and a silent no-op are indistinguishable from
            // outside the game, so this line exists before there is anything to report.
            Log.LogInfo(
                $"{PluginName} v{PluginVersion} loaded — renderer={HasRenderer}, " +
                $"patches={_harmony.GetPatchedMethods().Count()}" +
                (failed.Count > 0 ? $" ({failed.Count} FAILED: {string.Join(", ", failed)})" : "") +
                ", role is decided when a world loads.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            Instance = null;
        }
    }
}
