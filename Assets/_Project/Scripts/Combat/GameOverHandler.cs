using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MyGame.Core;

/// <summary>
/// Monitora i PlayerFaintHandler e triggera il GameOver sul server
/// quando tutti i player connessi sono fainted.
/// </summary>
public class GameOverHandler : MonoBehaviour
{
    [SerializeField] private bool logEvents = true;

    private readonly List<PlayerFaintHandler> _faintHandlers = new();
    private bool _subscribed = false;
    private int  _trackedCount = 0; // quanti handler validi abbiamo trovato l'ultima volta

    private void Start()
    {
        StartCoroutine(WaitAndSubscribe());
    }

    private System.Collections.IEnumerator WaitAndSubscribe()
    {
        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            yield return null;

        if (!NetworkManager.Singleton.IsServer) yield break;

        NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        _subscribed = true;

        // Aspetta che almeno un PlayerObject sia spawnato
        while (!AnyPlayerObjectReady())
            yield return null;

        RefreshFaintHandlers();
    }

    private bool AnyPlayerObjectReady()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            if (client.PlayerObject != null) return true;
        return false;
    }

    private void OnDestroy()
    {
        if (!_subscribed || NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        UnsubscribeAll();
    }

    private void OnClientConnected(ulong clientId)
    {
        // Il PlayerObject potrebbe non essere ancora assegnato al momento del callback:
        // aspettiamo un frame prima di aggiornare la lista.
        StartCoroutine(RefreshNextFrame());
    }

    private void OnClientDisconnected(ulong clientId) => RefreshFaintHandlers();

    private System.Collections.IEnumerator RefreshNextFrame()
    {
        yield return null;
        RefreshFaintHandlers();
    }

    private void RefreshFaintHandlers()
    {
        UnsubscribeAll();
        _faintHandlers.Clear();

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var handler = client.PlayerObject.GetComponent<PlayerFaintHandler>();
            if (handler == null) continue;

            _faintHandlers.Add(handler);
            handler.OnFaintStateChanged += OnPlayerFaintStateChanged;
        }

        _trackedCount = _faintHandlers.Count;
        if (logEvents)
            Debug.Log($"[GameOverHandler] Tracking {_trackedCount} player(s).");
    }

    private void UnsubscribeAll()
    {
        foreach (var h in _faintHandlers)
            if (h != null) h.OnFaintStateChanged -= OnPlayerFaintStateChanged;
    }

    private void OnPlayerFaintStateChanged(bool isFainted)
    {
        if (GameManager.Instance == null) return;
        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.GameOver) return;
        if (_faintHandlers.Count == 0) return;

        foreach (var h in _faintHandlers)
        {
            if (h == null) continue;
            if (!h.IsFainted) return;
        }

        if (logEvents) Debug.Log("[GameOverHandler] Tutti i player sono fainted → Game Over.");

        GameManager.Instance.ChangeState(GameManager.GameState.GameOver);

        if (CheckpointManager.Instance != null)
            CheckpointManager.Instance.TriggerRespawn();
    }
}
