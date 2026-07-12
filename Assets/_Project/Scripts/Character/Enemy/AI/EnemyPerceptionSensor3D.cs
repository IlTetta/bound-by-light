using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sensore di percezione 3D per i nemici (Physics.OverlapSphere).
/// API pubblica: FindClosestTarget, TryFindClosestClientId.
/// Ignora i bersagli a terra/morti (player fainted = HP 0 = IsDead).
/// </summary>
public sealed class EnemyPerceptionSensor3D : MonoBehaviour
{
    [SerializeField] private int bufferSize = 8;
    private Collider[] _hits;

    private void Awake()
    {
        _hits = new Collider[Mathf.Max(2, bufferSize)];
    }

    /// <summary>Restituisce il Transform del target NetworkObject più vicino nell'area.</summary>
    public Transform FindClosestTarget(Vector3 origin, float radius, LayerMask mask)
    {
        int count = Physics.OverlapSphereNonAlloc(origin, radius, _hits, mask);
        if (count <= 0) return null;

        float     best  = float.PositiveInfinity;
        Transform bestT = null;

        for (int i = 0; i < count; i++)
        {
            var col = _hits[i];
            if (col == null) continue;

            var no = col.GetComponentInParent<NetworkObject>();
            if (no == null || !no.IsSpawned) continue;

            // Salta i bersagli a terra o morti (player fainted = HP 0 = IsDead):
            // il nemico deve ignorarli e puntare un player ancora in piedi.
            var targetHealth = no.GetComponent<HealthNetwork>();
            if (targetHealth != null && targetHealth.IsDead) continue;

            float d = Vector3.Distance(origin, no.transform.position);
            if (d < best) { best = d; bestT = no.transform; }
        }

        return bestT;
    }

    /// <summary>Compatibilità con brain che passano Vector2 (XZ).</summary>
    public Transform FindClosestTarget(Vector2 originXZ, float radius, LayerMask mask)
        => FindClosestTarget(new Vector3(originXZ.x, 0f, originXZ.y), radius, mask);

    /// <summary>Restituisce l'OwnerClientId del target NetworkObject più vicino.</summary>
    public bool TryFindClosestClientId(Vector3 origin, float radius,
        LayerMask mask, out ulong clientId)
    {
        clientId = ulong.MaxValue;
        int count = Physics.OverlapSphereNonAlloc(origin, radius, _hits, mask);
        if (count <= 0) return false;

        float  best   = float.PositiveInfinity;
        ulong  bestId = ulong.MaxValue;
        bool   found  = false;

        for (int i = 0; i < count; i++)
        {
            var col = _hits[i];
            if (col == null) continue;

            var no = col.GetComponentInParent<NetworkObject>();
            if (no == null || !no.IsSpawned) continue;

            // Salta i bersagli a terra o morti (player fainted = HP 0 = IsDead).
            var targetHealth = no.GetComponent<HealthNetwork>();
            if (targetHealth != null && targetHealth.IsDead) continue;

            float d = Vector3.Distance(origin, no.transform.position);
            if (d < best) { best = d; bestId = no.OwnerClientId; found = true; }
        }

        clientId = bestId;
        return found;
    }

    /// <summary>Compatibilità con brain che passano Vector2 (XZ).</summary>
    public bool TryFindClosestClientId(Vector2 originXZ, float radius,
        LayerMask mask, out ulong clientId)
        => TryFindClosestClientId(new Vector3(originXZ.x, 0f, originXZ.y), radius, mask, out clientId);

#if UNITY_EDITOR
    public void DrawGizmos(Vector3 pos, float radius)
    {
        Gizmos.DrawWireSphere(pos, radius);
    }
#endif
}
