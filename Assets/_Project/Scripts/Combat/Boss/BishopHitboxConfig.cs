using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Configura a runtime le due hitbox melee del Vescovo e valida che tutti i
/// componenti richiesti siano presenti sul prefab.
///
/// POSIZIONAMENTO SUL PREFAB:
///
///   BishopMiniboss (root)
///   ├── BishopMinibossBrain          ← AI
///   ├── BishopHitboxConfig           ← questo script
///   ├── MeleeAttackTimeline
///   ├── NetworkProjectileWeapon
///   ├── HealthNetwork
///   ├── Rigidbody                    (3D, useGravity=false, constraints Y+rot)
///   ├── EnemyMotor3D
///   ├── NetworkObject
///   │
///   ├── HeavySmash_Hitbox  (figlio)
///   │     ├── SphereCollider         r ≈ 1.2  (isTrigger = true)
///   │     └── DamageOnTouchNetwork
///   │
///   └── Sweep_Hitbox       (figlio)
///         ├── MeshCollider / BoxCollider   ventaglio ~120° davanti (isTrigger = true)
///         └── DamageOnTouchNetwork
///
/// Il collider del Sweep va modellato sul piano XZ come un ventaglio.
/// BishopMinibossBrain ruota l'intero GO verso il target prima del windup.
/// </summary>
public sealed class BishopHitboxConfig : NetworkBehaviour
{
    [Header("Heavy Smash – hitbox circolare")]
    [Tooltip("GameObject figlio con SphereCollider + DamageOnTouchNetwork.")]
    [SerializeField] private DamageOnTouchNetwork _smashHitbox;

    [Header("Sweep – hitbox a ventaglio")]
    [Tooltip("GameObject figlio con MeshCollider o BoxCollider + DamageOnTouchNetwork.")]
    [SerializeField] private DamageOnTouchNetwork _sweepHitbox;

    [Header("Offset rispetto al root (locale)")]
    [Tooltip("Offset locale della HeavySmash rispetto al root del boss.")]
    [SerializeField] private Vector3 _smashLocalOffset = Vector3.zero;

    [Tooltip("Offset locale della Sweep rispetto al root del boss.")]
    [SerializeField] private Vector3 _sweepLocalOffset = new Vector3(0.6f, 0f, 0f);

    // ── Netcode ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Solo il server gestisce hitbox
        if (!IsServer) { enabled = false; return; }

        ApplyOffsets();
        ValidateSetup();
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    private void ApplyOffsets()
    {
        if (_smashHitbox != null)
            _smashHitbox.transform.localPosition = _smashLocalOffset;

        if (_sweepHitbox != null)
            _sweepHitbox.transform.localPosition = _sweepLocalOffset;
    }

    /// <summary>
    /// Controlla in fase di spawn che tutti i componenti obbligatori siano presenti.
    /// Se manca qualcosa stampa un errore chiaro in console senza crashare.
    /// </summary>
    private void ValidateSetup()
    {
        bool ok = true;

        if (_smashHitbox == null)
        {
            Debug.LogError($"[BishopHitboxConfig] '{name}': HeavySmash_Hitbox non assegnata! " +
                           "Crea un GameObject figlio con SphereCollider + DamageOnTouchNetwork " +
                           "e trascinalo nel campo SmashHitbox.", this);
            ok = false;
        }
        else
        {
            var col = _smashHitbox.GetComponent<SphereCollider>();
            if (col == null)
                Debug.LogWarning($"[BishopHitboxConfig] HeavySmash_Hitbox dovrebbe avere un " +
                                 "SphereCollider. Collider non trovato.", _smashHitbox);
        }

        if (_sweepHitbox == null)
        {
            Debug.LogError($"[BishopHitboxConfig] '{name}': Sweep_Hitbox non assegnata! " +
                           "Crea un GameObject figlio con MeshCollider/BoxCollider + DamageOnTouchNetwork " +
                           "e trascinalo nel campo SweepHitbox.", this);
            ok = false;
        }
        else
        {
            var col = _sweepHitbox.GetComponent<Collider>();
            if (col == null)
                Debug.LogWarning($"[BishopHitboxConfig] Sweep_Hitbox dovrebbe avere un " +
                                 "Collider 3D (MeshCollider o BoxCollider). Collider non trovato.", _sweepHitbox);
        }

        var brain = GetComponent<BishopMinibossBrain>();
        if (brain == null)
        {
            Debug.LogError($"[BishopHitboxConfig] '{name}': BishopMinibossBrain non trovato " +
                           "sullo stesso GameObject!", this);
            ok = false;
        }

        var timeline = GetComponent<MeleeAttackTimeline>();
        if (timeline == null)
        {
            Debug.LogError($"[BishopHitboxConfig] '{name}': MeleeAttackTimeline non trovata " +
                           "sullo stesso GameObject!", this);
            ok = false;
        }

        if (ok)
            Debug.Log($"[BishopHitboxConfig] '{name}': setup hitbox OK.");
    }

    // ── API pubblica (opzionale, utile per test) ──────────────────────────────

    public DamageOnTouchNetwork SmashHitbox => _smashHitbox;
    public DamageOnTouchNetwork SweepHitbox => _sweepHitbox;

    // ── Gizmos ────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_smashHitbox != null)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
            var sphere = _smashHitbox.GetComponent<SphereCollider>();
            float r = sphere != null ? sphere.radius : 0.5f;
            Gizmos.DrawSphere(transform.position + _smashLocalOffset, r);
        }

        if (_sweepHitbox != null)
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
            Vector3 dir = _sweepLocalOffset.sqrMagnitude > 0.001f
                ? _sweepLocalOffset.normalized
                : Vector3.forward;
            Gizmos.DrawRay(transform.position, dir * 1.8f);
        }
    }
#endif
}
