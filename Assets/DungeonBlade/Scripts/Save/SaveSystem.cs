using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DungeonBlade.Items;
using DungeonBlade.Inventory;
using EquipmentSlot = DungeonBlade.Items.EquipmentSlot;

namespace DungeonBlade.Save
{
    /// <summary>
    /// JSON save/load of player profile, bank, inventory, and progression.
    /// Per GDD 8.3: bank vault always safe; inventory safe once picked up.
    /// </summary>
    public class SaveSystem : MonoBehaviour
    {
        [Header("Config")]
        public string profileName = "default";
        public bool loadOnStart = true;

        [Header("Item Registry")]
        [Tooltip("Every item that can be saved/loaded must be listed here so itemId can round-trip.")]
        public ItemData[] itemRegistry;

        [Header("Refs")]
        public InventorySystem inventory;
        public Bank.BankSystem bank;
        public Progression.PlayerProgression progression;

        Dictionary<string, ItemData> idLookup;

        string SavePath => Path.Combine(Application.persistentDataPath, $"{profileName}.json");

        [Serializable] class SlotDto { public string itemId; public int quantity; }
        [Serializable] class EquipDto { public string slot; public string itemId; }
        [Serializable]
        class SaveDto
        {
            public int level;
            public int xp;
            public int skillPoints;

            public int gold;
            public int tokens;

            public List<SlotDto>  mainGrid = new List<SlotDto>();
            public List<SlotDto>  hotbar   = new List<SlotDto>();
            public List<EquipDto> equipment = new List<EquipDto>();
            public List<SlotDto>  vault    = new List<SlotDto>();

            public string savedAtIso;
            public int saveVersion = 1;
        }

        void Awake()
        {
            BuildLookup();
        }

        void Start()
        {
            if (loadOnStart) LoadAll();
        }

        void BuildLookup()
        {
            idLookup = new Dictionary<string, ItemData>();
            if (itemRegistry == null) return;
            foreach (var it in itemRegistry)
            {
                if (it != null && !string.IsNullOrEmpty(it.itemId))
                    idLookup[it.itemId] = it;
            }
        }

        public void SaveAll()
        {
            if (inventory == null) return;
            var dto = new SaveDto { savedAtIso = DateTime.UtcNow.ToString("o") };

            if (progression != null)
            {
                dto.level       = progression.level;
                dto.xp          = progression.currentXp;
                dto.skillPoints = progression.skillPoints;
            }

            dto.gold   = inventory.gold;
            dto.tokens = inventory.dungeonClearTokens;

            foreach (var s in inventory.mainGrid) AddSlotDto(dto.mainGrid, s);
            foreach (var s in inventory.hotbar)   AddSlotDto(dto.hotbar, s);
            foreach (var kv in inventory.equipment)
                if (!kv.Value.IsEmpty)
                    dto.equipment.Add(new EquipDto { slot = kv.Key.ToString(), itemId = kv.Value.item.itemId });

            if (bank != null)
                foreach (var s in bank.vault) AddSlotDto(dto.vault, s);

            string json = JsonUtility.ToJson(dto, prettyPrint: true);
            try
            {
                File.WriteAllText(SavePath, json);
                Debug.Log($"[SaveSystem] Saved to {SavePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Save failed: {e.Message}");
            }
        }

        public bool LoadAll()
        {
            if (!File.Exists(SavePath))
            {
                Debug.Log($"[SaveSystem] No save at {SavePath}; fresh profile.");
                return false;
            }
            try
            {
                string json = File.ReadAllText(SavePath);
                var dto = JsonUtility.FromJson<SaveDto>(json);
                if (dto == null) return false;
                ApplyDto(dto);
                Debug.Log($"[SaveSystem] Loaded {SavePath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Load failed: {e.Message}");
                return false;
            }
        }

        void ApplyDto(SaveDto dto)
        {
            if (progression != null)
                progression.LoadFromSave(dto.level == 0 ? 1 : dto.level, dto.xp, dto.skillPoints);

            if (inventory != null)
            {
                inventory.gold = dto.gold;
                inventory.dungeonClearTokens = dto.tokens;
                inventory.OnGoldChanged?.Invoke(inventory.gold);
                inventory.OnTokensChanged?.Invoke(inventory.dungeonClearTokens);

                ClearSlots(inventory.mainGrid);
                ClearSlots(inventory.hotbar);
                LoadSlots(dto.mainGrid, inventory.mainGrid);
                LoadSlots(dto.hotbar,   inventory.hotbar);

                foreach (var kv in inventory.equipment) kv.Value.Clear();
                foreach (var eq in dto.equipment)
                {
                    if (!Enum.TryParse(eq.slot, out EquipmentSlot slot)) continue;
                    if (!idLookup.TryGetValue(eq.itemId, out var item)) continue;
                    if (!inventory.equipment.ContainsKey(slot)) continue;
                    inventory.equipment[slot].item = item;
                    inventory.equipment[slot].quantity = 1;
                }

                inventory.OnInventoryChanged?.Invoke();
            }

            if (bank != null)
            {
                ClearSlots(bank.vault);
                LoadSlots(dto.vault, bank.vault);
                bank.OnVaultChanged?.Invoke();
            }
        }

        void AddSlotDto(List<SlotDto> list, InventorySlot s)
        {
            if (s.IsEmpty) { list.Add(new SlotDto()); return; }
            list.Add(new SlotDto { itemId = s.item.itemId, quantity = s.quantity });
        }

        void LoadSlots(List<SlotDto> src, InventorySlot[] dst)
        {
            if (src == null) return;
            int n = Mathf.Min(src.Count, dst.Length);
            for (int i = 0; i < n; i++)
            {
                var s = src[i];
                if (string.IsNullOrEmpty(s.itemId)) { dst[i].Clear(); continue; }
                if (idLookup.TryGetValue(s.itemId, out var item))
                {
                    dst[i].item = item;
                    dst[i].quantity = Mathf.Max(1, s.quantity);
                }
                else dst[i].Clear();
            }
        }

        void ClearSlots(InventorySlot[] arr) { foreach (var s in arr) s.Clear(); }

        public void DeleteSave()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }
}
