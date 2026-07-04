using System.Collections;
using MyGame.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Aggiorna la Barra Super leggendo GameManager.SharedBondEnergy (NetworkVariable).
/// Si aspetta che il GameManager venga spawnato a runtime, quindi usa una coroutine
/// per attendere che sia disponibile prima di sottoscriversi.
/// </summary>
public class BondEnergyDisplay : MonoBehaviour
{
    [Tooltip("Slider della barra super (Giallo).")]
    [SerializeField] private Slider slider;

    private void Start()
    {
        StartCoroutine(WaitForGameManager());
    }

    private IEnumerator WaitForGameManager()
    {
        while (GameManager.Instance == null)
            yield return null;

        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.value    = GameManager.Instance.SharedBondEnergy.Value;

        GameManager.Instance.SharedBondEnergy.OnValueChanged += OnEnergyChanged;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.SharedBondEnergy.OnValueChanged -= OnEnergyChanged;
    }

    private void OnEnergyChanged(float oldValue, float newValue)
    {
        slider.value = newValue;
    }
}
