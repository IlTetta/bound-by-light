using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pilastro distruttibile: si fratura quando la hitbox melee del boss lo colpisce.
///
/// ── STRUTTURA PREFAB RICHIESTA ───────────────────────────────────────────────
///
///   Pillar_Root  (questo GO)
///   ├── NetworkObject          ← NGO, gestisce il ClientRpc
///   ├── DestructiblePillar     ← questo script
///   └── CapsuleCollider        ← fisico, NON trigger (blocca movimento)
///   │
///   └── Pillar_Visual  (figlio)
///       ├── MeshFilter
///       ├── MeshRenderer
///       ├── Rigidbody          ← isKinematic=true, useGravity=true (i frammenti erediteranno useGravity)
///       └── Fracture           ← componente OpenFracture
///
/// ── PERCHÉ DUE GO SEPARATI? ──────────────────────────────────────────────────
///   OpenFracture.Fracture richiede Rigidbody sullo stesso GO della mesh.
///   NGO non vuole Rigidbody sullo stesso GO di NetworkObject (conflitto fisico).
///   Soluzione: Root = NetworkObject + Collider fisico / Visual = Mesh + Fracture.
///
/// ── SETUP ────────────────────────────────────────────────────────────────────
///   1. Crea la gerarchia sopra.
///   2. Su Rigidbody (Pillar_Visual): isKinematic=true, useGravity=true, constraints=tutti.
///   3. Su Fracture: Trigger Type = None (triggeriamo noi via codice).
///                   Inside Material = un materiale "taglio" (pietra grigia).
///                   Fragment Count = 20-30.
///   4. Assegna fractureVisual nell'Inspector di DestructiblePillar → Pillar_Visual.
///   5. Registra i pilastri nei Network Prefabs SOLO se sono spawned a runtime.
///      Se sono in scena (non NetworkPrefab), vengono registrati automaticamente da NGO.
///
/// ── FLUSSO ───────────────────────────────────────────────────────────────────
///   Boss hitbox (trigger) colpisce CapsuleCollider del pilastro
///   → OnTriggerEnter sul pilastro (server)
///   → FractureClientRpc → tutti i client chiamano CauseFracture()
///   → Fracture disattiva Pillar_Visual, crea frammenti figli che cadono
///   → CapsuleCollider disabilitato: il boss può calpestare i detriti
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class DestructiblePillar : NetworkBehaviour
{
    [Header("Riferimenti")]
    [Tooltip("Il figlio che contiene MeshFilter + MeshRenderer + Rigidbody + Fracture.")]
    [SerializeField] private GameObject fractureVisual;

    [Tooltip("Secondi prima che i detriti vengano distrutti. 0 = mai.")]
    [SerializeField] private float debrisLifetime = 8f;

    [Tooltip("Effetto particellare opzionale alla frattura (istanziato localmente su ogni client).")]
    [SerializeField] private GameObject fractureVfxPrefab;

    // ─── Stato ───────────────────────────────────────────────────────────────

    private bool     _fractured;
    private Collider _col;
    private Fracture _fracture;

    // ─── NGO ─────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _col      = GetComponent<Collider>();
        _fractured = false;

        // Cerca Fracture sul figlio (non su questo GO)
        _fracture = fractureVisual != null
            ? fractureVisual.GetComponent<Fracture>()
            : GetComponentInChildren<Fracture>();

        if (_fracture == null)
            Debug.LogWarning($"[DestructiblePillar] '{name}': componente Fracture non trovato su Pillar_Visual!", this);
    }

    // ─── Rilevamento boss ─────────────────────────────────────────────────────

    /// <summary>
    /// Unity chiama questo metodo su ENTRAMBI i GO partecipanti a una collisione trigger.
    /// Il boss ha Rigidbody + hitbox isTrigger → questo OnTriggerEnter scatta anche sul pilastro.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (_fractured) return;

        // Le hitbox del boss usano DamageOnTouchNetwork3D.
        bool isBossHitbox = other.GetComponent<DamageOnTouchNetwork3D>() != null
                         || other.GetComponentInParent<DamageOnTouchNetwork3D>() != null;

        if (!isBossHitbox) return;

        Debug.Log($"[DestructiblePillar] '{name}' colpito da hitbox boss '{other.name}' → frattura!");
        TriggerFractureServer();
    }

    // ─── Server ───────────────────────────────────────────────────────────────

    private void TriggerFractureServer()
    {
        _fractured = true;

        // Disabilita il collider fisico: boss e player possono ora attraversare
        if (_col != null) _col.enabled = false;

        // Notifica tutti i client di eseguire la frattura visuale
        FractureClientRpc();

        // Despawn via NGO dopo il debrisLifetime (server-authoritative cleanup).
        // Non usiamo Destroy() lato client — il NetworkObject.Despawn è l'unico punto di distruzione.
        if (debrisLifetime > 0f)
            StartCoroutine(DespawnAfterDelay(debrisLifetime));
        else
            NetworkObject.Despawn(true);

        Debug.Log($"[DestructiblePillar] '{name}' frantumato.");
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }

    // ─── Client RPC ───────────────────────────────────────────────────────────

    [ClientRpc]
    private void FractureClientRpc()
    {
        // Disabilita il collider anche sul client (sicurezza per client-side prediction)
        if (_col == null) _col = GetComponent<Collider>();
        if (_col != null) _col.enabled = false;

        // VFX opzionale
        if (fractureVfxPrefab != null)
            Instantiate(fractureVfxPrefab, transform.position, Quaternion.identity);

        // Triggera la frattura OpenFracture localmente (solo visuale, non sincronizzata)
        if (_fracture == null && fractureVisual != null)
            _fracture = fractureVisual.GetComponent<Fracture>();

        if (_fracture != null)
        {
            _fracture.CauseFracture();
            // La pulizia dei detriti è gestita dal server tramite NetworkObject.Despawn(true),
            // che distrugge il GO su tutti i peer — nessun Destroy() locale necessario.
        }
        else
        {
            Debug.LogWarning($"[DestructiblePillar] '{name}': Fracture component non trovato, " +
                             "nascondo solo il visual.");
            if (fractureVisual != null) fractureVisual.SetActive(false);
        }
    }
}
