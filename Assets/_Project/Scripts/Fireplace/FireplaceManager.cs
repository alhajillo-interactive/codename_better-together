using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

public class FireplaceManager : NetworkBehaviour
{
    private const int maxColliders = 20;
    [SerializeField] TextMeshProUGUI amountLabel;
    [SerializeField] int requiredAmount = 6;
    [Header("Logs detection")]
    [SerializeField] LayerMask logsLayer;
    [SerializeField] float detectorRadius = 1;

    [Networked, OnChangedRender(nameof(UpdateFireplaceCount))]
    int LogsInside { get; set; }

    readonly HashSet<FireplaceWood> logCounter = new();

    private Collider[] results = new Collider[maxColliders];

    public override void Spawned()
    {
        UpdateFireplaceCount();
    }
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        logCounter.Clear();
        int itemsFound = Physics.OverlapSphereNonAlloc(transform.position, detectorRadius, results, logsLayer);
        for (int i = 0; i < itemsFound; i++)
        {
            if (!results[i].TryGetComponent<FireplaceWood>(out var wood)) continue;
            logCounter.Add(wood);
        }
        LogsInside = logCounter.Count;
    }

    private void UpdateFireplaceCount()
    {
        amountLabel.text = $"{LogsInside}/{requiredAmount}";
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectorRadius);
    }
}
