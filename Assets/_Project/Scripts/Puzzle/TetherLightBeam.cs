using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(TetherManager))]
public class TetherLightBeam : NetworkBehaviour
{
    [Header("Ray Settings")]
    [SerializeField] private int maxReflections = 5;
    [SerializeField] private float maxRayLength = 50f;
    [SerializeField] private LayerMask lightLayerMask;

    [Header("Visual")]
    [SerializeField] private Color beamColor = new Color(1f, 0.85f, 0f, 1f);
    [SerializeField] private float beamWidth = 0.08f;

    private NetworkVariable<bool> _beamActive = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private TetherManager _manager;
    private LineRenderer _lr; // nome univoco, nessuna ambiguità con campi serializzati

    private static readonly string[] ShaderCandidates =
    {
        "Universal Render Pipeline/Unlit",
        "Unlit/Color",
        "Sprites/Default",
        "Legacy Shaders/Particles/Alpha Blended"
    };

    private void Awake()
    {
        _manager = GetComponent<TetherManager>();

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

        // Materiale
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
            if (!active) _lr.positionCount = 0;
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
        Transform p1 = _manager.GetTransformA();
        Transform p2 = _manager.GetTransformB();
        if (p1 == null || p2 == null) { _lr.positionCount = 0; return; }

        Vector3 posA = new Vector3(p1.position.x, p1.position.y, 0f);
        Vector3 posB = new Vector3(p2.position.x, p2.position.y, 0f);
        Vector3 origin = (posA + posB) * 0.5f;
        Vector3 tetherDir = (posB - posA).normalized;
        Vector3 rayDir = new Vector3(-tetherDir.y, tetherDir.x, 0f);

        var points = new List<Vector3> { origin };
        PropagateRay(origin, rayDir, points, maxReflections);

        _lr.positionCount = points.Count;
        _lr.SetPositions(points.ToArray());
    }

    private void PropagateRay(Vector3 origin, Vector3 dir, List<Vector3> pts, int bounces)
    {
        if (bounces < 0) return;

        Vector2 o2 = new Vector2(origin.x, origin.y);
        Vector2 d2 = new Vector2(dir.x, dir.y).normalized;

        RaycastHit2D hit = Physics2D.Raycast(o2, d2, maxRayLength, lightLayerMask);
        if (!hit.collider) { pts.Add(origin + dir * maxRayLength); return; }

        Vector3 hp = new Vector3(hit.point.x, hit.point.y, 0f);
        pts.Add(hp);

        var prism = hit.collider.GetComponent<PrismReflector>();
        if (prism != null)
        {
            Vector2 r2 = Vector2.Reflect(d2, hit.normal);
            Vector3 r3 = new Vector3(r2.x, r2.y, 0f);
            prism.OnHit();
            PropagateRay(hp + r3 * 0.02f, r3, pts, bounces - 1);
            return;
        }

        var receiver = hit.collider.GetComponent<LightReceiver>();
        receiver?.OnBeamReceived();
    }
}