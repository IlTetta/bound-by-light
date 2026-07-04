using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Gestisce i modelli visivi delle armi sincronizzando lo stato visivo su tutti i client.
///
/// Setup prefab:
///   1. Aggiungi questo script al root del PlayerPrefab.
///   2. Nella gerarchia del prefab, espandi il mesh child fino al bone RightHand.
///   3. Trascina WeaponVisual_Rifle e WeaponVisual_Shotgun come figli di RightHand.
///   4. Aggiusta localPosition e localRotation direttamente sul Transform di ciascun visual
///      (visibile in edit mode senza Play Mode).
///   5. Assegna rifleVisual e shotgunVisual in questo Inspector.
/// </summary>
public class WeaponVisualHandler : NetworkBehaviour
{
    [Header("Weapon Visuals")]
    [Tooltip("GameObject figlio del bone RightHand con il modello del rifle. Inizialmente inattivo.")]
    [SerializeField] private GameObject rifleVisual;

    [Tooltip("GameObject figlio del bone RightHand con il modello dello shotgun. Inizialmente inattivo.")]
    [SerializeField] private GameObject shotgunVisual;

    // ─── Network State ────────────────────────────────────────────────────────

    // Bitmask: bit0 = rifle acquisito, bit1 = shotgun acquisito
    private readonly NetworkVariable<int> _acquiredMask = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Indice arma visibile corrente (-1 = nessuna)
    private readonly NetworkVariable<int> _shownIndex = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Nasconde i visual PRIMA di OnNetworkSpawn per evitare che Start()
        // (che gira un frame dopo) sovrascriva lo stato corretto impostato da OnNetworkSpawn.
        // Senza questo, al reconnect (dove _shownIndex > -1) il visual veniva mostrato
        // in OnNetworkSpawn e poi subito nascosto da Start() nel frame successivo.
        if (rifleVisual)   rifleVisual.SetActive(false);
        if (shotgunVisual) shotgunVisual.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        // Ascolta i cambiamenti su tutti i client per aggiornare i visuals
        _acquiredMask.OnValueChanged += OnAcquiredMaskChanged;
        _shownIndex.OnValueChanged   += OnShownIndexChanged;

        // Assicura stato iniziale corretto
        UpdateVisuals(_shownIndex.Value);

        // L'owner ascolta anche gli switch arma del PlayerController
        if (!IsOwner) return;

        var controller = GetComponent<PlayerController>();
        if (controller != null)
            controller.OnWeaponSwitched += HandleWeaponSwitched;
    }

    public override void OnNetworkDespawn()
    {
        _acquiredMask.OnValueChanged -= OnAcquiredMaskChanged;
        _shownIndex.OnValueChanged   -= OnShownIndexChanged;

        var controller = GetComponent<PlayerController>();
        if (controller != null)
            controller.OnWeaponSwitched -= HandleWeaponSwitched;
    }

    // ─── API pubblica (chiamata da WeaponPickup.GrantWeaponClientRpc) ─────────

    /// <summary>
    /// Chiamato sull'owner locale quando raccoglie un'arma.
    /// Comunica la variazione al server.
    /// </summary>
    public void AcquireWeapon(int slot)
    {
        AcquireWeaponServerRpc(slot);
    }

    // ─── Server RPCs ──────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void AcquireWeaponServerRpc(int slot)
    {
        int newMask = _acquiredMask.Value | (1 << slot);
        _acquiredMask.Value = newMask;
        _shownIndex.Value   = slot;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetShownIndexServerRpc(int index)
    {
        _shownIndex.Value = index;
    }

    /// <summary>
    /// Ripristina lo stato visivo delle armi dal server (respawn/checkpoint).
    /// Mostra il modello SOLO se l'arma attiva è effettivamente posseduta;
    /// altrimenti nasconde tutto (_shownIndex = -1). Server-only.
    /// </summary>
    public void RestoreServer(bool slot0, bool slot1, int activeIndex)
    {
        if (!IsServer) return;

        int mask = (slot0 ? 1 : 0) | (slot1 ? 1 << 1 : 0);
        _acquiredMask.Value = mask;

        bool activeOwned = activeIndex >= 0 && activeIndex <= 1
                           && (mask & (1 << activeIndex)) != 0;
        _shownIndex.Value = activeOwned ? activeIndex : -1;
    }

    // ─── Handler eventi ───────────────────────────────────────────────────────

    private void HandleWeaponSwitched(int weaponIndex)
    {
        SetShownIndexServerRpc(weaponIndex);
    }

    private void OnAcquiredMaskChanged(int previous, int current)
    {
        // Niente da fare qui — _shownIndex gestisce già quale mostrare
    }

    private void OnShownIndexChanged(int previous, int current)
    {
        UpdateVisuals(current);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void UpdateVisuals(int shownIndex)
    {
        if (rifleVisual)   rifleVisual.SetActive(shownIndex == 0);
        if (shotgunVisual) shotgunVisual.SetActive(shownIndex == 1);
    }
}
