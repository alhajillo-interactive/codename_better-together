#if FUSION_WEAVER
using Fusion;
using UnityEngine.SceneManagement;

namespace BetterTogether.Networking
{
    /// <summary>
    /// Identifies networked scenes by asset path instead of Build Profile index.
    /// <para>
    /// Do not replace this with <see cref="NetworkSceneManagerDefault"/> + <c>SceneRef.FromIndex</c>: in Shared mode the
    /// player who creates the session decides the scene, and an index means "the Nth scene in MY list". If anyone's
    /// list differs (a local scene added, an old pull, a removed entry) everyone else loads the wrong scene or fails
    /// with "Scene not found: N" and the match never starts. A path means the same scene on every machine.
    /// Scenes still have to be enabled in the Build Profile list.
    /// </para>
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
