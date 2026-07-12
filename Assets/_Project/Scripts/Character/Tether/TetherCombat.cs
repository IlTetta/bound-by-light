using FMODUnity;
using MyGame.Core;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;



/// <summary>
/// Modulo Combat: Gestisce l'offesa tramite la corda e l'attivazione della Bond Ability.
/// </summary>
[RequireComponent(typeof(TetherManager))]
public class TetherCombat : NetworkBehaviour
{
    [Header("Tether Attack Settings")]
    [SerializeField] private float damagePerSecond = 20f; 
    [SerializeField] private float detectionRadius = 0.5f; 
    [SerializeField] private LayerMask enemyLayer;

    [Header("Bond Ability – Ultimate Sweep")]
    [Tooltip("Emitter FMOD riprodotto all'attivazione della Bond Ability (SUPER).")]
    [SerializeField] private StudioEventEmitter superSfxEmitter;
    [SerializeField] private GameObject ultimateProjectilePrefab;
    [SerializeField] private int   ultimateDamage         = 30;
    [SerializeField] private int   ultimateProjectileCount = 24;   // totale su 2 giri
    [SerializeField] private float ultimateSweepDuration  = 1.5f;
    [SerializeField] private GameObject bondExplosionVfx;
    [Tooltip("Quante volte il VFX/SFX si ripete durante la durata della super (0 = solo all'inizio).")]
    [SerializeField] private int vfxRepeatCount = 3;

    private TetherManager _manager;
    private bool _isUltimateActive;

    // Buffer statico per OverlapCapsuleNonAlloc — evita allocazioni GC ogni frame
    private static readonly Collider[] _overlapBuffer = new Collider[32];
    // Collezioni riusabili per il damage loop — evita new HashSet/List ogni Update
    private readonly HashSet<IEnemyEntity> _hitThisFrame = new HashSet<IEnemyEntity>();
    private readonly List<IEnemyEntity>    _toRemove     = new List<IEnemyEntity>();

    // Accumula il danno frazionario per nemico.
    // HealthNetwork accetta solo int: senza accumulatore ogni frame passa
    // ~0.33 di danno che viene troncato a 0 e scartato da "if (amount <= 0) return".
    // Quando l'accumulatore raggiunge o supera 1, viene estratta la parte intera
    // e applicata come danno reale, mentre il resto rimane in accumulo.
    private readonly Dictionary<IEnemyEntity, float> _damageAccumulator
        = new Dictionary<IEnemyEntity, float>();

    public override void OnNetworkSpawn()
    {
        _manager = GetComponent<TetherManager>();

        // Sottoscrizione all'evento del GameManager per feedback visivo/audio [cite: 53]
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnBondEnergyFull += HandleBondEnergyFull;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnBondEnergyFull -= HandleBondEnergyFull;
    }

    private void Update()
    {
        // La logica di danno viene eseguita solo sul Server per autorit� [cite: 166]
        if (!IsServer) return;

        Transform p1 = _manager.GetTransformA();
        Transform p2 = _manager.GetTransformB();
        if (p1 == null || p2 == null) return;

        ProcessTetherDamage(p1, p2);
    }

    private void LateUpdate()
    {
        // Il Tether è owned dal server, quindi IsOwner = false per il client.
        // Controlliamo invece che il client locale abbia un player in scena.
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject == null) return;
        if (GameManager.Instance == null) return;
        if (GameManager.Instance.SharedBondEnergy.Value < 100f) return;
        if (!GameManager.Instance.BondAbilityUnlocked.Value) return;
        if (_isUltimateActive) return;

