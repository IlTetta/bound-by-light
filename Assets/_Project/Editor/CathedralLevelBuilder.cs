#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tool Editor per generare il layout del livello "Cattedrale" nella scena attiva.
/// Apribile da: Tools → Bound By Light → Build Cathedral Level
///
/// Crea un GameObject genitore "Level_Cathedral" con tutte le stanze del diagramma:
///   • Garden + Nartex  (spawn)
///   • Nave
///   • Porch
///   • Crossing
///   • Transept (sinistra)
///   • Transept + Warehouse (destra)
///   • Apse + Choir (boss, in cima)
///
/// Ogni stanza viene creata con:
///   - Un GameObject "Room_[Nome]" con BoxCollider2D (bounds) e Room component stub
///   - Un figlio "Floor" (Quad) con colore placeholder
///   - Un figlio "Walls" con i muri perimetrali
///   - Un figlio "EntryTrigger" con BoxCollider2D trigger
///
/// IMPORTANTE: questo tool crea solo la struttura di base con placeholder.
///   Sostituisci i Quad con i tuoi asset grafici e configura le Room nell'Inspector.
/// </summary>
public class CathedralLevelBuilder : EditorWindow
{
    // ─── Dati stanze ──────────────────────────────────────────────────────────

    private struct RoomData
    {
        public string id;
        public string displayName;
        public Vector2 position;   // centro in world space (unità Unity)
        public Vector2 size;       // larghezza x altezza
        public Color   floorColor;
        public bool    isStartRoom;
        public bool    isArched;   // Apse ha la forma ad arco
    }

    // Layout dal diagramma — coordinate in unità Unity (1 u = ~2m in gioco)
    // Origine (0,0) = centro della mappa, Y cresce verso l'alto
    private static readonly RoomData[] Rooms = new[]
    {
        new RoomData
        {
            id          = "garden_nartex",
            displayName = "Garden + Nartex",
            position    = new Vector2(0f, -28f),
            size        = new Vector2(22f, 12f),
            floorColor  = new Color(0.55f, 0.75f, 0.45f),
            isStartRoom = true
        },
        new RoomData
        {
            id          = "nave",
            displayName = "Nave",
            position    = new Vector2(-6f, -14f),
            size        = new Vector2(12f, 16f),
            floorColor  = new Color(0.7f, 0.65f, 0.55f)
        },
        new RoomData
        {
            id          = "porch",
            displayName = "Porch",
            position    = new Vector2(10f, -14f),
            size        = new Vector2(14f, 16f),
            floorColor  = new Color(0.6f, 0.6f, 0.7f)
        },
        new RoomData
        {
            id          = "crossing",
            displayName = "Crossing",
            position    = new Vector2(0f, 0f),
            size        = new Vector2(28f, 14f),
            floorColor  = new Color(0.65f, 0.6f, 0.5f)
        },
        new RoomData
        {
            id          = "transept_left",
            displayName = "Transept",
            position    = new Vector2(-20f, 0f),
            size        = new Vector2(12f, 14f),
            floorColor  = new Color(0.5f, 0.55f, 0.65f)
        },
        new RoomData
        {
            id          = "transept_warehouse",
            displayName = "Transept + Warehouse",
            position    = new Vector2(20f, 0f),
            size        = new Vector2(12f, 14f),
            floorColor  = new Color(0.55f, 0.5f, 0.6f)
        },
        new RoomData
        {
            id          = "apse_choir",
            displayName = "Apse + Choir",
            position    = new Vector2(0f, 18f),
            size        = new Vector2(18f, 18f),
            floorColor  = new Color(0.4f, 0.4f, 0.55f),
            isArched    = true
        },
    };

    // ─── Editor Window ────────────────────────────────────────────────────────

    [MenuItem("Tools/Bound By Light/Build Cathedral Level")]
    public static void ShowWindow()
    {
        GetWindow<CathedralLevelBuilder>("Cathedral Level Builder");
    }

