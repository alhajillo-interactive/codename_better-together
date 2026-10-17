#if FUSION_WEAVER
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
    }
}
#endif
