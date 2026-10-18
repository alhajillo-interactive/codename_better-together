using System;
using UnityEngine;

public class WorldGizmos : MonoBehaviour
{
    [SerializeField] WorldGizmosConfig config;
    [SerializeField] GameObject icon;
    [SerializeField] LineRenderer line;
    [SerializeField] Transform target;

    internal void SetTarget(Transform transform)
    {
        target = transform;
    }

    void LateUpdate()
    {
        if (target == null) return;

        var targetPos = target.position;
        targetPos.y = config.WorldHeight;
        transform.position = targetPos;
        line.SetPosition(line.positionCount - 1, transform.InverseTransformPoint(target.position));

        float dist = Vector3.Distance(transform.position, Camera.main.transform.position);
        float targetScale = Mathf.InverseLerp(config.MinDistance, config.MaxDistance, dist);
        transform.localScale = new Vector3(targetScale, targetScale, targetScale);

        bool isVisible = targetScale > 0.1f;
        line.gameObject.SetActive(isVisible);
        icon.SetActive(isVisible);
    }
}
