using System;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Controller del player: gestisce input movimento, mira e due slot arma.
///
/// Setup prefab:
///   - Slot 0 (arma principale):  weapons[0] + ammos[0], stessa WeaponData assegnata in entrambi
///   - Slot 1 (arma secondaria):  weapons[1] + ammos[1], stessa WeaponData assegnata in entrambi
///
/// Tasti:
///   WASD/Arrows  — movimento
///   LMB (Fire1)  — sparo arma attiva
///   RMB          — interazione (pickup armi, NPC, ecc.)
///   R            — ricarica manuale
///   V            — cambia arma (alterna slot 0 ↔ 1, solo se entrambe acquisite)
/// </summary>
public sealed class PlayerController : NetworkBehaviour
{
    // ─── Weapon Slots ──────────────────────────────────────────────────────────

    [Header("Weapon Slot 0 (principale)")]
    [SerializeField] private NetworkProjectileWeapon weapon0;
    [SerializeField] private WeaponAmmo ammo0;

    [Header("Weapon Slot 1 (secondaria)")]
    [SerializeField] private NetworkProjectileWeapon weapon1;
    [SerializeField] private WeaponAmmo ammo1;

    // ─── Door Interaction ─────────────────────────────────────────────────────

    [Header("Interaction")]
    [Tooltip("Raggio entro cui E apre una porta / RMB raccoglie un'arma.")]
    [SerializeField] private float interactRadius = 1.5f;
    [Tooltip("Layer su cui sono presenti le RoomDoor.")]
    [SerializeField] private LayerMask doorLayer;
    [Tooltip("Layer su cui sono presenti i WeaponPickup.")]
    [SerializeField] private LayerMask pickupLayer;

    // ─── Run ───────────────────────────────────────────────────────────────────

    [Header("Run (optional)")]
    [SerializeField] private bool enableRun = true;
    [SerializeField] private float runMultiplier = 1.35f;

    // ─── Runtime ───────────────────────────────────────────────────────────────

    private PlayerMovementMotor3D _motor3D;
    private PlayerMovementMotor2D _motor2D;
    private IAimProvider _aimProvider;
    private PlayerAnimatorHandler _animatorHandler;
    private PlayerFaintHandler _faintHandler;

    // Indice arma attiva (0 o 1), locale al client owner
    private int _activeWeaponIndex = 0;

    // Armi acquisite tramite pickup
    private readonly bool[] _weaponAcquired = new bool[2];

    // Bitmask sincronizzato: bit 0 = slot 0, bit 1 = slot 1.
    // Owner scrive, server legge → WeaponPickup può rifiutare pickup duplicati.
    private readonly NetworkVariable<int> _weaponMask = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Cooldown client-side per throttlare gli RPC
    private float[] _localFireCooldowns = new float[2];

    // ─── Proprietà pubbliche ───────────────────────────────────────────────────

    public int ActiveWeaponIndex => _activeWeaponIndex;
    public NetworkProjectileWeapon ActiveWeapon => _activeWeaponIndex == 0 ? weapon0 : weapon1;
    public WeaponAmmo ActiveAmmo => _activeWeaponIndex == 0 ? ammo0 : ammo1;
    public bool HasAnyWeapon => _weaponAcquired[0] || _weaponAcquired[1];

    /// <summary>Array dei flag armi acquisite (slot 0 e 1). Read-only.</summary>
    public bool[] WeaponsAcquired => _weaponAcquired;

    /// <summary>Restituisce il componente WeaponAmmo per lo slot indicato.</summary>
    public WeaponAmmo GetAmmo(int slot) => slot == 0 ? ammo0 : ammo1;

    /// <summary>Fired sull'owner locale quando cambia arma. Parametro = nuovo indice (0 o 1).</summary>
    public event Action<int> OnWeaponSwitched;

    /// <summary>Fired sull'owner locale quando raccoglie un'arma. Parametro = slot (0 o 1).</summary>
    public event Action<int> OnWeaponAcquired;

    /// <summary>Fired sull'owner locale quando viene sparato un colpo.</summary>
    public event Action OnShot;

    /// <summary>Fired sull'owner locale quando viene richiesta una ricarica.</summary>
    public event Action OnReloadRequested;

    // ─── API pubblica ──────────────────────────────────────────────────────────

