using MyGame.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mostra/nasconde il canvas di Game Over quando GameManager.CurrentState
/// diventa GameOver.
///
/// Setup: metti questo script sul GameObject "Game Over Canvas" in scena.
/// Usa Canvas.enabled per non disabilitare lo script stesso.
/// </summary>
public class GameOverUI : MonoBehaviour
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

        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.GameOver)
            ShowGameOverPanel();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.CurrentState.OnValueChanged -= OnGameStateChanged;
    }

    private void OnGameStateChanged(GameManager.GameState previous, GameManager.GameState current)
    {
        if (current == GameManager.GameState.GameOver)
            ShowGameOverPanel();
        else if (previous == GameManager.GameState.GameOver)
            HideGameOverPanel(); // respawn completato → nascondi
    }

    private void ShowGameOverPanel()
    {
        if (_canvas != null) _canvas.enabled = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        Debug.Log("[GameOverUI] Game Over mostrato.");
    }

    private void HideGameOverPanel()
    {
        if (_canvas != null) _canvas.enabled = false;
        Debug.Log("[GameOverUI] Game Over nascosto (respawn completato).");
    }

}
