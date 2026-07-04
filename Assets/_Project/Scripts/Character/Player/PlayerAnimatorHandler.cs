using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

/// <summary>
/// Guida i parametri dell'Animator del player collegandosi agli eventi
/// di PlayerController e PlayerFaintHandler.
///
/// Setup prefab:
///   1. Aggiungi questo script al root del PlayerPrefab.
///   2. Assegna il campo 'animator' all'Animator presente sul child mesh
///      (es. il GameObject "Twin1v2@Breathing Idle").
///   3. Aggiungi un NetworkAnimator al root e assegna lo stesso Animator nel suo campo.
///
/// Parametri Animator richiesti (nomi esatti):
///   Speed       (Float)    — 0 = idle, >0.1 = run
///   HasWeapon   (Bool)     — false = Breathing Idle, true = Rifle Idle
///   WeaponIndex (Int)      — 0 = rifle, 1 = shotgun
///   Shoot       (Trigger)  — animazione sparo
///   Reload      (Trigger)  — animazione ricarica
///   Faint       (Trigger)  — personaggio cade a terra
///   GetUp       (Trigger)  — personaggio si rialza
/// </summary>
public sealed class PlayerAnimatorHandler : NetworkBehaviour
{
    [SerializeField] private Animator animator;

    // Indice del layer UpperBody nell'Animator (Base Layer = 0, UpperBody = 1)
    private const int UpperBodyLayer = 1;

    /// <summary>
    /// Ritorna true se l'UpperBody layer sta attualmente suonando lo stato "Reload".
    /// Usato da PlayerController per bloccare lo sparo durante la ricarica.
    /// </summary>
    public bool IsReloading =>
        animator != null &&
        animator.GetCurrentAnimatorStateInfo(UpperBodyLayer).IsName("Reload");

    // Hash dei parametri (più performante di passare stringhe ogni frame)
    private static readonly int SpeedHash       = Animator.StringToHash("Speed");
    private static readonly int HasWeaponHash   = Animator.StringToHash("HasWeapon");
    private static readonly int WeaponIndexHash = Animator.StringToHash("WeaponIndex");
    private static readonly int ShootHash       = Animator.StringToHash("Shoot");
    private static readonly int ReloadHash      = Animator.StringToHash("Reload");
    private static readonly int FaintHash       = Animator.StringToHash("Faint");
    private static readonly int GetUpHash       = Animator.StringToHash("GetUp");

    private PlayerController _controller;
    private NetworkAnimator  _netAnimator;
    private Vector3          _lastPosition;

    // Evita di inviare RPC ogni frame se Speed non è cambiato significativamente
    private float _lastSyncedSpeed = -1f;
    private const float SpeedSyncThreshold = 0.05f;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        _controller  = GetComponent<PlayerController>();
        _netAnimator = GetComponent<NetworkAnimator>();

        // Il movimento è interamente code-driven (PlayerMovementMotor).
        // La root motion delle clip sposterebbe il Transform del mesh figlio
        // indipendentemente dal parent, causando drift dalla camera e dai collider.
        if (animator != null)
            animator.applyRootMotion = false;

