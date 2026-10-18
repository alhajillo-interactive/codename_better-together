using System;
using UnityEngine;
using UnityEngine.UI;

public class StartMatchUI : MonoBehaviour
{
    [SerializeField] Button startButton;
    void Start()
    {
        startButton.onClick.AddListener(StartMatchBTN);
        GameManager.OnGameStarted += OnGameStarted;
    }
    void OnDestroy()
    {
        GameManager.OnGameStarted -= OnGameStarted;
    }

    private void OnGameStarted()
    {
        gameObject.SetActive(false);
    }

    void StartMatchBTN()
    {
        GameManager.Instance.StartMatch();
    }
}
