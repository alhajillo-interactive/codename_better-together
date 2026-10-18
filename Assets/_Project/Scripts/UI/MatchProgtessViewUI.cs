using System;
using UnityEngine;
using UnityEngine.UI;

public class MatchProgtessViewUI : MonoBehaviour
{
    [SerializeField] Slider slider;

    void Update()
    {
        ShowMatchTimeProgress(GameManager.MatchProgress);
    }

    private void ShowMatchTimeProgress(float percentage)
    {
        slider.value = percentage;
    }
}
