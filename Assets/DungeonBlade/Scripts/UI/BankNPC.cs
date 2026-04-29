using UnityEngine;
using DungeonBlade.Bank;

namespace DungeonBlade.UI
{
    /// <summary>
    /// Attach to the Bank NPC. Player walks within range, presses E, the
    /// referenced BankUI opens. Auto-finds BankUI / BankSystem in the scene
    /// if those references aren't pre-wired.
    ///
    /// Lives in its own .cs file (was originally inside BankUI.cs) because
    /// Unity's prefab YAML references a script via fileID 11500000 + the .cs
    /// file's GUID — that pair only resolves cleanly to the "primary" class
    /// in a file. With BankNPC alongside BankUI, prefabs ended up with no
    /// script attached at all.
    /// </summary>
    public class BankNPC : MonoBehaviour
    {
        public BankUI bankUI;
        public BankSystem bankSystem;
        public float interactRange = 5f;
        public GameObject promptUI;     // "Press E to interact"
        public KeyCode interactKey = KeyCode.E;

        bool warnedMissingUI;
        bool warnedNoPlayer;

        void Awake()
        {
            // Auto-resolve refs from the scene if the builder didn't wire them.
            if (bankUI == null) bankUI = FindObjectOfType<BankUI>(includeInactive: true);
            if (bankSystem == null) bankSystem = FindObjectOfType<BankSystem>(includeInactive: true);
            Debug.Log($"[BankNPC] Awake — bankUI={(bankUI ? bankUI.name : "<null>")}, bankSystem={(bankSystem ? "found" : "<null>")}, interactRange={interactRange}m");

            // Build a permanent "BANK" billboard sign + a "Press E" prompt at
            // runtime if the prefab only has the empty placeholder Prompt child.
            if (transform.Find("Sign") == null)
                CreateBillboardLabel("Sign", "BANK", new Vector3(0, 2.9f, 0), 0.18f, new Color(1f, 0.9f, 0.4f), startActive: true);

            if (promptUI == null)
            {
                var existing = transform.Find("Prompt");
                if (existing != null && existing.GetComponent<TextMesh>() == null)
                {
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
            // Last-chance ref recovery — BankUI may have been instantiated after
            // this NPC's Awake (additive scene loads, lazy bootstrap, etc.)
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
                    Debug.LogError("[BankNPC] interactKey pressed in range, but bankUI reference is null! No BankUI was found in the scene.");
                    warnedMissingUI = true;
                }
            }
        }
    }
}