        // Di default uno SkinnedMeshRenderer NON aggiorna i suoi bounds quando è
        // fuori dal frustum della camera. Dopo un teletrasporto (spostamento
        // istantaneo, es. CoopDoorTransition), i bounds restano quelli calcolati
        // nella posizione precedente e il culling può nascondere il modello per
        // sempre. Le armi (MeshRenderer non skinnato) non soffrono di questo
        // problema, il che spiegava perché restavano visibili mentre il corpo
        // del player spariva dopo il teleport.
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.updateWhenOffscreen = true;
    }

    public override void OnNetworkSpawn()
    {
        _lastPosition = transform.position;

        // Solo l'owner ascolta gli eventi di input e aggiorna i parametri
        if (!IsOwner) return;

        if (_controller != null)
        {
            _controller.OnShot            += HandleShot;
            _controller.OnReloadRequested += HandleReload;
            _controller.OnWeaponSwitched  += HandleWeaponSwitch;
            _controller.OnWeaponAcquired  += HandleWeaponAcquired;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_controller != null)
        {
            _controller.OnShot            -= HandleShot;
            _controller.OnReloadRequested -= HandleReload;
            _controller.OnWeaponSwitched  -= HandleWeaponSwitch;
            _controller.OnWeaponAcquired  -= HandleWeaponAcquired;
        }
    }

    private void Update()
    {
        // Solo l'owner aggiorna Speed — NetworkAnimator replica il valore agli altri client
        if (!IsOwner) return;
        if (animator == null) return;

        // Calcola la velocità dal delta di posizione (MovePosition non imposta linearVelocity)
        float speed = (transform.position - _lastPosition).magnitude / Time.deltaTime;
        _lastPosition = transform.position;

        // Imposta localmente per risposta immediata
        animator.SetFloat(SpeedHash, speed);

        // Se siamo un client (non il server), il NetworkAnimator è server-authoritative:
        // dobbiamo aggiornare l'animator anche sul server altrimenti esso sincronizzerà
        // Speed=0 sovrascrivendo il valore locale ogni tick di rete.
        if (!IsServer && Mathf.Abs(speed - _lastSyncedSpeed) > SpeedSyncThreshold)
        {
            _lastSyncedSpeed = speed;
            SyncSpeedServerRpc(speed);
        }
    }

    // ─── Handler eventi input (solo owner) ───────────────────────────────────

    private void HandleShot()
    {
        // SetTrigger via NetworkAnimator: l'owner chiama, NGO replica su tutti i client
        _netAnimator?.SetTrigger(ShootHash);
    }

    private void HandleReload()
    {
        _netAnimator?.SetTrigger(ReloadHash);
    }

    private void HandleWeaponSwitch(int weaponIndex)
    {
        animator?.SetInteger(WeaponIndexHash, weaponIndex);
        if (!IsServer)
            SyncWeaponIndexServerRpc(weaponIndex);
    }

    private void HandleWeaponAcquired(int slot)
    {
        // Prima arma raccolta: attiva la transizione Breathing_Idle → RifleIdle
        animator?.SetBool(HasWeaponHash, true);
        animator?.SetInteger(WeaponIndexHash, slot);
        if (!IsServer)
            SyncWeaponAcquiredServerRpc(slot);
    }

    // ─── ServerRpc per parametri non-trigger (client → server → NetworkAnimator sync) ──

    /// <summary>
    /// Aggiorna Speed sull'animator server-side, così NetworkAnimator può
    /// sincronizzarlo correttamente a tutti i client (incluso l'owner).
    /// </summary>
    [Rpc(SendTo.Server)]
    private void SyncSpeedServerRpc(float speed)
        => animator?.SetFloat(SpeedHash, speed);

    [Rpc(SendTo.Server)]
    private void SyncWeaponIndexServerRpc(int weaponIndex)
        => animator?.SetInteger(WeaponIndexHash, weaponIndex);

    [Rpc(SendTo.Server)]
    private void SyncWeaponAcquiredServerRpc(int slot)
    {
        animator?.SetBool(HasWeaponHash, true);
        animator?.SetInteger(WeaponIndexHash, slot);
    }

    // ─── API pubblica chiamata da PlayerFaintHandler (su TUTTI i client) ─────

    /// <summary>
    /// Chiamato da PlayerFaintHandler.OnIsFaintedChanged su tutti i client.
    /// Usa SetTrigger diretto (non NetworkAnimator) perché la sincronizzazione
    /// è già gestita dalla NetworkVariable _isFainted.
    /// </summary>
    public void TriggerFaintDirect(bool fainted)
    {
        if (animator == null) return;

        if (fainted)
            animator.SetTrigger(FaintHash);
        else
            animator.SetTrigger(GetUpHash);
    }
}
