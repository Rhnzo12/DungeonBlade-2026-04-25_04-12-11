using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DungeonBlade.Bank;
using DungeonBlade.Items;

namespace DungeonBlade.UI
{
    /// <summary>
    /// Bank / shop UI per GDD Section 8.
    /// Tabs: Vault (deposit/withdraw), Shop (buy), Sell, Token Exchange.
    /// Hook a BankNPC interact trigger to call Show(bankSystem).
    /// </summary>
    public class BankUI : MonoBehaviour
    {
        public enum Tab { Vault, Shop, Sell, Tokens }

        [Header("Refs")]
        public BankSystem bankSystem;
        public GameObject rootPanel;

        [Header("Tabs")]
        public Button vaultTab, shopTab, sellTab, tokenTab;
        public GameObject vaultPanel, shopPanel, sellPanel, tokenPanel;

        [Header("Row Prefab")]
        public GameObject shopRowPrefab;
        public Transform vaultParent;
        public Transform shopParent;
        public Transform sellParent;
        public Transform tokenParent;

        [Header("Status")]
        public TMP_Text goldText;
        public TMP_Text tokenText;
        public TMP_Text statusText;

        Tab currentTab = Tab.Shop;
        bool autoBuilt;

        void Awake()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
            EnsureBuilt();
            if (vaultTab != null) vaultTab.onClick.AddListener(() => SwitchTab(Tab.Vault));
            if (shopTab != null)  shopTab.onClick.AddListener(()  => SwitchTab(Tab.Shop));
            if (sellTab != null)  sellTab.onClick.AddListener(()  => SwitchTab(Tab.Sell));
            if (tokenTab != null) tokenTab.onClick.AddListener(() => SwitchTab(Tab.Tokens));
        }

        public void Show(BankSystem bank)
        {
            bankSystem = bank;
            EnsureBuilt();
            if (rootPanel != null) rootPanel.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SwitchTab(Tab.Shop);
            UpdateStatus();
        }

        /// <summary>
        /// Lazy procedural build of the bank UI so this component works even
        /// when an older scene was saved with empty tab/panel/prefab references
        /// (the historical scene builder only created an empty Root panel).
        /// Skipped entirely if the builder pre-wired everything.
        /// </summary>
        void EnsureBuilt()
        {
            if (autoBuilt) return;
            if (rootPanel == null) return;
            if (vaultTab != null && shopTab != null && sellTab != null && tokenTab != null
                && vaultPanel != null && shopPanel != null && sellPanel != null && tokenPanel != null
                && shopRowPrefab != null) return;

            autoBuilt = true;
            BankUIAutoBuilder.Build(this);
        }

