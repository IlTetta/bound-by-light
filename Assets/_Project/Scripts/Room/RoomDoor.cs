using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Porta di una stanza. Supporta due modalità:
///
///   • AutoOpen   — si apre appena <see cref="Open"/> viene chiamato
///                  (tipicamente da RoomWaveController.OnAllWavesCleared).
///
///   • KeyLocked  — si sblocca quando <see cref="Unlock"/> viene chiamato
///                  (tipicamente da RoomKey quando raccolta dal player).
///                  Dopo lo sblocco, un player può aprirla premendo E.
///
/// Setup prefab / scene:
///   1. Aggiungi questo componente al GameObject della porta.
///   2. Assegna <see cref="doorCollider"/> (il Collider che blocca il passaggio).
///   3. Assegna <see cref="doorVisual"/> (il child sprite/mesh da nascondere).
///   4. (Opzionale) Assegna un Animator e imposta <see cref="openAnimTrigger"/>.
///   5. Metti il GameObject della porta sul layer "Door" (configura in PlayerController).
/// </summary>
public class RoomDoor : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("Collider che blocca fisicamente il passaggio. Se null, cercato in Awake.")]
    [SerializeField] private Collider doorCollider;

    [Tooltip("Oggetto visivo della porta (sprite/mesh). Viene disattivato all'apertura.")]
    [SerializeField] private GameObject doorVisual;

    [Tooltip("Animator opzionale per l'animazione di apertura.")]
    [SerializeField] private Animator doorAnimator;

    [Header("Config")]
    [SerializeField] private string openAnimTrigger = "Open";

    // ─── NetworkVariables (sincronizzano lo stato su tutti i client) ─────────

    /// <summary>True = porta aperta, collider e visuale disattivati.</summary>
    public NetworkVariable<bool> IsOpen = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>True = la porta è stata sbloccata (chiave raccolta) e può essere aperta con E.</summary>
    public NetworkVariable<bool> IsUnlocked = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── Unity / NGO ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (doorCollider == null)
            doorCollider = GetComponent<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        IsOpen.OnValueChanged     += (_, open)     => ApplyOpenState(open);
        IsUnlocked.OnValueChanged += (_, unlocked) => OnUnlockChanged(unlocked);

        // Applica lo stato iniziale (importante se la porta spawna già aperta/sbloccata)
        ApplyOpenState(IsOpen.Value);
    }

    // ─── Public API (solo server) ─────────────────────────────────────────────

    /// <summary>
    /// Apre la porta immediatamente, senza richiedere la chiave.
    /// Usato da RoomWaveController per le porte ad apertura automatica.
    /// </summary>
    public void Open()
    {
        if (!IsServer || IsOpen.Value) return;
        IsOpen.Value = true;
    }

    /// <summary>
    /// Sblocca la porta: da questo momento i player possono aprirla premendo E.
    /// Usato da RoomKey quando viene raccolta.
    /// </summary>
    public void Unlock()
    {
        if (!IsServer || IsUnlocked.Value) return;
        IsUnlocked.Value = true;
        Debug.Log($"[RoomDoor] '{gameObject.name}' sbloccata.");
    }

    // ─── RPC ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Chiamato da PlayerController quando il player preme E vicino alla porta.
    /// Il server verifica che la porta sia sbloccata prima di aprirla.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestOpenServerRpc()
    {
        if (!IsUnlocked.Value)
        {
            Debug.Log($"[RoomDoor] '{gameObject.name}': tentativo di apertura fallito (non sbloccata).");
            return;
        }
        Open();
    }

    // ─── Visuals ──────────────────────────────────────────────────────────────

    private void ApplyOpenState(bool open)
    {
        if (doorCollider != null) doorCollider.enabled = !open;
        if (doorVisual    != null) doorVisual.SetActive(!open);

        if (open && doorAnimator != null)
            doorAnimator.SetTrigger(openAnimTrigger);
    }

    private void OnUnlockChanged(bool unlocked)
    {
        // Qui puoi aggiungere feedback visivo/audio per indicare ai player
        // che la porta è ora apribile (es. glow, suono di scatto serratura).
        if (unlocked)
            Debug.Log($"[RoomDoor] '{gameObject.name}' sbloccata — premi E per aprire.");
    }
}
