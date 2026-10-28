using UnityEngine;

namespace BetterTogether.Helpers
{
    /// <summary>
    /// Marker for objects that are meant to hang or hover (a lantern on a rope, a sign on a post). The editor floating
    /// audit (GnoMore/Auditar flotantes en escena) skips this object and its children. It has no runtime behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    public class IntentionallyFloating : MonoBehaviour
    {
    }
}
