using BetterTogether.Interaction;
using BetterTogether.Player;
using UnityEngine;

/// <summary>
/// Floating marker above a carryable item, with a line down to it. It grows with distance so far items stay readable,
/// and hides while the item is held, resting on a spot (e.g. a log in the campfire) or used up.
/// </summary>
public class WorldGizmos : MonoBehaviour
{
    [SerializeField] WorldGizmosConfig config;
    [SerializeField] GameObject icon;
    [SerializeField] LineRenderer line;
    [SerializeField] Transform target;

    private Carryable _carryable;
    private Camera _camera;
    private bool _visible = true;

    internal void SetTarget(Transform newTarget)
    {
        target = newTarget;
        _carryable = newTarget != null ? newTarget.GetComponent<Carryable>() : null;
    }

    void LateUpdate()
    {
        if (target == null)
        {
            SetVisible(false);
            return;
        }

        if (_camera == null)
            _camera = Camera.main;
        if (_camera == null)
            return;

        // Height is relative to the item, so markers follow the terrain.
        transform.position = target.position + Vector3.up * config.WorldHeight;

        float dist = Vector3.Distance(transform.position, _camera.transform.position);
        float targetScale = Mathf.InverseLerp(config.MinDistance, config.MaxDistance, dist);
        transform.localScale = new Vector3(targetScale, targetScale, targetScale);
        line.SetPosition(line.positionCount - 1, transform.InverseTransformPoint(target.position));

        SetVisible(targetScale > 0.1f && IsRelevant());
    }

    // Only loose items are worth pointing at, and only for the roles the config allows.
    private bool IsRelevant()
    {
        if (_carryable != null && (_carryable.IsHeld || _carryable.IsPinned || _carryable.Inert))
            return false;

        var local = PlayerCharacter.Local;
        if (local == null)
            return true;

        return local.Role == PlayerRole.Camper ? config.ShowToCamper : config.ShowToGnomes;
    }

    private void SetVisible(bool visible)
    {
        if (visible == _visible)
            return;

        _visible = visible;
        line.gameObject.SetActive(visible);
        icon.SetActive(visible);
    }
}