        if (Input.GetKeyDown(KeyCode.Space))
            ExecuteBondAbilityServerRpc();
    }

    /// <summary>
    /// Rileva i nemici lungo la corda e applica danni.
    /// </summary>
    private void ProcessTetherDamage(Transform p1, Transform p2)
    {
        // Capsula 3D orientata lungo la corda (piano XZ, Y fissa)
        Vector3 start3 = p1.position;
        Vector3 end3   = p2.position;

        // NonAlloc: riusa il buffer statico, nessuna allocazione GC a frame
        int hitCount = Physics.OverlapCapsuleNonAlloc(start3, end3, detectionRadius, _overlapBuffer, enemyLayer);

        // Tiene traccia di quali nemici sono stati colpiti in questo frame,
        // per azzerare l'accumulatore di chi non è più a contatto con la corda.
        _hitThisFrame.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            var col = _overlapBuffer[i];
            IEnemyEntity enemy = col.GetComponentInParent<IEnemyEntity>();
            if (enemy == null) continue;
            if (enemy.IsFlying) continue;

            _hitThisFrame.Add(enemy);

            if (!_damageAccumulator.ContainsKey(enemy))
                _damageAccumulator[enemy] = 0f;

            _damageAccumulator[enemy] += damagePerSecond * Time.deltaTime;

            // Applica solo la parte intera per rispettare la firma int di HealthNetwork
            int damageToApply = Mathf.FloorToInt(_damageAccumulator[enemy]);
            if (damageToApply < 1) continue;

            _damageAccumulator[enemy] -= damageToApply;

            bool isDead = enemy.TakeDamage(damageToApply);

            if (isDead)
                _damageAccumulator.Remove(enemy);
            // I reward (currency + energia) sono gestiti da EnemyRewardOnDeath
            // tramite l'evento HealthNetwork.OnServerDeath, per evitare doppio conteggio.
        }

        // Pulisce l'accumulatore per i nemici che non sono più a contatto.
        // Riusa _toRemove (campo) — nessuna new List a frame.
        _toRemove.Clear();
        foreach (var key in _damageAccumulator.Keys)
            if (!_hitThisFrame.Contains(key))
                _toRemove.Add(key);
        foreach (var key in _toRemove)
            _damageAccumulator.Remove(key);
    }

     /// <summary>
    /// Esegue l'attacco ad area (AoE) sincronizzato su entrambi i giocatori.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ExecuteBondAbilityServerRpc()
    {
        if (_isUltimateActive) return;

        Transform p1 = _manager.GetTransformA();
        Transform p2 = _manager.GetTransformB();
        if (p1 == null || p2 == null) return;

        Vector3 center = (p1.position + p2.position) * 0.5f;

        if (GameManager.Instance != null)
            GameManager.Instance.SharedBondEnergy.Value = 0f;

        StartCoroutine(UltimateSweepCoroutine(center));
        PlayBondVfxRpc();
    }

    private System.Collections.IEnumerator UltimateSweepCoroutine(Vector3 center)
    {
        _isUltimateActive = true;

        // 720° totali (2 giri completi) divisi in ultimateProjectileCount proiettili
        float angleStep = 720f / ultimateProjectileCount;
        float interval  = ultimateSweepDuration / ultimateProjectileCount;

        // Precalcola gli indici a cui far scattare il VFX/SFX aggiuntivo.
        // Il primo VFX è già stato sparato in ExecuteBondAbilityServerRpc (index 0).
        // Con vfxRepeatCount = 3 e 24 proiettili → indici 6, 12, 18.
        var vfxIndices = new System.Collections.Generic.HashSet<int>();
        if (vfxRepeatCount > 0)
        {
            float vfxStep = (float)ultimateProjectileCount / (vfxRepeatCount + 1);
            for (int v = 1; v <= vfxRepeatCount; v++)
                vfxIndices.Add(Mathf.RoundToInt(vfxStep * v));
        }

        for (int i = 0; i < ultimateProjectileCount; i++)
        {
            if (vfxIndices.Contains(i))
                PlayBondVfxRpc();

            float angleDeg = i * angleStep;
            SpawnUltimateProjectile(center, angleDeg);
            yield return new WaitForSeconds(interval);
        }

        _isUltimateActive = false;
    }

    private void SpawnUltimateProjectile(Vector3 origin, float angleDeg)
    {
        if (ultimateProjectilePrefab == null) return;

        float rad = angleDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

        var go = Instantiate(ultimateProjectilePrefab, origin,
                             Quaternion.LookRotation(dir, Vector3.up));
        var netObj = go.GetComponent<NetworkObject>();
        if (netObj == null) { Destroy(go); return; }
        netObj.Spawn();

        var proj = go.GetComponent<Projectile3D>();
        if (proj == null) return;

        proj.SetOwner(NetworkObject.NetworkObjectId);
        proj.SetFiredByEnemy(false);
        proj.SetDamage(ultimateDamage);
        proj.Initialize(dir);
    }

    [Rpc(SendTo.Everyone)]
    private void PlayBondVfxRpc()
    {
        Transform p1 = _manager.GetTransformA();
        Transform p2 = _manager.GetTransformB();

        if (bondExplosionVfx != null)
        {
            if (p1) Instantiate(bondExplosionVfx, p1.position, Quaternion.identity);
            if (p2) Instantiate(bondExplosionVfx, p2.position, Quaternion.identity);
        }

        // SFX SUPER
        superSfxEmitter?.Play();

        Debug.Log("BOND ABILITY ACTIVATED: VFX PLAYING");
    }

    private void HandleBondEnergyFull()
    {
        // Feedback locale quando la barra � piena (es. vibrazione o cambio colore corda) [cite: 53]
        Debug.Log("Il legame brilla! Energia pronta.");
    }
}