    private Vector2 _wallThickness = new Vector2(0.5f, 0.5f);
    private float   _unitScale     = 1f;
    private bool    _addRoomScript = true;
    private bool    _addTriggers   = true;

    private void OnGUI()
    {
        GUILayout.Label("Cathedral Level Builder", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _wallThickness = EditorGUILayout.Vector2Field("Spessore muri (x, y)", _wallThickness);
        _unitScale     = EditorGUILayout.FloatField("Scala unità", _unitScale);
        _addRoomScript = EditorGUILayout.Toggle("Aggiungi Room component", _addRoomScript);
        _addTriggers   = EditorGUILayout.Toggle("Aggiungi Entry Trigger", _addTriggers);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Genera il layout della cattedrale nella scena attiva.\n" +
            "I placeholder (Quad colorati) vanno sostituiti con i tuoi asset.",
            MessageType.Info);
        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(!UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().IsValid()))
        {
            if (GUILayout.Button("BUILD LEVEL", GUILayout.Height(40)))
                BuildLevel();

            if (GUILayout.Button("Elimina livello esistente", GUILayout.Height(25)))
                DestroyExisting();
        }
    }

    // ─── Build ────────────────────────────────────────────────────────────────

    private void BuildLevel()
    {
        DestroyExisting();

        // Root container
        GameObject root = new GameObject("Level_Cathedral");
        Undo.RegisterCreatedObjectUndo(root, "Build Cathedral Level");

        foreach (var data in Rooms)
            BuildRoom(root.transform, data);

        Debug.Log($"[CathedralLevelBuilder] Livello generato con {Rooms.Length} stanze.");
        Selection.activeGameObject = root;
    }

    private void BuildRoom(Transform parent, RoomData data)
    {
        Vector2 scaledPos  = data.position  * _unitScale;
        Vector2 scaledSize = data.size      * _unitScale;

        // ── Root stanza ──────────────────────────────────────────────────────
        GameObject roomGo = new GameObject($"Room_{data.id}");
        roomGo.transform.SetParent(parent, false);
        roomGo.transform.localPosition = new Vector3(scaledPos.x, scaledPos.y, 0f);

        // BoxCollider2D per i bounds (NON trigger, usato da Room.RoomBoundsWorld)
        var boundsCol = roomGo.AddComponent<BoxCollider2D>();
        boundsCol.size      = scaledSize;
        boundsCol.isTrigger = false;
        boundsCol.enabled   = false; // disabilitato: solo per riferimento bounds

        // ── Floor (Quad placeholder) ─────────────────────────────────────────
        CreateFloor(roomGo.transform, scaledSize, data.floorColor);

        // ── Muri perimetrali ─────────────────────────────────────────────────
        CreateWalls(roomGo.transform, scaledSize, data);

        // ── Label stanza (solo Editor) ───────────────────────────────────────
        // Nota: in play mode non visibile, solo in scena
        GameObject labelGo = new GameObject("_Label");
        labelGo.transform.SetParent(roomGo.transform, false);
        labelGo.transform.localPosition = Vector3.up * (scaledSize.y * 0.4f);

        // ── Entry Trigger ────────────────────────────────────────────────────
        if (_addTriggers)
            CreateEntryTrigger(roomGo.transform, scaledSize);

        // ── Spawn point (solo start room) ───────────────────────────────────
        if (data.isStartRoom)
        {
            GameObject spawnGo = new GameObject("PlayerSpawnPoint");
            spawnGo.transform.SetParent(roomGo.transform, false);
            spawnGo.transform.localPosition = Vector3.zero;
        }

        // ── Room component ───────────────────────────────────────────────────
        if (_addRoomScript)
        {
            // Non aggiungiamo Room qui perché richiede NetworkBehaviour.
            // Aggiungi Room manualmente dopo aver importato il progetto.
            // Al suo posto mettiamo un tag sull'oggetto.
            roomGo.tag = "Untagged"; // placeholder
        }

        // Aggiungi tag per riconoscere le stanze
        roomGo.name = $"Room_{data.id}";
    }

    // ─── Floor ───────────────────────────────────────────────────────────────

    private static void CreateFloor(Transform parent, Vector2 size, Color color)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
        floor.name = "Floor";
        floor.transform.SetParent(parent, false);
        floor.transform.localPosition  = Vector3.zero;
        floor.transform.localScale     = new Vector3(size.x, size.y, 1f);
        floor.transform.localRotation  = Quaternion.identity;

        // Colore placeholder
        var renderer = floor.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? Shader.Find("Standard"));
            mat.color = color;
            renderer.sharedMaterial = mat;
        }

        // Rimuovi il collider del Quad (usiamo il BoxCollider2D sulla room)
        var col = floor.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);
    }

    // ─── Walls ───────────────────────────────────────────────────────────────

    private void CreateWalls(Transform parent, Vector2 size, RoomData data)
    {
        GameObject wallsGo = new GameObject("Walls");
        wallsGo.transform.SetParent(parent, false);

        float hw = size.x * 0.5f; // half-width
        float hh = size.y * 0.5f; // half-height
        float wt = _wallThickness.x * _unitScale;

        // Muro superiore (non aggiunto per stanza con arco — gestito manualmente)
        if (!data.isArched)
            CreateWallSegment(wallsGo.transform, new Vector3(0f,  hh, 0f), new Vector3(size.x, wt, wt), "Wall_Top");

        // Muro inferiore (non aggiunto se è la start room — porta di entrata)
        CreateWallSegment(wallsGo.transform, new Vector3(0f, -hh, 0f), new Vector3(size.x, wt, wt), "Wall_Bottom");

        // Muri laterali
        CreateWallSegment(wallsGo.transform, new Vector3(-hw, 0f, 0f), new Vector3(wt, size.y, wt), "Wall_Left");
        CreateWallSegment(wallsGo.transform, new Vector3( hw, 0f, 0f), new Vector3(wt, size.y, wt), "Wall_Right");
    }

    private static void CreateWallSegment(Transform parent, Vector3 localPos, Vector3 scale, string name)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = localPos;
        wall.transform.localScale    = scale;

        // Colore grigio scuro per i muri
        var renderer = wall.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Standard"));
            mat.color = new Color(0.25f, 0.22f, 0.2f);
            renderer.sharedMaterial = mat;
        }

        // Converti in BoxCollider2D — i Cube hanno BoxCollider 3D
        // Per 2.5D usiamo Collider2D, quindi rimuoviamo il 3D e aggiungiamo 2D
        var col3d = wall.GetComponent<Collider>();
        if (col3d != null) DestroyImmediate(col3d);

        var col2d = wall.AddComponent<BoxCollider2D>();
        col2d.size = new Vector2(scale.x, scale.y);
    }

    // ─── Entry Trigger ────────────────────────────────────────────────────────

    private static void CreateEntryTrigger(Transform parent, Vector2 size)
    {
        GameObject triggerGo = new GameObject("EntryTrigger");
        triggerGo.transform.SetParent(parent, false);
        triggerGo.transform.localPosition = Vector3.zero;

        var col = triggerGo.AddComponent<BoxCollider2D>();
        col.size      = size * 0.9f; // leggermente più piccolo dei bounds
        col.isTrigger = true;

        // RoomEntryTrigger verrà aggiunto manualmente
        // (richiede Room reference che setup Editor non può risolvere automaticamente)
    }

    // ─── Cleanup ─────────────────────────────────────────────────────────────

    private static void DestroyExisting()
    {
        var existing = GameObject.Find("Level_Cathedral");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
            Debug.Log("[CathedralLevelBuilder] Livello precedente eliminato.");
        }
    }
}
#endif
