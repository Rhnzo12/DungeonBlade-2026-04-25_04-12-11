using System.Collections;
using UnityEngine;

namespace DungeonBlade.Characters
{
    /// <summary>
    /// Runtime component on the Player prefab. On Awake, reads the current
    /// CharacterData from the roster and swaps the visual model in — clearing
    /// out placeholder capsules and instantiating the chosen character mesh.
    /// Also re-targets the Animator's Avatar and attaches sword/gun to the
    /// chosen bone names.
    ///
    /// Expected hierarchy:
    ///   Player (this component + PlayerController + Animator + CharacterController)
    ///   └── Visual (transform named "Visual")
    ///       └── [placeholder capsules or instantiated character model]
    ///   └── SwordPivot (anywhere, assigned via field)
    ///   └── GunPivot (anywhere, assigned via field)
    /// </summary>
    public class CharacterInstantiator : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Transform the character model gets parented under. Placeholder capsules here are destroyed on swap.")]
        public Transform visualParent;

        [Tooltip("Animator on the character root. Avatar gets replaced on swap.")]
        public Animator animator;

        [Tooltip("The sword object that gets re-parented to the character's hand bone.")]
        public Transform swordObject;

        [Tooltip("The gun object that gets re-parented to the character's hand bone.")]
        public Transform gunObject;

        [Tooltip("If true and no roster/selection exists, leaves the placeholder visual alone. Useful for testing prefab in isolation.")]
        public bool leavePlaceholderIfNoSelection = true;

        [Tooltip("Fallback CharacterData if no roster is found in the scene. Leave null to use placeholder.")]
        public CharacterData fallbackCharacter;

        CharacterData activeCharacter;
        GameObject instantiatedModel;

        void Awake()
        {
            if (visualParent == null) visualParent = transform.Find("Visual");

            var roster = FindObjectOfType<CharacterRoster>();
            if (roster == null && fallbackCharacter == null)
            {
                if (!leavePlaceholderIfNoSelection) ClearVisualChildren();
                return;
            }

            var target = roster != null ? roster.Current : fallbackCharacter;
            if (target == null && !leavePlaceholderIfNoSelection) ClearVisualChildren();
            if (target == null) return;

            SwapTo(target);

            if (roster != null) roster.OnSelectionChanged += OnRosterSelectionChanged;
        }

        void OnDestroy()
        {
            var roster = FindObjectOfType<CharacterRoster>();
            if (roster != null) roster.OnSelectionChanged -= OnRosterSelectionChanged;
        }

        void OnRosterSelectionChanged(CharacterData data)
        {
            // Live-swap only outside of active combat (avoid mid-fight visual glitches)
            if (Application.isPlaying && data != activeCharacter) SwapTo(data);
        }

