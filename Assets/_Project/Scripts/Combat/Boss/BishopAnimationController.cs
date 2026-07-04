using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pilota l'Animator del Bishop Miniboss in base agli eventi del Brain e della Timeline.
///
/// SETUP PREFAB:
///   • Aggiungi questo script sullo stesso GameObject root del Boss
///     (accanto a BishopMinibossBrain).
///   • Assegna nell'Inspector:
///       - _animator  : l'Animator sul modello 3D figlio (o sullo stesso GO se è 2D sprite)
///       - _brain     : BishopMinibossBrain (auto-fetch se non assegnato)
///       - _timeline  : MeleeAttackTimeline  (auto-fetch se non assegnato)
///       - _health    : HealthNetwork        (auto-fetch se non assegnato)
///
/// ANIMATOR PARAMETERS richiesti (tutti Trigger salvo IsMoving):
///   bool  IsMoving        – true mentre il boss si sposta verso il target
///   trigger HeavySmash   – avvia l'animazione Heavy Smash (Attack1)
///   trigger Sweep        – avvia l'animazione Sweep (Attack2)
///   trigger RangedAttack – avvia l'animazione Ranged (RangeAttack1)
///   trigger Death        – avvia l'animazione Death (non ritorna a Idle)
///
/// ANIMATOR STATES suggeriti:
///   Idle          (default) ← Mage@Idle clip, loop
///   Walk                    ← Mage@Walk clip, loop
///   HeavySmash              ← Mage@Attack1 clip, NO loop
///   Sweep                   ← Mage@Attack2 clip, NO loop
///   RangedAttack            ← Mage@RangeAttack1 clip, NO loop
///   Death                   ← Mage@Death clip, NO loop
///
/// TRANSITIONS:
///   Any State → Death        (Trigger Death,   interruzione immediata, can transition to self = false)
///   Idle      → Walk         (bool IsMoving == true)
///   Walk      → Idle         (bool IsMoving == false)
///   Any State → HeavySmash   (Trigger HeavySmash,   has exit time = false)
///   Any State → Sweep        (Trigger Sweep,         has exit time = false)
///   Any State → RangedAttack (Trigger RangedAttack,  has exit time = false)
///   HeavySmash   → Idle      (has exit time = true, exit time ≈ 0.95)
///   Sweep        → Idle      (has exit time = true, exit time ≈ 0.95)
///   RangedAttack → Idle      (has exit time = true, exit time ≈ 0.95)
/// </summary>
[RequireComponent(typeof(BishopMinibossBrain))]
public sealed class BishopAnimationController : NetworkBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("L'Animator sul modello del boss (o sullo stesso GO).")]
    [SerializeField] private Animator _animator;

    [Tooltip("Auto-fetched se non assegnato.")]
    [SerializeField] private BishopMinibossBrain _brain;

    [Tooltip("Auto-fetched se non assegnato.")]
    [SerializeField] private MeleeAttackTimeline _timeline;

    [Tooltip("Auto-fetched se non assegnato.")]
    [SerializeField] private HealthNetwork _health;

    // ── Animator parameter hashes (prestazione) ───────────────────────────────
    private static readonly int HashIsMoving      = Animator.StringToHash("isMoving");
    private static readonly int HashHeavySmash    = Animator.StringToHash("HeavySmash");
    private static readonly int HashSweep         = Animator.StringToHash("Sweep");
    private static readonly int HashRangedAttack  = Animator.StringToHash("RangedAttack");
    private static readonly int HashDeath         = Animator.StringToHash("Death");

    // ── Stato locale ──────────────────────────────────────────────────────────
    private bool _isDead = false;
    private bool _eventsHooked = false;

    // ── Unity / Netcode ───────────────────────────────────────────────────────
    private void Awake()
    {
        if (_brain    == null) _brain    = GetComponent<BishopMinibossBrain>();
        if (_timeline == null) _timeline = GetComponent<MeleeAttackTimeline>();
        if (_health   == null) _health   = GetComponent<HealthNetwork>();

        // Se l'Animator non è assegnato prova a trovarlo sul figlio del modello
        if (_animator == null) _animator = GetComponentInChildren<Animator>();

        // Di default uno SkinnedMeshRenderer NON aggiorna i suoi bounds quando è
        // fuori dal frustum della camera: dopo che i player si teletrasportano
        // nella stanza del boss, i bounds del modello del boss possono restare
        // quelli calcolati nella posizione/inquadratura precedente e il culling
        // lo nasconde per sempre, anche se il modello è correttamente spawnato.
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.updateWhenOffscreen = true;
    }

    public override void OnNetworkSpawn()
    {
        // Forza subito lo stato Idle per evitare T-pose al primo frame.
        // Senza questo, l'animator rimane in Entry/T-pose finché il Brain
        // non invia il primo SetMovingClientRpc (1-2 frame di ritardo).
        if (_animator != null)
        {
            _animator.SetBool(HashIsMoving, false);
            _animator.Play("Idle", 0, 0f); // entra subito nello stato Idle (layer 0)
        }

        // Le animazioni si eseguono su tutti i client, non solo sul server.
        // Abboniamo però gli eventi solo UNA volta.
        if (_eventsHooked) return;

        // ---- eventi MeleeAttackTimeline ----
        // Questi eventi vengono sparati solo sul server; per sincronizzare
        // le animazioni sui client useremo un ClientRpc (vedi sotto).
        // Se il progetto è single-player / host-only puoi semplificare.
        if (IsServer)
        {
            _timeline.HitWindowOpened += OnHitWindowOpened; // non usato qui, ma utile per VFX
            _health.OnServerDeath += OnServerDeath;
        }

        _eventsHooked = true;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;
        _timeline.HitWindowOpened -= OnHitWindowOpened;
        _health.OnServerDeath -= OnServerDeath;
        _eventsHooked = false;
    }

    // ── API pubblica chiamata da BishopMinibossBrain ──────────────────────────

    /// <summary>
    /// Chiamato dal Brain prima di iniziare un attacco.
    /// Il Brain lo invoca lato server; il metodo propaga ai client via RPC.
    /// </summary>
    public void PlayAttackAnimation(BishopAttackType attackType)
    {
        if (!IsSpawned || !IsServer) return;
        PlayAttackAnimationClientRpc(attackType);
    }

    /// <summary>
    /// Aggiorna il parametro IsMoving in base alla velocità del Rigidbody.
    /// Chiamato ogni FixedUpdate dal Brain (lato server) oppure localmente
    /// tramite RPC se vuoi un aggiornamento più fluido sui client.
    /// </summary>
    public void SetMoving(bool moving)
    {
        if (!IsSpawned || !IsServer) return;
        SetMovingClientRpc(moving);
    }

    // ── ClientRpc ─────────────────────────────────────────────────────────────

    [ClientRpc]
    private void PlayAttackAnimationClientRpc(BishopAttackType attackType)
    {
        if (_animator == null || _isDead) return;

        switch (attackType)
        {
            case BishopAttackType.HeavySmash:
                _animator.SetTrigger(HashHeavySmash);
                break;
            case BishopAttackType.Sweep:
                _animator.SetTrigger(HashSweep);
                break;
            case BishopAttackType.HolyBolt:
            case BishopAttackType.TripleShot:
                _animator.SetTrigger(HashRangedAttack);
                break;
        }
    }

    [ClientRpc]
    private void SetMovingClientRpc(bool moving)
    {
        if (_animator == null || _isDead) return;
        _animator.SetBool(HashIsMoving, moving);
    }

    [ClientRpc]
    private void PlayDeathClientRpc()
    {
        if (_animator == null) return;
        _isDead = true;
        // Resetta eventuali trigger pendenti per non interrompere la Death
        _animator.ResetTrigger(HashHeavySmash);
        _animator.ResetTrigger(HashSweep);
        _animator.ResetTrigger(HashRangedAttack);
        _animator.SetBool(HashIsMoving, false);
        _animator.SetTrigger(HashDeath);
    }

    // ── Gestione eventi server ────────────────────────────────────────────────

    private void OnServerDeath()
    {
        PlayDeathClientRpc();
    }

    // Non usato per animazioni, ma utile se vuoi aggiungere VFX al hit-window
    private void OnHitWindowOpened() { }
}

/// <summary>
/// Enum condiviso tra Brain e AnimationController per identificare il tipo di attacco.
/// Mettilo in un file separato o lascialo qui (purché non ci sia duplicato).
/// </summary>
public enum BishopAttackType
{
    None,
    HeavySmash,
    Sweep,
    HolyBolt,
    TripleShot
}
