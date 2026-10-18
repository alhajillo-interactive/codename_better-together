using UnityEngine;

[CreateAssetMenu(menuName = "codename_better-together/WorldGizmosConfig")]
public class WorldGizmosConfig : ScriptableObject
{
    [Tooltip("At this distance (m) or farther the marker is at full size.")]
    [SerializeField] float maxDist = 10f;
    [Tooltip("Closer than this (m) the marker shrinks away, since the item is already in plain sight.")]
    [SerializeField] float minDist = 1f;
    [Tooltip("Height of the marker above the item (m).")]
    [SerializeField] float worldGizmosHeight;
    [Tooltip("Whether the camper sees the markers.")]
    [SerializeField] bool showToCamper = true;
    [Tooltip("Whether the gnomes see the markers.")]
    [SerializeField] bool showToGnomes = true;

    public float WorldHeight => worldGizmosHeight;
    public float MinDistance => minDist;
    public float MaxDistance => maxDist;
    public bool ShowToCamper => showToCamper;
    public bool ShowToGnomes => showToGnomes;
}
