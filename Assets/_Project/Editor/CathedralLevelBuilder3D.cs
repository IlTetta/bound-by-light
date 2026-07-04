#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tool Editor 3D per generare il layout del livello "Cattedrale".
/// Le stanze sono posizionate sul piano XZ (Y = quota, default 0).
/// Floor orizzontali (Plane), muri verticali (Cube con BoxCollider 3D).
///
/// Apribile da: Tools → Bound By Light → Build Cathedral Level 3D
/// </summary>
public class CathedralLevelBuilder3D : EditorWindow
{
    // ─── Dati stanze ──────────────────────────────────────────────────────────

    private struct RoomData
    {
        public string  id;
        public string  displayName;
        public Vector2 position;   // centro XZ in world space
        public Vector2 size;       // larghezza (X) x profondità (Z)
        public Color   floorColor;
        public bool    isStartRoom;
    }

    // Layout sul piano XZ — stesse dimensioni del builder 2D
    private static readonly RoomData[] Rooms = new[]
    {
        new RoomData { id = "garden_nartex",      displayName = "Garden + Nartex",
                       position = new Vector2(0f, 28f),  size = new Vector2(22f, 12f),
                       floorColor = new Color(0.55f, 0.75f, 0.45f), isStartRoom = true },

        new RoomData { id = "nave",               displayName = "Nave",
                       position = new Vector2(-6f, 14f), size = new Vector2(12f, 16f),
                       floorColor = new Color(0.7f, 0.65f, 0.55f) },

        new RoomData { id = "porch",              displayName = "Porch",
                       position = new Vector2(10f, 14f), size = new Vector2(14f, 16f),
                       floorColor = new Color(0.6f, 0.6f, 0.7f) },

        new RoomData { id = "crossing",           displayName = "Crossing",
                       position = new Vector2(0f, 0f),   size = new Vector2(28f, 14f),
                       floorColor = new Color(0.65f, 0.6f, 0.5f) },

        new RoomData { id = "transept_left",      displayName = "Transept",
                       position = new Vector2(-20f, 0f), size = new Vector2(12f, 14f),
                       floorColor = new Color(0.5f, 0.55f, 0.65f) },

        new RoomData { id = "transept_warehouse", displayName = "Transept + Warehouse",
                       position = new Vector2(20f, 0f),  size = new Vector2(12f, 14f),
                       floorColor = new Color(0.55f, 0.5f, 0.6f) },

        new RoomData { id = "apse_choir",         displayName = "Apse + Choir",
                       position = new Vector2(0f, -18f), size = new Vector2(18f, 18f),
                       floorColor = new Color(0.4f, 0.4f, 0.55f) },
    };

    // ─── Inspector ────────────────────────────────────────────────────────────

    private float _wallHeight     = 3f;
    private float _wallThickness  = 0.4f;
    private float _floorY         = 0f;
    private float _unitScale      = 1f;
    private bool  _addTriggers    = true;

    [MenuItem("Tools/Bound By Light/Build Cathedral Level 3D")]
    public static void ShowWindow() =>
        GetWindow<CathedralLevelBuilder3D>("Cathedral Level Builder 3D");

