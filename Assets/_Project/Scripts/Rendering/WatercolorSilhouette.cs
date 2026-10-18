using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterTogether.Rendering
{
    /// <summary>
    /// Renders a stencil mask and an expanded backface copy of each visible mesh.
    /// The mask removes ink from overlaps between pieces of the same character.
    /// </summary>
    public sealed class WatercolorSilhouette : MonoBehaviour
    {
        [SerializeField] private Transform _modelRoot;
        [SerializeField] private Material _outlineMaterial;
        [SerializeField] private Material _maskMaterial;

        private readonly List<OutlinePair> _pairs = new();

        private sealed class OutlinePair
        {
            public Renderer Source;
            public Renderer Mask;
            public Renderer Outline;
            public SkinnedMeshRenderer SourceSkinned;
            public SkinnedMeshRenderer MaskSkinned;
            public SkinnedMeshRenderer OutlineSkinned;
        }

        private void Awake()
        {
            if (_modelRoot == null || _outlineMaterial == null || _maskMaterial == null)
            {
                Debug.LogWarning("[WatercolorSilhouette] Assign the model root and both materials.", this);
                return;
            }

            // Snapshot before adding children so the generated renderers are never copied again.
            Renderer[] renderers = _modelRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer source in renderers)
            {
                Material[] materials = source.sharedMaterials;
                Material replacement = null;
                bool hasOutline = false;
                foreach (Material material in materials)
                {
                    if (material == _outlineMaterial)
                        hasOutline = true;
                    else if (material != null && replacement == null)
                        replacement = material;
                }

                // Old duplicated hulls used ink as their only material.
                if (replacement == null)
                {
                    if (hasOutline)
                        source.enabled = false;
                    continue;
                }

                // Some imported submeshes also had ink in a regular material slot.
                if (hasOutline)
                {
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] == _outlineMaterial)
                            materials[i] = replacement;
                    source.sharedMaterials = materials;
                }

                SkinnedMeshRenderer skinned = source as SkinnedMeshRenderer;
                Mesh mesh = skinned != null
                    ? skinned.sharedMesh
                    : source.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                    continue;

                OutlinePair pair = new() { Source = source, SourceSkinned = skinned };
                pair.Mask = MakeCopy(source, mesh, _maskMaterial, "Watercolor Mask");
                pair.Outline = MakeCopy(source, mesh, _outlineMaterial, "Watercolor Outline");
                pair.MaskSkinned = pair.Mask as SkinnedMeshRenderer;
                pair.OutlineSkinned = pair.Outline as SkinnedMeshRenderer;
                _pairs.Add(pair);
            }

            SyncCopies();
        }

        private static Renderer MakeCopy(Renderer source, Mesh mesh, Material material, string name)
        {
            GameObject copy = new(name);
            copy.layer = source.gameObject.layer;
            copy.transform.SetParent(source.transform, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;

            Renderer renderer;
            if (source is SkinnedMeshRenderer skinnedSource)
            {
                SkinnedMeshRenderer skinnedCopy = copy.AddComponent<SkinnedMeshRenderer>();
                skinnedCopy.sharedMesh = mesh;
                skinnedCopy.bones = skinnedSource.bones;
                skinnedCopy.rootBone = skinnedSource.rootBone;
                skinnedCopy.quality = skinnedSource.quality;
                skinnedCopy.localBounds = skinnedSource.localBounds;
                skinnedCopy.updateWhenOffscreen = skinnedSource.updateWhenOffscreen;
                renderer = skinnedCopy;
            }
            else
            {
                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = copy.AddComponent<MeshRenderer>();
            }

            Material[] materials = new Material[mesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;
            return renderer;
        }

        private void LateUpdate() => SyncCopies();

        private void SyncCopies()
        {
            foreach (OutlinePair pair in _pairs)
            {
                bool visible = pair.Source.enabled &&
                    pair.Source.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
                pair.Mask.enabled = visible;
                pair.Outline.enabled = visible;

                if (pair.SourceSkinned == null)
                    continue;

                int shapeCount = pair.SourceSkinned.sharedMesh.blendShapeCount;
                for (int i = 0; i < shapeCount; i++)
                {
                    float weight = pair.SourceSkinned.GetBlendShapeWeight(i);
                    pair.MaskSkinned.SetBlendShapeWeight(i, weight);
                    pair.OutlineSkinned.SetBlendShapeWeight(i, weight);
                }
            }
        }
    }
}
