using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DungeonBlade.Items;
using EquipmentSlot = DungeonBlade.Items.EquipmentSlot;

namespace DungeonBlade.Inventory
{
    /// <summary>
    /// Serializable single inventory slot. Null item = empty.
    /// </summary>
    [Serializable]
    public class InventorySlot
    {
        public ItemData item;
        public int quantity;

        public bool IsEmpty => item == null || quantity <= 0;

        public void Clear() { item = null; quantity = 0; }

        public bool CanStack(ItemData other)
        {
            if (item == null || other == null) return false;
            if (!item.stackable || !other.stackable) return false;
            if (item.itemId != other.itemId) return false;
            return quantity < item.maxStack;
        }
    }

    /// <summary>
    /// Main inventory per GDD Section 7.
    /// - 48-slot main grid (6x8)
    /// - 9 equipment slots (Head/Chest/Legs/Boots/MainHand/OffHand/Ring1/Ring2/Amulet)
    /// - 4-slot hotbar for consumables (keys 1-4)
    /// - Gold currency, weight system (optional toggle)
    /// </summary>
    public class InventorySystem : MonoBehaviour
    {
        public const int MainGridSize = 48;   // 6x8
        public const int HotbarSize   = 4;

        [Header("Storage")]
        public InventorySlot[] mainGrid;
        public InventorySlot[] hotbar;
        public Dictionary<EquipmentSlot, InventorySlot> equipment = new Dictionary<EquipmentSlot, InventorySlot>();

        [Header("Economy")]
        public int gold;
        public int dungeonClearTokens;

        [Header("Weight (optional)")]
        public bool weightEnabled = false;
        public float maxCarryWeight = 100f;

        [Header("Refs")]
        public DungeonBlade.Player.PlayerStats playerStats;
        public DungeonBlade.Player.PlayerCombat playerCombat;

        [Header("Events")]
        public UnityEvent OnInventoryChanged;
        public UnityEvent<int> OnGoldChanged;
        public UnityEvent<int> OnTokensChanged;
        public UnityEvent<EquipmentSlot, ItemData> OnItemEquipped;
        public UnityEvent<EquipmentSlot, ItemData> OnItemUnequipped;
        public UnityEvent<ItemData> OnItemPickup;

        public float CurrentWeight { get; private set; }
        public bool IsOverweight => weightEnabled && CurrentWeight > maxCarryWeight;

        void Awake()
        {
            // Init storage
            if (mainGrid == null || mainGrid.Length != MainGridSize)
            {
                mainGrid = new InventorySlot[MainGridSize];
                for (int i = 0; i < MainGridSize; i++) mainGrid[i] = new InventorySlot();
            }
            if (hotbar == null || hotbar.Length != HotbarSize)
            {
                hotbar = new InventorySlot[HotbarSize];
                for (int i = 0; i < HotbarSize; i++) hotbar[i] = new InventorySlot();
            }
            foreach (EquipmentSlot s in Enum.GetValues(typeof(EquipmentSlot)))
            {
                if (s == EquipmentSlot.None) continue;
                equipment[s] = new InventorySlot();
            }
        }