    private void OnGUI()
    {
        GUILayout.Label("Cathedral Level Builder 3D", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _wallHeight    = EditorGUILayout.FloatField("Altezza muri",       _wallHeight);
        _wallThickness = EditorGUILayout.FloatField("Spessore muri",      _wallThickness);
        _floorY        = EditorGUILayout.FloatField("Quota pavimento Y",  _floorY);
        _unitScale     = EditorGUILayout.FloatField("Scala unità",        _unitScale);
        _addTriggers   = EditorGUILayout.Toggle("Aggiungi Entry Trigger", _addTriggers);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Genera le stanze sul piano XZ con floor orizzontali e muri verticali.\n" +
            "Compatibile con fisica 3D (Rigidbody, Collider 3D).",
            MessageType.Info);
        EditorGUILayout.Space();

        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("BUILD LEVEL 3D", GUILayout.Height(42)))
            BuildLevel();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        if (GUILayout.Button("Elimina livello esistente", GUILayout.Height(28)))
            DestroyExisting();
    }

    // ─── Build ────────────────────────────────────────────────────────────────

    private void BuildLevel()
    {
        DestroyExisting();

        GameObject root = new GameObject("Level_Cathedral");
        Undo.RegisterCreatedObjectUndo(root, "Build Cathedral Level 3D");

        foreach (var data in Rooms)
            BuildRoom(root.transform, data);

        Debug.Log($"[CathedralLevelBuilder3D] {Rooms.Length} stanze generate (piano XZ).");
        Selection.activeGameObject = root;
    }

    private void BuildRoom(Transform parent, RoomData data)
    {
        float sx = data.size.x * _unitScale;
        float sz = data.size.y * _unitScale;
        float px = data.position.x * _unitScale;
        float pz = data.position.y * _unitScale; // Y del layout → Z del world

        // ── Root stanza ──────────────────────────────────────────────────────
        GameObject roomGo = new GameObject($"Room_{data.id}");
        roomGo.transform.SetParent(parent, false);
        roomGo.transform.localPosition = new Vector3(px, _floorY, pz);

        // BoxCollider 3D per i bounds (disabilitato, solo riferimento)
        var boundsCol = roomGo.AddComponent<BoxCollider>();
        boundsCol.center  = new Vector3(0f, _wallHeight * 0.5f, 0f);
        boundsCol.size    = new Vector3(sx, _wallHeight, sz);
        boundsCol.enabled = false;

        // ── Floor ────────────────────────────────────────────────────────────
        CreateFloor(roomGo.transform, sx, sz, data.floorColor);

        // ── Muri ─────────────────────────────────────────────────────────────
        CreateWalls(roomGo.transform, sx, sz);

        // ── Entry Trigger ────────────────────────────────────────────────────
        if (_addTriggers)
            CreateEntryTrigger(roomGo.transform, sx, sz);

        // ── Spawn point ──────────────────────────────────────────────────────
        if (data.isStartRoom)
        {
            GameObject sp = new GameObject("PlayerSpawnPoint");
            sp.transform.SetParent(roomGo.transform, false);
            sp.transform.localPosition = Vector3.zero;
        }
    }

    // ─── Floor ───────────────────────────────────────────────────────────────

    private static void CreateFloor(Transform parent, float sx, float sz, Color color)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(parent, false);
        // Plane ha dimensione 10x10 di default → scala per ottenere sx x sz
        floor.transform.localPosition = Vector3.zero;
        floor.transform.localScale    = new Vector3(sx / 10f, 1f, sz / 10f);
        floor.transform.localRotation = Quaternion.identity;

        var renderer = floor.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard"));
            mat.color = color;
            renderer.sharedMaterial = mat;
        }

        // Il Plane ha già un MeshCollider → lo manteniamo per la fisica (il player ci cammina sopra)
    }

    // ─── Walls ───────────────────────────────────────────────────────────────

    private void CreateWalls(Transform parent, float sx, float sz)
    {
        GameObject wallsGo = new GameObject("Walls");
        wallsGo.transform.SetParent(parent, false);

        float halfX = sx * 0.5f;
        float halfZ = sz * 0.5f;
        float wt    = _wallThickness;
        float wh    = _wallHeight;
        float wy    = wh * 0.5f; // centro Y del muro

        // Muro Nord (Z positivo)
        CreateWall(wallsGo.transform, new Vector3(0f, wy,  halfZ), new Vector3(sx, wh, wt), "Wall_North");
        // Muro Sud (Z negativo)
        CreateWall(wallsGo.transform, new Vector3(0f, wy, -halfZ), new Vector3(sx, wh, wt), "Wall_South");
        // Muro Ovest (X negativo)
        CreateWall(wallsGo.transform, new Vector3(-halfX, wy, 0f), new Vector3(wt, wh, sz), "Wall_West");
        // Muro Est (X positivo)
        CreateWall(wallsGo.transform, new Vector3( halfX, wy, 0f), new Vector3(wt, wh, sz), "Wall_East");
    }

    private static void CreateWall(Transform parent, Vector3 localPos, Vector3 size, string name)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = localPos;
        wall.transform.localScale    = size;

        var renderer = wall.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard"));
            mat.color = new Color(0.25f, 0.22f, 0.2f);
            renderer.sharedMaterial = mat;
        }
        // BoxCollider 3D rimane sul Cube — è corretto per fisica 3D
    }

    // ─── Entry Trigger ────────────────────────────────────────────────────────

    private void CreateEntryTrigger(Transform parent, float sx, float sz)
    {
        GameObject trigGo = new GameObject("EntryTrigger");
        trigGo.transform.SetParent(parent, false);
        trigGo.transform.localPosition = new Vector3(0f, _wallHeight * 0.5f, 0f);

        var col = trigGo.AddComponent<BoxCollider>();
        col.center    = Vector3.zero;
        col.size      = new Vector3(sx * 0.9f, _wallHeight, sz * 0.9f);
        col.isTrigger = true;
    }

    // ─── Cleanup ─────────────────────────────────────────────────────────────

    private static void DestroyExisting()
    {
        var existing = GameObject.Find("Level_Cathedral");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
            Debug.Log("[CathedralLevelBuilder3D] Livello precedente eliminato.");
        }
    }
}
#endif
