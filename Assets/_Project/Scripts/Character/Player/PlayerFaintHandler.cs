using FMODUnity;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Gestisce lo stato "faint" di un player e la rianimazione da parte del compagno.
///
/// Flusso:
///   1. HealthNetwork chiama TriggerFaint() sul server quando CurrentHealth == 0
///   2. Il server setta _isFainted = true (NetworkVariable → replicato a tutti)
///   3. OnIsFaintedChanged disabilita PlayerController e PlayerMovementMotor2D sull'owner
///   4. Il compagno vivo entra nel raggio di rianimazione (reviveRadius) e tiene premuto H
///   5. Dopo reviveHoldTime secondi, il server richiama Revive(): ripristina X% di vita
///      e riabilita i componenti di movimento
/// </summary>
[RequireComponent(typeof(HealthNetwork))]
public class PlayerFaintHandler : NetworkBehaviour
{
    [Header("SFX")]
    [Tooltip("Emitter FMOD riprodotto quando il player sviene (Player Death).")]
    [SerializeField] private StudioEventEmitter faintSfxEmitter;

    [Tooltip("Emitter FMOD riprodotto quando il player viene rianimato (Revive).")]
    [SerializeField] private StudioEventEmitter reviveSfxEmitter;

    [Header("Revive Settings")]
    [Tooltip("Raggio entro cui il compagno deve trovarsi per rianimare.\n" +
             "Misurato centro-centro sul piano XZ, in world units. Il player ha " +
             "raggio ~1.16 (capsule 5.81 × scale 0.2), quindi sotto ~2.4 i due " +
             "modelli devono compenetrarsi. Usa il gizmo per tararlo.")]
    [SerializeField] private float reviveRadius = 4.5f;

    [Tooltip("Percentuale di vita ripristinata alla rianimazione (0-1).")]
    [Range(0f, 1f)]
    [SerializeField] private float reviveHealthPercent = 0.3f;

    [Tooltip("Secondi di hold del tasto H necessari per completare la rianimazione.")]
    [SerializeField] private float reviveHoldTime = 3f;

    private readonly NetworkVariable<bool> _isFainted = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Progresso di rianimazione. Server-write, ma replicato a tutti perché
    // ReviveIndicator lo legge su entrambi i client per disegnare l'arco.
    private readonly NetworkVariable<float> _reviveProgress = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private float _reviveRpcTimer  = 0f; // throttle lato client: invia RPC max a 10fps

    private HealthNetwork _health;
    private PlayerController _controller;
    private PlayerMovementMotor3D _motor3D;
    private PlayerMovementMotor2D _motor2D;

    // Riferimento al compagno che si sta tentando di rianimare (usato per resettare il progresso server)
    private PlayerFaintHandler _currentReviveTarget;

    public bool IsFainted => _isFainted.Value;

    /// <summary>Raggio di rianimazione in world units. Letto da ReviveIndicator.</summary>
    public float ReviveRadius => reviveRadius;

    /// <summary>Progresso di rianimazione normalizzato 0..1. Replicato su tutti i client.</summary>
    public float ReviveProgress01 =>
        reviveHoldTime > 0f ? Mathf.Clamp01(_reviveProgress.Value / reviveHoldTime) : 0f;

    /// <summary>
    /// True sull'owner mentre sta tenendo H per rianimare il compagno.
    /// PlayerController lo legge per bloccare sparo e interazioni.
    /// </summary>
    public bool IsReviving { get; private set; }

    /// <summary>
    /// Evento server-side: fired quando lo stato faint cambia.
    /// GameOverHandler si sottoscrive per rilevare quando tutti i player sono fainted.
    /// </summary>
    public event System.Action<bool> OnFaintStateChanged; 

    // --- Lifecycle ---

    public override void OnNetworkSpawn()
    {
        _health = GetComponent<HealthNetwork>();
        _controller = GetComponent<PlayerController>();
        _motor3D = GetComponent<PlayerMovementMotor3D>();
        _motor2D = GetComponent<PlayerMovementMotor2D>();

        _isFainted.OnValueChanged += OnIsFaintedChanged;

        if (_isFainted.Value)
            ApplyFaintedState(true);
    }

    public override void OnNetworkDespawn()
    {
        _isFainted.OnValueChanged -= OnIsFaintedChanged;
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (_isFainted.Value) return; // non può rianimarsi da solo

        PlayerFaintHandler faintedCompanion = null;

        if (Input.GetKey(KeyCode.H))
            faintedCompanion = FindFaintedCompanionInRange();

        bool wasReviving = IsReviving;
        IsReviving = faintedCompanion != null;

        // Blocca/sblocca movimento quando lo stato di rianimazione cambia
        if (IsReviving != wasReviving)
        {
            SetMotorLocked(IsReviving);

            if (!IsReviving)
            {
                // H rilasciato o compagno uscito dal raggio: resetta progresso server-side
                _currentReviveTarget?.ResetReviveProgressServerRpc();
                _currentReviveTarget = null;
            }
            else
            {
                _currentReviveTarget = faintedCompanion;
            }
        }

        if (IsReviving)
        {
            // Throttle: invia la RPC al server max a 10 volte al secondo.
            // Passiamo il deltaTime accumulato così il server accumula esattamente
            // il tempo reale trascorso, indipendentemente dalla frequenza di invio.
            _reviveRpcTimer += Time.deltaTime;
            if (_reviveRpcTimer >= 0.1f)
            {
                faintedCompanion.TryReviveNearbyServerRpc(_reviveRpcTimer);
                _reviveRpcTimer = 0f;
            }
        }
        else
        {
            _reviveRpcTimer = 0f;
        }
    }

