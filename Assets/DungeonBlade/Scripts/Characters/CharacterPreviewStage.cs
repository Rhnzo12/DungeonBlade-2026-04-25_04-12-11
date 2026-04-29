using UnityEngine;

namespace DungeonBlade.Characters
{
    /// <summary>
    /// Off-screen 3D character preview rig used by the character-select UI.
    /// A dedicated Camera renders the spawned CharacterData.modelPrefab into
    /// a RenderTexture, which the UI displays via a RawImage. Optionally
    /// idle-rotates the model so the preview feels alive.
    ///
    /// Place anywhere in the scene that's well outside the player's normal
    /// view (e.g. world position (100, 0, 0)). The camera here is independent
    /// from the gameplay camera and only renders the preview layer.
    ///
    /// Wire CharacterSelectUI.previewStage to this component.
    /// </summary>
    public class CharacterPreviewStage : MonoBehaviour
    {
        [Header("Refs (auto-wired by builder)")]
        [Tooltip("Camera that renders the preview model into the RenderTexture. If left null on Awake, a child camera is searched.")]
        public Camera previewCamera;

        [Tooltip("Transform under which the character model is instantiated.")]
        public Transform spawnPoint;

        [Header("Render Texture")]
        public int textureWidth = 512;
        public int textureHeight = 768;

        [Header("Framing")]
        [Tooltip("Y offset added to spawnPoint when looking at the model — usually around chest/face height for a standard humanoid.")]
        public float lookAtHeight = 1.0f;

        [Tooltip("Default uniform scale applied to the spawned model. Use to match the same target height as the gameplay character.")]
        public float modelTargetHeight = 1.8f;

        [Header("Idle Spin")]
        public bool idleRotate = true;
        public float idleRotateSpeed = 12f;        // degrees / second

        public RenderTexture Texture { get; private set; }

        GameObject currentModel;
        CharacterData currentData;

        void Awake()
        {
            if (previewCamera == null) previewCamera = GetComponentInChildren<Camera>();
            if (spawnPoint == null)
            {
                var go = new GameObject("SpawnPoint");
                go.transform.SetParent(transform, false);
                spawnPoint = go.transform;
            }

            Texture = new RenderTexture(textureWidth, textureHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "CharacterPreview_RT",
                antiAliasing = 2,
                useMipMap = false,
            };
            Texture.Create();

            if (previewCamera != null)
            {
                previewCamera.targetTexture = Texture;
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = new Color(0.05f, 0.06f, 0.10f, 1f);
                previewCamera.cullingMask = ~0;     // render everything; keeping it simple
                previewCamera.orthographic = false;
                previewCamera.fieldOfView = 35f;
            }
        }

        void OnDestroy()
        {
            if (Texture != null)
            {
                if (previewCamera != null) previewCamera.targetTexture = null;
                Texture.Release();
                Texture = null;
            }
        }

        void Update()
        {
            if (idleRotate && currentModel != null)
                currentModel.transform.Rotate(0f, idleRotateSpeed * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        /// <summary>Swap the previewed character.</summary>
        public void Show(CharacterData data)
        {
            if (data == currentData) return;
            currentData = data;

            if (currentModel != null)
            {
                Destroy(currentModel);
                currentModel = null;
            }

            if (data == null || data.modelPrefab == null || spawnPoint == null) return;

            currentModel = Instantiate(data.modelPrefab, spawnPoint);
            currentModel.name = "Preview_" + data.characterId;
            currentModel.transform.localPosition = data.modelOffset;
            currentModel.transform.localEulerAngles = data.modelRotation;
            currentModel.transform.localScale = Vector3.one * Mathf.Max(0.01f, data.modelScale);

            // Force-disable Animators so the model stays in T-pose / bind pose.
            // The preview is a static silhouette; animation isn't required and
            // would reset our framing each frame.
            foreach (var a in currentModel.GetComponentsInChildren<Animator>())
                a.enabled = false;

            StartCoroutine(FrameModelNextFrame(data));
        }

        System.Collections.IEnumerator FrameModelNextFrame(CharacterData data)
        {
            // One frame for skinned bounds to settle, then size + frame.
            yield return null;
            if (currentModel == null) yield break;

            // Rescale to target height using post-pose mesh bounds.
            if (modelTargetHeight > 0f && TryGetWorldHeight(currentModel, out float h) && h > 0.01f)
            {
                float ratio = modelTargetHeight / h;
                if (ratio < 0.95f || ratio > 1.05f)
                    currentModel.transform.localScale *= ratio;
            }

            // Frame: place camera ~3.2m back from look point, slight tilt down.
            if (previewCamera != null && spawnPoint != null)
            {
                Vector3 lookAt = spawnPoint.position + Vector3.up * lookAtHeight;
                previewCamera.transform.position = lookAt + new Vector3(0f, 0.15f, -3.2f);
                previewCamera.transform.LookAt(lookAt);
            }
        }

        static bool TryGetWorldHeight(GameObject root, out float height)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr == null) continue;
                var b = smr.bounds;
                if (b.min.y < minY) minY = b.min.y;
                if (b.max.y > maxY) maxY = b.max.y;
            }
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (mr == null) continue;
                var b = mr.bounds;
                if (b.min.y < minY) minY = b.min.y;
                if (b.max.y > maxY) maxY = b.max.y;
            }
            if (minY == float.MaxValue) { height = 0f; return false; }
            height = Mathf.Max(0f, maxY - minY);
            return true;
        }
    }
}
