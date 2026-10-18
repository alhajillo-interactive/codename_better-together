using BetterTogether.Match;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class StartMatchUI : MonoBehaviour
{
    [SerializeField] Button startButton;
    void Start()
    {
        startButton.onClick.AddListener(StartMatchBTN);
        MatchManager.OnGameStarted += OnGameStarted;
        MatchManager.OnLobby += OnLobby;
    }

    // The cursor is locked in first person, so the host can also start with Enter.
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            StartMatchBTN();

        // Hide as soon as the countdown starts, not only when playing.
        var manager = MatchManager.Instance;
        if (manager != null && manager.Phase != MatchPhase.Waiting)
            gameObject.SetActive(false);
    }
    void OnDestroy()
    {
        MatchManager.OnGameStarted -= OnGameStarted;
        MatchManager.OnLobby -= OnLobby;
    }

    private void OnGameStarted()
    {
        gameObject.SetActive(false);
    }

    private void OnLobby()
    {
        gameObject.SetActive(true);
    }

    void StartMatchBTN()
    {
        if (MatchManager.Instance != null)
            MatchManager.Instance.StartMatch();
    }
}
