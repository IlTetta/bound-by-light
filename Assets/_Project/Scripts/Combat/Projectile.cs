using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class Projectile : NetworkBehaviour
{
    [Header("Movement")]
    public float Speed = 20f;
    public float Acceleration = 0f;
    public float MaxSpeed = 50f;

    [Header("Orientation")]
    public bool FaceMovement = true;

    [Header("Safety & Lifecycle")]
    public float InitialInvulnerabilityDuration = 0.05f;
    public float LifeTime = 5f; // Auto-distruzione/ritorno al pool dopo X secondi
    public bool DamageOwner = false;

    [Header("Damage")]
    public int Damage = 10;
    private int _damage;

    protected Vector3 _direction;
    protected float _currentSpeed;
    protected ulong _ownerId;
    protected bool _isInitialized = false;
    protected float _spawnTime;

    private Rigidbody2D _rb;
    private Collider2D _collider;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _collider = GetComponent<Collider2D>();
        // Importante: il proiettile deve essere cinematico per un controllo totale
        if (_rb != null)
        {
            _rb.bodyType = RigidbodyType2D.Kinematic;
            // NECESSARIO: senza questo flag, Kinematic vs Kinematic non genera OnTriggerEnter2D
            _rb.useFullKinematicContacts = true;
        }
    }

    /// <summary>
    /// Chiamato dal WeaponProjectile (Server) per configurare il colpo
    /// </summary>
    public void Initialize(Vector3 direction)
    {
        _direction = direction.normalized;
        _currentSpeed = Speed;
        _spawnTime = Time.time;
        _isInitialized = true;

        // Reset dei componenti per il riciclo dal pool
        if (_collider != null) _collider.enabled = true;

        // Se deve guardare nella direzione del movimento
        if (FaceMovement)
        {
            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        // Avvia il timer per il ritorno al pool se non colpisce nulla
        StartCoroutine(LifeTimeRoutine());
        StartCoroutine(InitialInvulnerabilityRoutine());
        _damage = Damage;
    }

    public void SetDamage(int damage)
    {
        _damage = damage;
    }

    /// <summary>
    /// Passa il NetworkObjectId (non il ClientId) di chi ha sparato,
    /// così l'host (clientId=0) non colpisce se stesso ma può colpire i nemici (anch'essi clientId=0).
    /// </summary>
    public void SetOwner(ulong ownerNetworkObjectId)
    {
        _ownerId = ownerNetworkObjectId;
    }

    /// <summary>
    /// Se true, il proiettile ignora i colpi su altri nemici (IEnemyEntity).
    /// Settato da NetworkProjectileWeapon in base a chi spara.
    /// </summary>
    public void SetFiredByEnemy(bool firedByEnemy)
    {
        _firedByEnemy = firedByEnemy;
    }
    private bool _firedByEnemy = false;

    void FixedUpdate()
    {
        if (!IsServer || !_isInitialized) return;

        // Gestione Accelerazione
        if (Acceleration != 0)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, MaxSpeed, Acceleration * Time.fixedDeltaTime);
        }

        // Movimento Cinematico pulito (evita il tunneling)
        Vector3 nextPosition = transform.position + (_direction * _currentSpeed * Time.fixedDeltaTime);
        _rb.MovePosition(nextPosition);
    }

    private IEnumerator InitialInvulnerabilityRoutine()
    {
        if (_collider == null) yield break;

        _collider.enabled = false;
        yield return new WaitForSeconds(InitialInvulnerabilityDuration);
        _collider.enabled = true;
    }

    private IEnumerator LifeTimeRoutine()
    {
        yield return new WaitForSeconds(LifeTime);
        var netObj = GetComponent<NetworkObject>();
        if (IsServer && netObj != null && netObj.IsSpawned)
            OnDeath();
    }

    /// <summary>
    /// Logica di collisione (Gestita dal Server)
    /// </summary>
    void OnTriggerEnter2D(Collider2D other)
    {
        // Solo il Server gestisce la logica dei danni
        if (!IsServer) return;

        // 1. Cerca il NetworkObject nella gerarchia (il collider può essere su un child)
        var targetNetworkObj = other.GetComponentInParent<NetworkObject>();

        // 2. Controllo DamageOwner: evita di colpire chi ha sparato.
        //    Confronta NetworkObjectId (univoco per oggetto) NON OwnerClientId
        //    (host e nemici hanno entrambi OwnerClientId=0, causando falsi positivi)
        if (!DamageOwner && targetNetworkObj != null && targetNetworkObj.NetworkObjectId == _ownerId)
            return;

        // 3. I proiettili dei nemici non colpiscono altri nemici
        if (_firedByEnemy && other.GetComponentInParent<IEnemyEntity>() != null)
            return;

        // 4. Cerco il componente Health sulla vittima (GetComponentInParent per hitbox su child objects)
        HealthNetwork health = other.GetComponentInParent<HealthNetwork>();
        if (health != null)
        {
            Vector2 knockbackForce = _direction * 5f;
            health.ApplyDamageServer(_damage, knockbackForce, _ownerId);
        }

        // Il proiettile muore dopo l'impatto
        OnDeath();
    }

    protected virtual void OnDeath()
    {
        _isInitialized = false;

        // Esegui StopAt logico
        if (_rb != null) _rb.linearVelocity = Vector2.zero;
        if (_collider != null) _collider.enabled = false;

        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
            netObj.Despawn(true);
    }
}