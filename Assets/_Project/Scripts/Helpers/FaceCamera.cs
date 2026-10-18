using UnityEngine;

public class FaceCamera : MonoBehaviour
{

    private Camera m_Camera;

    void Awake()
    {
        if (TryGetComponent<Canvas>(out var canvas))
        {
            canvas.worldCamera = Camera.main;
        }
    }

    void LateUpdate()
    {
        if (m_Camera == null) m_Camera = Camera.main;

        Vector3 targetDir = m_Camera.transform.forward;
        targetDir.y = 0; // Lock Y axis
        transform.rotation = Quaternion.LookRotation(targetDir, transform.up);
    }
}