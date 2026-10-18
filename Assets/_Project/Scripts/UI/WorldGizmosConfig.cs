using UnityEngine;

[CreateAssetMenu(menuName = "codename_better-together/WorldGizmosConfig")]
public class WorldGizmosConfig : ScriptableObject
{
    [SerializeField] float maxDist = 10f;
    [SerializeField] float minDist = 1f;
    [SerializeField] float worldGizmosHeight;
    public float WorldHeight => worldGizmosHeight;
    public float MinDistance => minDist;
    public float MaxDistance => maxDist;
}