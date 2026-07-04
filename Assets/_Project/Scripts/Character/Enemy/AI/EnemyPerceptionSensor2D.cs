using Unity.Netcode;
using UnityEngine;

public sealed class EnemyPerceptionSensor2D : MonoBehaviour
{
    [SerializeField] private int bufferSize = 8;
    private Collider2D[] _hits;

    private void Awake() {
        _hits = new Collider2D[Mathf.Max(2, bufferSize)];
    }

    public Transform FindClosestTarget(Vector2 origin, float radius,LayerMask mask) {
        
        int count = Physics2D.OverlapCircleNonAlloc(origin, radius, _hits, mask);
        if (count <= 0) return null;

        float best = float.PositiveInfinity;
        Transform bestT = null;

        for (int i = 0; i < count; i++) {
            var col = _hits[i];
            if (col == null) continue;

            var no = col.GetComponentInParent<NetworkObject>();
            if (no == null || !no.IsSpawned) continue;

            float d = Vector2.Distance(origin, no.transform.position);
            if (d < best) {
                best = d;
                bestT = no.transform;
            }
        }
        return bestT;
    }

    public bool TryFindClosestClientId(Vector2 origin, float radius, LayerMask mask, out ulong clientId) {
        clientId = 0;
        int count = Physics2D.OverlapCircleNonAlloc(origin, radius, _hits, mask);
        if (count <= 0) return false;

        float best = float.PositiveInfinity;
        ulong bestId = 0;

        for (int i = 0; i < count; i++) {
            var col = _hits[i];
            if (col == null) continue;

            var no = col.GetComponentInParent<NetworkObject>();
            if (no == null || !no.IsSpawned) continue;

            // OwnerClientId = client che controlla quel player
            float d = Vector2.Distance(origin, no.transform.position);
            if (d < best) {
                best = d;
                bestId = no.OwnerClientId;
            }
        }

        if (bestId == 0 && count > 0) {
            // In Netcode, Host di solito ha clientId = 0, quindi 0 potrebbe essere un client valido
            // Quindi si usa un bool
        }

        clientId = bestId;
        return true;
    }

#if UNITY_EDITOR
    public void DrawGizmos(Vector3 pos, float radius) {
        Gizmos.DrawWireSphere(pos, radius);
    }
#endif
}
