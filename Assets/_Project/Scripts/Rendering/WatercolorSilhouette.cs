using UnityEngine;

namespace BetterTogether.Rendering
{
    /// <summary>
    /// Deprecated (C2.7). The watercolor silhouette is now drawn by two URP RenderObjects features
    /// (stencil mask + ink) over the "Silhouette" layer, built by GnoMore/Construir siluetas por Renderer Feature.
    /// This stub never duplicates renderers; it only keeps old serialized references valid and switches itself off.
    /// The builder removes it from the Camper/Gnome prefabs; the script can be deleted once nothing references it.
    /// </summary>
    public sealed class WatercolorSilhouette : MonoBehaviour
    {
        [SerializeField, Tooltip("Unused. Kept so existing prefab data keeps loading.")] private Transform _modelRoot;
        [SerializeField, Tooltip("Unused. The ink material lives in the Watercolor Silhouette Ink renderer feature.")] private Material _outlineMaterial;
        [SerializeField, Tooltip("Unused. The mask material lives in the Watercolor Silhouette Mask renderer feature.")] private Material _maskMaterial;

        private void Awake() => enabled = false;
    }
}
