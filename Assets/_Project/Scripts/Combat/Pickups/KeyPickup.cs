using System.Collections;
using FMODUnity;
using MyGame.Core;

using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pickup chiave. Il primo player che lo raccoglie aggiunge 1 chiave al pool condiviso
/// (GameManager.SharedKeyCount). Le chiavi NON vengono consumate aprendo le porte:
/// sono requisiti di accesso permanenti.
///
/// Setup prefab Key:
///   1. Aggiungi NetworkObject.
///   2. Aggiungi questo componente.
///   3. Sul Collider (es. BoxCollider o CapsuleCollider) spunta Is Trigger.
///   4. Registra il prefab nella lista Network Prefabs del NetworkManager.
///   5. Spawna tramite RoomRewardSpawner al clear delle stanze Crossing, Warehouse, Puzzle.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class KeyPickup : NetworkBehaviour
{
    [Header("Animazione")]
    [SerializeField] private float floatSpeed     = 1.2f;
    [SerializeField] private float floatAmplitude = 0.15f;
    [SerializeField] private float rotateSpeed    = 45f;

    [Header("SFX")]
    [Tooltip("Emitter FMOD per il suono di raccolta chiave (Key Obtained).")]
    [SerializeField] private StudioEventEmitter keyObtainedEmitter;

    [Header("VFX raccolta")]
    [Tooltip("Effetto opzionale istanziato localmente alla raccolta.")]
    [SerializeField] private GameObject collectVfxPrefab;

    // ─── Stato ───────────────────────────────────────────────────────────────

    private Vector3 _startPos;
    private bool    _collected;

    // ─── Unity ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _startPos  = transform.position;
        _collected = false;
    }

    private void Update()
    {
        // Animazione floating locale (solo estetica)
        transform.position = _startPos +
            Vector3.up * (Mathf.Sin(Time.time * floatSpeed) * floatAmplitude);
        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
    }

    // ─── Trigger ─────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || _collected) return;

        // Qualsiasi player può raccoglierla
        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj == null || !netObj.CompareTag("Player")) return;

        _collected = true;

        // Aggiorna il contatore globale (UI e KeyDisplay)
        GameManager.Instance?.AddKey();
        // Notifica LevelManager così sblocca le MultiKeyDoor quando si raggiunge la soglia
        LevelManager.Instance?.RegisterKeyCollected();

        // Notifica i client dell'effetto visivo, poi despawna dopo un frame
        // (evita race condition: il despawn potrebbe arrivare prima del ClientRpc)
        CollectClientRpc();
        StartCoroutine(DespawnNextFrame());

        Debug.Log($"[KeyPickup] Chiave raccolta da clientId={netObj.OwnerClientId}.");
    }

    // ─── Helpers server ───────────────────────────────────────────────────────

    private IEnumerator DespawnNextFrame()
    {
        yield return null;  // attende un frame: garantisce che CollectClientRpc sia accodato prima del despawn
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(destroy: true);
    }

    // ─── Client RPC ───────────────────────────────────────────────────────────

    [ClientRpc]
    private void CollectClientRpc()
    {
        keyObtainedEmitter?.Play();

        if (collectVfxPrefab != null)
            Instantiate(collectVfxPrefab, transform.position, Quaternion.identity);
    }
}