    private PlayerFaintHandler FindFaintedCompanionInRange()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            if (client.PlayerObject == NetworkObject) continue; // skip se stesso

            var handler = client.PlayerObject.GetComponent<PlayerFaintHandler>();
            if (handler == null) continue;
            if (!handler.IsFainted) continue;

            Vector3 a = transform.position; a.y = 0f;
            Vector3 b = client.PlayerObject.transform.position; b.y = 0f;
            float dist = Vector3.Distance(a, b);
            if (dist <= reviveRadius)
                return handler;
        }
        return null;
    }

    // --- API pubblica ---

    /// <summary>
    /// Chiamato dal server (da HealthNetwork) quando CurrentHealth raggiunge 0.
    /// </summary>
    public void TriggerFaint()
    {
        if (!IsServer) return;
        if (_isFainted.Value) return;

        _reviveProgress.Value = 0f;
        _isFainted.Value = true;
        OnFaintStateChanged?.Invoke(true);

        Debug.Log($"[PlayerFaintHandler] {gameObject.name} è fainted.");
    }

    // --- Logica di rianimazione ---

    /// <summary>
    /// Resetta il progresso di rianimazione server-side.
    /// Chiamato quando il compagno rilascia H o esce dal raggio prima del completamento.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ResetReviveProgressServerRpc()
    {
        _reviveProgress.Value = 0f;
    }

    /// <summary>
    /// Chiamato dal client owner del compagno vivo (al massimo 10 volte/s grazie al throttle).
    /// Riceve il deltaTime accumulato così il server accumula esattamente il tempo reale trascorso.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void TryReviveNearbyServerRpc(float deltaTime, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (!_isFainted.Value) return;

        // Clamp delta per sicurezza
        deltaTime = Mathf.Clamp(deltaTime, 0f, 0.5f);

        // Il controllo distanza avviene già lato client in FindFaintedCompanionInRange.
        // Non ripetiamo la verifica qui perché il server potrebbe non avere la posizione
        // aggiornata del joiner (problema noto con NT authority).
        _reviveProgress.Value += deltaTime;

        if (_reviveProgress.Value >= reviveHoldTime)
            Revive();
    }

    /// <summary>
    /// Rianimazione forzata dal server (es. checkpoint respawn).
    /// _isFainted è una NetworkVariable server-write: impostarla qui replica
    /// automaticamente lo stato a tutti i client (host + joiner) e fa scattare
    /// OnIsFaintedChanged ovunque.
    /// </summary>
    public void ForceRevive(int overrideHp = -1)
    {
        if (!IsServer || !_isFainted.Value) return;
        _reviveProgress.Value = 0f;
        _isFainted.Value = false;
        OnFaintStateChanged?.Invoke(false);
        _health.CurrentHealth.Value = overrideHp > 0
            ? Mathf.Min(overrideHp, _health.MaxHealth)
            : Mathf.Max(1, Mathf.RoundToInt(_health.MaxHealth * reviveHealthPercent));
    }

    private void Revive()
    {
        if (!IsServer) return;

        _reviveProgress.Value = 0f;
        _isFainted.Value = false;
        OnFaintStateChanged?.Invoke(false);

        _health.CurrentHealth.Value = Mathf.Max(1,
            Mathf.RoundToInt(_health.MaxHealth * reviveHealthPercent));

        Debug.Log($"[PlayerFaintHandler] {gameObject.name} rianimato con " +
                  $"{_health.CurrentHealth.Value} HP.");
    }

    // --- Logica di stato ---
    
    private void OnIsFaintedChanged(bool previous, bool current)
    {
        // Animazione su TUTTI i client (la NetworkVariable gestisce già la sincronizzazione)
        GetComponent<PlayerAnimatorHandler>()?.TriggerFaintDirect(current);

        // SFX — udibile su tutti i client
        PlayFaintSfx(current);

        // Input/movement solo sull'owner
        ApplyFaintedState(current);
    }

    private void PlayFaintSfx(bool isFainted)
    {
        if (isFainted) faintSfxEmitter?.Play();
        else           reviveSfxEmitter?.Play();
    }

    private void ApplyFaintedState(bool fainted)
    {
        // Solo l'owner disabilita/abilita i propri componenti di input
        if (!IsOwner) return;

        // Disabilita controller e motor sull'owner
        SetMotorLocked(fainted);
        if (_controller != null)
            _controller.enabled = !fainted;

        Debug.Log($"[PlayerFaintHandler] {gameObject.name} fainted={fainted} " +
                  $"(owner={IsOwner})");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetMotorLocked(bool locked)
    {
        if (_motor3D != null) _motor3D.SetLocked(locked);
        else if (_motor2D != null) _motor2D.SetLocked(locked);
    }

    // ── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Disegnato sempre, anche a design-time: prima il gizmo usciva solo se
        // _isFainted.Value era true, quindi nel prefab non si vedeva mai e il
        // raggio non era tarabile a vista.
        bool faintedNow = Application.isPlaying && IsSpawned && _isFainted.Value;

        Gizmos.color = faintedNow
            ? new Color(1f, 0.2f, 0.1f, 0.7f)   // fainted: in attesa di revive
            : new Color(1f, 0.5f, 0f, 0.35f);   // range nominale

        Gizmos.DrawWireSphere(transform.position, reviveRadius);
    }
#endif
}