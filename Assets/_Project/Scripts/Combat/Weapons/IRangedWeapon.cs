using UnityEngine;

public interface IRangedWeapon
{
    bool CanFire { get; }
    bool TryFire(Vector2 origin, Vector2 targetPosition);
}
