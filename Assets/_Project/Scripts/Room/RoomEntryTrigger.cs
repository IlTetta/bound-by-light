using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Trigger di ingresso nella stanza.
/// Quando almeno un player entra nel volume, attiva la <see cref="Room"/> collegata.
///
/// Setup:
///   1. Aggiungi un figlio al GameObject della Room.
///   2. Aggiungi BoxCollider2D con "Is Trigger" = true.
///   3. Aggiungi questo componente.
///   4. Il campo "room" viene cercato automaticamente nel parent, oppure assegnalo manualmente.
///
/// Layer:
///   Il collider del player deve trovarsi nel layer configurato in "playerLayer".
///   Default: il layer chiamato "Player".
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class RoomEntryTrigger : MonoBehaviour
{
    [Header("Riferimenti")]
    [Tooltip("La Room da attivare. Se null, cercata automaticamente nel parent.")]
    [SerializeField] private Room room;

    [Header("Config")]
    [Tooltip("Layer mask dei player. Assicurati che i player siano su 'Player' layer.")]
    [SerializeField] private LayerMask playerLayer;

    [Tooltip("Numero minimo di player che devono entrare prima di attivare la stanza. " +
             "1 = appena il primo player entra.")]
    [SerializeField] [Min(1)] private int playersRequiredToActivate = 1;

    // ─── Stato ────────────────────────────────────────────────────────────────

    private int _playersInside = 0;
    private bool _activated = false;

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (room == null)
            room = GetComponentInParent<Room>();

        if (room == null)
            Debug.LogError($"[RoomEntryTrigger] '{gameObject.name}': " +
                           "nessuna Room trovata nel parent. Assegnala manualmente.");
        else
            room.OnRoomReset += HandleRoomReset;

        // Assicura che il collider sia trigger
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;

        // Default layer "Player" se non assegnato
        if (playerLayer.value == 0)
            playerLayer = LayerMask.GetMask("Player");
    }

    private void OnDestroy()
    {
        if (room != null)
            room.OnRoomReset -= HandleRoomReset;
    }

    /// <summary>Riarma il trigger quando la stanza torna a Sealed (reset al respawn).</summary>
    private void HandleRoomReset(Room _)
    {
        _activated     = false;
        _playersInside = 0;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_activated) return;
        if (!IsPlayer(other)) return;

        _playersInside++;

        // Solo il server gestisce la logica di attivazione
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        if (_playersInside >= playersRequiredToActivate)
            TryActivate();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;
        _playersInside = Mathf.Max(0, _playersInside - 1);
    }

    // ─── Logica ───────────────────────────────────────────────────────────────

    private void TryActivate()
    {
        if (_activated || room == null) return;
        if (room.State != RoomState.Sealed) return;

        _activated = true;
        room.Activate();
        Debug.Log($"[RoomEntryTrigger] Stanza '{room.RoomDisplayName}' attivata " +
                  $"({_playersInside} player dentro).");
    }

    private bool IsPlayer(Collider2D col) =>
        ((1 << col.gameObject.layer) & playerLayer) != 0;

    // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var col = GetComponent<BoxCollider2D>();
        if (col == null) return;

        Gizmos.color = _activated
            ? new Color(1f, 0.3f, 0f, 0.25f)
            : new Color(0.2f, 0.8f, 1f, 0.25f);

        Gizmos.DrawCube(
            transform.TransformPoint(col.offset),
            col.size);

        Gizmos.color = _activated ? Color.red : Color.cyan;
        Gizmos.DrawWireCube(
            transform.TransformPoint(col.offset),
            col.size);
    }
#endif
}