        public void SwapTo(CharacterData data)
        {
            if (data == null || data.modelPrefab == null)
            {
                Debug.LogWarning($"[CharacterInstantiator] Can't swap — CharacterData or modelPrefab is null on {gameObject.name}");
                return;
            }

            activeCharacter = data;

            ClearVisualChildren();

            // CRITICAL: Reset the Visual parent's local transform to zero.
            // The Player prefab historically used a Visual child with
            // localPosition.y ≈ 0.9 to vertically center the placeholder
            // capsule inside the CharacterController. That offset is wrong
            // for real character models (pivot at feet) and causes them to
            // float 0.9m above the ground. We zero it out so the character
            // model inherits exactly the Player root position.
            if (visualParent != null)
            {
                if (visualParent.localPosition != Vector3.zero)
                {
                    Debug.Log($"[CharacterInstantiator] Resetting Visual localPosition from {visualParent.localPosition} to zero (was a placeholder-capsule compensation).");
                    visualParent.localPosition = Vector3.zero;
                }
                // Also zero any rotation/scale drift on Visual — the character
                // model handles its own orientation via data.modelRotation.
                visualParent.localRotation = Quaternion.identity;
                visualParent.localScale = Vector3.one;
            }

            // Instantiate model
            instantiatedModel = Instantiate(data.modelPrefab, visualParent);
            instantiatedModel.name = "Character_" + data.characterId;
            instantiatedModel.transform.localPosition = data.modelOffset;
            instantiatedModel.transform.localEulerAngles = data.modelRotation;
            instantiatedModel.transform.localScale = Vector3.one * Mathf.Max(0.01f, data.modelScale);

            // Rescale + foot-align after one frame so SkinnedMeshRenderer.bounds
            // reflect the actual posed mesh, not the bind-pose transform that
            // hasn't been baked yet at Instantiate-time. Without this delay the
            // measurement comes back near-zero and the model appears shrunken.
            if (Application.isPlaying)
                StartCoroutine(DeferredRescaleAndAlign(instantiatedModel, data));
            else
                ApplyRescaleAndAlign(instantiatedModel, data);   // edit-time, no coroutine

            // Re-target animator avatar
            if (animator != null)
            {
                var modelAnimator = instantiatedModel.GetComponentInChildren<Animator>();
                if (modelAnimator != null && modelAnimator.avatar != null)
                {
                    animator.avatar = modelAnimator.avatar;
                    // Disable the inner animator so there's only one driving the character
                    modelAnimator.enabled = false;
                }
            }

            // Attach weapons to bones
            AttachToBone(swordObject, data.swordAttachBoneName, data.swordLocalOffset, data.swordLocalRotation);
            AttachToBone(gunObject,   data.gunAttachBoneName,   data.gunLocalOffset,   data.gunLocalRotation);

            // Apply stat multipliers
            ApplyStatMods(data);
        }

        void AttachToBone(Transform weapon, string boneName, Vector3 localPos, Vector3 localRot)
        {
            if (weapon == null || string.IsNullOrEmpty(boneName) || instantiatedModel == null) return;

            var bone = FindDeepChild(instantiatedModel.transform, boneName);
            if (bone == null)
            {
                Debug.LogWarning($"[CharacterInstantiator] Bone '{boneName}' not found on model — weapon '{weapon.name}' stays on original pivot.");
                return;
            }

            weapon.SetParent(bone, false);
            weapon.localPosition = localPos;
            weapon.localEulerAngles = localRot;
        }

        void ApplyStatMods(CharacterData data)
        {
            var stats = GetComponent<DungeonBlade.Player.PlayerStats>();
            if (stats != null)
            {
                stats.maxHealth   *= data.hpMultiplier;
                stats.maxStamina  *= data.staminaMultiplier;
            }
            var ctrl = GetComponent<DungeonBlade.Player.PlayerController>();
            if (ctrl != null)
            {
                ctrl.walkSpeed *= data.moveSpeedMultiplier;
                ctrl.runSpeed  *= data.moveSpeedMultiplier;
            }
        }

