using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pulsante di debug per saltare la stanza di combattimento.
/// Quando il player locale entra nel trigger e preme X, tutti i nemici
/// vivi vengono uccisi e la stanza viene marcata come cleared.
///
/// Setup in scena:
///   1. Crea un GameObject con BoxCollider (Is Trigger = true) e NetworkObject.
///   2. Aggiungi questo componente e assegna waveController.
///   3. Il prefab NON deve essere registrato nei Network Prefabs: questo GO
///      è in-scene, non spawnato a runtime.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CombatSkipButton : NetworkBehaviour
{
    [SerializeField] private RoomWaveController waveController;
    [Tooltip("La Room di questa stanza. Serve per marcarla Cleared anche se non era stata Activated.")]
    [SerializeField] private Room room;

    private bool _localPlayerInside;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Update()
    {
        if (!_localPlayerInside) return;
        if (Input.GetKeyDown(KeyCode.X))
            RequestSkipServerRpc();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsLocalPlayer(other)) return;
        _localPlayerInside = true;
        Debug.Log("[CombatSkipButton] Player vicino al pulsante — premi X per saltare il combattimento.");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsLocalPlayer(other)) return;
        _localPlayerInside = false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSkipServerRpc()
    {
        if (waveController == null)
        {
            Debug.LogWarning("[CombatSkipButton] waveController non assegnato.");
            return;
        }
        waveController.ForceComplete();
        // Room.SetCleared() è idempotente e funziona anche da stato Sealed,
        // quindi garantiamo la transizione anche se Activate() non era stato chiamato.
        room?.SetCleared();
    }

    private static bool IsLocalPlayer(Collider other)
    {
        var netObj = other.GetComponentInParent<NetworkObject>();
        return netObj != null && netObj.IsOwner;
    }
}
