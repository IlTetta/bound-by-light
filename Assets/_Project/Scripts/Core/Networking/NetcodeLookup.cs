using UnityEngine;
using Unity.Netcode;

public static class NetcodeLookup
{
    public static Transform FindPlayerTransformByClientId(ulong clientId) {
        if (NetworkManager.Singleton == null) return null;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)) {
            var playerObj = client.PlayerObject;
            return playerObj != null ? playerObj.transform : null;
        }

        return null;
    }
    
}
