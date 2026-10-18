using Fusion;
using TMPro;
using UnityEngine;

public class FireplaceManager : NetworkBehaviour
{
    [SerializeField] TextMeshProUGUI amountLabel;
    [SerializeField] int requiredAmount = 6;
    private const int MAX_TRACKED = 32;

    // The replicated list — all clients see the same set of object IDs
    [Networked, Capacity(MAX_TRACKED)]
    private NetworkLinkedList<NetworkObject> Tracked { get; }

    private ChangeDetector _detector;

    public override void Spawned()
    {
        _detector = GetChangeDetector(ChangeDetector.Source.SimulationState);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!HasStateAuthority) return;

        // Only track objects that have a NetworkObject
        if (!other.TryGetComponent<FireplaceWood>(out var wood)) return;

        // If not already in the list, add it (this is the "enter" detection)
        if (!Tracked.Contains(wood.NObject))
        {
            Tracked.Add(wood.NObject);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!HasStateAuthority) return;

        if (!other.TryGetComponent<FireplaceWood>(out var wood)) return;

        Tracked.Remove(wood.NObject);
    }

    // ─── RENDERING (all clients) ─────────────────────────────────────

    public override void Render()
    {
        foreach (var change in _detector.DetectChanges(this))
        {
            if (change == nameof(Tracked))
            {
                UpdateFireplaceCount();
            }
        }
    }

    private void UpdateFireplaceCount()
    {

        amountLabel.text = $"{Tracked.Count}/{requiredAmount}";
    }
}
