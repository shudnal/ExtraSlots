using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using TMPro;
using UnityEngine;
using static ExtraSlots.Slots;

namespace ExtraSlots.HotBars;

public static class QuickBars
{
    public const string vanillaBarName = "HotKeyBar";

    private static List<HotkeyBar> bars;
    private static int _currentBarIndex = -1;

    private static readonly HashSet<string> barNames = new HashSet<string>(){
            vanillaBarName,
            QuickSlotsHotBar.barName,
            AmmoSlotsHotBar.barName,
            FoodSlotsHotBar.barName,
        };

    private readonly struct ElementExtraData
    {
        public readonly RectTransform BindingRect;
        public readonly TMP_Text BindingText;
        public readonly UnityEngine.UI.Image QueuedImage;

        public ElementExtraData(RectTransform bindingRect, TMP_Text bindingText, UnityEngine.UI.Image queuedImage)
        {
            BindingRect = bindingRect;
            BindingText = bindingText;
            QueuedImage = queuedImage;
        }
    }

    private static readonly Dictionary<GameObject, ElementExtraData> elementsExtraData = new Dictionary<GameObject, ElementExtraData>(32);
    private static readonly Dictionary<HotkeyBar, HotkeyBarRefreshGate> refreshGates = new Dictionary<HotkeyBar, HotkeyBarRefreshGate>();
    private static readonly Dictionary<ItemDrop.ItemData, Vector2i> projectedItemPositions = new Dictionary<ItemDrop.ItemData, Vector2i>();
    private static readonly List<ItemDrop.ItemData> itemsToUse = new List<ItemDrop.ItemData>();

    private sealed class HotkeyBarRefreshGate
    {
        private const float HeartbeatInterval = 1f;
        private static int inventoryRevision;

        private ItemDrop.ItemData[] items = new ItemDrop.ItemData[8];
        private int[] stacks = new int[8];
        private float[] durabilities = new float[8];
        private bool[] equipped = new bool[8];
        private int[] qualities = new int[8];
        private int[] variants = new int[8];
        private int[] gridX = new int[8];

        private int itemCount;
        private int elementCount = -1;
        private int selected;
        private int revision;
        private int actionQueueCount;
        private Player.MinorActionData firstQueuedAction;
        private bool gamepadActive;
        private bool playerAlive;
        private float lastRefreshTime;

        internal bool ShouldRefresh(HotkeyBar bar, Player player)
        {
            if (elementCount == -1
                || !player.IsDead() != playerAlive
                || bar.m_elements.Count != elementCount
                || bar.m_selected != selected
                || revision != inventoryRevision
                || ZInput.IsGamepadActive() != gamepadActive
                || player.GetActionQueueCount() != actionQueueCount
                || !ReferenceEquals(player.m_actionQueue.Count > 0 ? player.m_actionQueue[0] : null, firstQueuedAction)
                || Time.unscaledTime - lastRefreshTime > HeartbeatInterval)
                return true;

            for (int i = 0; i < itemCount; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null
                    || item.m_stack != stacks[i]
                    || item.m_durability != durabilities[i]
                    || item.m_equipped != equipped[i]
                    || item.m_quality != qualities[i]
                    || item.m_variant != variants[i]
                    || item.m_gridPos.x != gridX[i])
                    return true;

                if (item.m_shared.m_useDurability && item.m_durability <= 0f)
                    return true;
            }

            return false;
        }

