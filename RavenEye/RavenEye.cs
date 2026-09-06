using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RavenIron.RavenEye.Config;
using RavenIron.RavenEye.Core;

namespace RavenIron.RavenEye
{
    /// <summary>
    /// The entry point. Binds config, installs the two postfixes and the console, starts the
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

            _harmony = new Harmony(PluginId);
            _harmony.PatchAll();

            // A plain MonoBehaviour driven from Update — deliberately NOT a coroutine.
            gameObject.AddComponent<RavenEyeTick>();

            // Proof of life. A silent success and a silent no-op are indistinguishable from
            // outside the game, so this line exists before there is anything to report.
            Log.LogInfo(
                $"{PluginName} v{PluginVersion} loaded — renderer={HasRenderer}, " +
                $"patches={_harmony.GetPatchedMethods().Count()}, " +
                $"role is decided when a world loads.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            Instance = null;
        }
    }
}
