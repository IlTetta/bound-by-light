using MyGame.Core;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mostra il numero di chiavi raccolte nell'HUD del player locale.
///
/// Setup (sul PlayerPrefab, come MedikitDisplay):
///   1. Aggiungi questo componente al root del PlayerPrefab.
///   2. Assegna countText: TMP_Text con "x0" nell'HUD.
///   3. (Opzionale) assegna keyIcons: 3 Image che si colorano man mano che raccogli le chiavi.
/// </summary>
public class KeyDisplay : NetworkBehaviour
{
    [Header("UI – testo")]
    [Tooltip("TMP_Text che mostra 'x N' dove N è il numero di chiavi.")]
    [SerializeField] private TMP_Text countText;

    [Header("UI – icone (opzionale)")]
    [Tooltip("Fino a 3 immagini icona-chiave. Si colorano progressivamente al raccogliere.")]
    [SerializeField] private Image[] keyIcons;
    [SerializeField] private Color iconActive   = Color.yellow;
    [SerializeField] private Color iconInactive = new Color(0.3f, 0.3f, 0.3f, 0.5f);

    // ─── NGO ─────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.SharedKeyCount.OnValueChanged += OnKeyCountChanged;
            UpdateDisplay(GameManager.Instance.SharedKeyCount.Value);
        }
        else
        {
            // GameManager potrebbe non essere ancora spawned — riprova al primo frame
            StartCoroutine(WaitForGameManager());
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        if (GameManager.Instance != null)
            GameManager.Instance.SharedKeyCount.OnValueChanged -= OnKeyCountChanged;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private System.Collections.IEnumerator WaitForGameManager()
    {
        while (GameManager.Instance == null)
            yield return null;

        GameManager.Instance.SharedKeyCount.OnValueChanged += OnKeyCountChanged;
        UpdateDisplay(GameManager.Instance.SharedKeyCount.Value);
    }

    private void OnKeyCountChanged(int _, int newCount) => UpdateDisplay(newCount);

    private void UpdateDisplay(int count)
    {
        if (countText != null)
            countText.text = "x" + count;

        if (keyIcons != null)
        {
            for (int i = 0; i < keyIcons.Length; i++)
            {
                if (keyIcons[i] == null) continue;
                keyIcons[i].color = i < count ? iconActive : iconInactive;
            }
        }
    }
}