        void Update()
        {
            // Hotbar keys
            for (int i = 0; i < HotbarSize; i++)
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1 + i))
                    UseHotbar(i);
            }
        }

        // ─────── Public API ───────

        public bool TryAddItem(ItemData item, int quantity = 1)
        {
            if (item == null) return false;
            int remaining = quantity;

            // Stack onto existing if possible
            if (item.stackable)
            {
                foreach (var slot in AllActiveSlots())
                {
                    if (slot.CanStack(item))
                    {
                        int space = item.maxStack - slot.quantity;
                        int move = Mathf.Min(space, remaining);
                        slot.quantity += move;
                        remaining -= move;
                        if (remaining <= 0) break;
                    }
                }
            }

            // Put rest in first empty main grid slot
            while (remaining > 0)
            {
                InventorySlot empty = FirstEmpty(mainGrid);
                if (empty == null) { InventoryFull(item, remaining); break; }
                empty.item = item;
                empty.quantity = Mathf.Min(item.maxStack == 0 ? 1 : item.maxStack, remaining);
                if (!item.stackable) empty.quantity = 1;
                remaining -= empty.quantity;
            }

            RecalcWeight();
            OnInventoryChanged?.Invoke();
            OnItemPickup?.Invoke(item);
            return remaining <= 0;
        }

        public bool RemoveItem(ItemData item, int quantity = 1)
        {
            int removed = 0;
            foreach (var slot in AllActiveSlots())
            {
                if (slot.item == item)
                {
                    int take = Mathf.Min(slot.quantity, quantity - removed);
                    slot.quantity -= take;
                    removed += take;
                    if (slot.quantity <= 0) slot.Clear();
                    if (removed >= quantity) break;
                }
            }
            RecalcWeight();
            OnInventoryChanged?.Invoke();
            return removed == quantity;
        }

        public int CountItem(ItemData item)
        {
            int count = 0;
            foreach (var slot in AllActiveSlots())
                if (slot.item == item) count += slot.quantity;
            return count;
        }

        public void AddGold(int amount)
        {
            if (amount == 0) return;
            gold = Mathf.Max(0, gold + amount);
            OnGoldChanged?.Invoke(gold);
        }

        public bool SpendGold(int amount)
        {
            if (gold < amount) return false;
            gold -= amount;
            OnGoldChanged?.Invoke(gold);
            return true;
        }

        public void AddTokens(int amount)
        {
            dungeonClearTokens = Mathf.Max(0, dungeonClearTokens + amount);
            OnTokensChanged?.Invoke(dungeonClearTokens);
        }

        public bool SpendTokens(int amount)
        {
            if (dungeonClearTokens < amount) return false;
            dungeonClearTokens -= amount;
            OnTokensChanged?.Invoke(dungeonClearTokens);
            return true;
        }

        // ─────── Equipment ───────

        public bool Equip(ItemData item, EquipmentSlot preferredSlot = EquipmentSlot.None)
        {
            if (item == null) return false;
            EquipmentSlot slot = preferredSlot;

            if (slot == EquipmentSlot.None)
            {
                if (item is WeaponData w)
                {
                    slot = w.weaponType == WeaponType.Sword ? EquipmentSlot.MainHand : EquipmentSlot.OffHand;
                }
                else if (item is ArmorData a)
                {
                    slot = a.slot;
                }
                else return false;
            }

            if (!equipment.ContainsKey(slot)) return false;

            // Swap: existing equipped goes to inventory
            var existing = equipment[slot];
            if (!existing.IsEmpty)
            {
                var oldItem = existing.item;
                existing.Clear();
                OnItemUnequipped?.Invoke(slot, oldItem);
                TryAddItem(oldItem);
                UnapplyEquipmentStats(oldItem);
            }

            RemoveItem(item, 1);   // take from main grid
            equipment[slot].item = item;
            equipment[slot].quantity = 1;
            ApplyEquipmentStats(item);
            OnItemEquipped?.Invoke(slot, item);
            OnInventoryChanged?.Invoke();
            return true;
        }

        public bool Unequip(EquipmentSlot slot)
        {
            if (!equipment.ContainsKey(slot) || equipment[slot].IsEmpty) return false;
            var item = equipment[slot].item;
            equipment[slot].Clear();
            UnapplyEquipmentStats(item);
            TryAddItem(item);
            OnItemUnequipped?.Invoke(slot, item);
            OnInventoryChanged?.Invoke();
            return true;
        }

        public ItemData GetEquipped(EquipmentSlot slot)
        {
            if (equipment.TryGetValue(slot, out var s)) return s.item;
            return null;
        }

        void ApplyEquipmentStats(ItemData item)
        {
            if (playerStats == null || item == null) return;
            if (item is ArmorData a)
            {
                playerStats.flatArmor    += a.flatDefense;
                playerStats.percentArmor  = Mathf.Clamp(playerStats.percentArmor + a.percentDefense, 0f, 0.85f);
                playerStats.maxHealth    += a.bonusMaxHp;
                playerStats.maxStamina   += a.bonusMaxStamina;
                playerStats.staminaRegenPerSec += a.staminaRegenBonus;
            }
            if (item is WeaponData w && playerCombat != null)
            {
                if (w.weaponType == WeaponType.Sword)
                {
                    playerCombat.comboDamage = new float[]
                    {
                        w.meleeDamage * 0.8f,
                        w.meleeDamage * 1.0f,
                        w.meleeDamage * 1.5f,
                    };
                    playerCombat.heavyDamage = w.meleeDamage * 2.5f;
                }
                else
                {
                    playerCombat.gunDamage   = w.rangedDamage;
                    playerCombat.gunFireRate = w.fireRate;
                    playerCombat.gunMagSize  = w.magSize;
                    playerCombat.gunReloadTime = w.reloadTime;
                    playerCombat.gunSpreadHip  = w.spreadHip;
                    playerCombat.gunSpreadAds  = w.spreadAds;
                }
            }
        }

        void UnapplyEquipmentStats(ItemData item)
        {
            if (playerStats == null || item == null) return;
            if (item is ArmorData a)
            {
                playerStats.flatArmor   -= a.flatDefense;
                playerStats.percentArmor = Mathf.Max(0f, playerStats.percentArmor - a.percentDefense);
                playerStats.maxHealth   -= a.bonusMaxHp;
                playerStats.maxStamina  -= a.bonusMaxStamina;
                playerStats.staminaRegenPerSec -= a.staminaRegenBonus;
            }
        }

        // ─────── Hotbar ───────

        public bool SetHotbar(int index, ItemData item)
        {
            if (index < 0 || index >= HotbarSize) return false;
            if (item != null && item.category != ItemCategory.Consumable) return false;
            hotbar[index].item = item;
            hotbar[index].quantity = item != null ? CountItem(item) : 0;
            OnInventoryChanged?.Invoke();
            return true;
        }

        public void UseHotbar(int index)
        {
            if (index < 0 || index >= HotbarSize) return;
            var slot = hotbar[index];
            if (slot.IsEmpty) return;
            if (slot.item is ConsumableData c)
            {
                if (c.Use(playerStats, transform))
                {
                    RemoveItem(c, 1);
                    slot.quantity = CountItem(c);
                    if (slot.quantity <= 0) slot.Clear();
                    OnInventoryChanged?.Invoke();
                }
            }
        }

        // ─────── Helpers ───────

        IEnumerable<InventorySlot> AllActiveSlots()
        {
            for (int i = 0; i < mainGrid.Length; i++) yield return mainGrid[i];
            for (int i = 0; i < hotbar.Length; i++)   yield return hotbar[i];
        }

        InventorySlot FirstEmpty(InventorySlot[] arr)
        {
            for (int i = 0; i < arr.Length; i++) if (arr[i].IsEmpty) return arr[i];
            return null;
        }

        void InventoryFull(ItemData item, int unused)
        {
            Debug.LogWarning($"Inventory full; {unused}x {item.displayName} dropped.");
        }

        void RecalcWeight()
        {
            if (!weightEnabled) { CurrentWeight = 0f; return; }
            float w = 0f;
            foreach (var slot in AllActiveSlots())
                if (!slot.IsEmpty && slot.item.category != ItemCategory.Consumable
                                  && slot.item.category != ItemCategory.Material)
                    w += slot.item.weight * slot.quantity;
            CurrentWeight = w;
        }
    }
}
