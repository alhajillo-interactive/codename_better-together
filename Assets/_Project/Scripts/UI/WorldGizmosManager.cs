using BetterTogether.Interaction;
using UnityEngine;

public class WorldGizmosManager : MonoBehaviour
{
    [SerializeField] WorldGizmos prefab;

    void Start()
    {
        var carriables = FindObjectsByType<Carryable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in carriables)
        {
            var gizmos = Instantiate(prefab, transform);
            gizmos.SetTarget(c.transform);
        }
    }
}
