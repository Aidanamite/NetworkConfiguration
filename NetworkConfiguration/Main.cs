using BepInEx;
using ConfigTweaks;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Timers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NetworkConfiguration
{
    [BepInPlugin("com.aidanamite.NetworkConfiguration", "Network Configuration", "1.0.0")]
    [BepInDependency("com.aidanamite.ConfigTweaks")]
    public class Main : BaseUnityPlugin
    {
        [ConfigField]
        public static int MinimumRetries = 1;
        [ConfigField]
        public static int DownloadTimeoutOverride = -1;
        [ConfigField]
        public static int UploadTimeoutOverride = -1;
        [ConfigField]
        public static float ServerConnectionTimeout = -1;

        public void Awake()
        {
            Config.ConfigReloaded += (x, y) =>
            {
                var m = FindObjectOfType<ConnectivityMonitor>();
                if (m)
                {
                    var t = m.GetConnectionCheckTimer();
                    if (t != null)
                        t.Interval = (ServerConnectionTimeout > 0 ? ServerConnectionTimeout : m._ConnectionTimeout) * 1000d;
                }
            };
            new Harmony("com.aidanamite.NetworkConfiguration").PatchAll();
            Logger.LogInfo("Loaded");
        }
    }

    static class ExtentionMethods
    {
        static FieldInfo _mConnectionCheckTimer = typeof(ConnectivityMonitor).GetField("mConnectionCheckTimer", ~BindingFlags.Default);
        public static Timer GetConnectionCheckTimer(this ConnectivityMonitor monitor) => (Timer)_mConnectionCheckTimer.GetValue(monitor);
    }

    [HarmonyPatch(typeof(UtUtilities), "OnServerError")]
    static class Patch_ForceRetry
    {
        static void Postfix(ref int inRetryCount, ref bool __result)
        {
            if (!__result && inRetryCount < Main.MinimumRetries)
            {
                __result = true;
                inRetryCount++;
            }
        }
    }

    [HarmonyPatch(typeof(UtWWWAsync.WWWProcess), "Update")]
    static class Patch_MessageTimeout
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo f)
                {
                    if (f.Name == "_DownloadTimeOutInSecs")
                        code.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_MessageTimeout), nameof(EditDownloadTimeout))));
                    else if (f.Name == "_PostTimeOutInSecs")
                        code.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_MessageTimeout), nameof(EditUploadTimeout))));
                }
            return code;
        }
        static int EditDownloadTimeout(int original)
        {
            if (Main.DownloadTimeoutOverride > 0)
                return Main.DownloadTimeoutOverride;
            return original;
        }
        static int EditUploadTimeout(int original)
        {
            if (Main.UploadTimeoutOverride > 0)
                return Main.UploadTimeoutOverride;
            return original;
        }
    }

    [HarmonyPatch(typeof(ConnectivityMonitor), "Update")]
    static class Patch_ConnectionTimeout
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo f)
                {
                    if (f.Name == "_ConnectionTimeout")
                        code.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_ConnectionTimeout), nameof(EditTimeout))));
                }
            return code;
        }
        static float EditTimeout(float original)
        {
            if (Main.ServerConnectionTimeout > 0)
                return Main.ServerConnectionTimeout;
            return original;
        }
    }
}