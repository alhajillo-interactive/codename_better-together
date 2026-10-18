using System;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class GameManager : NetworkBehaviour
{
    static public event UnityAction OnGameOver;
    static public event UnityAction OnGameStarted;
    static public float MatchProgress { get; private set; } = 0;
    static GameManager _instance;
    static public GameManager Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<GameManager>();
            return _instance;
        }
    }

    [SerializeField, Min(10)] float matchDurationTimeSeconds = 60;
    [Networked] public TickTimer MatchTimer { get; private set; }
    [Networked, OnChangedRender(nameof(OnStateChanged))] public PlayState CurrentState { get; private set; }

    // triggers each time the state changes on all clients
    private void OnStateChanged()
    {
        Debug.Log("state changed");

        if (CurrentState == PlayState.Playing)
        {
            MatchProgress = 0;
            OnGameStarted?.Invoke();
        }
        if (CurrentState == PlayState.Ended) OnGameOver?.Invoke();
    }

    public enum PlayState { Lobby, Playing, Ended }
    public void StartMatch()
    {
        if (HasStateAuthority)
        {
            DoStartMatch();
        }
        else
        {
            RPC_StartMatch();
        }
    }

    // Only executes on the State Authority (master client)
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_StartMatch() => DoStartMatch();
    private void DoStartMatch()
    {
        if (CurrentState != PlayState.Lobby) return;
        CurrentState = PlayState.Playing;
        MatchTimer = TickTimer.CreateFromSeconds(Runner, matchDurationTimeSeconds);
    }
    // Runs on every peer, every tick
    public override void FixedUpdateNetwork()
    {
        if (CurrentState != PlayState.Playing) return;

        // Only the State Authority (master client) acts on expiry
        if (HasStateAuthority && MatchTimer.Expired(Runner))
        {
            CurrentState = PlayState.Ended;
        }
    }

    // UI update (runs on all clients, not tick-locked)
    public override void Render()
    {
        if (CurrentState != PlayState.Playing) return;

        float? remaining = MatchTimer.RemainingTime(Runner);
        if (remaining.HasValue)
        {
            float normalized = remaining.Value / matchDurationTimeSeconds;
            MatchProgress = 1 - normalized;
        }
    }


}