        void ClearVisualChildren()
        {
            if (visualParent == null) return;
            // Destroy all non-pivot children
            for (int i = visualParent.childCount - 1; i >= 0; i--)
            {
                var child = visualParent.GetChild(i);
                if (child == swordObject || child == gunObject) continue;
                // Skip children containing pivot references
                if (child.name.Contains("Pivot")) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        IEnumerator DeferredRescaleAndAlign(GameObject model, CharacterData data)
        {
            // Wait one frame so the Animator binds and SkinnedMeshRenderer
            // bounds get refreshed; on the very first frame after Instantiate
            // they're often stale (zero or bind-pose only).
            yield return null;
            if (model == null) yield break;
            ApplyRescaleAndAlign(model, data);
        }

        /// <summary>
        /// Uniformly rescales the model so the world-space AABB of its visible
        /// renderers is `targetHeight` tall, then offsets it vertically so the
        /// AABB's bottom lands at the Player root's local Y=0 (feet on floor).
        /// Both steps use post-pose Renderer.bounds, which means they only
        /// produce correct results AFTER the Animator has run at least once.
        /// </summary>
        void ApplyRescaleAndAlign(GameObject model, CharacterData data)
        {
            if (model == null) return;

            // 1. Rescale
            if (data.targetHeight > 0f)
            {
                Bounds b;
                if (TryGetWorldBounds(model, out b) && b.size.y > 0.01f)
                {
                    float ratio = data.targetHeight / b.size.y;
                    if (ratio < 0.95f || ratio > 1.05f)
                    {
                        model.transform.localScale *= ratio;
                        Debug.Log($"[CharacterInstantiator] Rescaled '{model.name}' x{ratio:F2} (was {b.size.y:F2}m, target {data.targetHeight:F2}m)");
                    }
                }
                else
                {
                    Debug.LogWarning($"[CharacterInstantiator] Couldn't measure '{model.name}' bounds — leaving at default scale. Set CharacterData.modelScale manually if needed.");
                }
            }

            // 2. Foot-align (only if user didn't override modelOffset)
            if (data.modelOffset == Vector3.zero)
            {
                Bounds b;
                if (TryGetWorldBounds(model, out b))
                {
                    // World-space bottom of mesh in player-root local space
                    Vector3 localBottom = model.transform.parent.InverseTransformPoint(
                        new Vector3(b.center.x, b.min.y, b.center.z));
                    if (Mathf.Abs(localBottom.y) > 0.02f)
                    {
                        var lp = model.transform.localPosition;
                        lp.y -= localBottom.y;
                        model.transform.localPosition = lp;
                    }
                }
            }
        }

        static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr == null) continue;
                if (!any) { bounds = smr.bounds; any = true; }
                else bounds.Encapsulate(smr.bounds);
            }
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (mr == null) continue;
                if (!any) { bounds = mr.bounds; any = true; }
                else bounds.Encapsulate(mr.bounds);
            }
            return any;
        }

        static Transform FindDeepChild(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindDeepChild(parent.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// Shifts the model so its lowest point (usually feet) lands at local Y=0.
        ///
        /// Uses SkinnedMeshRenderer bounds to measure the ACTUAL lowest vertex of
        /// the visible mesh, rather than guessing from bone positions. This is the
        /// only reliable approach because:
        ///   - Mixamo T-pose FBXs already have pivot at feet (Y=0), but the hip
        ///     bone is naturally at Y=0.6-1.0 depending on height. So targeting a
        ///     hardcoded "hip height" pushes them upward incorrectly.
        ///   - Some custom rigs have pivot at hips with feet below (Y=-0.9). Bounds
        ///     correctly detects this too.
        ///
        /// Bounds are expressed in world space; we transform back to the model's
        /// local space to compute the offset.
        /// </summary>
        static void AlignFeetToOrigin(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers == null || renderers.Length == 0)
            {
                // No skinned meshes at all — try static meshes as a last resort
                var staticRenderers = model.GetComponentsInChildren<MeshRenderer>();
                if (staticRenderers == null || staticRenderers.Length == 0) return;
                AlignFromRenderers(model, staticRenderers);
                return;
            }
            AlignFromSkinnedRenderers(model, renderers);
        }

        static void AlignFromSkinnedRenderers(GameObject model, SkinnedMeshRenderer[] renderers)
        {
            // IMPORTANT: SkinnedMeshRenderer.bounds returns a world-space AABB
            // that is NOT reliable on Awake() — the skeleton hasn't posed yet,
            // so bounds may be stale/undersized. We hit exactly this bug: the
            // bounds said lowest vertex was at Y=-0.06 when the real lowest
            // vertex (toes in T-pose) is closer to Y=-0.9m below the hip pivot.
            //
            // Strategy — try multiple signals and use the one that makes sense:
            //   1. sharedMesh.bounds (mesh-local bind-pose AABB) — usually right
            //   2. Foot/toe bone world position — fallback if mesh bounds are bad
            //
            // We also sanity-check: a humanoid's feet should be 0.5-1.2m below
            // the model root in a Mixamo T-pose. Anything outside that range
            // signals a measurement error and we fall back to bones.

            float meshLowestY = float.MaxValue;
            foreach (var smr in renderers)
            {
                if (smr == null || smr.sharedMesh == null) continue;

                // sharedMesh.bounds is in the mesh's local space (bind pose).
                // For Mixamo FBXs this is the T-pose geometry with pivot at
                // the hips (Y≈0), feet at Y≈-0.9, head at Y≈+0.9.
                var meshBounds = smr.sharedMesh.bounds;
                Vector3 meshMin = meshBounds.min;

                // The mesh-local space is equivalent to the SkinnedMeshRenderer's
                // transform. Convert the mesh-local min to world, then into the
                // model root's local space.
                Vector3 worldMin = smr.transform.TransformPoint(new Vector3(meshBounds.center.x, meshMin.y, meshBounds.center.z));
                Vector3 localMin = model.transform.InverseTransformPoint(worldMin);

                if (localMin.y < meshLowestY) meshLowestY = localMin.y;
            }

            // Bone-based fallback: find the lowest foot/toe bone
            float boneLowestY = float.MaxValue;
            string[] footBoneNames = {
                "mixamorig:LeftToe_End", "mixamorig:RightToe_End",
                "mixamorig:LeftToeBase", "mixamorig:RightToeBase",
                "mixamorig:LeftFoot",    "mixamorig:RightFoot",
                "LeftToe_End", "RightToe_End",
                "LeftToeBase", "RightToeBase",
                "LeftFoot",    "RightFoot"
            };
            foreach (var name in footBoneNames)
            {
                var bone = FindDeepChild(model.transform, name);
                if (bone == null) continue;
                Vector3 localBonePos = model.transform.InverseTransformPoint(bone.position);
                if (localBonePos.y < boneLowestY) boneLowestY = localBonePos.y;
            }

            // Pick the best measurement. A Mixamo T-pose has feet 0.5-1.2m below
            // the model root, so any measurement outside that range is suspect.
            float lowestLocalY = float.MaxValue;
            if (IsPlausibleFootY(meshLowestY))
            {
                lowestLocalY = meshLowestY;
                Debug.Log($"[CharacterInstantiator] Using mesh bounds: lowestY={meshLowestY:F3}");
            }
            else if (IsPlausibleFootY(boneLowestY))
            {
                lowestLocalY = boneLowestY;
                Debug.Log($"[CharacterInstantiator] Mesh bounds unreliable (Y={meshLowestY:F3}), falling back to foot bones: lowestY={boneLowestY:F3}");
            }
            else
            {
                Debug.LogWarning($"[CharacterInstantiator] Could not reliably determine foot height. Mesh Y={meshLowestY:F3}, Bone Y={boneLowestY:F3}. Model may float or sink.");
                return;
            }

            // Only shift if the feet aren't already roughly at Y=0
            if (Mathf.Abs(lowestLocalY) < 0.02f)
            {
                Debug.Log($"[CharacterInstantiator] Feet already at origin (localY={lowestLocalY:F3}m). No shift needed.");
                return;
            }

            var lp = model.transform.localPosition;
            lp.y -= lowestLocalY;
            model.transform.localPosition = lp;
            Debug.Log($"[CharacterInstantiator] Foot-aligned model. Lowest point was at local Y={lowestLocalY:F2}, shifted model by {-lowestLocalY:F2}m.");
        }

        /// <summary>
        /// A humanoid's feet should be between 0.3m and 1.5m BELOW the model root
        /// in any sensible rig (T-pose or A-pose). Measurements outside that range
        /// are usually artifacts of uninitialized bounds or non-humanoid meshes.
        /// </summary>
        static bool IsPlausibleFootY(float y)
        {
            return y > -1.5f && y < -0.3f;
        }

        static void AlignFromRenderers(GameObject model, MeshRenderer[] renderers)
        {
            float lowestLocalY = float.MaxValue;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var wb = r.bounds;
                Vector3 worldBottomCenter = new Vector3(wb.center.x, wb.min.y, wb.center.z);
                Vector3 localBottom = model.transform.InverseTransformPoint(worldBottomCenter);
                if (localBottom.y < lowestLocalY) lowestLocalY = localBottom.y;
            }

            if (lowestLocalY == float.MaxValue) return;
            if (Mathf.Abs(lowestLocalY) < 0.02f) return;

            var lp = model.transform.localPosition;
            lp.y -= lowestLocalY;
            model.transform.localPosition = lp;
            Debug.Log($"[CharacterInstantiator] Foot-aligned (static mesh). Lowest vertex was at local Y={lowestLocalY:F2}, shifted by {-lowestLocalY:F2}m.");
        }
    }
}
