using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonBlade.Characters
{
    /// <summary>
    /// Character select screen. Built at scene-build time by DBSceneBuilder —
    /// shows a row of character portraits, displays details of the hovered one,
    /// and commits the selection to the CharacterRoster when the user clicks
    /// "Confirm".
    ///
    /// Opens automatically when the lobby loads and the user hasn't picked yet,
    /// or can be triggered manually by interacting with a "Character Change"
    /// NPC.
    ///
    /// NOTE: CharacterPortraitButton is defined in its own file
    /// (CharacterPortraitButton.cs). Keeping it here as a secondary class
    /// caused persistent "Missing Script" errors on the prefab.
    /// </summary>
    public class CharacterSelectUI : MonoBehaviour
    {
        [Header("Refs (wired by builder)")]
        public GameObject rootPanel;
        public Transform portraitRowParent;
        public GameObject portraitButtonPrefab;
        public Image detailPortrait;
        public TMP_Text detailName;
        public TMP_Text detailFlavor;
        public TMP_Text detailStats;
        public Button confirmButton;
        public Button closeButton;

        [Header("Config")]
        public bool openAutomaticallyIfNoSelection = true;
        public bool closeOnConfirm = true;

        CharacterRoster roster;
        CharacterData pendingSelection;
        readonly List<CharacterPortraitButton> spawnedButtons = new List<CharacterPortraitButton>();

        void Start()
        {
            roster = FindObjectOfType<CharacterRoster>();
            if (roster == null)
            {
                Debug.LogWarning("[CharacterSelectUI] No CharacterRoster in scene — can't populate.");
                gameObject.SetActive(false);
                return;
            }

            Populate();

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveAllListeners();
                confirmButton.onClick.AddListener(Confirm);
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }

            // Preview the current selection
            pendingSelection = roster.Current;
            UpdateDetails(pendingSelection);

            if (openAutomaticallyIfNoSelection && !PlayerPrefs.HasKey("DungeonBlade.SelectedCharacterId"))
                Open();
            else
                rootPanel.SetActive(false);
        }

        void Populate()
        {
            if (portraitRowParent == null || portraitButtonPrefab == null) return;

            // Clear existing
            for (int i = portraitRowParent.childCount - 1; i >= 0; i--)
                Destroy(portraitRowParent.GetChild(i).gameObject);
            spawnedButtons.Clear();

            foreach (var c in roster.characters)
            {
                if (c == null) continue;
                var btnGO = Instantiate(portraitButtonPrefab, portraitRowParent);
                var btn = btnGO.GetComponent<CharacterPortraitButton>();
                if (btn == null) btn = btnGO.AddComponent<CharacterPortraitButton>();
                btn.Setup(c, OnPortraitHovered, OnPortraitClicked);
                spawnedButtons.Add(btn);
            }
        }

        void OnPortraitHovered(CharacterData c) => UpdateDetails(c);
        void OnPortraitClicked(CharacterData c) { pendingSelection = c; UpdateDetails(c); }

        void UpdateDetails(CharacterData c)
        {
            if (c == null) return;
            if (detailPortrait != null)
            {
                // Drop any leftover initial overlay first; we'll re-add it for fallback.
                var oldOverlay = detailPortrait.transform.Find("Initial");
                if (c.portrait != null)
                {
                    detailPortrait.sprite = CharacterPortraitButton.CropTopFraction(c.portrait, c.portraitCropTopFraction);
                    detailPortrait.color = Color.white;
                    detailPortrait.preserveAspect = true;
                    detailPortrait.enabled = true;
                    if (oldOverlay != null) oldOverlay.gameObject.SetActive(false);
                }
                else
                {
                    // Show colored fallback so the portrait panel isn't blank
                    detailPortrait.sprite = null;
                    detailPortrait.color = CharacterPortraitButton.ColorFromIdPublic(c.characterId ?? c.displayName ?? "default");
                    detailPortrait.enabled = true;
                    CharacterPortraitButton.EnsureInitialOverlay(detailPortrait.transform, c.displayName);
                    if (oldOverlay != null) oldOverlay.gameObject.SetActive(true);
                }
            }
            if (detailName != null) detailName.text = c.displayName;
            if (detailFlavor != null) detailFlavor.text = c.flavorText;
            if (detailStats != null)
            {
                var sb = new System.Text.StringBuilder();
                if (c.hpMultiplier != 1f)          sb.AppendLine($"HP: x{c.hpMultiplier:0.##}");
                if (c.staminaMultiplier != 1f)     sb.AppendLine($"Stamina: x{c.staminaMultiplier:0.##}");
                if (c.moveSpeedMultiplier != 1f)   sb.AppendLine($"Speed: x{c.moveSpeedMultiplier:0.##}");
                if (c.meleeDamageMultiplier != 1f) sb.AppendLine($"Melee Dmg: x{c.meleeDamageMultiplier:0.##}");
                if (c.rangedDamageMultiplier != 1f)sb.AppendLine($"Ranged Dmg: x{c.rangedDamageMultiplier:0.##}");
                detailStats.text = sb.Length == 0 ? "<i>Balanced stats</i>" : sb.ToString();
            }
        }

        public void Open()
        {
            if (rootPanel != null) rootPanel.SetActive(true);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void Confirm()
        {
            if (roster != null && pendingSelection != null)
                roster.Select(pendingSelection);
            if (closeOnConfirm) Close();
        }
    }
}
