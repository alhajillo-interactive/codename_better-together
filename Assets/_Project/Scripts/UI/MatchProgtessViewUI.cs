using System;
using BetterTogether.Match;
using UnityEngine;
using UnityEngine.UI;

public class MatchProgtessViewUI : MonoBehaviour
{
    [SerializeField] Slider slider;

    void Update()
    {
        ShowMatchTimeProgress(MatchManager.MatchProgress);
    }

    private void ShowMatchTimeProgress(float percentage)
    {
        slider.value = percentage;
    }
}
