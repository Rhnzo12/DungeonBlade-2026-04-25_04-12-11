using UnityEngine;

namespace DungeonBlade.Characters
{
    /// <summary>
    /// A selectable player character. Drop the character's visual prefab/model
    /// into `modelPrefab` — at runtime it gets instantiated as a child of the
    /// Player's Visual transform, replacing the placeholder capsule.
    ///
    /// Create via: Create → DungeonBlade → Character.
    /// </summary>
    [CreateAssetMenu(menuName = "DungeonBlade/Character", fileName = "Character_")]
    public class CharacterData : ScriptableObject
    {
        [Header("Identity")]
        public string characterId = "char_default";
        public string displayName = "Warrior";
        [TextArea(2, 4)]
        public string flavorText = "A seasoned blade from the southern reaches.";

        [Header("Visual")]
        [Tooltip("The character model prefab or FBX. Instantiated under Player/Visual at runtime.")]
        public GameObject modelPrefab;

        [Tooltip("Portrait for character select UI. Optional — falls back to a runtime camera snapshot.")]
        public Sprite portrait;

        [Tooltip("Fraction of the portrait image to render, measured from the top. 1 = whole image, 0.5 = top half. Use ~0.55 for full-body images so only the head/upper torso shows in the portrait UI.")]
        [Range(0.1f, 1f)]
        public float portraitCropTopFraction = 0.55f;

        [Tooltip("Local position offset for the model relative to Visual. Use to align with the CharacterController's capsule.")]
        public Vector3 modelOffset = Vector3.zero;

        [Tooltip("Local euler rotation for the model. Most Mixamo models face +Z by default — adjust if yours faces -Z.")]
        public Vector3 modelRotation = Vector3.zero;

        [Tooltip("Uniform scale applied to the model. Use if the import scale is off (common with Blender exports).")]
        public float modelScale = 1f;

        [Tooltip("If > 0, auto-rescale the model after instantiation so its visible mesh ends up this many world units tall. Useful when FBX files have inconsistent import scales. Set to 0 to disable. Typical adult human height: 1.8.")]
        public float targetHeight = 1.8f;

        [Header("Stat Modifiers (optional — leave at 1.0 for neutral)")]
        [Tooltip("Multiplier on PlayerStats.maxHealth at spawn. 1.1 = +10% HP.")]
        public float hpMultiplier = 1f;

        [Tooltip("Multiplier on PlayerStats.maxStamina at spawn.")]
        public float staminaMultiplier = 1f;

        [Tooltip("Multiplier on PlayerController.moveSpeed.")]
        public float moveSpeedMultiplier = 1f;

        [Tooltip("Multiplier on melee damage output.")]
        public float meleeDamageMultiplier = 1f;

        [Tooltip("Multiplier on ranged damage output.")]
        public float rangedDamageMultiplier = 1f;

        [Header("Weapon Attachments")]
        [Tooltip("Name of the bone or transform to use as sword pivot (e.g., 'mixamorig:RightHand'). Leave blank to reuse existing SwordPivot.")]
        public string swordAttachBoneName = "";

        [Tooltip("Name of the bone or transform to use as gun pivot. Leave blank to reuse existing GunPivot.")]
        public string gunAttachBoneName = "";

        [Tooltip("Local offset of the sword relative to the attach bone.")]
        public Vector3 swordLocalOffset = Vector3.zero;
        public Vector3 swordLocalRotation = Vector3.zero;

        [Tooltip("Local offset of the gun relative to the attach bone.")]
        public Vector3 gunLocalOffset = Vector3.zero;
        public Vector3 gunLocalRotation = Vector3.zero;
    }
}
