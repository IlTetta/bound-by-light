using Unity.Netcode;
using UnityEngine;


/// <summary>
/// Applica una forza correttiva ai due player quando superano maxDistance.
///
/// Comportamento:
///   - Entro maxDistance: nessuna forza, movimento libero
///   - Oltre maxDistance: forza proporzionale all'eccesso * stiffness
///     applicata tramite AddImpact() sul PlayerMovementMotor2D di ciascun player
///
/// Stiffness alta  → quasi un muro (il movimento volontario non supera maxDist)
/// Stiffness bassa → corda elastica (il knockback la sfonda momentaneamente)
///
/// Gira solo sul server, che invia la forza ai client owner via ClientRpc.
/// </summary>
public class TetherPhysics : NetworkBehaviour
{
    [Header("Tether Settings")]
    [SerializeField] private float maxDistance = 7f;
    [Tooltip("Rigidità della corda. Alto = quasi un muro. Basso = elastica.\n" +
             "Valori consigliati: 15-30 per elastica, 60-100 per rigida.")]
    [SerializeField] private float stiffness = 25f;

    private TetherManager _manager;

    // Cache per evitare GetComponent<NetworkObject> e new ulong[] ad ogni FixedUpdate
    private NetworkObject _netObjA;
    private NetworkObject _netObjB;
    private readonly ulong[] _targetIdsA = new ulong[1];
    private readonly ulong[] _targetIdsB = new ulong[1];

    public override void OnNetworkSpawn()
    {
        _manager = GetComponent<TetherManager>();
        enabled = IsServer; // Solo il server calcola e invia le forze
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        Transform tA = _manager.GetTransformA();
        Transform tB = _manager.GetTransformB();
        if (tA == null || tB == null) return;

        // Popola la cache una sola volta (i player non cambiano durante la sessione)
        if (_netObjA == null && tA != null)
        {
            _netObjA = tA.GetComponent<NetworkObject>();
            if (_netObjA != null) _targetIdsA[0] = _netObjA.OwnerClientId;
        }
        if (_netObjB == null && tB != null)
        {
            _netObjB = tB.GetComponent<NetworkObject>();
            if (_netObjB != null) _targetIdsB[0] = _netObjB.OwnerClientId;
        }

        // Distanza sul piano XZ (3D isometrico — ignora Y)
        Vector3 posA3 = tA.position; posA3.y = 0f;
        Vector3 posB3 = tB.position; posB3.y = 0f;
        float currentDist = Vector3.Distance(posA3, posB3);

        if (currentDist <= maxDistance)
        {
            SendTetherForce(_netObjA, _targetIdsA, Vector2.zero);
            SendTetherForce(_netObjB, _targetIdsB, Vector2.zero);
            return;
        }

        float   excess    = currentDist - maxDistance;
        Vector3 dir3AtoB  = (posB3 - posA3).normalized;
        Vector2 dirAtoB   = new Vector2(dir3AtoB.x, dir3AtoB.z);

        Vector2 forceA = dirAtoB * excess * stiffness;
        Vector2 forceB = -forceA;

        SendTetherForce(_netObjA, _targetIdsA, forceA);
        SendTetherForce(_netObjB, _targetIdsB, forceB);
    }

    private void SendTetherForce(NetworkObject netObj, ulong[] targetIds, Vector2 force)
    {
        if (netObj == null) return;

        ApplyTetherForceClientRpc(force,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = targetIds }
            });
    }

    [ClientRpc]
    private void ApplyTetherForceClientRpc(Vector2 force, ClientRpcParams _ = default)
    {
        var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        // Supporta sia motore 2D che 3D
        var motor3D = localPlayer.GetComponent<PlayerMovementMotor3D>();
        if (motor3D != null) { motor3D.SetTetherForce(force); return; }

        var motor2D = localPlayer.GetComponent<PlayerMovementMotor2D>();
        if (motor2D != null) motor2D.SetTetherForce(force);
    }

    public void SetMaxDistance(float newMaxDist)
    {
        if (IsServer)
            maxDistance = newMaxDist;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        TetherManager mgr = GetComponent<TetherManager>();
        if (mgr == null) return;

        Transform tA = mgr.GetTransformA();
        Transform tB = mgr.GetTransformB();

        if (tA != null && tB != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(tA.position, tB.position);
        }

        if (tA != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(tA.position, maxDistance);
        }
    }
#endif
}