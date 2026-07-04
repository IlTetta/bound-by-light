using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Chiave di stanza. Spawna al completamento di tutte le wave (tramite RoomWaveController).
/// Quando un player ci cammina sopra, sblocca le porte associate e si distrugge.
///
/// Dopo lo sblocco, ENTRAMBI i player possono aprire QUALSIASI delle porte sbloccate
/// premendo E nelle vicinanze — permettendo ai due di scegliere quale percorso intraprendere.
///
/// Setup prefab:
///   - Rigidbody2D: Kinematic, IsTrigger
///   - Collider2D: IsTrigger = true
///   - NetworkObject
///   - Questo componente
/// </summary>
public class RoomKey : NetworkBehaviour
{
    // Lista delle porte da sbloccare al pickup.
    // Assegnata da RoomWaveController subito dopo lo spawn (server-side).
    private List<RoomDoor> _doorsToUnlock = new();

    /// <summary>
    /// Chiamato da RoomWaveController dopo lo spawn per configurare le porte target.
    /// Deve essere chiamato server-side prima che qualsiasi player possa raccogliere la chiave.
    /// </summary>
    public void SetDoors(List<RoomDoor> doors)
    {
        _doorsToUnlock = new List<RoomDoor>(doors);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        // Solo i player raccolgono la chiave — controlla motore 3D o 2D
        bool isPlayer = other.GetComponentInParent<PlayerMovementMotor3D>() != null
                     || other.GetComponentInParent<PlayerMovementMotor2D>() != null;
        if (!isPlayer) return;

        int unlocked = 0;
        foreach (var door in _doorsToUnlock)
        {
            if (door != null)
            {
                door.Unlock();
                unlocked++;
            }
        }

        Debug.Log($"[RoomKey] Raccolta dal player. {unlocked} porte sbloccate — premi E per aprire.");
        NetworkObject.Despawn(true);
    }
}
