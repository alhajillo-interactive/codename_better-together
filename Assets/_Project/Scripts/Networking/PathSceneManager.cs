#if FUSION_WEAVER
using Eflatun.SceneReference;
using Fusion;
using UnityEngine.SceneManagement;

namespace BetterTogether.Networking
{
    /// <summary>
    /// The default scene manager identifies scenes by build index, which only works if every machine has the exact
    /// same Build Profile scene list. This one identifies them by asset path instead, so a reordered or extended list
    /// on one machine can't make other players load a different scene. Scenes still need to be in the list.
    /// </summary>
    public class PathSceneManager : NetworkSceneManagerDefault
    {
        public override SceneRef GetSceneRef(string sceneNameOrPath)
        {
            var buildIndex = FusionUnitySceneManagerUtils.GetSceneBuildIndex(sceneNameOrPath);
            return buildIndex >= 0
                ? SceneRef.FromPath(SceneUtility.GetScenePathByBuildIndex(buildIndex))
                : base.GetSceneRef(sceneNameOrPath);
        }

        /// <summary>
        /// Resolves a SceneReference to a path-based SceneRef. Use this for every networked scene load
        /// (never SceneRef.FromIndex) so all clients agree on the scene.
        /// </summary>
        public static bool TryGetSceneRef(SceneReference scene, out SceneRef sceneRef, out string error)
        {
            sceneRef = SceneRef.None;

            if (scene == null || !scene.TryGetPath(out var path))
            {
                error = "No scene assigned (or the scene asset was deleted).";
                return false;
            }

            if (SceneUtility.GetBuildIndexByScenePath(path) < 0)
            {
                error = $"'{path}' is not enabled in the Build Profile scene list.";
                return false;
            }

            sceneRef = SceneRef.FromPath(path);
            error = null;
            return true;
        }
    }
}
#endif
