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
    private static readonly Dictionary<HotkeyBar, HotkeyBarRenderContext> renderContexts = new Dictionary<HotkeyBar, HotkeyBarRenderContext>();
    private static bool localSlotIndicesPatched;
    private static readonly List<ItemDrop.ItemData> itemsToUse = new List<ItemDrop.ItemData>();

    private sealed class HotkeyBarRefreshGate
    {
        private const float HeartbeatInterval = 1f;
        private const float ErrorRetryInterval = 1f;
        private const float ErrorLogInterval = 30f;
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
        private bool updateFailed;
        private float nextRetryTime;
        private float nextErrorLogTime;
        private int suppressedErrors;

        internal bool CanAttemptUpdate => !updateFailed || Time.unscaledTime >= nextRetryTime;

        internal bool ShouldRefresh(HotkeyBar bar, Player player)
        {
            if (updateFailed)
                return CanAttemptUpdate;

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

        internal void ReportSuccess()
        {
            updateFailed = false;
            suppressedErrors = 0;
        }

        internal void ReportFailure(HotkeyBar bar, Exception exception)
        {
            updateFailed = true;
            elementCount = -1;
            float now = Time.unscaledTime;
            nextRetryTime = now + ErrorRetryInterval;
            if (bar?.m_elements != null)
                foreach (HotkeyBar.ElementData element in bar.m_elements)
                    if (element?.m_go != null)
                        elementsExtraData.Remove(element.m_go);

            if (now < nextErrorLogTime)
            {
                suppressedErrors++;
                return;
            }

            nextErrorLogTime = now + ErrorLogInterval;
            string barName = bar ? bar.name : "<destroyed>";
            string itemDetails = bar?.m_items == null ? "<null>" : string.Join(", ", bar.m_items.Select(item =>
                item == null ? "<null>" : $"{item.m_shared?.m_name ?? "<unknown>"}@{item.m_gridPos}"));
            ExtraSlots.LogWarning($"Hotbar '{barName}' update failed; retrying in {ErrorRetryInterval:0} second. " +
                $"Repeated errors are logged at most once every {ErrorLogInterval:0} seconds per bar. " +
                $"Elements={bar?.m_elements?.Count ?? -1}, items={bar?.m_items?.Count ?? -1}, suppressed={suppressedErrors}. " +
                $"Items: [{itemDetails}].\n{exception}");
            suppressedErrors = 0;
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
        elementsExtraData.Clear();
        refreshGates.Clear();
        _currentBarIndex = -1;
        bars = null;
    }

    public static void InvalidateRendering()
    {
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
            return QuickSlotsHotBar.RegisteredSlots;
        if (name == AmmoSlotsHotBar.barName)
            return AmmoSlotsHotBar.RegisteredSlots;
        if (name == FoodSlotsHotBar.barName)
            return FoodSlotsHotBar.RegisteredSlots;
        return Array.Empty<Slot>();
    }

    [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.ToggleBindingHint))]
    private static class HotkeyBar_ToggleBindingHint_ExtraSlotLabels
    {
        private static void Postfix(HotkeyBar __instance, bool bShouldEnable)
        {
            if (!__instance || __instance.m_elements == null)
                return;

            // Only our three panels have a registered layout; leave all other bars unchanged.
            Slot[] barSlots = GetSlotsForBar(__instance.name);
            int elementCount = Math.Min(__instance.m_elements.Count, barSlots.Length);
            for (int index = 0; index < elementCount; index++)
            {
                HotkeyBar.ElementData element = __instance.m_elements[index];
                Slot slot = barSlots[index];
                if (element == null || !element.m_go || slot == null)
                    continue;

                // Restore custom labels in this call, without waiting for the next icon refresh.
                TMP_Text bindingText = GetElementExtraData(element).BindingText;
                if (bindingText)
                    bindingText.text = bShouldEnable ? slot.GetShortcutText() : string.Empty;
            }
        }
    }

    private static int GetSlotOffset(string name)
    {
        if (name == AmmoSlotsHotBar.barName)
            return AmmoSlotsHotBar.barSlotIndex;
        if (name == FoodSlotsHotBar.barName)
            return FoodSlotsHotBar.barSlotIndex;
        return QuickSlotsHotBar.barSlotIndex;
    }

    private static int GetDesiredElementCount(string name, Slot[] barSlots)
    {
        bool showEmpty = name == QuickSlotsHotBar.barName ? ExtraSlots.quickSlotsAlwaysShowEmpty.Value
            : name == AmmoSlotsHotBar.barName ? ExtraSlots.ammoSlotsAlwaysShowEmpty.Value
            : name == FoodSlotsHotBar.barName && ExtraSlots.foodSlotsAlwaysShowEmpty.Value;
        if (!showEmpty)
            return 0;

        for (int i = barSlots.Length - 1; i >= 0; i--)
            if (barSlots[i]?.IsActive == true)
                return i + 1;

        return 0;
    }

    // Keep the allocation count and every element lookup on the same immutable slot snapshot.
    private static int ResolveElementCount(int occupiedCount, HotkeyBar bar) =>
        bar && renderContexts.TryGetValue(bar, out HotkeyBarRenderContext context)
            ? Math.Max(occupiedCount, context.EmptyElementCount) : occupiedCount;

    private sealed class HotkeyBarRenderContext
    {
        internal readonly HotkeyBar Bar;
        internal readonly Slot[] BarSlots;
        internal readonly ItemDrop.ItemData[] SlotItems;
        internal readonly Dictionary<ItemDrop.ItemData, int> ElementIndices =
            new Dictionary<ItemDrop.ItemData, int>(PlayerInventoryOperations.ItemReferenceComparer.Instance);
        internal int EmptyElementCount;
        internal bool ItemsCollected;

        internal HotkeyBarRenderContext(HotkeyBar bar)
        {
            Bar = bar;
            BarSlots = (Slot[])GetSlotsForBar(bar.name).Clone();
            SlotItems = new ItemDrop.ItemData[BarSlots.Length];
        }

        internal void CollectItems(Inventory inventory, List<ItemDrop.ItemData> items)
        {
            if (ItemsCollected || !ReferenceEquals(items, Bar.m_items))
                throw new InvalidOperationException($"Unexpected item collection for hotbar '{Bar.name}'.");

            items.Clear();
            EmptyElementCount = GetDesiredElementCount(Bar.name, BarSlots);
            if (inventory != null && ReferenceEquals(inventory, PlayerInventory))
            {
                for (int index = 0; index < BarSlots.Length; index++)
                {
                    Slot slot = BarSlots[index];
                    ItemDrop.ItemData item = slot?.IsActive == true ? slot.Item : null;
                    if (item == null)
                        continue;

                    if (ElementIndices.ContainsKey(item))
                        throw new InvalidOperationException($"The same item occupies multiple slots in hotbar '{Bar.name}'.");

                    ElementIndices.Add(item, index);
                    SlotItems[index] = item;
                    items.Add(item);
                }
            }
            ItemsCollected = true;
        }
    }

    private static bool IsExtraSlotsHotBar(string name) => name == QuickSlotsHotBar.barName
        || name == AmmoSlotsHotBar.barName || name == FoodSlotsHotBar.barName;

    private static void CollectHotbarItems(Inventory inventory, List<ItemDrop.ItemData> items, HotkeyBar bar)
    {
        if (!bar || !IsExtraSlotsHotBar(bar.name))
        {
            inventory.GetBoundItems(items);
            return;
        }

        if (!renderContexts.TryGetValue(bar, out HotkeyBarRenderContext context))
            throw new InvalidOperationException($"Missing render context for hotbar '{bar.name}'.");

        context.CollectItems(inventory, items);
    }

    private static int GetHotbarItemIndex(ItemDrop.ItemData item, HotkeyBar bar)
    {
        if (!bar || !IsExtraSlotsHotBar(bar.name))
            return item.m_gridPos.x;

        if (item != null && renderContexts.TryGetValue(bar, out HotkeyBarRenderContext context)
            && context.ElementIndices.TryGetValue(item, out int index))
            return index;

        // Never clamp an unknown item onto another slot or fall back to its inventory column.
        throw new InvalidOperationException($"An item outside the slot snapshot reached hotbar '{bar.name}'.");
    }

    [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.UpdateIcons))]
    private static class HotkeyBar_UpdateIcons_LocalSlotIndices
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> original = instructions.ToList();
            List<CodeInstruction> code = original.Select(instruction => new CodeInstruction(instruction)).ToList();
            localSlotIndicesPatched = false;

            FieldInfo gridPosition = AccessTools.Field(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.m_gridPos));
            FieldInfo column = AccessTools.Field(typeof(Vector2i), nameof(Vector2i.x));
            FieldInfo elements = AccessTools.Field(typeof(HotkeyBar), nameof(HotkeyBar.m_elements));
            MethodInfo getBoundItems = AccessTools.Method(typeof(Inventory), nameof(Inventory.GetBoundItems));
            MethodInfo countGetter = AccessTools.PropertyGetter(typeof(List<HotkeyBar.ElementData>), "Count");
            MethodInfo collectItems = AccessTools.Method(typeof(QuickBars), nameof(CollectHotbarItems));
            MethodInfo getIndex = AccessTools.Method(typeof(QuickBars), nameof(GetHotbarItemIndex));
            MethodInfo resolveCount = AccessTools.Method(typeof(QuickBars), nameof(ResolveElementCount));
            List<int> coordinateReads = new List<int>();
            int collectionCall = -1;
            int allocationCheck = -1;
            CodeInstruction storeCount = null;

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(getBoundItems))
                {
                    if (collectionCall >= 0 || code[i].blocks.Count != 0)
                        return Unsupported(original, "ambiguous item collection");
                    collectionCall = i;
                }

                if (Equals(code[i].operand, gridPosition))
                {
                    if ((code[i].opcode != OpCodes.Ldflda && code[i].opcode != OpCodes.Ldfld)
                        || i + 1 >= code.Count || code[i + 1].opcode != OpCodes.Ldfld
                        || !Equals(code[i + 1].operand, column)
                        || code[i + 1].labels.Count != 0 || code[i + 1].blocks.Count != 0)
                        return Unsupported(original, "unsupported inventory-coordinate access");
                    coordinateReads.Add(i);
                }

                if (i + 3 >= code.Count || !code[i].LoadsField(elements) || !code[i + 1].Calls(countGetter))
                    continue;

                CodeInstruction store = StoreLoadedLocal(code[i + 2]);
                OpCode comparison = code[i + 3].opcode;
                if (store == null || comparison != OpCodes.Beq && comparison != OpCodes.Beq_S
                    && comparison != OpCodes.Bne_Un && comparison != OpCodes.Bne_Un_S && comparison != OpCodes.Ceq)
                    continue;
                if (allocationCheck >= 0 || code[i + 3].blocks.Count != 0)
                    return Unsupported(original, "ambiguous element allocation check");
                allocationCheck = i + 3;
                storeCount = store;
            }

            if (collectionCall < 0 || coordinateReads.Count == 0 || allocationCheck < 0)
                return Unsupported(original, $"collection={collectionCall}, coordinateReads={coordinateReads.Count}, allocation={allocationCheck}");

            // Replace only reads inside UpdateIcons. The real ItemData coordinates and the
            // inventory's GetBoundItems method stay unchanged, including during nested callbacks.
            foreach (int index in coordinateReads)
            {
                code[index].opcode = OpCodes.Ldarg_0;
                code[index].operand = null;
                code[index + 1].opcode = OpCodes.Call;
                code[index + 1].operand = getIndex;
            }

            CodeInstruction collection = code[collectionCall];
            collection.opcode = OpCodes.Call;
            collection.operand = collectItems;
            CodeInstruction comparisonInstruction = code[allocationCheck];
            List<CodeInstruction> result = new List<CodeInstruction>(code.Count + 5);
            foreach (CodeInstruction instruction in code)
            {
                if (ReferenceEquals(instruction, collection) || ReferenceEquals(instruction, comparisonInstruction))
                {
                    CodeInstruction loadBar = new CodeInstruction(OpCodes.Ldarg_0);
                    loadBar.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    result.Add(loadBar);
                    if (ReferenceEquals(instruction, comparisonInstruction))
                    {
                        // Update the local as well as the comparison operand; otherwise allocation
                        // could use a different count from the one that triggered the rebuild.
                        result.Add(new CodeInstruction(OpCodes.Call, resolveCount));
                        result.Add(new CodeInstruction(OpCodes.Dup));
                        result.Add(storeCount);
                    }
                }
                result.Add(instruction);
            }

            localSlotIndicesPatched = true;
            return result;
        }

        private static IEnumerable<CodeInstruction> Unsupported(List<CodeInstruction> original, string reason)
        {
            ExtraSlots.LogWarning($"Hotbar slot-index patch could not be applied ({reason}). " +
                "Extra hotbar icon updates are disabled; native and other mods' bars remain unchanged.");
            return original;
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
        {
            for (int i = 0; i < bar.m_items.Count; i++)
            {
                ItemDrop.ItemData item = bar.m_items[i];
                if (item != null && item.m_gridPos.y == 0 && item.m_gridPos.x == index)
                    return item;
            }
            return null;
        }

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
                    player.UseItem(null, item, false);
            }
            return false;
        }
    }

    private static void UpdateQueuedIndicators(HotkeyBar bar, Player player)
    {
        if (!bar || player == null || player.m_actionQueue.Count == 0)
            return;

        string name = bar.name;
        bool updateEquippedState = name == QuickSlotsHotBar.barName || name == AmmoSlotsHotBar.barName || name == FoodSlotsHotBar.barName;
        for (int i = 0; i < bar.m_elements.Count; i++)
        {
            HotkeyBar.ElementData element = bar.m_elements[i];
            if (element?.m_go == null)
                continue;

            ElementExtraData extraData = GetElementExtraData(element);
            QueuedEquipIndicator.UpdateHotbarElement(element, extraData.QueuedImage, GetItemForElement(bar, i), player, updateEquippedState);
        }
    }

    private static Vector3 LeftTopPoint => Hud.instance ? new Vector3(-Hud.instance.m_rootObject.transform.position.x, Hud.instance.m_rootObject.transform.position.y, 0) : new Vector3(-1280, 720, 0);

    private static List<HotkeyBar> GetHotKeyBarsToControl() => Hud.instance ? Hud.instance.m_rootObject.GetComponentsInChildren<HotkeyBar>().Where(IsBarToControl).OrderBy(bar => Vector3.Distance(bar.transform.localPosition, LeftTopPoint)).ToList() : null;

    private static bool UpdateCurrentHotkeyBar(bool joyHotbarLeft, bool joyHotbarRight, bool joyHotbarUse)
    {
        // Block the caller's initial-bar fallback as well as actions on an existing bar.
        if (!IsHotkeyBarsActive())
            return true;

        if (_currentBarIndex < 0 || _currentBarIndex > bars.Count - 1)
            return false;

        HotkeyBar hotkeyBar = bars[_currentBarIndex];
        if (hotkeyBar.m_selected < 0 || hotkeyBar.m_selected > hotkeyBar.m_elements.Count - 1)
            return false;

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
        // Slot shortcut validation checks TakeInput only after an actual key press.
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

            if ((joyHotbarLeft || joyHotbarRight || joyHotbarUse)
                && !UpdateCurrentHotkeyBar(joyHotbarLeft, joyHotbarRight, joyHotbarUse))
            {
                ChangeActiveHotkeyBar();
            }

            List<HotkeyBar> currentBars = bars;
            if (currentBars == null)
                return;

            for (int i = 0; i < currentBars.Count && ReferenceEquals(currentBars, bars); i++)
            {
                HotkeyBar bar = currentBars[i];
                if (!bar)
                    continue;

                if (!refreshGates.TryGetValue(bar, out HotkeyBarRefreshGate refreshGate))
                {
                    refreshGate = new HotkeyBarRefreshGate();
                    refreshGates[bar] = refreshGate;
                }
                if (!refreshGate.CanAttemptUpdate || renderContexts.ContainsKey(bar))
                    continue;

                try
                {
                    bar.m_selected = _currentBarIndex == i
                        ? Mathf.Clamp(bar.m_selected, -1, bar.m_elements.Count - 1)
                        : -1;

                    if (refreshGate.ShouldRefresh(bar, player))
                    {
                        bar.UpdateIcons(player);
                        refreshGate.Resample(bar, player);
                    }

                    UpdateQueuedIndicators(bar, player);
                    refreshGate.ReportSuccess();
                }
                catch (Exception exception)
                {
                    // A failing bar must not abort the other bars or repeat at the frame rate.
                    refreshGate.ReportFailure(bar, exception);
                }
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
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HotkeyBar __instance, out HotkeyBarRenderContext __state)
        {
            __state = null;
            if (!__instance || !IsExtraSlotsHotBar(__instance.name))
                return true;

            // Different bars have independent snapshots. A recursive update of the very same
            // bar cannot safely clear/rebuild its live m_items and m_elements lists.
            if (!localSlotIndicesPatched || renderContexts.ContainsKey(__instance))
                return false;

            __state = new HotkeyBarRenderContext(__instance);
            renderContexts.Add(__instance, __state);
            return true;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(HotkeyBar __instance, HotkeyBarRenderContext __state, bool __runOriginal)
        {
            if (!__runOriginal || __state == null || !__state.ItemsCollected || !__instance)
                return;

            string currentBarName = __instance.name;

            bool hideStackSize;
            int widthInElements;
            bool fillUp;
            float elementSpace;

            if (currentBarName == FoodSlotsHotBar.barName)
            {
                hideStackSize = ExtraSlots.foodSlotsHideStackSize.Value;
                widthInElements = ExtraSlots.foodSlotsWidthInElements.Value;
                fillUp = ExtraSlots.foodSlotsFillDirectionUp.Value;
                elementSpace = ExtraSlots.foodSlotsElementSpace.Value;
            }
            else if (currentBarName == AmmoSlotsHotBar.barName)
            {
                hideStackSize = ExtraSlots.ammoSlotsHideStackSize.Value;
                widthInElements = ExtraSlots.ammoSlotsWidthInElements.Value;
                fillUp = ExtraSlots.ammoSlotsFillDirectionUp.Value;
                elementSpace = ExtraSlots.ammoSlotsElementSpace.Value;
            }
            else
            {
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

                if (index >= __state.BarSlots.Length || __state.BarSlots[index] is not Slot slot)
                    continue;

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
                    && __state.SlotItems[index] is ItemDrop.ItemData item
                    && (item.IsEquipable() || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable))
                {
                    elementData.m_amount.SetText(elementData.m_stackText.ToFastString());
                }

                elementData.m_go.transform.localPosition =
                    new Vector3(index % widthInElements, (fillUp ? 1 : -1) * (index / widthInElements), 0f) * elementSpace;
            }
        }

        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(ref HotkeyBarRenderContext __state, Exception __exception)
        {
            HotkeyBarRenderContext context = __state;
            __state = null;
            if (context != null && renderContexts.TryGetValue(context.Bar, out HotkeyBarRenderContext active)
                && ReferenceEquals(active, context))
                renderContexts.Remove(context.Bar);

            return __exception;
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
