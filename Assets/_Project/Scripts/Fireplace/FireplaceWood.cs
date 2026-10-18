using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class FireplaceWood : MonoBehaviour
{
    public NetworkObject NObject { get; private set; }
    void Awake()
    {
        NObject = GetComponent<NetworkObject>();
    }
}
