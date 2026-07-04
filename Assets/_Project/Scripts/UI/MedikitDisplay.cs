using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Mostra il numero di medikit nel HUD del player locale.
///
/// Setup prefab:
///   1. Aggiungi questo componente al root del PlayerPrefab (accanto ad AmmoDisplay).
///   2. Assegna countText: un TMP_Text nel canvas HUD del prefab (es. "x3").
///   3. Assegna (opzionale) medikitIcon: l'Image icona, viene nascosta se count = 0.
/// </summary>
public class MedikitDisplay : NetworkBehaviour
{
    [Header("UI")]
    [Tooltip("TMP_Text che mostra 'x N' dove N è il numero di medikit.")]
    [SerializeField] private TMP_Text countText;

    [Tooltip("(Opzionale) Root dell'icona medikit nella HUD — nascosta quando count = 0.")]
    [SerializeField] private GameObject medikitIcon;

    private PlayerMedikit _medikit;

    // ─── NGO ─────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // Nasconde gli elementi UI per i player non-owner
            if (countText  != null) countText.gameObject.SetActive(false);
            if (medikitIcon != null) medikitIcon.SetActive(false);
            return;
        }

        _medikit = GetComponent<PlayerMedikit>();
        if (_medikit == null)
        {
            Debug.LogWarning("[MedikitDisplay] PlayerMedikit non trovato sul player.");
            return;
        }

        _medikit.OnCountChanged += UpdateDisplay;

        // Inizializza con il valore corrente
        UpdateDisplay(_medikit.MedikitCount.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (_medikit != null)
            _medikit.OnCountChanged -= UpdateDisplay;
    }

    // ─── Update UI ────────────────────────────────────────────────────────────

    private void UpdateDisplay(int count)
    {
        if (countText != null)
            countText.text = "x" + count;

        // L'icona è sempre visibile — mostra x0 quando l'inventario è vuoto
    }
}
