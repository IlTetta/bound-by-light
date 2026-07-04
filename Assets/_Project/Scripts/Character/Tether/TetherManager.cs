using Unity.Netcode;
using UnityEngine;

public class TetherManager : NetworkBehaviour
{
    public TetherPhysics PhysicsModule { get; private set; }
    public TetherResize ResizeModule { get; private set; }
    public TetherCombat CombatModule { get; private set; }

    // Riferimenti di rete ai due player
    public NetworkVariable<NetworkObjectReference> PlayerARef = new NetworkVariable<NetworkObjectReference>(
       default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<NetworkObjectReference> PlayerBRef = new NetworkVariable<NetworkObjectReference>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Variabili locali per memorizzare i Transform una volta trovati
    private Transform _transformA;
    private Transform _transformB;

    void Awake()
    {
        PhysicsModule = GetComponent<TetherPhysics>();
        ResizeModule = GetComponent<TetherResize>();
        CombatModule = GetComponent<TetherCombat>();
    }

    void Update()
    {
        // Polling finché entrambi i transform non sono risolti.
        // Una volta trovati, disabilitiamo Update: i cambiamenti successivi arrivano
        // via OnValueChanged (già sottoscritto in OnNetworkSpawn).
        if (_transformA == null || _transformB == null)
        {
            UpdatePlayerTransforms();
        }

        if (_transformA != null && _transformB != null)
            enabled = false;
    }

    private void UpdatePlayerTransforms()
    {
        // Cerchiamo di ottenere il Transform dal NetworkObjectReference di Player A
        if (PlayerARef.Value.TryGet(out NetworkObject netA))
        {
            _transformA = netA.transform;
        }

        // Cerchiamo di ottenere il Transform dal NetworkObjectReference di Player B
        if (PlayerBRef.Value.TryGet(out NetworkObject netB))
            _transformB = netB.transform;
    }
   
    public override void OnNetworkSpawn()
    {
        // Quando i riferimenti di rete cambiano (es. reconnect), aggiorna i Transform
        // e riattiva Update nel caso uno dei due sia diventato null.
        PlayerARef.OnValueChanged += (oldValue, newValue) => { UpdatePlayerTransforms(); enabled = true; };
        PlayerBRef.OnValueChanged += (oldValue, newValue) => { UpdatePlayerTransforms(); enabled = true; };

        // Chiamata iniziale nel caso i dati siano gi� arrivati
        UpdatePlayerTransforms();
    }
    
    public Transform GetTransformA()
    {
        return _transformA;
    }

    public Transform GetTransformB()
    {
        return _transformB;
    }
}