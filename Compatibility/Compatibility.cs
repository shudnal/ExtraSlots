using HarmonyLib;
using System;
using System.Reflection;

namespace ExtraSlots.Compatibility
{
    internal static class Helper
    {
        internal static void CheckForCompatibility()
        {
            Compatibility.EpicLootCompat.CheckForCompatibility();

            Compatibility.BetterArcheryCompat.CheckForCompatibility();

            Compatibility.PlantEasilyCompat.CheckForCompatibility();

            Compatibility.ValheimPlusCompat.CheckForCompatibility();

            Compatibility.BetterProgressionCompat.CheckForCompatibility();

            Compatibility.MagicPluginCompat.CheckForCompatibility();

            Compatibility.ZenBeehiveCompat.CheckForCompatibility();

            Compatibility.BBHCompat.CheckForCompatibility();
        }

        internal static void RemoveHarmonyPatch(this Assembly assembly, Type patchedType, string patchedMethod, string patcherClassName, string patcherClassMethod, string reason)
        {
            Type patcherType = assembly.GetType(patcherClassName);
            if (patcherType == null)
            {
                ExtraSlots.LogInfo($"{patcherClassName} is not found.");
                return;
            }

            if (AccessTools.Method(patchedType, patchedMethod) is not MethodInfo method)
            {
                ExtraSlots.LogInfo($"Method {patchedType.Name}.{patchedMethod} is not found.");
                return;
            }

            if (AccessTools.Method(patcherType, patcherClassMethod) is not MethodInfo patch)
            {
                ExtraSlots.LogInfo($"Patch {patcherType.Name}.{patcherClassMethod} is not found.");
                return;
            }

            ExtraSlots.instance.harmony.Unpatch(method, patch);
            ExtraSlots.LogInfo($"{patcherClassName}:{patcherClassMethod} was unpatched to {reason}.");
        }

        internal static void RemoveHarmonyPatch(this Assembly assembly, MethodBase method, string patcherClassName, string patcherClassMethod, string reason)
        {
            Type patcherType = assembly.GetType(patcherClassName);
            if (patcherType == null)
            {
                ExtraSlots.LogInfo($"{patcherClassName} is not found.");
                return;
            }

            if (AccessTools.Method(patcherType, patcherClassMethod) is not MethodInfo patch)
            {
                ExtraSlots.LogInfo($"Patch {patcherType.Name}.{patcherClassMethod} is not found.");
                return;
            }

            ExtraSlots.instance.harmony.Unpatch(method, patch);
            ExtraSlots.LogInfo($"{patcherClassName}:{patcherClassMethod} was unpatched to {reason}.");
        }
    }
}