        public void Hide()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            if (rootPanel != null && rootPanel.activeSelf && Input.GetKeyDown(KeyCode.Escape))
                Hide();
        }

        void SwitchTab(Tab t)
        {
            currentTab = t;
            if (vaultPanel != null) vaultPanel.SetActive(t == Tab.Vault);
            if (shopPanel != null) shopPanel.SetActive(t == Tab.Shop);
            if (sellPanel != null) sellPanel.SetActive(t == Tab.Sell);
            if (tokenPanel != null) tokenPanel.SetActive(t == Tab.Tokens);

            switch (t)
            {
                case Tab.Vault:  BuildVault();  break;
                case Tab.Shop:   BuildShop();   break;
                case Tab.Sell:   BuildSell();   break;
                case Tab.Tokens: BuildTokens(); break;
            }
        }

        void BuildVault()
        {
            if (vaultParent == null || shopRowPrefab == null || bankSystem == null) return;
            ClearChildren(vaultParent);
            for (int i = 0; i < bankSystem.vault.Length; i++)
            {
                var slot = bankSystem.vault[i];
                if (slot.IsEmpty) continue;
                int idx = i;  // capture
                var row = Instantiate(shopRowPrefab, vaultParent);
                var ui = row.GetComponent<BankRowUI>() ?? row.AddComponent<BankRowUI>();
                ui.Setup(slot.item, slot.quantity, "Withdraw", () =>
                {
                    bankSystem.WithdrawToInventory(idx, 1);
                    BuildVault();
                    UpdateStatus();
                });
            }
        }

        void BuildShop()
        {
            if (shopParent == null || shopRowPrefab == null || bankSystem == null) return;
            ClearChildren(shopParent);
            foreach (var entry in bankSystem.shopItems)
            {
                if (entry?.item == null) continue;
                var row = Instantiate(shopRowPrefab, shopParent);
                var ui = row.GetComponent<BankRowUI>() ?? row.AddComponent<BankRowUI>();
                int unit = entry.priceOverride > 0 ? entry.priceOverride : entry.item.baseValue;
                ui.Setup(entry.item, 1, $"Buy ({unit}g)", () =>
                {
                    if (bankSystem.Buy(entry, 1)) { SetStatus($"Bought {entry.item.displayName}"); UpdateStatus(); }
                    else SetStatus("Not enough gold.");
                });
            }
        }

        void BuildSell()
        {
            if (sellParent == null || shopRowPrefab == null || bankSystem == null) return;
            ClearChildren(sellParent);
            var inv = bankSystem.playerInventory;
            if (inv == null) return;
            for (int i = 0; i < inv.mainGrid.Length; i++)
            {
                var slot = inv.mainGrid[i];
                if (slot.IsEmpty) continue;
                var item = slot.item;
                int qty = slot.quantity;
                var row = Instantiate(shopRowPrefab, sellParent);
                var ui = row.GetComponent<BankRowUI>() ?? row.AddComponent<BankRowUI>();
                ui.Setup(item, qty, $"Sell ({item.SellPrice}g)", () =>
                {
                    if (bankSystem.Sell(item, 1)) { SetStatus($"Sold {item.displayName}"); UpdateStatus(); BuildSell(); }
                });
            }
        }

        void BuildTokens()
        {
            if (tokenParent == null || shopRowPrefab == null || bankSystem == null) return;
            ClearChildren(tokenParent);
            foreach (var entry in bankSystem.tokenShopItems)
            {
                if (entry?.item == null) continue;
                var row = Instantiate(shopRowPrefab, tokenParent);
                var ui = row.GetComponent<BankRowUI>() ?? row.AddComponent<BankRowUI>();
                int cost = entry.priceOverride > 0 ? entry.priceOverride : 1;
                ui.Setup(entry.item, 1, $"Redeem ({cost}t)", () =>
                {
                    if (bankSystem.RedeemToken(entry, 1)) { SetStatus($"Redeemed {entry.item.displayName}"); UpdateStatus(); }
                    else SetStatus("Not enough tokens.");
                });
            }
        }

        void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }

        void UpdateStatus()
        {
            var inv = bankSystem?.playerInventory;
            if (inv == null) return;
            if (goldText != null) goldText.text = $"Gold: {inv.gold:N0}";
            if (tokenText != null) tokenText.text = $"Tokens: {inv.dungeonClearTokens}";
        }

        void SetStatus(string s) { if (statusText != null) statusText.text = s; }
    }

    /// <summary>Single row in a bank tab — icon, name, qty/price, action button.</summary>
    public class BankRowUI : MonoBehaviour
    {
        public Image icon;
        public TMP_Text nameText;
        public TMP_Text qtyText;
        public Button actionButton;
        public TMP_Text actionText;

        public void Setup(ItemData item, int qty, string actionLabel, System.Action onClick)
        {
            if (icon == null || nameText == null || actionButton == null)
            {
                // Auto-find
                foreach (var c in GetComponentsInChildren<Image>(true))
                    if (c.gameObject != gameObject && icon == null) icon = c;
                var texts = GetComponentsInChildren<TMP_Text>(true);
                if (texts.Length > 0) nameText = texts[0];
                if (texts.Length > 1) qtyText = texts[1];
                actionButton = GetComponentInChildren<Button>(true);
                actionText = actionButton != null ? actionButton.GetComponentInChildren<TMP_Text>(true) : null;
            }

            if (icon != null)      { icon.sprite = item.icon; icon.enabled = item.icon != null; }
            if (nameText != null)  { nameText.text = item.displayName; nameText.color = ItemData.RarityColor(item.rarity); }
            if (qtyText != null)   qtyText.text = qty > 1 ? $"x{qty}" : "";
            if (actionText != null) actionText.text = actionLabel;
            if (actionButton != null)
            {
                actionButton.onClick.RemoveAllListeners();
                actionButton.onClick.AddListener(() => onClick?.Invoke());
            }
        }
    }

    /// <summary>
    /// Builds tabs, panels, status row, close button, and a row-prefab for
    /// the BankUI at runtime. Only used when the scene was saved without
    /// these references wired up (older DBSceneBuilder.BuildBankUI stub).
    /// </summary>
    static class BankUIAutoBuilder
    {
        public static void Build(BankUI ui)
        {
            var root = ui.rootPanel.transform;

            // Title
            MakeText(root, "Title", "BANK & SHOP", 24, TextAlignmentOptions.Center, bold: true,
                anchor: AnchorPreset.TopStretch, offset: new Vector2(0, -10), size: new Vector2(0, 40));

            // Tab bar
            var tabBar = MakePanel(root, "TabBar",
                anchor: AnchorPreset.TopStretch, offset: new Vector2(0, -56), size: new Vector2(-40, 38));
            SetImage(tabBar, new Color(0, 0, 0, 0.0f));
            ui.shopTab  = MakeTabButton(tabBar.transform, "ShopTab",  "Shop",   0);
            ui.sellTab  = MakeTabButton(tabBar.transform, "SellTab",  "Sell",   1);
            ui.vaultTab = MakeTabButton(tabBar.transform, "VaultTab", "Vault",  2);
            ui.tokenTab = MakeTabButton(tabBar.transform, "TokenTab", "Tokens", 3);

            // Status row (top right): gold + tokens, plus center status text
            ui.goldText = MakeText(root, "Gold",  "Gold: 0",  16, TextAlignmentOptions.Right,
                anchor: AnchorPreset.TopRight, offset: new Vector2(-20, -16), size: new Vector2(220, 24));
            ui.goldText.color = new Color(1f, 0.85f, 0.4f);
            ui.tokenText = MakeText(root, "Tokens", "Tokens: 0", 14, TextAlignmentOptions.Right,
                anchor: AnchorPreset.TopRight, offset: new Vector2(-20, -38), size: new Vector2(220, 20));
            ui.tokenText.color = new Color(0.6f, 0.85f, 1f);
            ui.statusText = MakeText(root, "Status", "", 14, TextAlignmentOptions.Center,
                anchor: AnchorPreset.BottomStretch, offset: new Vector2(0, 12), size: new Vector2(-180, 24));
            ui.statusText.color = new Color(0.85f, 0.85f, 0.9f);

            // Close button
            var closeBtn = MakeButton(root, "CloseButton", "Close",
                anchor: AnchorPreset.BottomRight, offset: new Vector2(-20, 12), size: new Vector2(140, 36),
                bgColor: new Color(0.4f, 0.18f, 0.18f, 1f));
            closeBtn.GetComponent<Button>().onClick.AddListener(ui.Hide);

            // Content area (each tab gets a ScrollView whose Content is the parent)
            ui.shopPanel  = MakeScrollPanel(root, "ShopPanel",  out ui.shopParent);
            ui.sellPanel  = MakeScrollPanel(root, "SellPanel",  out ui.sellParent);
            ui.vaultPanel = MakeScrollPanel(root, "VaultPanel", out ui.vaultParent);
            ui.tokenPanel = MakeScrollPanel(root, "TokenPanel", out ui.tokenParent);

            // Row prefab — built once, referenced by all tabs. We don't save it
            // as an asset (we're at runtime) — Instantiate clones it directly.
            // Parent it under an inactive stash so the template doesn't render,
            // while keeping its own activeSelf=true so cloned instances are active.
            var stash = new GameObject("BankUI_Stash", typeof(RectTransform));
            stash.transform.SetParent(ui.transform, false);
            stash.SetActive(false);
            ui.shopRowPrefab = BuildRowPrefab(stash.transform);
        }

        // ───── Builders ─────
        static GameObject MakeScrollPanel(Transform parent, string name, out Transform contentParent)
        {
            var panel = MakePanel(parent, name,
                anchor: AnchorPreset.Stretch,
                offset: new Vector2(20, 56),
                size: new Vector2(-40, -110));
            SetImage(panel, new Color(0.07f, 0.07f, 0.1f, 1f));

            var scroll = panel.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = MakePanel(panel.transform, "Viewport",
                anchor: AnchorPreset.Stretch, offset: Vector2.zero, size: Vector2.zero);
            SetImage(viewport, new Color(0, 0, 0, 0.0f));
            var mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scroll.viewport = viewport.GetComponent<RectTransform>();

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1); contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 0);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 4;
            vlg.childControlHeight = false; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRT;

            contentParent = content.transform;
            panel.SetActive(false);
            return panel;
        }

        static Button MakeTabButton(Transform parent, string name, string label, int index)
        {
            const float TabWidth = 120;
            const float TabSpacing = 4;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(index * (TabWidth + TabSpacing), 0);
            rt.sizeDelta = new Vector2(TabWidth, 0);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.26f, 0.4f, 1f);
            var btn = go.AddComponent<Button>();
            var cb = btn.colors; cb.highlightedColor = new Color(0.3f, 0.5f, 0.9f); btn.colors = cb;
            MakeText(go.transform, "Label", label, 14, TextAlignmentOptions.Center, bold: true,
                anchor: AnchorPreset.Stretch, offset: Vector2.zero, size: Vector2.zero);
            return btn;
        }

        static GameObject BuildRowPrefab(Transform stash)
        {
            var go = new GameObject("BankRow_Template", typeof(RectTransform));
            go.transform.SetParent(stash, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 48);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.14f, 0.9f);

            var icon = new GameObject("Icon", typeof(RectTransform));
            icon.transform.SetParent(go.transform, false);
            var iconImg = icon.AddComponent<Image>();
            iconImg.raycastTarget = false; iconImg.enabled = false;
            var iconRT = icon.GetComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0, 0.5f); iconRT.anchorMax = new Vector2(0, 0.5f);
            iconRT.pivot = new Vector2(0, 0.5f);
            iconRT.anchoredPosition = new Vector2(8, 0);
            iconRT.sizeDelta = new Vector2(36, 36);

            var nameText = MakeText(go.transform, "Name", "Item", 15, TextAlignmentOptions.MidlineLeft, bold: true,
                anchor: AnchorPreset.Custom, offset: new Vector2(54, 0), size: new Vector2(220, 30));
            var nameRT = nameText.GetComponent<RectTransform>();
            nameRT.anchorMin = new Vector2(0, 0.5f); nameRT.anchorMax = new Vector2(0, 0.5f);
            nameRT.pivot = new Vector2(0, 0.5f);

            var qtyText = MakeText(go.transform, "Qty", "", 13, TextAlignmentOptions.MidlineRight,
                anchor: AnchorPreset.Custom, offset: new Vector2(-150, 0), size: new Vector2(80, 30));
            var qtyRT = qtyText.GetComponent<RectTransform>();
            qtyRT.anchorMin = new Vector2(1, 0.5f); qtyRT.anchorMax = new Vector2(1, 0.5f);
            qtyRT.pivot = new Vector2(1, 0.5f);

            var actionBtn = MakeButton(go.transform, "Action", "Buy",
                anchor: AnchorPreset.Custom, offset: new Vector2(-8, 0), size: new Vector2(120, 32),
                bgColor: new Color(0.2f, 0.5f, 0.3f, 1f));
            var actionRT = actionBtn.GetComponent<RectTransform>();
            actionRT.anchorMin = new Vector2(1, 0.5f); actionRT.anchorMax = new Vector2(1, 0.5f);
            actionRT.pivot = new Vector2(1, 0.5f);

            // Wire row script — finds children by name when Setup() runs.
            var rowScript = go.AddComponent<BankRowUI>();
            rowScript.icon = iconImg;
            rowScript.nameText = nameText;
            rowScript.qtyText = qtyText;
            rowScript.actionButton = actionBtn.GetComponent<Button>();
            rowScript.actionText = actionBtn.GetComponentInChildren<TMP_Text>();
            return go;
        }

        // ───── Layout primitives ─────
        enum AnchorPreset { TopStretch, BottomStretch, TopRight, BottomRight, Stretch, Custom }

        static GameObject MakePanel(Transform parent, string name, AnchorPreset anchor, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.4f);
            ApplyAnchor(go.GetComponent<RectTransform>(), anchor, offset, size);
            return go;
        }

        static GameObject MakeButton(Transform parent, string name, string label,
                                     AnchorPreset anchor, Vector2 offset, Vector2 size, Color bgColor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            go.AddComponent<Button>();
            ApplyAnchor(go.GetComponent<RectTransform>(), anchor, offset, size);
            MakeText(go.transform, "Label", label, 13, TextAlignmentOptions.Center,
                anchor: AnchorPreset.Stretch, offset: Vector2.zero, size: Vector2.zero);
            return go;
        }

        static TMP_Text MakeText(Transform parent, string name, string text, int fontSize,
                                 TextAlignmentOptions align, AnchorPreset anchor, Vector2 offset, Vector2 size,
                                 bool bold = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = fontSize; t.alignment = align;
            t.color = Color.white;
            if (bold) t.fontStyle = FontStyles.Bold;
            t.raycastTarget = false;
            ApplyAnchor(go.GetComponent<RectTransform>(), anchor, offset, size);
            return t;
        }

        static void ApplyAnchor(RectTransform rt, AnchorPreset anchor, Vector2 offset, Vector2 size)
        {
            switch (anchor)
            {
                case AnchorPreset.TopStretch:
                    rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(0.5f, 1); break;
                case AnchorPreset.BottomStretch:
                    rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
                    rt.pivot = new Vector2(0.5f, 0); break;
                case AnchorPreset.TopRight:
                    rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(1, 1); break;
                case AnchorPreset.BottomRight:
                    rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 0);
                    rt.pivot = new Vector2(1, 0); break;
                case AnchorPreset.Stretch:
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                    rt.pivot = new Vector2(0.5f, 0.5f); break;
            }
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
        }

        static void SetImage(GameObject go, Color c)
        {
            var img = go.GetComponent<Image>();
            if (img != null) img.color = c;
        }
    }

    /// <summary>Attach to Bank NPC. Press F within range to open bank.</summary>
    public class BankNPC : MonoBehaviour
    {
        public BankUI bankUI;
        public BankSystem bankSystem;
        public float interactRange = 5f;
        public GameObject promptUI;   // "Press E to interact"
        public KeyCode interactKey = KeyCode.E;

        bool warnedMissingUI;
        bool warnedNoPlayer;

        void Awake()
        {
            // The Lobby scene historically only set BankNPC's position override,
            // never wired bankUI / bankSystem — so pressing E did nothing because
            // bankUI was null. Auto-resolve from the scene as a safety net.
            if (bankUI == null) bankUI = FindObjectOfType<BankUI>(includeInactive: true);
            if (bankSystem == null) bankSystem = FindObjectOfType<BankSystem>(includeInactive: true);
            Debug.Log($"[BankNPC] Awake — bankUI={(bankUI ? bankUI.name : "<null>")}, bankSystem={(bankSystem ? "found" : "<null>")}, interactRange={interactRange}m");

            // Old prefabs only had an empty "Prompt" GameObject and no permanent
            // "Sign" — without these the NPC is just an unmarked yellow capsule.
            // Build them at runtime if missing so the scene works without rebuild.
            if (transform.Find("Sign") == null)
                CreateBillboardLabel("Sign", "BANK", new Vector3(0, 2.9f, 0), 0.18f, new Color(1f, 0.9f, 0.4f), startActive: true);

            if (promptUI == null)
            {
                var existing = transform.Find("Prompt");
                if (existing != null && existing.GetComponent<TextMesh>() == null)
                {
                    // Empty placeholder Prompt from the old prefab — replace with a real label.
                    var tm = existing.gameObject.AddComponent<TextMesh>();
                    tm.text = "Press E to interact";
                    tm.fontSize = 60; tm.characterSize = 0.12f;
                    tm.alignment = TextAlignment.Center; tm.anchor = TextAnchor.MiddleCenter;
                    tm.color = new Color(0.85f, 0.95f, 1f);
                    if (existing.GetComponent<DungeonBlade.Runtime.Billboard>() == null)
                        existing.gameObject.AddComponent<DungeonBlade.Runtime.Billboard>();
                    existing.gameObject.SetActive(false);
                    promptUI = existing.gameObject;
                }
                else if (existing == null)
                {
                    promptUI = CreateBillboardLabel("Prompt", "Press E to interact",
                        new Vector3(0, 2.3f, 0), 0.12f, new Color(0.85f, 0.95f, 1f), startActive: false);
                }
                else
                {
                    promptUI = existing.gameObject;
                }
            }
        }

        GameObject CreateBillboardLabel(string name, string text, Vector3 localPos, float charSize, Color color, bool startActive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 60;
            tm.characterSize = charSize;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = color;
            go.AddComponent<DungeonBlade.Runtime.Billboard>();
            go.SetActive(startActive);
            return go;
        }

        void Update()
        {
            // Last-chance ref recovery — BankUI may have been instantiated
            // after this NPC's Awake (additive scene loads, lazy bootstrap, etc.)
            if (bankUI == null) bankUI = FindObjectOfType<BankUI>(includeInactive: true);
            if (bankSystem == null) bankSystem = FindObjectOfType<BankSystem>(includeInactive: true);

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                if (!warnedNoPlayer)
                {
                    Debug.LogWarning("[BankNPC] No GameObject tagged 'Player' in scene — interaction won't trigger.");
                    warnedNoPlayer = true;
                }
                return;
            }
            float d = Vector3.Distance(transform.position, player.transform.position);
            bool inRange = d <= interactRange;
            if (promptUI != null) promptUI.SetActive(inRange);

            if (inRange && Input.GetKeyDown(interactKey))
            {
                Debug.Log($"[BankNPC] E pressed @ {d:F1}m. bankUI={(bankUI ? "ok" : "null")}, bankSystem={(bankSystem ? "ok" : "null")}");
                if (bankUI != null)
                {
                    bankUI.Show(bankSystem);
                }
                else if (!warnedMissingUI)
                {
                    Debug.LogError($"[BankNPC] interactKey pressed in range, but bankUI reference is null! Scene builder didn't wire it. Run DungeonBlade → Build Everything.");
                    warnedMissingUI = true;
                }
            }
        }
    }
}
