using Unity.Netcode;
using UnityEngine;
using MyGame.Core;

/// <summary>
/// Da aggiungere su ogni prefab nemico accanto a HealthNetwork.
/// Quando il nemico muore (qualunque sia la causa: proiettile, corda, Bond Ability)
/// assegna currency ed energia al GameManager leggendo i valori da IEnemyEntity.
///
/// TetherCombat chiama già AddResources per le uccisioni via corda;
/// questo componente copre le uccisioni via proiettile e qualsiasi altra fonte futura.
/// Per evitare il doppio conteggio, TetherCombat NON chiama più AddResources direttamente:
/// ci pensa sempre questo componente tramite l'evento OnServerDeath.
/// </summary>
[RequireComponent(typeof(HealthNetwork))]
public sealed class EnemyRewardOnDeath : NetworkBehaviour
{
    private HealthNetwork _health;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        _health = GetComponent<HealthNetwork>();
        if (_health != null)
            _health.OnServerDeath += HandleDeath;
    }

    public override void OnNetworkDespawn()
    {
        if (_health != null)
            _health.OnServerDeath -= HandleDeath;
    }

    private void HandleDeath()
    {
        if (GameManager.Instance == null) return;

        // Legge i reward dall'interfaccia IEnemyEntity già implementata nel brain.
        // Nessuna duplicazione di valori: sono definiti una sola volta nel brain/config.
        var entity = GetComponent<IEnemyEntity>();
        if (entity == null) return;

        GameManager.Instance.AddResources(entity.CurrencyReward, entity.EnergyReward);

        Debug.Log($"[EnemyRewardOnDeath] {gameObject.name} morto: " +
                  $"+{entity.CurrencyReward} currency, +{entity.EnergyReward} energia.");
    }
}
