using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Azioni dei bottoni del menu di pausa.
/// Aggiungi questo script al GO "Pause Menu" e assegna i metodi agli OnClick dei bottoni.
/// </summary>
public class PauseMenuActions : MonoBehaviour
{
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    // Assegna al bottone CONTINUE
    public void OnContinueClicked()
    {
        PauseController pc = FindFirstObjectByType<PauseController>();
        pc?.SetState(GameState.Gameplay);
    }

    // Assegna al bottone QUIT
    public void OnQuitClicked()
    {
        Time.timeScale = 1f;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();

        SceneManager.LoadScene(mainMenuSceneName);
    }
}
