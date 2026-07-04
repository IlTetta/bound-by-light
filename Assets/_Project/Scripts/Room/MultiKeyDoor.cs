using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Porta che richiede N chiavi per sbloccarsi.
/// Corrisponde al simbolo {3} nel glossario del diagramma.
///
/// A differenza di RoomDoor (che si sblocca con una singola chiave),
/// questa porta ascolta il contatore globale di LevelManager.
/// LevelManager chiama UnlockWithKeys() automaticamente quando
/// totalKeysCollected >= KeysRequired.
///
/// Setup:
///   1. Aggiungi questo componente alla porta nella scena.
///   2. Assegna Collider (blocca il passaggio) e doorVisual.
///   3. Imposta keysRequired (es. 3).
///   4. Trascina questo GameObject nell'array multiKeyDoors del LevelManager.
///
/// IMPORTANTE: Questo componente NON eredita da RoomDoor per evitare
/// conflitti di logica. È autonomo e si integra direttamente con LevelManager.
/// </summary>
public class MultiKeyDoor : NetworkBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Riferimenti")]
    [Tooltip("Collider che blocca fisicamente il passaggio.")]
    [SerializeField] private Collider doorCollider;

    [Tooltip("GameObject visivo della porta (disattivato all'apertura).")]
    [SerializeField] private GameObject doorVisual;

    [Tooltip("Animator opzionale per l'animazione di apertura.")]
    [SerializeField] private Animator doorAnimator;

    [Header("Config")]
    [Tooltip("Numero di chiavi necessarie per aprire questa porta.")]
    [SerializeField] [Min(1)] private int keysRequired = 3;

    [SerializeField] private string openAnimTrigger = "Open";

    // ─── NetworkVariables ─────────────────────────────────────────────────────

    /// <summary>True se la porta è stata sbloccata (abbastanza chiavi raccolte).</summary>
    public NetworkVariable<bool> IsUnlocked { get; } = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>True se la porta è completamente aperta.</summary>
    public NetworkVariable<bool> IsOpen { get; } = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── Proprietà ────────────────────────────────────────────────────────────

    public int KeysRequired => keysRequired;

    // ─── NGO ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (doorCollider == null)
            doorCollider = GetComponent<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        IsUnlocked.OnValueChanged += (_, v) => OnUnlockChanged(v);
        IsOpen.OnValueChanged     += (_, v) => ApplyOpenState(v);

        // Stato iniziale
        ApplyOpenState(IsOpen.Value);
        UpdateVisualLockIndicator(IsUnlocked.Value);
    }

    // ─── API pubblica (server only) ───────────────────────────────────────────

    /// <summary>
    /// Sblocca la porta perché sono state raccolte abbastanza chiavi.
    /// Chiamato da LevelManager.
    /// </summary>
    public void UnlockWithKeys()
    {
        if (!IsServer || IsUnlocked.Value) return;
        IsUnlocked.Value = true;
        Debug.Log($"[MultiKeyDoor] '{gameObject.name}' sbloccata con {keysRequired} chiavi.");
    }

    /// <summary>
    /// Apre la porta. Il player deve interagire (premi E) oppure l'apertura è automatica
    /// dopo lo sblocco, a seconda del design.
    /// Chiamato da PlayerController o automaticamente da UnlockWithKeys().
    /// </summary>
    public void Open()
    {
        if (!IsServer || IsOpen.Value) return;
        IsOpen.Value = true;
    }

    // ─── Interazione player (RPC) ─────────────────────────────────────────────

    /// <summary>
    /// Richiesta di apertura da parte di un player che preme E.
    /// Il server verifica che la porta sia sbloccata.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestOpenServerRpc()
    {
        if (!IsUnlocked.Value)
        {
            Debug.Log($"[MultiKeyDoor] '{gameObject.name}': servono " +
                      $"{keysRequired} chiavi (raccolte: {LevelManager.Instance?.KeysCollected}).");
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
        UpdateVisualLockIndicator(unlocked);

        if (unlocked)
        {
            Debug.Log($"[MultiKeyDoor] '{gameObject.name}' sbloccata! " +
                      $"Premi E per aprire (o si apre automaticamente).");
            // Apertura automatica: decommentare se non si vuole richiedere E
            // if (IsServer) Open();
        }
    }

    private void UpdateVisualLockIndicator(bool unlocked)
    {
        // Aggiungere feedback visivo (glow, cambio materiale, UI icon)
        // Es: GetComponent<Renderer>().material.color = unlocked ? Color.green : Color.red;
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsUnlocked.Value ? Color.green : Color.red;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);
        UnityEditor.Handles.Label(
            transform.position + Vector3.up,
            $"Chiavi richieste: {keysRequired}\nRaccolte: {LevelManager.Instance?.KeysCollected ?? 0}");
    }
#endif
}
