using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PuzzleRoomTestBootstrapper : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject gameManagerPrefab;
    public GameObject tetherPrefab;
    public GameObject playerPrefab;

    [Header("Spawn Points")]
    public Transform spawnPoint0;
    public Transform spawnPoint1;

    private readonly List<ulong> _connectedClients = new();

    private void Start()
    {
        var nm = NetworkManager.Singleton;
        nm.OnServerStarted += OnServerStarted;
        nm.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnServerStarted()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        // Spawna GameManager
        if (gameManagerPrefab != null)
        {
            var gm = Instantiate(gameManagerPrefab);
            gm.GetComponent<NetworkObject>().Spawn();
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        _connectedClients.Add(clientId);

        // Spawna il player con ownership al client
        Transform spawnPoint = _connectedClients.Count == 1 ? spawnPoint0 : spawnPoint1;
        var playerGo = Instantiate(playerPrefab, spawnPoint.position, Quaternion.identity);
        playerGo.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

        // Quando entrambi i player sono connessi, spawna il Tether
        if (_connectedClients.Count == 2)
            StartCoroutine(SpawnTetherNextFrame());
    }

    private IEnumerator SpawnTetherNextFrame()
    {
        yield return null; // aspetta che i PlayerObject siano registrati

        var nm = NetworkManager.Singleton;
        var tether = Instantiate(tetherPrefab);
        var tetherNet = tether.GetComponent<NetworkObject>();
        tetherNet.Spawn();

        var tetherManager = tether.GetComponent<TetherManager>();

        // Collega i due player al tether
        var clientList = new List<ulong>(nm.ConnectedClientsIds);
        if (clientList.Count >= 2)
        {
            var playerA = nm.ConnectedClients[clientList[0]].PlayerObject;
            var playerB = nm.ConnectedClients[clientList[1]].PlayerObject;
            tetherManager.PlayerARef.Value = playerA;
            tetherManager.PlayerBRef.Value = playerB;
        }
    }
}