using Fusion;
using UnityEngine;

namespace BetterTogether.Fireplace
{
    /// <summary>
    /// Marks a log so the campfire zone counts it, and gives the campfire its <see cref="NetworkObject"/>.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class FireplaceWood : MonoBehaviour
    {
        public NetworkObject NObject { get; private set; }

        private void Awake()
        {
            NObject = GetComponent<NetworkObject>();
        }
    }
}
