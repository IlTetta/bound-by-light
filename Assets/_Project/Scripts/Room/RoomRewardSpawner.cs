using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawna ricompense (medikit, ammo, ecc.) quando la stanza viene completata.
///
/// Setup:
///   1. Aggiungi questo componente al GO della Room (stesso GO che ha il componente Room).
///   2. Aggiungi entry alla lista Rewards: prefab NetworkObject + punto di spawn.
///   3. I prefab DEVONO essere registrati nella lista Network Prefabs del NetworkManager.
///
/// Le ricompense vengono spawnate una sola volta al primo clear della stanza.
/// </summary>
[RequireComponent(typeof(Room))]
public class RoomRewardSpawner : NetworkBehaviour
{
    [System.Serializable]
    public struct RewardEntry
    {
        [Tooltip("Prefab da spawnare (deve avere NetworkObject).")]
        public NetworkObject prefab;

        [Tooltip("Punto in cui spawna il prefab. Se null, usa la posizione della Room.")]
        public Transform spawnPoint;
    }

    [Header("Rewards")]
    [Tooltip("Lista di prefab da spawnare al clear della stanza.")]
    [SerializeField] private List<RewardEntry> rewards = new();

    // ─── Stato ───────────────────────────────────────────────────────────────

    private Room _room;
    private bool _spawned;

    // ─── NGO ─────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        _room = GetComponent<Room>();
        if (_room == null) return;

        _room.OnRoomCleared += HandleRoomCleared;

        // Se la stanza è già cleared (reload scena o start room), spawna subito
        if (_room.State == RoomState.Cleared)
            SpawnRewards();
    }

    public override void OnNetworkDespawn()
    {
        if (_room != null)
            _room.OnRoomCleared -= HandleRoomCleared;
    }

    // ─── Handler ─────────────────────────────────────────────────────────────

    private void HandleRoomCleared(Room _) => SpawnRewards();

    private void SpawnRewards()
    {
        if (!IsServer || _spawned || rewards == null) return;
        _spawned = true;

        foreach (var entry in rewards)
        {
            if (entry.prefab == null) continue;

            Vector3 pos = entry.spawnPoint != null
                ? entry.spawnPoint.position
                : transform.position;

            var obj = Instantiate(entry.prefab, pos, Quaternion.identity);
            obj.Spawn(destroyWithScene: true);

            Debug.Log($"[RoomRewardSpawner] Spawnata ricompensa '{entry.prefab.name}' @ {pos}");
        }
    }
}
