using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ExtraSlots.Compatibility
{
    internal static class CompatibilityHelper
    {
        internal static void CheckForCompatibility()
        {
            EpicLootCompat.CheckForCompatibility();

            BetterArcheryCompat.CheckForCompatibility();

            PlantEasilyCompat.CheckForCompatibility();

            ValheimPlusCompat.CheckForCompatibility();

            BetterProgressionCompat.CheckForCompatibility();

            ZenBeehiveCompat.CheckForCompatibility();

            BBHCompat.CheckForCompatibility();

            Recycle_N_Reclaim.CheckForCompatibility();
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

        internal static void TryAddMethodToPatch(this Assembly assembly, List<MethodBase> list, string methodClassName, string methodName, string reason)
        {
            Type methodType = assembly.GetType(methodClassName);
            if (methodType == null)
            {
                ExtraSlots.LogInfo($"{methodClassName} is not found.");
                return;
            }

            if (AccessTools.Method(methodType, methodName) is not MethodInfo method)
            {
                ExtraSlots.LogInfo($"Method {methodType.Name}.{methodName} is not found.");
                return;
            }
            
            list.Add(method);
            ExtraSlots.LogInfo($"{methodClassName}:{methodName} is patched to {reason}.");
        }
    }
}
