using Unity.Netcode;
using UnityEngine;
using TMPro;
using FMODUnity;

/// <summary>
/// Aggiorna la UI munizioni leggendo direttamente da WeaponAmmo (NetworkVariable).
/// I suoni di sparo e ricarica vengono riprodotti tramite gli eventi di PlayerController,
/// così sono sincronizzati con la vera logica di gioco e non con l'input diretto.
/// </summary>
public class AmmoDisplay : NetworkBehaviour
{
    [SerializeField] public TMP_Text ammoDisplay;

    [Tooltip("Root del pannello ammo (la barra intera, non solo il testo). " +
             "Assegna il GameObject padre della barra nel prefab. " +
             "Viene nascosto automaticamente sui player remoti.")]
    [SerializeField] private GameObject ammoBarRoot;

    [SerializeField] private StudioEventEmitter shotsEmitter;
    [SerializeField] private StudioEventEmitter reloadEmitter;

    private PlayerController _controller;
    private WeaponAmmo _trackedAmmo;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // Nasconde l'intera barra per il player remoto:
            // usa ammoBarRoot se assegnato, altrimenti si limita al testo.
            if (ammoBarRoot != null)
                ammoBarRoot.SetActive(false);
            else if (ammoDisplay != null)
                ammoDisplay.gameObject.SetActive(false);
            return;
        }

        _controller = GetComponent<PlayerController>();
        if (_controller == null) return;

        _controller.OnShot            += PlayShotSound;
        _controller.OnReloadRequested  += PlayReloadSound;
        _controller.OnWeaponSwitched   += TrackWeapon;

        // Traccia l'arma attiva al momento dello spawn
        TrackWeapon(_controller.ActiveWeaponIndex);
    }

    public override void OnNetworkDespawn()
    {
        if (_controller != null)
        {
            _controller.OnShot            -= PlayShotSound;
            _controller.OnReloadRequested  -= PlayReloadSound;
            _controller.OnWeaponSwitched   -= TrackWeapon;
        }

        UntrackCurrentAmmo();
    }

    private void TrackWeapon(int weaponIndex)
    {
        UntrackCurrentAmmo();

        _trackedAmmo = _controller.ActiveAmmo;
        if (_trackedAmmo == null) return;

        _trackedAmmo.CurrentAmmoLoaded.OnValueChanged    += OnAmmoChanged;
        _trackedAmmo.CurrentAmmoAvailable.OnValueChanged += OnAmmoChanged;
        UpdateDisplay(_trackedAmmo.CurrentAmmoLoaded.Value, _trackedAmmo.CurrentAmmoAvailable.Value);
    }

    private void UntrackCurrentAmmo()
    {
        if (_trackedAmmo != null)
        {
            _trackedAmmo.CurrentAmmoLoaded.OnValueChanged    -= OnAmmoChanged;
            _trackedAmmo.CurrentAmmoAvailable.OnValueChanged -= OnAmmoChanged;
        }
        _trackedAmmo = null;
    }

    private void OnAmmoChanged(int oldValue, int newValue)
    {
        if (_trackedAmmo != null)
            UpdateDisplay(_trackedAmmo.CurrentAmmoLoaded.Value, _trackedAmmo.CurrentAmmoAvailable.Value);
    }

    private void UpdateDisplay(int loaded, int available)
    {
        if (ammoDisplay != null)
            ammoDisplay.text = loaded + " / " + available;
    }

    private void PlayShotSound()   => shotsEmitter?.Play();
    private void PlayReloadSound() => reloadEmitter?.Play();
}
