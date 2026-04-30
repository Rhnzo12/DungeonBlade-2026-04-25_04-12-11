using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DungeonBlade.Items;
using DungeonBlade.Inventory;
using EquipmentSlot = DungeonBlade.Items.EquipmentSlot;

namespace DungeonBlade.UI
{
    /// <summary>
    /// Grid-based inventory UI per GDD 7.2.
    /// - 6x8 main grid
    /// - 9 equipment slots down the left
    /// - 4-slot hotbar at bottom
    /// - Item tooltip on hover, right-click context menu
    /// Attach to a Canvas; call Show() / Hide() from input.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        [Header("Refs")]
        public InventorySystem inventory;
        public GameObject rootPanel;

        [Header("Slot Prefab")]
        public GameObject slotPrefab;
        public Transform mainGridParent;
        public Transform hotbarParent;
        public Transform equipmentParent;

        [Header("Tooltip")]
        public GameObject tooltipPanel;
        public TMP_Text tooltipName;
        public TMP_Text tooltipRarity;
        public TMP_Text tooltipStats;
        public TMP_Text tooltipFlavor;

        [Header("Context Menu")]
        public GameObject contextMenuPanel;
        public Button btnEquip;
        public Button btnUse;
        public Button btnSell;
        public Button btnDiscard;

        [Header("Other UI")]
        public TMP_Text goldText;
        public TMP_Text tokenText;
        public TMP_Text weightText;

        readonly List<InventorySlotUI> mainSlotUIs = new List<InventorySlotUI>();
        readonly List<InventorySlotUI> hotbarSlotUIs = new List<InventorySlotUI>();
        readonly Dictionary<EquipmentSlot, InventorySlotUI> equipSlotUIs = new Dictionary<EquipmentSlot, InventorySlotUI>();

        InventorySlot activeSlot;
        EquipmentSlot? activeEquipSlot;

