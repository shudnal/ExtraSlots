using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots.Compatibility
{
    /// <summary>
    /// Keeps ServerManager's inventory-only snapshots compatible with slot provenance and
    /// deferred ownership. The native save queue, validation, revisions and ACKs remain intact.
    /// </summary>
    internal static class ServerManagerCompat
    {
        internal const string GUID = "sighsorry.ServerManager";

        private static Bindings bindings;
        private static bool initializationAttempted;
        private static readonly ConditionalWeakTable<object, DeferredSession> deferredSessions =
            new ConditionalWeakTable<object, DeferredSession>();

        private sealed class DeferredSession { }

        internal static bool IsManagedProfile => TryGetManagedContext(out _, out _, out _);

        internal static bool IsAuthoritativeProfile =>
            TryGetManagedContext(out _, out _, out bool backupOnly) && !backupOnly;

        private static void Initialize()
        {
            if (initializationAttempted || !Chainloader.PluginInfos.TryGetValue(GUID, out var plugin)
                || plugin.Instance == null)
                return;

            initializationAttempted = true;
            Bindings candidate = null;
            MethodInfo offerPrefix = null;
            MethodInfo hostPrefix = null;
            try
            {
                // All plugins have completed Awake before ZNet.Awake. Resolving here avoids a
                // soft-dependency cycle and also covers the listen host before its profile loads.
                candidate = new Bindings(plugin.Instance.GetType().Assembly);
                offerPrefix = typeof(ServerManagerCompat).GetMethod(nameof(BeforeOfferClientSave),
                    BindingFlags.Static | BindingFlags.NonPublic);
                hostPrefix = typeof(ServerManagerCompat).GetMethod(nameof(BeforeHostInventoryCapture),
                    BindingFlags.Static | BindingFlags.NonPublic);
                instance.harmony.Patch(candidate.OfferClientSave,
                    prefix: new HarmonyMethod(offerPrefix) { priority = Priority.Last });
                instance.harmony.Patch(candidate.CaptureHostInventory,
                    prefix: new HarmonyMethod(hostPrefix) { priority = Priority.Last });
                bindings = candidate;
                LogInfo($"ServerManager {plugin.Metadata.Version} compatibility enabled: slot snapshots, authoritative backup guard and deferred inventory saves.");
            }
            catch (Exception exception)
            {
                // Never leave half of the client/host adapter installed after a contract change.
                bindings = null;
                RemovePatch(candidate?.CaptureHostInventory, hostPrefix);
                RemovePatch(candidate?.OfferClientSave, offerPrefix);
                LogWarning($"ServerManager compatibility could not be enabled. Its character-save API may have changed; do not assume deferred inventory is synchronized safely. {exception}");
            }
        }

        private static void RemovePatch(MethodInfo original, MethodInfo patch)
        {
            if (original == null || patch == null)
                return;

            try { instance.harmony.Unpatch(original, patch); }
            catch (Exception exception)
            {
                LogWarning($"ServerManager compatibility patch cleanup failed for {original.Name}: {exception}");
            }
        }

        private static bool TryGetManagedContext(out object context, out PlayerProfile profile, out bool backupOnly)
        {
            context = null;
            profile = null;
            backupOnly = false;
            Bindings api = bindings;
            if (api == null || !Game.instance || !ZNet.instance || ZNet.instance.IsDedicated())
                return false;

            // Do not infer authority from plugin presence or ZNet.IsSinglePlayer. A listen
            // host temporarily closes its listener while preparing the managed character.
            if (ZNet.instance.IsServer())
            {
                if (!(bool)Invoke(api.HostIsActive, null))
                    return false;

                context = api.HostState.GetValue(null);
                if (context == null)
                    return false;

                profile = api.HostProfile.GetValue(context) as PlayerProfile;
                object session = api.HostSession.GetValue(context);
                if (session == null)
                    return false;
                backupOnly = (bool)Invoke(api.HostBackupOnly, session);
            }
            else
            {
                context = api.Client.GetValue(null);
                if (context == null || !(bool)Invoke(api.ClientActive, context))
                    return false;

                profile = Invoke(api.ClientProfile, context) as PlayerProfile;
                backupOnly = (bool)Invoke(api.ClientBackupOnly, context);
            }

            return profile != null && ReferenceEquals(profile, Game.instance.GetPlayerProfile());
        }

        private static bool RequiresFullProfile(object context, Player player)
        {
            if (player?.m_customData != null
                && player.m_customData.TryGetValue(DeferredInventory.CustomDataKey, out string payload)
                && !string.IsNullOrEmpty(payload)
                && !deferredSessions.TryGetValue(context, out _))
            {
                deferredSessions.Add(context, new DeferredSession());
                LogInfo("ServerManager deferred inventory detected: using full character snapshots for the rest of this session to keep physical and deferred items together.");
            }

            // Empty now does not mean empty in the last accepted server profile. Keep the latch
            // through restoration and respawns; the weak session key expires on disconnect.
            return deferredSessions.TryGetValue(context, out _);
        }

        private static void BeforeOfferClientSave(object[] __args)
        {
            Bindings api = bindings;
            if (api == null || !Equals(__args[2], api.InventoryDirtyReason))
                return;

            Player player = Player.m_localPlayer;
            if (player == null
                || !TryGetManagedContext(out object context, out PlayerProfile profile, out _)
                || !ReferenceEquals(context, __args[0])
                || !RequiresFullProfile(context, player))
                return;

            object codec = api.Codec.GetValue(null)
                ?? throw new InvalidOperationException("ServerManager's profile codec is unavailable for an atomic deferred-inventory save.");
            byte[] payload = (byte[])Invoke(api.CaptureProfile, codec, profile, player);

            // Change both the body and its native reason before Offer assigns a capture ID.
            // The existing queue will classify it as a full save, preserve pacing and build
            // the correct ACK baseline. Never append private bytes to Inventory.Save.
            __args[1] = payload;
            __args[2] = api.PeriodicFullReason;
        }

        private static bool BeforeHostInventoryCapture(object __0, Player __1)
        {
            Bindings api = bindings;
            if (api == null || __1 == null
                || !TryGetManagedContext(out object context, out _, out _)
                || !ReferenceEquals(context, __0)
                || !RequiresFullProfile(context, __1))
                return true;

            // Let the host's existing full-save scheduler own capture, error handling and
            // its safety interval. Calling CaptureFull every second would bypass that pacing.
            Invoke(api.ScheduleHostFull, null, context, Stopwatch.GetTimestamp());
            api.HostInventoryDue.SetValue(context, 0L);
            return false;
        }

        private static object Invoke(MethodInfo method, object target, params object[] arguments)
        {
            try { return method.Invoke(target, arguments); }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                // ServerManager must see its original failure, including fatal exceptions,
                // rather than mistake a reflection wrapper for a recoverable save error.
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class ZNet_Awake_InitializeServerManagerCompatibility
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix() => Initialize();
        }

        [HarmonyPatch(typeof(DeferredInventory), nameof(DeferredInventory.EnsureLoaded))]
        private static class DeferredInventory_EnsureLoaded_ObserveServerManagerStorage
        {
            private static void Prefix(Player player)
            {
                // Observe before deferred restoration can remove the last serialized entry.
                // Capture-time inspection alone would miss a queue restored during Player.Load.
                if (player != null && player == CurrentPlayer
                    && TryGetManagedContext(out object context, out _, out _))
                    RequiresFullProfile(context, player);
            }
        }

        private sealed class Bindings
        {
            internal readonly FieldInfo Client;
            internal readonly FieldInfo Codec;
            internal readonly MethodInfo ClientActive;
            internal readonly MethodInfo ClientProfile;
            internal readonly MethodInfo ClientBackupOnly;
            internal readonly MethodInfo OfferClientSave;
            internal readonly MethodInfo CaptureProfile;
            internal readonly object InventoryDirtyReason;
            internal readonly object PeriodicFullReason;
            internal readonly FieldInfo HostState;
            internal readonly MethodInfo HostIsActive;
            internal readonly FieldInfo HostProfile;
            internal readonly FieldInfo HostSession;
            internal readonly MethodInfo HostBackupOnly;
            internal readonly FieldInfo HostInventoryDue;
            internal readonly MethodInfo CaptureHostInventory;
            internal readonly MethodInfo ScheduleHostFull;

            internal Bindings(Assembly assembly)
            {
                Type runtime = RequireType(assembly, "ServerManager.ServerManagerRuntime");
                Type client = RequireType(assembly, "ServerManager.ServerManagerRuntime+ClientConnection");
                Type codec = RequireType(assembly, "ServerManager.ValheimPlayerProfileCodec");
                Type reason = RequireType(assembly, "ServerManager.ClientCharacterSaveReason");
                Type host = RequireType(assembly, "ServerManager.LocalHostCharacterRuntime");
                Type hostState = RequireType(assembly, "ServerManager.LocalHostCharacterRuntime+HostState");
                Type session = RequireType(assembly, "ServerManager.CharacterSession");
                if (!reason.IsEnum)
                    throw new InvalidOperationException("ServerManager's character save reason is not an enum.");

                InventoryDirtyReason = Enum.Parse(reason, "InventoryDirty");
                PeriodicFullReason = Enum.Parse(reason, "PeriodicFull");
                if (Equals(InventoryDirtyReason, PeriodicFullReason))
                    throw new InvalidOperationException("ServerManager's inventory and full save reasons must differ.");

                Client = RequireField(runtime, "_client", client, true);
                Codec = RequireField(runtime, "_profileCodec", codec, true);
                ClientActive = RequireGetter(client, "ServerCharacterActive", typeof(bool), false);
                ClientProfile = RequireGetter(client, "ManagedProfile", typeof(PlayerProfile), false);
                ClientBackupOnly = RequireGetter(client, "BackupOnly", typeof(bool), false);
                OfferClientSave = RequireMethod(runtime, "OfferClientSave", typeof(ulong), true,
                    client, typeof(byte[]), reason);
                CaptureProfile = RequireMethod(codec, "CaptureProfileToBytes", typeof(byte[]), false,
                    typeof(PlayerProfile), typeof(Player));

                HostState = RequireField(host, "_active", hostState, true);
                HostIsActive = RequireGetter(host, "IsActive", typeof(bool), true);
                HostProfile = RequireField(hostState, "ManagedProfile", typeof(PlayerProfile), false);
                HostSession = RequireField(hostState, "Session", session, false);
                HostBackupOnly = RequireGetter(session, "BackupOnly", typeof(bool), false);
                HostInventoryDue = RequireField(hostState, "InventoryDue", typeof(long), false);
                if (HostInventoryDue.IsInitOnly)
                    throw new InvalidOperationException("ServerManager's host inventory deadline is read-only.");
                CaptureHostInventory = RequireMethod(host, "CaptureInventory", typeof(void), true,
                    hostState, typeof(Player));
                ScheduleHostFull = RequireMethod(host, "ScheduleFull", typeof(void), true,
                    hostState, typeof(long));
            }

            private static BindingFlags Flags(bool isStatic) => BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly | (isStatic ? BindingFlags.Static : BindingFlags.Instance);

            private static Type RequireType(Assembly assembly, string name) => assembly.GetType(name, true);

            private static FieldInfo RequireField(Type type, string name, Type fieldType, bool isStatic)
            {
                FieldInfo field = type.GetField(name, Flags(isStatic));
                if (field == null || field.FieldType != fieldType)
                    throw new MissingFieldException(type.FullName, name);
                return field;
            }

            private static MethodInfo RequireGetter(Type type, string name, Type result, bool isStatic)
            {
                PropertyInfo property = type.GetProperty(name, Flags(isStatic));
                MethodInfo getter = property?.GetGetMethod(true);
                if (getter == null || getter.ReturnType != result || getter.IsStatic != isStatic
                    || getter.GetParameters().Length != 0)
                    throw new MissingMethodException(type.FullName, "get_" + name);
                return getter;
            }

            private static MethodInfo RequireMethod(Type type, string name, Type result, bool isStatic, params Type[] parameters)
            {
                MethodInfo method = type.GetMethod(name, Flags(isStatic), null, parameters, null);
                if (method == null || method.ReturnType != result || method.ContainsGenericParameters)
                    throw new MissingMethodException(type.FullName, name);
                return method;
            }
        }
    }
}
