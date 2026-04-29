using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonBlade.Characters
{
    /// <summary>
    /// Single portrait button in the character select row.
    /// Must live in its own .cs file (not inside CharacterSelectUI.cs) or
    /// Unity's prefab serialization can't resolve the script reference,
    /// leading to persistent "Missing Script" errors on CharacterPortraitButton.prefab.
    /// </summary>
    public class CharacterPortraitButton : MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler,
        UnityEngine.EventSystems.IPointerClickHandler
    {
        public Image portraitImage;
        public TMP_Text nameLabel;
        public Image frameImage;

        CharacterData data;
        System.Action<CharacterData> onHover;
        System.Action<CharacterData> onClick;

        public void Setup(CharacterData d, System.Action<CharacterData> hov, System.Action<CharacterData> clk)
        {
            data = d; onHover = hov; onClick = clk;

            // Auto-resolve refs if builder didn't set them explicitly
            if (portraitImage == null) portraitImage = GetComponent<Image>();
            if (nameLabel == null) nameLabel = GetComponentInChildren<TMP_Text>();

            if (portraitImage != null)
            {
                if (d.portrait != null)
                {
                    portraitImage.sprite = CropTopFraction(d.portrait, d.portraitCropTopFraction);
                    portraitImage.color = Color.white;
                    portraitImage.preserveAspect = true;
                    // If a previous fallback added an initial-letter child, hide it.
                    var leftover = portraitImage.transform.Find("Initial");
                    if (leftover != null) leftover.gameObject.SetActive(false);
                }
                else
                {
                    // Fallback: use a unique color derived from the character ID
                    // so each portrait is visually distinct even without art.
                    portraitImage.color = ColorFromId(d.characterId ?? d.displayName ?? "default");
                    EnsureInitialOverlay(portraitImage.transform, d.displayName);
                }
            }
            if (nameLabel != null) nameLabel.text = d.displayName;
        }

        // Cache so we don't re-allocate a sub-sprite every time the user
        // hovers a portrait button.
        static readonly Dictionary<(Sprite source, int pct), Sprite> CropCache = new Dictionary<(Sprite, int), Sprite>();

        /// <summary>
        /// Returns a sprite that renders only the top `topFraction` of the
        /// source image, leaving the lower body cropped out. Useful for
        /// turning full-body character art into half-body portraits without
        /// requiring the user to pre-crop their images.
        /// </summary>
        public static Sprite CropTopFraction(Sprite source, float topFraction)
        {
            if (source == null) return null;
            if (topFraction >= 0.999f) return source;
            topFraction = Mathf.Clamp(topFraction, 0.1f, 1f);

            int pct = Mathf.RoundToInt(topFraction * 100f);
            var key = (source, pct);
            if (CropCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = source.texture;
            if (tex == null) return source;

            // Sprite.Create works against the GPU texture — no Read/Write Enabled
            // requirement, since we only specify a UV sub-rect.
            int newH = Mathf.Max(1, Mathf.RoundToInt(source.rect.height * topFraction));
            var rect = new Rect(
                source.rect.x,
                source.rect.y + source.rect.height - newH,   // top portion
                source.rect.width,
                newH);
            var sub = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
            sub.name = source.name + "_Top" + pct;
            CropCache[key] = sub;
            return sub;
        }

        /// <summary>
        /// Adds (or updates) a big single-letter overlay so each fallback
        /// portrait is identifiable by name initial, not just color hue.
        /// </summary>
        public static void EnsureInitialOverlay(Transform parent, string displayName)
        {
            const string OverlayName = "Initial";
            var existing = parent.Find(OverlayName);
            TMP_Text label;
            if (existing == null)
            {
                var go = new GameObject(OverlayName, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.sizeDelta = new Vector2(-12, -36); rt.anchoredPosition = new Vector2(0, 8);
                label = go.AddComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = 64;
                label.fontStyle = FontStyles.Bold;
                label.raycastTarget = false;
            }
            else
            {
                label = existing.GetComponent<TMP_Text>();
            }
            if (label != null && !string.IsNullOrEmpty(displayName))
            {
                label.text = char.ToUpperInvariant(displayName[0]).ToString();
                label.color = new Color(1f, 1f, 1f, 0.85f);
            }
        }

        /// <summary>Deterministic color from any string — spreads through hue space.</summary>
        static Color ColorFromId(string id)
        {
            // Hash to a stable hue 0..1
            int h = 0;
            foreach (var c in id) h = (h * 31 + c) & 0x7FFFFFFF;
            float hue = (h % 1000) / 1000f;
            // Higher saturation + value than before so colored fallback portraits
            // read clearly against the dark detail/row backdrops.
            return Color.HSVToRGB(hue, 0.7f, 0.92f);
        }

        /// <summary>Public access so CharacterSelectUI can use the same color scheme for detail panel.</summary>
        public static Color ColorFromIdPublic(string id) => ColorFromId(id);

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData _) => onHover?.Invoke(data);
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData _) => onClick?.Invoke(data);
    }
}
