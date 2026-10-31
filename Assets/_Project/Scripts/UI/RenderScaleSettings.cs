using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BetterTogether.UI
{
    /// <summary>
    /// Local option "Resolución: 100 / 85 / 70 %" (PlayerPrefs <see cref="PrefsKey"/>): scales the 3D image, never the HUD, which
    /// is an overlay canvas. Applied at startup and on every change. Local only: nothing here touches the network.
    /// </summary>
    public static class RenderScaleSettings
    {
        public const string PrefsKey = "renderScale";

        private static readonly float[] Steps = { 1f, 0.85f, 0.7f };

        /// <summary>The saved scale, always one of the steps.</summary>
        public static float Value
        {
            get
            {
                var saved = PlayerPrefs.GetFloat(PrefsKey, Steps[0]);
                return Steps[IndexOf(saved)];
            }
        }

        /// <summary>Moves to the next step, wrapping around, then saves and applies it.</summary>
        public static void Next()
        {
            var next = Steps[(IndexOf(Value) + 1) % Steps.Length];
            PlayerPrefs.SetFloat(PrefsKey, next);
            Apply(next);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyAtStartup()
        {
            Apply(Value);

#if UNITY_EDITOR
            // The setter changes the pipeline asset itself: leaving Play Mode must not leave the project at a player's choice.
            Application.quitting -= RestoreAsset;
            Application.quitting += RestoreAsset;
#endif
        }

        private static void Apply(float scale)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)
                asset.renderScale = scale;
        }

#if UNITY_EDITOR
        private static void RestoreAsset() => Apply(Steps[0]);
#endif

        // Closest step, so a value saved by an older version still lands on a valid one.
        private static int IndexOf(float scale)
        {
            var best = 0;
            for (var i = 1; i < Steps.Length; i++)
            {
                if (Mathf.Abs(Steps[i] - scale) < Mathf.Abs(Steps[best] - scale))
                    best = i;
            }

            return best;
        }
    }
}
