using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ponte tra HealthNetwork (valore reale in rete) e HealthDisplay (UI).
/// Si sottoscrive a CurrentHealth.OnValueChanged e aggiorna la barra vita.
/// </summary>
public class PlayerHealth : NetworkBehaviour
{
    public HealthDisplay healthDisplay;

    private HealthNetwork _healthNetwork;

    public override void OnNetworkSpawn()
    {
        _healthNetwork = GetComponent<HealthNetwork>();
        if (_healthNetwork == null || healthDisplay == null) return;

        healthDisplay.SetMaxHealth(_healthNetwork.MaxHealth);
        healthDisplay.SetHealth(_healthNetwork.CurrentHealth.Value);

        _healthNetwork.CurrentHealth.OnValueChanged += OnHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (_healthNetwork != null)
            _healthNetwork.CurrentHealth.OnValueChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int oldValue, int newValue)
    {
        healthDisplay.SetHealth(newValue);
    }
}
