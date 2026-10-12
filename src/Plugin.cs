using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace OaMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BasePlugin
    {
        public const string PluginGuid = "com.shirone.oamod";
        public const string PluginName = "OA Mod";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Logs;

        internal static ConfigEntry<bool> KeepRoomOpen;
        internal static ConfigEntry<KeyCode> FlyKey;
        internal static ConfigEntry<float> FlySpeed;
        internal static ConfigEntry<float> FlyFastMultiplier;
        internal static ConfigEntry<float> LookSensitivity;

        public override void Load()
        {
            Logs = Log;

            KeepRoomOpen = Config.Bind("Room", "KeepOpen", true,
                "Host only. Reopen the room after the game starts so invited friends can join a match in progress. " +
                "The room stays hidden from the public browser. Turn it off outside our own lobbies.");
            FlyKey = Config.Bind("FlyCam", "ToggleKey", KeyCode.F6,
                "Toggle the free camera. A KeyCode name; None disables the key.");
            FlySpeed = Config.Bind("FlyCam", "Speed", 12f,
                new ConfigDescription("Units per second.", new AcceptableValueRange<float>(1f, 100f)));
            FlyFastMultiplier = Config.Bind("FlyCam", "FastMultiplier", 4f,
                new ConfigDescription("Speed multiplier while Left Shift is held.", new AcceptableValueRange<float>(1f, 20f)));
            LookSensitivity = Config.Bind("FlyCam", "LookSensitivity", 2f,
                new ConfigDescription("Degrees per unit of mouse movement.", new AcceptableValueRange<float>(0.1f, 10f)));

            // One at a time: a patch that no longer applies must not take the other one with it.
            var harmony = new Harmony(PluginGuid);
            foreach (var patch in new[] { typeof(BallCacheGrowPatch), typeof(InstantiateLog) })
            {
                try { harmony.PatchAll(patch); }
                catch (System.Exception e) { Log.LogWarning($"LateJoin: {patch.Name} unavailable ({e.GetType().Name}: {e.Message}); a mid-game join will not work properly."); }
            }

            ClassInjector.RegisterTypeInIl2Cpp<OaBehaviour>();
            var host = new GameObject("OaMod.Host");
            Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<OaBehaviour>();

            Log.LogInfo($"{PluginName} {PluginVersion} loaded. Fly camera on {FlyKey.Value}; keep room open: {KeepRoomOpen.Value}.");
        }
    }
}
