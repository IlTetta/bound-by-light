using MyGame.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Mostra il pannello "Demo Completata" quando GameManager.CurrentState
/// diventa GameEnding (triggerato dalla morte del boss).
/// Blocca anche il movimento del player locale.
/// </summary>
public class DemoCompletedUI : MonoBehaviour
{
    private Canvas _canvas;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        if (_canvas != null) _canvas.enabled = false;
    }

    private void Start()
    {
        StartCoroutine(WaitAndSubscribe());
    }

    private System.Collections.IEnumerator WaitAndSubscribe()
    {
        while (GameManager.Instance == null)
            yield return null;

        GameManager.Instance.CurrentState.OnValueChanged += OnGameStateChanged;

        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.GameEnding)
            ShowPanel();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.CurrentState.OnValueChanged -= OnGameStateChanged;
    }

    private void OnGameStateChanged(GameManager.GameState previous, GameManager.GameState current)
    {
        if (current == GameManager.GameState.GameEnding)
            ShowPanel();
    }

    private void ShowPanel()
    {
        if (_canvas != null) _canvas.enabled = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        LockLocalPlayer();

        Debug.Log("[DemoCompletedUI] Demo completata!");
    }

    private void LockLocalPlayer()
    {
        var localClient = NetworkManager.Singleton?.LocalClient;
        if (localClient?.PlayerObject == null) return;

        var motor3D = localClient.PlayerObject.GetComponent<PlayerMovementMotor3D>();
        if (motor3D != null) { motor3D.SetLocked(true); return; }

        var motor2D = localClient.PlayerObject.GetComponent<PlayerMovementMotor2D>();
        motor2D?.SetLocked(true);
    }
}
