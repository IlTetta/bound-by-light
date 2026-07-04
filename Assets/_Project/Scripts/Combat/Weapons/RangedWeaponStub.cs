using UnityEngine;

public sealed class RangedWeaponStub : MonoBehaviour, IRangedWeapon
{
    [SerializeField] private float cooldown = 0.8f;
    private float _cooldownTimer;

    public bool CanFire => _cooldownTimer <= 0f;

    private void Update() {
        if (_cooldownTimer > 0f)
            _cooldownTimer -= Time.deltaTime;
    }

    public bool TryFire(Vector2 origin, Vector2 targetPosition) {
        if (!CanFire)
            return false;

        _cooldownTimer = cooldown;

        Debug.DrawLine(origin, targetPosition, Color.yellow, 0.12f);

        return true;
    }
}
