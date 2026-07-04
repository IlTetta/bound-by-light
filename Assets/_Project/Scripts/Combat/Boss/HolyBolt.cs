using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Proiettile Holy Bolt del MiniBoss Vescovo.
/// Estende Projectile3D aggiungendo VFX e SFX di impatto su tutti i client.
///
/// ── STRUTTURA PREFAB ─────────────────────────────────────────────────────────
///
///   HolyBolt  (root)
///   ├── NetworkObject
///   ├── Rigidbody              isKinematic=true, useGravity=false, freezeRotation=true
///   ├── SphereCollider         isTrigger=true, radius=0.25
///   ├── HolyBolt (script)
///   └── Visual  (figlio)
///       ├── MeshFilter         mesh sfera/orb (usa una Sphere primitive)
///       ├── MeshRenderer       materiale emissivo (HDR gold, es. #FFD966 * 3)
///       ├── ParticleSystem     trail luminoso — Looping ✓, Play On Awake ✓
///       └── Light              Point Light, colore #FFD966, Range 4, Intensity 3
///
/// ── IMPOSTAZIONI CONSIGLIATE (componente HolyBolt nell'Inspector) ────────────
///   Speed                    14
///   Acceleration              0  (volo rettilineo)
///   LifeTime                  4
///   InitialInvulnerabilityDuration  0.05
///   FaceMovement              true
///   BlockingLayers            Wall, Obstacle (layers che fermano il bolt)
///   Damage                   15  (sovrascritto da Brain via SetDamageOverride)
///
/// ── NOTA NGO ─────────────────────────────────────────────────────────────────
///   SpawnImpactVfxClientRpc viene inviato PRIMA di base.OnDeath() che chiama
///   NetworkObject.Despawn. NGO garantisce ordinamento per-connessione: l'RPC
///   arriva ai client prima del messaggio di despawn → nessuna race condition.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class HolyBolt : Projectile3D
{
    [Header("Holy Bolt — VFX")]
    [Tooltip("Prefab istanziato localmente su tutti i client all'impatto. " +
             "Deve contenere un ParticleSystem one-shot (Stop Action = Destroy).")]
    [SerializeField] private GameObject _impactVfxPrefab;

    [Tooltip("Secondi prima che il VFX d'impatto venga distrutto. " +
             "Usato come fallback se il ParticleSystem non si auto-distrugge.")]
    [SerializeField] private float _impactVfxDuration = 1.5f;

    [Header("Holy Bolt — SFX")]
    [Tooltip("Evento FMOD riprodotto su ogni client all'impatto (opzionale).")]
    [SerializeField] private FMODUnity.EventReference _impactSfx;

    // ─── Override Projectile3D ────────────────────────────────────────────────

    /// <summary>
    /// Sovrascrive OnDeath di Projectile3D.
    /// Prima invia l'RPC per i VFX/SFX, poi chiama il despawn base.
    /// </summary>
    protected override void OnDeath()
    {
        // Propaga VFX/SFX su tutti i client prima del despawn.
        // L'RPC viene processato prima del messaggio di Despawn grazie all'ordinamento NGO.
        SpawnImpactVfxClientRpc(transform.position);

        // Disabilita il collider e despawna il NetworkObject (via Projectile3D.OnDeath)
        base.OnDeath();
    }

    // ─── RPC ─────────────────────────────────────────────────────────────────

    [ClientRpc]
    private void SpawnImpactVfxClientRpc(Vector3 hitPosition)
    {
        if (_impactVfxPrefab != null)
        {
            GameObject vfx = Instantiate(_impactVfxPrefab, hitPosition, Quaternion.identity);

            // Fallback: distruggi il VFX dopo N secondi se il ParticleSystem
            // non gestisce la propria pulizia (Stop Action = Destroy).
            Destroy(vfx, _impactVfxDuration);
        }

        if (!_impactSfx.IsNull)
        {
            FMOD.Studio.EventInstance inst = FMODUnity.RuntimeManager.CreateInstance(_impactSfx);
            inst.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(hitPosition));
            inst.start();
            inst.release();
        }
    }
}
