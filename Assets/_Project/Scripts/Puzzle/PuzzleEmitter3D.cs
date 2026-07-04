using System.Collections.Generic;
using FMODUnity;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Emitter fisso (non dipende dal Tether) per il puzzle 3D della Cathedral.
/// Spara un raggio orizzontale nel piano XZ a una quota configurabile.
/// Richiede un layer dedicato "PuzzleLight" assegnato a Prismi e Receiver.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PuzzleEmitter3D : NetworkBehaviour
{
    [Header("Beam")]
    [SerializeField] private Vector3 emitDirection = Vector3.forward;
    [SerializeField] private int maxReflections = 5;
    [SerializeField] private float maxRayLength = 30f;
    [SerializeField] private LayerMask lightLayerMask;
    [Tooltip("Altezza del raggio rispetto al Y dell'Emitter (es. 1 = 1m sopra il pavimento).")]
    [SerializeField] private float beamHeight = 1f;

    [Header("Visual")]
    [ColorUsage(true, true)]
    [SerializeField] private Color beamColor = new Color(1f, 0.85f, 0f, 1f);
    [SerializeField] private float beamWidth = 0.15f;

    [Header("SFX")]
    [Tooltip("Emitter FMOD riprodotto quando il raggio si attiva (Luce).")]
    [SerializeField] private StudioEventEmitter beamSfxEmitter;

    [Header("Hit Indicators")]
    [Tooltip("Prefab opzionale per i marcatori di rimbalzo. Se null usa una sfera procedurale.")]
    [SerializeField] private GameObject hitPointPrefab;
    [SerializeField] private float hitPointSize = 0.25f;

    private NetworkVariable<bool> _beamActive = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private LineRenderer _lr;
    private readonly List<GameObject> _hitMarkers = new();

    private static readonly string[] ShaderCandidates =
    {
        "Universal Render Pipeline/Unlit",
        "Unlit/Color",
        "Sprites/Default",
        "Legacy Shaders/Particles/Alpha Blended"
    };

    private void Awake()
    {
        var go = new GameObject("_BeamLR");
        go.transform.SetParent(transform, false);
        _lr = go.AddComponent<LineRenderer>();
        _lr.useWorldSpace = true;
        _lr.startWidth = beamWidth;
        _lr.endWidth = beamWidth;
        _lr.startColor = beamColor;
        _lr.endColor = beamColor;
        _lr.positionCount = 0;
        _lr.sortingOrder = 2;
        _lr.enabled = false;

        Shader shader = null;
        foreach (var s in ShaderCandidates) { shader = Shader.Find(s); if (shader != null) break; }
        if (shader != null)
        {
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", beamColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", beamColor);
            _lr.material = mat;
        }
    }

    public override void OnNetworkSpawn()
    {
        _beamActive.OnValueChanged += (_, active) =>
        {
            _lr.enabled = active;
            if (active)
                beamSfxEmitter?.Play();
            else
            {
                _lr.positionCount = 0;
                SetHitMarkersActive(false);
            }
        };
        _lr.enabled = _beamActive.Value;
    }

    public void SetBeamActive(bool active)
    {
        if (!IsServer) return;
        _beamActive.Value = active;
    }

    private void LateUpdate()
    {
        if (!_beamActive.Value) return;
        CastBeam();
    }

    private void CastBeam()
    {
        float y = transform.position.y + beamHeight;
        Vector3 origin = new Vector3(transform.position.x, y, transform.position.z);
        Vector3 dir = new Vector3(emitDirection.x, 0f, emitDirection.z).normalized;
        if (dir == Vector3.zero) dir = Vector3.forward;

        var points = new List<Vector3> { origin };
        PropagateRay(origin, dir, points, maxReflections);

        _lr.positionCount = points.Count;
        _lr.SetPositions(points.ToArray());
        UpdateHitMarkers(points);
    }

    // Mostra sfere nei punti di rimbalzo (pts[1..N-2], esclusi origine e punto finale libero).
    private void UpdateHitMarkers(List<Vector3> points)
    {
        int bounceCount = Mathf.Max(0, points.Count - 2);

        while (_hitMarkers.Count < bounceCount)
            _hitMarkers.Add(CreateHitMarker());

        for (int i = 0; i < _hitMarkers.Count; i++)
        {
            bool active = i < bounceCount;
            _hitMarkers[i].SetActive(active);
            if (active)
                _hitMarkers[i].transform.position = points[i + 1];
        }
    }

    private GameObject CreateHitMarker()
    {
        GameObject marker;
        if (hitPointPrefab != null)
        {
            marker = Instantiate(hitPointPrefab, transform);
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.SetParent(transform);
            Destroy(marker.GetComponent<Collider>());

            var r = marker.GetComponent<Renderer>();
            Shader shader = null;
            foreach (var s in ShaderCandidates) { shader = Shader.Find(s); if (shader != null) break; }
            if (shader != null)
            {
                var mat = new Material(shader);
                Color markerColor = beamColor * 1.5f;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", markerColor);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     markerColor);
                r.material = mat;
            }
        }

        marker.transform.localScale = Vector3.one * hitPointSize;
        return marker;
    }

    private void SetHitMarkersActive(bool active)
    {
        foreach (var m in _hitMarkers) m.SetActive(active);
    }

    private void PropagateRay(Vector3 origin, Vector3 dir, List<Vector3> pts, int bounces)
    {
        if (bounces < 0) return;

        if (!Physics.Raycast(origin, dir, out RaycastHit hit, maxRayLength, lightLayerMask))
        {
            pts.Add(origin + dir * maxRayLength);
            return;
        }

        pts.Add(hit.point);

        var prism = hit.collider.GetComponent<PrismReflector3D>();
        if (prism != null)
        {
            // Riflessione nel piano XZ (ignora componente Y della normale)
            Vector3 normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            if (normal == Vector3.zero) normal = -dir; // fallback
            Vector3 reflected = Vector3.Reflect(dir, normal);
            reflected.y = 0f;
            prism.OnHit();
            PropagateRay(hit.point + reflected * 0.02f, reflected, pts, bounces - 1);
            return;
        }

        hit.collider.GetComponent<LightReceiver3D>()?.OnBeamReceived();
    }

    private void OnDrawGizmos()
    {
        float y = transform.position.y + beamHeight;
        Vector3 origin = new Vector3(transform.position.x, y, transform.position.z);
        Vector3 dir = new Vector3(emitDirection.x, 0f, emitDirection.z).normalized;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, 0.2f);
        Gizmos.DrawRay(origin, dir * 3f);
    }
}
