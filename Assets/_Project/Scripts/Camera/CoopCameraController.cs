using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Camera cooperativa stile Hades: segue il punto medio tra i player registrati.
/// I player si registrano chiamando RegisterPlayer/UnregisterPlayer (nessun polling).
/// </summary>
public sealed class CoopCameraController : MonoBehaviour
{
    [Header("Riferimenti")]
    [Tooltip("Se null usa Camera.main")]
    [SerializeField] private Camera cam;

    [Header("Follow")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 10f, 10f);
    [SerializeField] private float followSmooth = 8f;

    [Header("Zoom dinamico (solo con 2 player)")]
    [SerializeField] private float zoomMin = 5f;
    [SerializeField] private float zoomMax = 10f;
    [SerializeField] private float zoomPadding = 2f;
    [SerializeField] private float zoomSmooth = 5f;

    [Header("Camera shake")]
    [SerializeField] private float shakeDefaultDuration = 0.18f;
    [SerializeField] private float shakeDefaultIntensity = 0.12f;

    [Tooltip("Near clip plane della camera. Più basso = meno clipping su mesh grandi. Default Unity: 0.3")]
    [SerializeField] private float nearClipPlane = 0.05f;

    // ─── Stato ───────────────────────────────────────────────────────────────
    private readonly List<Transform>    _players      = new();
    private readonly List<IAimProvider> _aimProviders = new();

    private Vector3 _currentPos;
    private float   _currentZoom;
    private bool    _initialized;

    private float   _shakeTimer;
    private float   _shakeDuration;
    private float   _shakeIntensity;
    private Vector3 _shakeOffset;

    public static CoopCameraController Instance { get; private set; }

    /// <summary>Transform dei player attualmente registrati (read-only).</summary>
    public IReadOnlyList<Transform> Players => _players;

    /// <summary>La camera usata dal controller (null-safe).</summary>
    public Camera Cam => cam;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
        cam ??= Camera.main;

        if (cam == null)
        {
            Debug.LogError("[CoopCamera] Camera.main non trovata.");
            return;
        }

        // Near clip molto basso: riduce il "buco" che si vede quando la camera
        // si avvicina a mesh grandi (boss, pilastri). Default Unity = 0.3,
        // valori sotto 0.05 possono causare Z-fighting su oggetti lontani.
        cam.nearClipPlane = nearClipPlane;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        if (cam == null) return;

        PurgeNull();

        if (_players.Count == 0) return;

        Vector3 target = Midpoint() + followOffset;

        if (!_initialized)
        {
            _currentPos  = target;
            _currentZoom = zoomMin;
            _initialized = true;
        }

        float t = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);
        _currentPos = Vector3.Lerp(_currentPos, target, t);

        float zoom = _players.Count >= 2 ? TargetZoom() : zoomMin;
        _currentZoom = Mathf.Lerp(_currentZoom, zoom,
            1f - Mathf.Exp(-zoomSmooth * Time.deltaTime));

        if (cam.orthographic)
            cam.orthographicSize = _currentZoom;

        UpdateShake();

        cam.transform.position = _currentPos + _shakeOffset;
    }

    // ─── API pubblica ─────────────────────────────────────────────────────────

    public void RegisterPlayer(Transform player)
    {
        if (player != null && !_players.Contains(player))
        {
            _players.Add(player);
            Debug.Log($"[CoopCamera] Player registrato: {player.name} — totale: {_players.Count}");
        }
    }

    public void UnregisterPlayer(Transform player)
    {
        _players.Remove(player);
        Debug.Log($"[CoopCamera] Player rimosso: {player?.name} — totale: {_players.Count}");
    }

    public void RegisterAimProvider(IAimProvider p)   { if (!_aimProviders.Contains(p)) _aimProviders.Add(p); }
    public void UnregisterAimProvider(IAimProvider p) { _aimProviders.Remove(p); }

    public void Shake(float duration, float intensity)
    {
        if (_shakeTimer > 0f && intensity <= _shakeIntensity) return;
        _shakeDuration  = duration;
        _shakeIntensity = intensity;
        _shakeTimer     = duration;
    }

    public void Shake() => Shake(shakeDefaultDuration, shakeDefaultIntensity);

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void PurgeNull()
    {
        for (int i = _players.Count - 1; i >= 0; i--)
            if (_players[i] == null) _players.RemoveAt(i);
    }

    private Vector3 Midpoint()
    {
        if (_players.Count == 1) return _players[0].position;
        Vector3 sum = Vector3.zero;
        foreach (var p in _players) sum += p.position;
        return sum / _players.Count;
    }

    private float TargetZoom()
    {
        Vector3 a = _players[0].position; a.y = 0f;
        Vector3 b = _players[1].position; b.y = 0f;
        float dist = Vector3.Distance(a, b);
        return Mathf.Clamp((dist + zoomPadding) * 0.5f, zoomMin, zoomMax);
    }

    private void UpdateShake()
    {
        if (_shakeTimer <= 0f) { _shakeOffset = Vector3.zero; return; }
        _shakeTimer -= Time.deltaTime;
        float amp = _shakeIntensity * (_shakeTimer / Mathf.Max(0.001f, _shakeDuration));
        _shakeOffset = new Vector3(
            Random.Range(-1f, 1f) * amp,
            Random.Range(-1f, 1f) * amp,
            0f);
    }
}