        void Awake()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
            if (tooltipPanel != null) tooltipPanel.SetActive(false);
            if (contextMenuPanel != null) contextMenuPanel.SetActive(false);
        }

        void Start()
        {
            if (inventory == null) inventory = DungeonBlade.Core.GameServices.Inventory;
            if (inventory == null) { Debug.LogError("InventoryUI: no InventorySystem"); return; }

            BuildSlots();
            inventory.OnInventoryChanged.AddListener(Refresh);
            inventory.OnGoldChanged.AddListener(_ => Refresh());
            inventory.OnTokensChanged.AddListener(_ => Refresh());
            Refresh();

            if (btnEquip != null)  btnEquip.onClick.AddListener(OnEquipClicked);
            if (btnUse != null)    btnUse.onClick.AddListener(OnUseClicked);
            if (btnSell != null)   btnSell.onClick.AddListener(OnSellClicked);
            if (btnDiscard != null) btnDiscard.onClick.AddListener(OnDiscardClicked);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.Tab))
            {
                Toggle();
            }
        }

        public void Toggle()
        {
            if (rootPanel == null) return;
            bool active = !rootPanel.activeSelf;
            rootPanel.SetActive(active);
            Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = active;
            if (!active) { HideTooltip(); HideContextMenu(); }
            else Refresh();
        }

        void BuildSlots()
        {
            if (slotPrefab == null || mainGridParent == null) return;

            // Main grid
            ClearChildren(mainGridParent);
            mainSlotUIs.Clear();
            for (int i = 0; i < InventorySystem.MainGridSize; i++)
            {
                var go = Instantiate(slotPrefab, mainGridParent);
                var ui = go.GetComponent<InventorySlotUI>() ?? go.AddComponent<InventorySlotUI>();
                ui.Bind(this, InventorySlotUI.Kind.Main, i);
                mainSlotUIs.Add(ui);
            }

            if (hotbarParent != null)
            {
                ClearChildren(hotbarParent);
                hotbarSlotUIs.Clear();
                for (int i = 0; i < InventorySystem.HotbarSize; i++)
                {
                    var go = Instantiate(slotPrefab, hotbarParent);
                    var ui = go.GetComponent<InventorySlotUI>() ?? go.AddComponent<InventorySlotUI>();
                    ui.Bind(this, InventorySlotUI.Kind.Hotbar, i);
                    hotbarSlotUIs.Add(ui);
                }
            }

            if (equipmentParent != null)
            {
                ClearChildren(equipmentParent);
                equipSlotUIs.Clear();
                var slots = new[] {
                    EquipmentSlot.Head, EquipmentSlot.Chest, EquipmentSlot.Legs, EquipmentSlot.Boots,
                    EquipmentSlot.MainHand, EquipmentSlot.OffHand,
                    EquipmentSlot.Ring1, EquipmentSlot.Ring2, EquipmentSlot.Amulet,
                };
                foreach (var s in slots)
                {
                    var go = Instantiate(slotPrefab, equipmentParent);
                    var ui = go.GetComponent<InventorySlotUI>() ?? go.AddComponent<InventorySlotUI>();
                    ui.BindEquipment(this, s);
                    equipSlotUIs[s] = ui;
                }
            }
        }

        void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }

        public void Refresh()
        {
            if (inventory == null) return;
            for (int i = 0; i < mainSlotUIs.Count; i++) mainSlotUIs[i].Render(inventory.mainGrid[i]);
            for (int i = 0; i < hotbarSlotUIs.Count; i++) hotbarSlotUIs[i].Render(inventory.hotbar[i]);
            foreach (var kv in equipSlotUIs) kv.Value.Render(inventory.equipment[kv.Key]);

            if (goldText != null) goldText.text = $"Gold: {inventory.gold:N0}";
            if (tokenText != null) tokenText.text = $"Tokens: {inventory.dungeonClearTokens}";
            if (weightText != null && inventory.weightEnabled)
                weightText.text = $"Weight: {inventory.CurrentWeight:F1} / {inventory.maxCarryWeight:F0}";
            else if (weightText != null) weightText.text = "";
        }

        public void ShowTooltip(InventorySlot slot, RectTransform anchor)
        {
            if (slot == null || slot.IsEmpty || tooltipPanel == null) return;
            var item = slot.item;
            tooltipPanel.SetActive(true);
            tooltipPanel.transform.position = anchor.position + new Vector3(80, -30, 0);
            if (tooltipName != null) { tooltipName.text = item.displayName; tooltipName.color = ItemData.RarityColor(item.rarity); }
            if (tooltipRarity != null) { tooltipRarity.text = item.rarity.ToString().ToUpper(); tooltipRarity.color = ItemData.RarityColor(item.rarity); }
            if (tooltipStats != null) tooltipStats.text = BuildStatText(item);
            if (tooltipFlavor != null) tooltipFlavor.text = item.flavorText;
        }

        public void HideTooltip() { if (tooltipPanel != null) tooltipPanel.SetActive(false); }

        string BuildStatText(ItemData item)
        {
            string s = $"Lvl req: {item.levelRequirement}\nValue: {item.baseValue}g";
            if (item is WeaponData w)
            {
                if (w.weaponType == WeaponType.Sword)
                    s += $"\nMelee DMG: {w.meleeDamage}\nAttack Speed: {w.attackSpeedMult:F2}x";
                else
                    s += $"\nRanged DMG: {w.rangedDamage}\nFire Rate: {w.fireRate}\nMag: {w.magSize}";
                if (w.hasSpecialProperty) s += $"\n<color=#ff66cc>{w.specialPropertyDescription}</color>";
            }
            else if (item is ArmorData a)
            {
                if (a.flatDefense    > 0) s += $"\n+{a.flatDefense} DEF";
                if (a.percentDefense > 0) s += $"\n+{a.percentDefense*100:F0}% Resist";
                if (a.bonusMaxHp     > 0) s += $"\n+{a.bonusMaxHp} Max HP";
                if (a.bonusMaxStamina> 0) s += $"\n+{a.bonusMaxStamina} Max Stamina";
            }
            else if (item is ConsumableData c)
            {
                s += $"\nEffect: {c.effect} ({c.value})";
            }
            return s;
        }

        public void ShowContextMenu(InventorySlot slot, Vector3 position)
        {
            if (slot == null || slot.IsEmpty || contextMenuPanel == null) return;
            activeSlot = slot;
            activeEquipSlot = null;
            contextMenuPanel.SetActive(true);
            contextMenuPanel.transform.position = position;
            bool canEquip = slot.item is WeaponData || slot.item is ArmorData;
            bool canUse = slot.item is ConsumableData;
            if (btnEquip != null) btnEquip.gameObject.SetActive(canEquip);
            if (btnUse != null) btnUse.gameObject.SetActive(canUse);
        }

        public void HideContextMenu() { if (contextMenuPanel != null) contextMenuPanel.SetActive(false); }

        void OnEquipClicked()
        {
            if (activeSlot != null && inventory.Equip(activeSlot.item)) Refresh();
            HideContextMenu();
        }
        void OnUseClicked()
        {
            if (activeSlot?.item is ConsumableData c && c.Use(inventory.playerStats, inventory.transform))
            {
                inventory.RemoveItem(c, 1);
                Refresh();
            }
            HideContextMenu();
        }
        void OnSellClicked()
        {
            var bank = DungeonBlade.Core.GameServices.Bank;
            if (bank != null && activeSlot != null) bank.Sell(activeSlot.item, 1);
            HideContextMenu();
            Refresh();
        }
        void OnDiscardClicked()
        {
            if (activeSlot != null) inventory.RemoveItem(activeSlot.item, activeSlot.quantity);
            HideContextMenu();
            Refresh();
        }
    }
}