        internal void Resample(HotkeyBar bar, Player player)
        {
            playerAlive = !player.IsDead();
            itemCount = playerAlive ? bar.m_items.Count : 0;

            if (itemCount > items.Length)
                Grow(itemCount);

            for (int i = 0; i < itemCount; i++)
            {
                ItemDrop.ItemData item = bar.m_items[i];
                items[i] = item;
                if (item == null)
                    continue;

                stacks[i] = item.m_stack;
                durabilities[i] = item.m_durability;
                equipped[i] = item.m_equipped;
                qualities[i] = item.m_quality;
                variants[i] = item.m_variant;
                gridX[i] = item.m_gridPos.x;
            }

            for (int i = itemCount; i < items.Length; i++)
                items[i] = null;

            elementCount = bar.m_elements.Count;
            selected = bar.m_selected;
            revision = inventoryRevision;
            actionQueueCount = player.GetActionQueueCount();
            firstQueuedAction = actionQueueCount > 0 ? player.m_actionQueue[0] : null;
            gamepadActive = ZInput.IsGamepadActive();
            lastRefreshTime = Time.unscaledTime;
        }

        private void Grow(int size)
        {
            items = new ItemDrop.ItemData[size];
            stacks = new int[size];
            durabilities = new float[size];
            equipped = new bool[size];
            qualities = new int[size];
            variants = new int[size];
            gridX = new int[size];
        }

