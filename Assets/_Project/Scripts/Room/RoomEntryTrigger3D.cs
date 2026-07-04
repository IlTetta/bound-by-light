using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Trigger di ingresso nella stanza — versione 3D.
/// Sostituisce RoomEntryTrigger (2D): usa Collider 3D e OnTriggerEnter.
/// Stessa logica: conta i player che entrano, attiva la Room quando
/// il numero minimo è raggiunto.
///
/// Setup:
///   1. Aggiungi un figlio al GameObject della Room.
///   2. Aggiungi BoxCollider con "Is Trigger" = true.
///   3. Aggiungi questo componente.
///   4. Il campo "room" viene cercato automaticamente nel parent.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RoomEntryTrigger3D : MonoBehaviour
{
    [Header("Riferimenti")]
    [SerializeField] private Room room;

    [Header("Config")]
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] [Min(1)] private int playersRequiredToActivate = 1;

    private int  _playersInside = 0;
    private bool _activated     = false;

    private void Awake()
    {
        if (room == null)
            room = GetComponentInParent<Room>();

        if (room == null)
            Debug.LogError($"[RoomEntryTrigger3D] '{gameObject.name}': " +
                           "nessuna Room trovata nel parent.");
        else
            room.OnRoomReset += HandleRoomReset;

        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        if (playerLayer.value == 0)
            playerLayer = LayerMask.GetMask("Player");
    }

    private void OnDestroy()
    {
        if (room != null)
            room.OnRoomReset -= HandleRoomReset;
    }

    /// <summary>
    /// Riarma il trigger quando la stanza torna a Sealed (reset al respawn),
    /// così i player che rientrano possono riattivarla e far ri-spawnare i nemici.
    /// </summary>
    private void HandleRoomReset(Room _)
    {
        _activated     = false;
        _playersInside = 0;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_activated) return;
        if (!IsPlayer(other)) return;

        _playersInside++;

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        if (_playersInside >= playersRequiredToActivate)
            TryActivate();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playersInside = Mathf.Max(0, _playersInside - 1);
    }

    private void TryActivate()
    {
        if (_activated || room == null) return;
        if (room.State != RoomState.Sealed) return;

        _activated = true;
        room.Activate();
        Debug.Log($"[RoomEntryTrigger3D] Stanza '{room.RoomDisplayName}' attivata " +
                  $"({_playersInside} player dentro).");
    }

    private bool IsPlayer(Collider col) =>
        ((1 << col.gameObject.layer) & playerLayer) != 0;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var col = GetComponent<BoxCollider>();
        if (col == null) return;

        Gizmos.color = _activated
            ? new Color(1f, 0.3f, 0f, 0.25f)
            : new Color(0.2f, 0.8f, 1f, 0.25f);

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(col.center, col.size);

        Gizmos.color = _activated ? Color.red : Color.cyan;
        Gizmos.DrawWireCube(col.center, col.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
