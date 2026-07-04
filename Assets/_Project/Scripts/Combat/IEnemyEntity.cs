using UnityEngine;

public interface IEnemyEntity
{
    bool IsFlying { get; }

    bool IsDiruptor { get; }

    int CurrencyReward { get; }

    float EnergyReward { get; }

    bool TakeDamage(float amount);
}