    /// <summary>
    /// Chiamato da WeaponPickup (via RPC) per assegnare un'arma al player.
    /// </summary>
    public void AcquireWeapon(int slot)
    {
        if (slot < 0 || slot > 1) return;
        if (_weaponAcquired[slot]) return;

        _weaponAcquired[slot] = true;
        _activeWeaponIndex    = slot;
        if (IsOwner) _weaponMask.Value |= (1 << slot); // notifica il server
        OnWeaponAcquired?.Invoke(slot);
        OnWeaponSwitched?.Invoke(slot);
        Debug.Log($"[PlayerController] Arma acquisita: slot {slot}");
    }

    /// <summary>Usato dal server (WeaponPickup) per sapere se il player ha già quell'arma.</summary>
    public bool HasWeaponSlot(int slot) => (_weaponMask.Value & (1 << slot)) != 0;

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _motor3D         = GetComponent<PlayerMovementMotor3D>();
        _motor2D         = GetComponent<PlayerMovementMotor2D>();
        _aimProvider     = GetComponent<MouseAimProvider>();
        _animatorHandler = GetComponent<PlayerAnimatorHandler>();
        _faintHandler    = GetComponent<PlayerFaintHandler>();
    }

    public override void OnNetworkSpawn()
    {
        enabled = IsOwner;
        Debug.Log($"[PlayerController] OnNetworkSpawn — IsOwner={IsOwner}, enabled={enabled}");
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (PauseController.CurrentGameState == GameState.Paused) return;

        // Aggiorna cooldown locali
        for (int i = 0; i < 2; i++)
            if (_localFireCooldowns[i] > 0f)
                _localFireCooldowns[i] -= Time.deltaTime;

        HandleWeaponSwitchInput();
        HandleWeaponInput();
        HandleDoorInteraction();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        float x = Input.GetAxisRaw("Horizontal");
        float y = Input.GetAxisRaw("Vertical");
        Vector2 input = new Vector2(x, y);

        bool run = enableRun && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

        if (_motor3D != null)
            _motor3D.SetMoveInput(input, run, runMultiplier);
        else if (_motor2D != null)
            _motor2D.SetMoveInput(input, run, runMultiplier);
    }

    // ─── Weapon Input ─────────────────────────────────────────────────────────

    private void HandleWeaponSwitchInput()
    {
        if (!Input.GetKeyDown(KeyCode.V)) return;
        if (_faintHandler != null && _faintHandler.IsReviving) return;
        // Permetti lo switch solo se entrambe le armi sono state acquisite
        if (!_weaponAcquired[0] || !_weaponAcquired[1]) return;

        _activeWeaponIndex = 1 - _activeWeaponIndex; // alterna 0 ↔ 1
        OnWeaponSwitched?.Invoke(_activeWeaponIndex);
        Debug.Log($"[PlayerController] Arma attiva: slot {_activeWeaponIndex}");
    }

    /// <summary>
    /// Ripristina lo stato armi dopo una reconnessione (chiamato lato server da PlayerSpawnServer).
    /// Setta i flag server-side e notifica l'owner via ClientRpc per aggiornare la UI.
    /// </summary>
    public void RestoreWeaponStateServer(bool slot0, bool slot1, int activeIndex)
    {
        if (!IsServer) return;

        // Imposta ESATTAMENTE lo stato dello snapshot (anche a false: il player
        // può respawnare senza armi se al checkpoint non ne aveva).
        _weaponAcquired[0] = slot0;
        _weaponAcquired[1] = slot1;

        // Se l'arma "attiva" dello snapshot non è posseduta, ripiega su una posseduta.
        if (activeIndex < 0 || activeIndex > 1 || !_weaponAcquired[activeIndex])
            activeIndex = _weaponAcquired[0] ? 0 : (_weaponAcquired[1] ? 1 : 0);
        _activeWeaponIndex = activeIndex;

        // Allinea i modelli visivi (autoritativo su tutti i client): mostra solo se
        // c'è davvero un'arma posseduta, altrimenti nasconde.
        var visual = GetComponent<WeaponVisualHandler>();
        if (visual != null) visual.RestoreServer(slot0, slot1, activeIndex);

        SyncWeaponStateClientRpc(slot0, slot1, activeIndex);
    }

    [ClientRpc]
    private void SyncWeaponStateClientRpc(bool slot0, bool slot1, int activeIndex)
    {
        if (!IsOwner) return;

        // Notifica l'acquisizione solo per gli slot appena diventati posseduti
        if (slot0 && !_weaponAcquired[0]) OnWeaponAcquired?.Invoke(0);
        if (slot1 && !_weaponAcquired[1]) OnWeaponAcquired?.Invoke(1);

        // Aggiorna lo stato locale esattamente allo snapshot
        _weaponAcquired[0] = slot0;
        _weaponAcquired[1] = slot1;
        _activeWeaponIndex = activeIndex;

        // Mostra il modello dell'arma (via OnWeaponSwitched → WeaponVisualHandler)
        // SOLO se il player possiede almeno un'arma. Senza questo guard il modello
        // appariva anche a mani vuote pur non potendo sparare.
        if (slot0 || slot1)
            OnWeaponSwitched?.Invoke(activeIndex);
    }

    /// <summary>Forza lo switch all'arma indicata (chiamato dall'inventario).</summary>
    public void SwitchToWeapon(int slot)
    {
        if (slot < 0 || slot > 1) return;
        if (!_weaponAcquired[slot]) return;
        if (_activeWeaponIndex == slot) return;
        _activeWeaponIndex = slot;
        OnWeaponSwitched?.Invoke(_activeWeaponIndex);
        Debug.Log($"[PlayerController] Switch da inventario: slot {_activeWeaponIndex}");
    }

    private void HandleWeaponInput()
    {
        // Non sparare se non si ha ancora un'arma
        if (!_weaponAcquired[_activeWeaponIndex]) return;

        // Non sparare mentre è in corso l'animazione di ricarica o la rianimazione del compagno
        if (_animatorHandler != null && _animatorHandler.IsReloading) return;
        if (_faintHandler != null && _faintHandler.IsReviving) return;

        NetworkProjectileWeapon activeWeapon = ActiveWeapon;
        WeaponAmmo activeAmmo               = ActiveAmmo;

        if (activeWeapon == null) return;

        // ── Sparo continuo con LMB ─────────────────────────────────────────
        if (Input.GetButton("Fire1"))
        {
            bool cooldownReady = _localFireCooldowns[_activeWeaponIndex] <= 0f;
            bool hasAmmo       = activeAmmo == null || activeAmmo.CurrentAmmoLoaded.Value > 0;

            if (cooldownReady && hasAmmo && _aimProvider != null)
            {
                // Imposta il cooldown locale prendendo il valore dall'arma
                _localFireCooldowns[_activeWeaponIndex] = activeWeapon.FireCooldown;

                Vector2 dir = _aimProvider.AimDirection;
                RequestShootServerRpc(dir, _activeWeaponIndex);
                OnShot?.Invoke();
            }
            else if (activeAmmo != null && activeAmmo.CurrentAmmoLoaded.Value <= 0)
            {
                // Auto-reload se il caricatore è vuoto
                activeAmmo.RequestReloadServerRpc();
                activeWeapon.BeginReloadServerRpc();
                OnReloadRequested?.Invoke();
            }
        }

        // ── Reload manuale con R ───────────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.R) && activeAmmo != null)
        {
            activeAmmo.RequestReloadServerRpc();
            activeWeapon.BeginReloadServerRpc();
            OnReloadRequested?.Invoke();
        }
    }

    // ─── Door Interaction ─────────────────────────────────────────────────────

    private void HandleDoorInteraction()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;
        if (_faintHandler != null && _faintHandler.IsReviving) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, interactRadius, doorLayer);
        foreach (var col in hits)
        {
            RoomDoor door = col.GetComponentInParent<RoomDoor>();
            if (door != null && door.IsUnlocked.Value && !door.IsOpen.Value)
            {
                door.RequestOpenServerRpc();
                return;
            }
        }
    }

    // ─── Server RPCs ─────────────────────────────────────────────────────────

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestShootServerRpc(Vector2 aimDirection, int weaponIndex)
    {
        NetworkProjectileWeapon weapon = weaponIndex == 0 ? weapon0 : weapon1;
        WeaponAmmo ammo                = weaponIndex == 0 ? ammo0   : ammo1;

        if (weapon == null) return;

        // Controllo server-side delle munizioni (fonte di verità)
        if (ammo != null && ammo.CurrentAmmoLoaded.Value <= 0) return;

        bool fired = weapon.TryFire(transform.position, aimDirection);

        if (fired && ammo != null)
            ammo.ConsumeAmmo(1);
    }
}
