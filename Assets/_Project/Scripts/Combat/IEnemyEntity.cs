using UnityEngine;

public interface IEnemyEntity
{
    bool IsFlying { get; }

    int CurrencyReward { get; }

    float EnergyReward { get; }

    bool TakeDamage(float amount);
}
