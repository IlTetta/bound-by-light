using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Visualizza la salute del player su uno Slider e un TMP_Text.
/// Aggiorna la UI solo quando la salute cambia (event-driven),
/// evitando allocazioni stringa ogni frame.
///
/// Collegamento: chiama SetMaxHealth() e SetHealth() da PlayerHealth.cs
/// quando HealthNetwork.CurrentHealth.OnValueChanged si attiva.
/// </summary>
public class HealthDisplay : MonoBehaviour
{
    public Slider    sliders;
    public TMP_Text  healthDisplay;
    public Image     baseImage;
    public Sprite    highHealth;
    public Sprite    lowHealth;

    // Soglia sotto cui mostrare lo sprite "low health"
    private const float LowHealthThreshold = 30f;

    // ─── API ────────────────────────────────────────────────────────────────────

    public void SetMaxHealth(int health)
    {
        sliders.maxValue = health;
        sliders.value    = health;
        RefreshDisplay();
    }

    public void SetHealth(int health)
    {
        sliders.value = health;
        RefreshDisplay();
    }

    // ─── Privato ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aggiorna testo e sprite in un unico posto.
    /// Chiamato solo quando la salute cambia effettivamente.
    /// </summary>
    private void RefreshDisplay()
    {
        if (healthDisplay != null)
        {
            // Usa string.Format con il buffer interno di TMP per minimizzare allocazioni.
            healthDisplay.text = $"{(int)sliders.value} / {(int)sliders.maxValue}";
        }

        if (baseImage != null)
        {
            bool isLow = sliders.value < LowHealthThreshold;
            Sprite target = isLow ? lowHealth : highHealth;
            if (baseImage.sprite != target)      // assegna solo se cambia
                baseImage.sprite = target;
        }
    }
}
