using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pulsante di debug per saltare il puzzle della stanza.
/// Quando il player locale entra nel trigger e preme E, il puzzle viene
/// marcato come risolto (reward spawnati + porte aperte).
///
/// Setup in scena:
///   1. Crea un GameObject con BoxCollider (Is Trigger = true) e NetworkObject.
///   2. Aggiungi questo componente e assegna puzzleManager.
///   3. Metti il GameObject su un layer che i player possono colpire (es. Default).
///   4. Visivamente: usa un mesh/sprite che sia riconoscibile come "skip button".
/// </summary>
[RequireComponent(typeof(Collider))]
public class PuzzleSkipButton : NetworkBehaviour
{
    [SerializeField] private PuzzleRoomManager puzzleManager;

    private bool _localPlayerInside;

    private void Awake()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void Update()
    {
        if (!_localPlayerInside) return;
        if (Input.GetKeyDown(KeyCode.P))
            RequestSkipServerRpc();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsLocalPlayer(other)) return;
        _localPlayerInside = true;
        Debug.Log("[PuzzleSkipButton] Player vicino al pulsante — premi P per saltare il puzzle.");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsLocalPlayer(other)) return;
        _localPlayerInside = false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSkipServerRpc()
    {
        if (puzzleManager == null)
        {
            Debug.LogWarning("[PuzzleSkipButton] puzzleManager non assegnato.");
            return;
        }
        Debug.Log("[PuzzleSkipButton] Puzzle forzato come risolto (skip button).");
        puzzleManager.ForceComplete();
    }

    private static bool IsLocalPlayer(Collider other)
    {
        var netObj = other.GetComponentInParent<NetworkObject>();
        return netObj != null && netObj.IsOwner;
    }
}
