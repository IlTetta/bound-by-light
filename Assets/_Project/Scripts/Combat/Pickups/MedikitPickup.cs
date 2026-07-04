using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pickup medikit. Aggiunge 1 medikit all'inventario del primo player che lo tocca.
///
/// Setup prefab FirstAidKit_White:
///   1. Aggiungi NetworkObject (se non presente).
///   2. Aggiungi questo componente.
///   3. Sul BoxCollider spunta "Is Trigger".
///   4. Registra il prefab nella lista Network Prefabs del NetworkManager.
///
/// Il medikit viene despawnato appena raccolto.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class MedikitPickup : NetworkBehaviour
{
    [Header("Config")]
    [Tooltip("Numero di medikit che questo pickup fornisce.")]
    [SerializeField] private int medikitAmount = 1;

    [Header("Floating animation")]
    [SerializeField] private float floatSpeed     = 1.4f;
    [SerializeField] private float floatAmplitude = 0.12f;
    [SerializeField] private float rotateSpeed    = 60f;

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
        // Animazione floating locale (solo estetica, nessun sync)
        transform.position = _startPos +
            Vector3.up * (Mathf.Sin(Time.time * floatSpeed) * floatAmplitude);

        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
    }

    // ─── Trigger ─────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        // Solo il server esegue la logica di pickup
        if (!IsServer || _collected) return;

        var medikit = other.GetComponentInParent<PlayerMedikit>();
        if (medikit == null) return;

        _collected = true;
        medikit.AddMedikit(medikitAmount);

        Debug.Log($"[MedikitPickup] Raccolto da {other.name}. Medikit erogati: {medikitAmount}");

        // Despawn: distrugge l'oggetto su tutti i client
        NetworkObject.Despawn(destroy: true);
    }
}