        internal static void BumpRevision(Humanoid humanoid)
        {
            if (humanoid == Player.m_localPlayer)
                inventoryRevision++;
        }
    }

    public static RectTransform InstantiateHotKeyBar(string barName)
    {
        RectTransform vanillaBar = Hud.instance.m_rootObject.transform.Find(vanillaBarName).GetComponent<RectTransform>();
        RectTransform result = UnityEngine.Object.Instantiate(vanillaBar, Hud.instance.m_rootObject.transform, true);
        result.name = barName;
        result.localPosition = Vector3.zero;
        result.SetSiblingIndex(vanillaBar.GetSiblingIndex() + 1);

        return result;
    }

    public static void ResetBars()
    {
        RestoreProjectedGridPositions();
        elementsExtraData.Clear();
        refreshGates.Clear();
        _currentBarIndex = -1;
        bars = null;
    }

    public static void InvalidateRendering()
    {
        RestoreProjectedGridPositions();
        // Vanilla caches only the numeric stack, not our choice of compact/full stack text.
        if (bars != null)
            foreach (HotkeyBar bar in bars)
                if (bar)
                    foreach (HotkeyBar.ElementData element in bar.m_elements)
                        if (element != null)
                            element.m_stackText = -1;
        elementsExtraData.Clear();
        refreshGates.Clear();
    }

    // Patch this method if you want your bar to be controlled in the same way
    public static bool IsBarToControl(HotkeyBar bar) => bar && barNames.Contains(bar.name);

    public static void UseCustomBarItem(HotkeyBar bar)
    {
        // Patch this method to use selected item from your hotbar
    }

    private static ElementExtraData GetElementExtraData(HotkeyBar.ElementData elementData)
    {
        GameObject go = elementData.m_go;

        if (elementsExtraData.TryGetValue(go, out ElementExtraData extraData))
            return extraData;

        if (elementsExtraData.Count > 128)
            elementsExtraData.Where(entry => !entry.Key).Select(entry => entry.Key).ToList().ForEach(key => elementsExtraData.Remove(key));

        Transform binding = go.transform.Find("binding");

        extraData = new ElementExtraData(
            binding.GetComponent<RectTransform>(),
            binding.GetComponent<TMP_Text>(),
            go.transform.Find("queued")?.GetComponent<UnityEngine.UI.Image>()
        );

        elementsExtraData.Add(go, extraData);

        return extraData;
    }

    private static Slot[] GetSlotsForBar(string name)
    {
        if (name == QuickSlotsHotBar.barName)
            return GetQuickSlots();
        if (name == AmmoSlotsHotBar.barName)
            return GetAmmoSlots();
        if (name == FoodSlotsHotBar.barName)
            return GetFoodSlots();
        return Array.Empty<Slot>();
    }

    private static int GetSlotOffset(string name)
    {
        if (name == AmmoSlotsHotBar.barName)
            return AmmoSlotsHotBar.barSlotIndex;
        if (name == FoodSlotsHotBar.barName)
            return FoodSlotsHotBar.barSlotIndex;
        return QuickSlotsHotBar.barSlotIndex;
    }

    private static int GetDesiredElementCount(string name)
    {
        bool showEmpty = name == QuickSlotsHotBar.barName ? ExtraSlots.quickSlotsAlwaysShowEmpty.Value
            : name == AmmoSlotsHotBar.barName ? ExtraSlots.ammoSlotsAlwaysShowEmpty.Value
            : name == FoodSlotsHotBar.barName && ExtraSlots.foodSlotsAlwaysShowEmpty.Value;
        if (!showEmpty)
            return 0;

        Slot[] barSlots = GetSlotsForBar(name);
        for (int i = barSlots.Length - 1; i >= 0; i--)
            if (barSlots[i]?.IsActive == true)
                return i + 1;

        return 0;
    }

    // Use the configured empty tail when vanilla decides whether to rebuild m_elements. Appending
    // elements in a postfix makes the next UpdateIcons destroy and recreate them on every refresh.
    private static int ResolveElementCount(int occupiedCount, HotkeyBar bar) =>
        bar ? Math.Max(occupiedCount, GetDesiredElementCount(bar.name)) : occupiedCount;

    [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.UpdateIcons))]
    private static class HotkeyBar_UpdateIcons_StableElementCount
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.ToList();
            FieldInfo elements = AccessTools.Field(typeof(HotkeyBar), nameof(HotkeyBar.m_elements));
            MethodInfo countGetter = AccessTools.PropertyGetter(typeof(List<HotkeyBar.ElementData>), "Count");
            int match = -1;
            CodeInstruction storeCount = null;

            for (int i = 0; i + 3 < code.Count; i++)
            {
                if (!code[i].LoadsField(elements) || !code[i + 1].Calls(countGetter))
                    continue;

                CodeInstruction store = StoreLoadedLocal(code[i + 2]);
                OpCode comparison = code[i + 3].opcode;
                if (store == null || comparison != OpCodes.Beq && comparison != OpCodes.Beq_S
                    && comparison != OpCodes.Bne_Un && comparison != OpCodes.Bne_Un_S && comparison != OpCodes.Ceq)
                    continue;

                if (match >= 0)
                {
                    ExtraSlots.LogWarning("Hotbar element-count patch found multiple allocation checks. Empty hotbar slots are unavailable; vanilla rendering is unchanged.");
                    return code;
                }

                match = i + 2;
                storeCount = store;
            }

            if (match < 0)
            {
                ExtraSlots.LogWarning("Hotbar element-count patch could not find the allocation check. Empty hotbar slots are unavailable; vanilla rendering is unchanged.");
                return code;
            }

            // Stack before insertion: [m_elements.Count, requiredCount]. Update the local as well as
            // the comparison operand, so vanilla's allocation loop uses the same adjusted count.
            code.InsertRange(match + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(QuickBars), nameof(ResolveElementCount))),
                new CodeInstruction(OpCodes.Dup),
                storeCount
            });
            return code;
        }

        private static CodeInstruction StoreLoadedLocal(CodeInstruction load)
        {
            if (load.opcode == OpCodes.Ldloc_0) return new CodeInstruction(OpCodes.Stloc_0);
            if (load.opcode == OpCodes.Ldloc_1) return new CodeInstruction(OpCodes.Stloc_1);
            if (load.opcode == OpCodes.Ldloc_2) return new CodeInstruction(OpCodes.Stloc_2);
            if (load.opcode == OpCodes.Ldloc_3) return new CodeInstruction(OpCodes.Stloc_3);
            if (load.opcode == OpCodes.Ldloc_S) return new CodeInstruction(OpCodes.Stloc_S, load.operand);
            if (load.opcode == OpCodes.Ldloc) return new CodeInstruction(OpCodes.Stloc, load.operand);
            return null;
        }
    }

    private static ItemDrop.ItemData GetItemForElement(HotkeyBar bar, int index)
    {
        if (bar.name == vanillaBarName)
            return bar.m_items.FirstOrDefault(item => item != null && item.m_gridPos.y == 0 && item.m_gridPos.x == index);

        int slotIndex = index + GetSlotOffset(bar.name);
        if (slotIndex < 0 || slotIndex >= slots.Length)
            return null;

        Slot slot = slots[slotIndex];
        return slot?.IsActive == true ? slot.Item : null;
    }

    [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.ElementClicked))]
    private static class HotkeyBar_ElementClicked_UseExtraSlot
    {
        private static bool Prefix(HotkeyBar __instance, Player player, int i)
        {
            if (__instance.name == vanillaBarName || !IsBarToControl(__instance))
                return true;

            if (player == CurrentPlayer && ZInput.IsTouchPressedDown() && ZInput.HasDoubleTapped()
                && i >= 0 && i < __instance.m_elements.Count)
            {
                __instance.m_selected = i;
                ItemDrop.ItemData item = GetItemForElement(__instance, i);
                if (item != null)
                    player.UseItem(null, item);
            }
            return false;
        }
    }

    private static void UpdateQueuedIndicators(HotkeyBar bar, Player player)
    {
        if (!bar || player == null || player.m_actionQueue.Count == 0)
            return;

        for (int i = 0; i < bar.m_elements.Count; i++)
        {
            HotkeyBar.ElementData element = bar.m_elements[i];
            if (element?.m_go == null)
                continue;

            ElementExtraData extraData = GetElementExtraData(element);
            QueuedEquipIndicator.Update(extraData.QueuedImage, GetItemForElement(bar, i), player);
        }
    }

    private static void ProjectGridPositionsForBar(List<ItemDrop.ItemData> items, string name)
    {
        Slot[] barSlots = GetSlotsForBar(name);
        for (int i = 0; i < items.Count; i++)
        {
            ItemDrop.ItemData item = items[i];
            if (item == null)
                continue;

            if (!projectedItemPositions.ContainsKey(item))
                projectedItemPositions[item] = item.m_gridPos;

            int localIndex = Array.FindIndex(barSlots, slot => slot != null && slot.IsActive && ReferenceEquals(slot.Item, item));
            item.m_gridPos = new Vector2i(localIndex >= 0 ? localIndex : i, item.m_gridPos.y);
        }
    }

    private static void RestoreProjectedGridPositions()
    {
        foreach (KeyValuePair<ItemDrop.ItemData, Vector2i> entry in projectedItemPositions)
            if (entry.Key != null)
                entry.Key.m_gridPos = entry.Value;

        projectedItemPositions.Clear();
    }

    private static Vector3 LeftTopPoint => Hud.instance ? new Vector3(-Hud.instance.m_rootObject.transform.position.x, Hud.instance.m_rootObject.transform.position.y, 0) : new Vector3(-1280, 720, 0);

    private static List<HotkeyBar> GetHotKeyBarsToControl() => Hud.instance ? Hud.instance.m_rootObject.GetComponentsInChildren<HotkeyBar>().Where(IsBarToControl).OrderBy(bar => Vector3.Distance(bar.transform.localPosition, LeftTopPoint)).ToList() : null;

    private static bool UpdateCurrentHotkeyBar(bool joyHotbarLeft, bool joyHotbarRight, bool joyHotbarUse)
    {
        if (_currentBarIndex < 0 || _currentBarIndex > bars.Count - 1)
            return false;

        HotkeyBar hotkeyBar = bars[_currentBarIndex];
        bool isHotkeyBarsActive = IsHotkeyBarsActive();
        if (hotkeyBar.m_selected < 0 || hotkeyBar.m_selected > hotkeyBar.m_elements.Count - 1 || !isHotkeyBarsActive)
            return !isHotkeyBarsActive;

        if (joyHotbarLeft && --hotkeyBar.m_selected < 0)
            ChangeActiveHotkeyBar(next: false);
        else if (joyHotbarRight && ++hotkeyBar.m_selected > hotkeyBar.m_elements.Count - 1)
            ChangeActiveHotkeyBar(next: true);
        else if (joyHotbarUse)
            if (hotkeyBar.name == QuickSlotsHotBar.barName)
                Player.m_localPlayer.UseItem(Player.m_localPlayer.GetInventory(), QuickSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), fromInventoryGui: false);
            else if (hotkeyBar.name == AmmoSlotsHotBar.barName)
                Player.m_localPlayer.UseItem(Player.m_localPlayer.GetInventory(), AmmoSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), fromInventoryGui: false);
            else if (hotkeyBar.name == FoodSlotsHotBar.barName)
                Player.m_localPlayer.UseItem(Player.m_localPlayer.GetInventory(), FoodSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), fromInventoryGui: false);
            else if (hotkeyBar.name == vanillaBarName)
                Player.m_localPlayer.UseHotbarItem(hotkeyBar.m_selected + 1);
            else
                UseCustomBarItem(hotkeyBar);

        return true;
    }

    private static void ChangeActiveHotkeyBar(bool next = true)
    {
        int[] activeBars = bars.Where(bar => bar.m_elements.Count > 0).Select(bar => bars.IndexOf(bar)).ToArray();
        if (activeBars.Length == 0)
        {
            _currentBarIndex = -1;
            return;
        }

        int index = Array.IndexOf(activeBars, _currentBarIndex);
        index = (index == -1) ? 0 : index + (next ? 1 : -1);

        _currentBarIndex = activeBars[(index + activeBars.Length) % activeBars.Length];
        bars[_currentBarIndex].m_selected = next ? 0 : bars[_currentBarIndex].m_elements.Count - 1;
    }

    private static bool IsHotkeyBarsActive() => !InventoryGui.IsVisible() && !Menu.IsVisible() && !GameCamera.InFreeFly()
                                                && !Minimap.IsOpen() && !Hud.IsPieceSelectionVisible() && !StoreGui.IsVisible()
                                                && !Console.IsVisible() && !Chat.instance.HasFocus() && !PlayerCustomizaton.IsBarberGuiVisible()
                                                && !Hud.InRadial();

    // Runs every frame Player.Update
    internal static void UpdateItemUse()
    {
        if (!Player.m_localPlayer.TakeInput())
            return;

        if (!PreventSimilarHotkeys.IsAnyExtraSlotsHotkeyDown())
            return;

        if (!ExtraSlots.useSingleHotbarItem.Value)
        {
            List<ItemDrop.ItemData> items = GetItemsToUse();

            for (int i = 0; i < items.Count; i++)
                Player.m_localPlayer.UseItem(PlayerInventory, items[i], fromInventoryGui: false);
        }
        else if (GetItemToUse() is ItemDrop.ItemData item)
        {
            Player.m_localPlayer.UseItem(PlayerInventory, item, fromInventoryGui: false);
        }
    }

    private static ItemDrop.ItemData GetItemToUse()
    {
        Slot quickSlotUsed = QuickSlotsHotBar.GetSlotWithShortcutDown();
        Slot ammoSlotUsed = AmmoSlotsHotBar.GetSlotWithShortcutDown();
        Slot foodSlotUsed = FoodSlotsHotBar.GetSlotWithShortcutDown();

        if (quickSlotUsed != null && ammoSlotUsed != null && foodSlotUsed != null)
        {
            int quickModifiers = quickSlotUsed.GetShortcut().Modifiers.Count();
            int ammoModifiers = ammoSlotUsed.GetShortcut().Modifiers.Count();
            int foodModifiers = foodSlotUsed.GetShortcut().Modifiers.Count();

            if (quickModifiers >= ammoModifiers && quickModifiers >= foodModifiers)
                return quickSlotUsed.Item;
            else if (ammoModifiers >= quickModifiers && ammoModifiers >= foodModifiers)
                return ammoSlotUsed.Item;
            else
                return foodSlotUsed.Item;
        }
        else if (quickSlotUsed != null && ammoSlotUsed != null)
        {
            if (quickSlotUsed.GetShortcut().Modifiers.Count() >= ammoSlotUsed.GetShortcut().Modifiers.Count())
                return quickSlotUsed.Item;
            else
                return ammoSlotUsed.Item;
        }
        else if (quickSlotUsed != null && foodSlotUsed != null)
        {
            if (quickSlotUsed.GetShortcut().Modifiers.Count() >= foodSlotUsed.GetShortcut().Modifiers.Count())
                return quickSlotUsed.Item;
            else
                return foodSlotUsed.Item;
        }
        else if (ammoSlotUsed != null && foodSlotUsed != null)
        {
            if (ammoSlotUsed.GetShortcut().Modifiers.Count() >= foodSlotUsed.GetShortcut().Modifiers.Count())
                return ammoSlotUsed.Item;
            else
                return foodSlotUsed.Item;
        }
        else if (quickSlotUsed != null)
            return quickSlotUsed.Item;
        else if (ammoSlotUsed != null)
            return ammoSlotUsed.Item;
        else if (foodSlotUsed != null)
            return foodSlotUsed.Item;

        return null;
    }

    private static List<ItemDrop.ItemData> GetItemsToUse()
    {
        itemsToUse.Clear();
        if (QuickSlotsHotBar.GetSlotsWithShortcutDown() is IEnumerable<Slot> quickItems)
            foreach (Slot slot in quickItems)
                itemsToUse.Add(slot.Item);

        if (AmmoSlotsHotBar.GetSlotsWithShortcutDown() is IEnumerable<Slot> ammoItems)
            foreach (Slot slot in ammoItems)
                itemsToUse.Add(slot.Item);

        if (FoodSlotsHotBar.GetSlotsWithShortcutDown() is IEnumerable<Slot> foodItems)
            foreach (Slot slot in foodItems)
                itemsToUse.Add(slot.Item);

        return itemsToUse;
    }

    private static bool GetJoyButtonDown(string name) => !Compatibility.PlantEasilyCompat.DisableGamepadInput && ZInput.GetButtonDown(name) && !ZInput.GetButton("JoyAltKeys");

    private static bool NoBarsToControl()
    {
        if (bars == null || bars.Count == 0)
            return true;

        if (bars.Count != 1)
            return false;

        HotkeyBar bar = bars[0];

        return !bar || bar.name == vanillaBarName;
    }

    private static bool AreBarsValid()
    {
        if (bars == null)
            return false;

        for (int i = 0; i < bars.Count; i++)
        {
            HotkeyBar bar = bars[i];

            if (!bar || bar.m_elements == null || bar.m_items == null)
                return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
    public static class Hud_Update_BarController
    {
        public static void Postfix()
        {
            Player player = Player.m_localPlayer;
            if (!player)
                return;

            bool barsRefreshed =
                QuickSlotsHotBar.Refresh() |
                AmmoSlotsHotBar.Refresh() |
                FoodSlotsHotBar.Refresh();

            if (barsRefreshed)
            {
                ResetBars();
                return;
            }

            bars ??= GetHotKeyBarsToControl();

            if (!AreBarsValid())
            {
                ResetBars();
                return;
            }

            if (NoBarsToControl())
                return;

            bool joyHotbarLeft = GetJoyButtonDown("JoyHotbarLeft");
            bool joyHotbarRight = GetJoyButtonDown("JoyHotbarRight");
            bool joyHotbarUse = GetJoyButtonDown("JoyHotbarUse");

            if (!UpdateCurrentHotkeyBar(
                    joyHotbarLeft,
                    joyHotbarRight,
                    joyHotbarUse)
                && (joyHotbarLeft || joyHotbarRight || joyHotbarUse))
            {
                ChangeActiveHotkeyBar();
            }

            for (int i = 0; i < bars.Count; i++)
            {
                HotkeyBar bar = bars[i];

                bar.m_selected = _currentBarIndex == i
                    ? Mathf.Clamp(bar.m_selected, -1, bar.m_elements.Count - 1)
                    : -1;

                if (!refreshGates.TryGetValue(bar, out HotkeyBarRefreshGate refreshGate))
                {
                    refreshGate = new HotkeyBarRefreshGate();
                    refreshGates[bar] = refreshGate;
                }

                if (refreshGate.ShouldRefresh(bar, player))
                {
                    bar.UpdateIcons(player);
                    refreshGate.Resample(bar, player);
                }

                UpdateQueuedIndicators(bar, player);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnInventoryChanged))]
    private static class Player_OnInventoryChanged_BumpHotbarRevision
    {
        private static void Postfix(Player __instance) => HotkeyBarRefreshGate.BumpRevision(__instance);
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    private static class Humanoid_EquipItem_BumpHotbarRevision
    {
        private static void Postfix(Humanoid __instance) => HotkeyBarRefreshGate.BumpRevision(__instance);
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    private static class Humanoid_UnequipItem_BumpHotbarRevision
    {
        private static void Postfix(Humanoid __instance) => HotkeyBarRefreshGate.BumpRevision(__instance);
    }

    [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
    public static class Hud_OnDestroy_ResetBars
    {
        public static void Postfix() => ResetBars();
    }

    [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.Update))]
    public static class HotkeyBar_Update_PreventCall
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(HotkeyBar __instance)
        {
            return !IsBarToControl(__instance) || NoBarsToControl();
        }
    }
    [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.UpdateIcons))]
    public static class HotkeyBar_UpdateIcons_QuickBarsUpdate
    {
        public static bool inCall;
        public static string barName;

        private static bool IsExtraSlotsHotBar(string name)
        {
            return name == QuickSlotsHotBar.barName
                || name == AmmoSlotsHotBar.barName
                || name == FoodSlotsHotBar.barName;
        }

        public static void Prefix(HotkeyBar __instance)
        {
            if (!__instance || !IsExtraSlotsHotBar(__instance.name))
                return;

            barName = __instance.name;
            inCall = true;
        }

        [HarmonyPriority(Priority.First)]
        public static void Postfix(HotkeyBar __instance)
        {
            if (!inCall || !__instance || !IsExtraSlotsHotBar(__instance.name))
                return;

            string currentBarName = barName;

            RestoreProjectedGridPositions();

            int slotOffset;
            bool hideStackSize;
            int widthInElements;
            bool fillUp;
            float elementSpace;

            if (currentBarName == FoodSlotsHotBar.barName)
            {
                slotOffset = FoodSlotsHotBar.barSlotIndex;
                hideStackSize = ExtraSlots.foodSlotsHideStackSize.Value;
                widthInElements = ExtraSlots.foodSlotsWidthInElements.Value;
                fillUp = ExtraSlots.foodSlotsFillDirectionUp.Value;
                elementSpace = ExtraSlots.foodSlotsElementSpace.Value;
            }
            else if (currentBarName == AmmoSlotsHotBar.barName)
            {
                slotOffset = AmmoSlotsHotBar.barSlotIndex;
                hideStackSize = ExtraSlots.ammoSlotsHideStackSize.Value;
                widthInElements = ExtraSlots.ammoSlotsWidthInElements.Value;
                fillUp = ExtraSlots.ammoSlotsFillDirectionUp.Value;
                elementSpace = ExtraSlots.ammoSlotsElementSpace.Value;
            }
            else
            {
                slotOffset = QuickSlotsHotBar.barSlotIndex;
                hideStackSize = ExtraSlots.quickSlotsHideStackSize.Value;
                widthInElements = ExtraSlots.quickSlotsWidthInElements.Value;
                fillUp = ExtraSlots.quickSlotsFillDirectionUp.Value;
                elementSpace = ExtraSlots.quickSlotsElementSpace.Value;
            }

            widthInElements = Mathf.Max(1, widthInElements);

            for (int index = 0; index < __instance.m_elements.Count; index++)
            {
                HotkeyBar.ElementData elementData = __instance.m_elements[index];

                if (elementData == null || !elementData.m_go)
                    continue;

                int slotIndex = index + slotOffset;
                if (slotIndex < 0 || slotIndex >= slots.Length)
                    continue;

                Slot slot = slots[slotIndex];

                ElementExtraData extraData = GetElementExtraData(elementData);
                EquipmentPanel.SetSlotLabel(extraData.BindingRect, extraData.BindingText, slot, hotbarElement: true);

                if (!elementData.m_used)
                {
                    elementData.m_icon.gameObject.SetActive(false);
                    elementData.m_durability.gameObject.SetActive(false);
                    elementData.m_equiped.SetActive(false);
                    elementData.m_queued.SetActive(false);
                    elementData.m_amount.gameObject.SetActive(false);
                }
                elementData.m_selection.SetActive(ZInput.IsGamepadActive() && index == __instance.m_selected);

                if (hideStackSize
                    && elementData.m_amount.gameObject.activeInHierarchy
                    && slot.Item is ItemDrop.ItemData item
                    && (item.IsEquipable() || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable))
                {
                    elementData.m_amount.SetText(elementData.m_stackText.ToFastString());
                }

                elementData.m_go.transform.localPosition =
                    new Vector3(index % widthInElements, (fillUp ? 1 : -1) * (index / widthInElements), 0f) * elementSpace;
            }
        }

        public static Exception Finalizer(Exception __exception)
        {
            RestoreProjectedGridPositions();

            inCall = false;
            barName = null;

            return __exception;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetBoundItems))]
    public static class Inventory_GetBoundItems_QuickBarsItems
    {
        public static bool Prefix(Inventory __instance, List<ItemDrop.ItemData> bound)
        {
            if (__instance != PlayerInventory || !HotkeyBar_UpdateIcons_QuickBarsUpdate.inCall)
                return true;

            string currentBarName = HotkeyBar_UpdateIcons_QuickBarsUpdate.barName;

            if (currentBarName == QuickSlotsHotBar.barName)
            {
                bound.Clear();
                QuickSlotsHotBar.GetItems(bound);
                ProjectGridPositionsForBar(bound, currentBarName);
                return false;
            }

            if (currentBarName == AmmoSlotsHotBar.barName)
            {
                bound.Clear();
                AmmoSlotsHotBar.GetItems(bound);
                ProjectGridPositionsForBar(bound, currentBarName);
                return false;
            }

            if (currentBarName == FoodSlotsHotBar.barName)
            {
                bound.Clear();
                FoodSlotsHotBar.GetItems(bound);
                ProjectGridPositionsForBar(bound, currentBarName);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
    public static class Hud_Awake_CreateQuickBars
    {
        public static void Postfix()
        {
            QuickSlotsHotBar.MarkDirty();
            AmmoSlotsHotBar.MarkDirty();
            FoodSlotsHotBar.MarkDirty();
        }
    }

    [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
    public static class Hud_OnDestroy_ClearQuickBars
    {
        public static void Postfix()
        {
            QuickSlotsHotBar.ClearBar();
            AmmoSlotsHotBar.ClearBar();
            FoodSlotsHotBar.ClearBar();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    private static class Player_Update_SlotsUse
    {
        private static void Postfix(Player __instance)
        {
            if (!IsValidPlayer(__instance))
                return;

            UpdateItemUse();
        }
    }
}
