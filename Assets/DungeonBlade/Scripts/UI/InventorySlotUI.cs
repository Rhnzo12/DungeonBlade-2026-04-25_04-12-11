using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using DungeonBlade.Inventory;
using DungeonBlade.Items;
using EquipmentSlot = DungeonBlade.Items.EquipmentSlot;

namespace DungeonBlade.UI
{
    /// <summary>
    /// One cell of the inventory grid. Must live in a file named InventorySlotUI.cs
    /// (matches class name) so Unity's serializer can resolve the MonoScript
    /// reference when loading prefabs — otherwise saved prefabs show
    /// "missing script" warnings.
    /// </summary>
    public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public enum Kind { Main, Hotbar, Equipment }

        public Image background;
        public Image icon;
        public TMP_Text countText;

        InventoryUI ui;
        Kind kind;
        int index;
        EquipmentSlot equipmentSlot;
        InventorySlot currentSlot;

        public void Bind(InventoryUI ui, Kind kind, int index)
        {
            this.ui = ui; this.kind = kind; this.index = index;
            EnsureRefs();
        }

        public void BindEquipment(InventoryUI ui, EquipmentSlot slot)
        {
            this.ui = ui; this.kind = Kind.Equipment; equipmentSlot = slot;
            EnsureRefs();
        }

        void EnsureRefs()
        {
            if (icon == null)
            {
                foreach (var img in GetComponentsInChildren<Image>(true))
                    if (img != GetComponent<Image>()) { icon = img; break; }
            }
            if (countText == null) countText = GetComponentInChildren<TMP_Text>(true);
            if (background == null) background = GetComponent<Image>();
        }

        public void Render(InventorySlot slot)
        {
            currentSlot = slot;
            if (slot == null || slot.IsEmpty)
            {
                if (icon != null) icon.enabled = false;
                if (countText != null) countText.text = "";
                if (background != null) background.color = new Color(0.1f, 0.1f, 0.15f, 0.8f);
                return;
            }
            if (icon != null) { icon.enabled = true; icon.sprite = slot.item.icon; }
            if (countText != null) countText.text = slot.quantity > 1 ? slot.quantity.ToString() : "";
            if (background != null) background.color = ItemData.RarityColor(slot.item.rarity) * 0.6f;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (currentSlot != null) ui?.ShowTooltip(currentSlot, GetComponent<RectTransform>());
        }

        public void OnPointerExit(PointerEventData e) { ui?.HideTooltip(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (currentSlot == null || currentSlot.IsEmpty) return;
            if (e.button == PointerEventData.InputButton.Right)
                ui?.ShowContextMenu(currentSlot, e.position);
        }
    }
}
