using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → BoundByLight → Cathedral Puzzle Setup
///
/// Crea la struttura completa del puzzle nella Room_transept_left:
///   PuzzleRoot
///     ├─ PuzzleRoomManager  (NetworkObject + PuzzleRoomManager)
///     ├─ PuzzleEmitter      (NetworkObject + PuzzleEmitter3D)
///     ├─ Pedestal_A         (BeamPedestal3D + mesh cilindro)
///     ├─ Pedestal_B         (BeamPedestal3D + mesh cilindro)
///     ├─ Prism_01..N        (NetworkObject + PrismReflector3D + BoxCollider)
///     └─ LightReceiver      (LightReceiver3D + BoxCollider + mesh cubo)
///
/// Dopo la creazione segui le istruzioni nel pannello.
/// </summary>
public class CathedralPuzzleSetup : EditorWindow
{
    private GameObject _parentRoom;

    private Vector3 _emitterPos   = new Vector3(0f, 0f,  0f);
    private Vector3 _pedestalAPos = new Vector3(-2f, 0f, 0f);
    private Vector3 _pedestalBPos = new Vector3( 2f, 0f, 0f);
    private Vector3 _receiverPos  = new Vector3(0f, 0f,  5f);
    private int     _prismCount   = 2;
    private float   _prismSpacing = 3f;

    [MenuItem("Tools/BoundByLight/Cathedral Puzzle Setup")]
    public static void ShowWindow()
        => GetWindow<CathedralPuzzleSetup>("Cathedral Puzzle");

    private void OnGUI()
    {
        GUILayout.Label("Cathedral Puzzle Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        _parentRoom = (GameObject)EditorGUILayout.ObjectField(
            "Parent (Room_transept_left)", _parentRoom, typeof(GameObject), true);

        EditorGUILayout.Space(6);
        GUILayout.Label("Posizioni locali rispetto al Parent", EditorStyles.boldLabel);
        _emitterPos   = EditorGUILayout.Vector3Field("Emitter",    _emitterPos);
        _pedestalAPos = EditorGUILayout.Vector3Field("Pedestal A", _pedestalAPos);
        _pedestalBPos = EditorGUILayout.Vector3Field("Pedestal B", _pedestalBPos);
        _receiverPos  = EditorGUILayout.Vector3Field("Receiver",   _receiverPos);

        EditorGUILayout.Space(4);
        _prismCount   = EditorGUILayout.IntSlider("Numero Prismi",  _prismCount,   1, 4);
        _prismSpacing = EditorGUILayout.FloatField("Spaziatura XZ", _prismSpacing);

        EditorGUILayout.Space(8);

        if (_parentRoom == null)
        {
            EditorGUILayout.HelpBox(
                "Assegna Room_transept_left (o un GameObject vuoto nella stanza) nel campo Parent.",
                MessageType.Warning);
            return;
        }

        if (GUILayout.Button("Crea Struttura Puzzle", GUILayout.Height(32)))
            CreatePuzzle();

        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(
            "Dopo la creazione:\n\n" +
            "1. LAYER: crea il layer 'PuzzleLight' in Project Settings → Tags & Layers.\n" +
            "   Assegna 'PuzzleLight' ai GameObject Prism_XX e LightReceiver.\n\n" +
            "2. EMITTER: su PuzzleEmitter3D configura:\n" +
            "   • Light Layer Mask → PuzzleLight\n" +
            "   • Emit Direction → direzione iniziale del raggio (piano XZ)\n" +
            "   • Beam Height → altezza del raggio (default 1)\n\n" +
            "3. PEDANE: su ogni BeamPedestal3D configura:\n" +
            "   • Player Layer → layer 'Player'\n\n" +
            "4. PRISMI: il BoxCollider (thin in X, tall in Y) è già configurato.\n" +
            "   Orienta emitDirection e posiziona i prism per creare il percorso.\n\n" +
            "5. PLAYER: aggiungi PlayerPuzzleInteractor al PlayerPrefab root.\n\n" +
            "6. NGO: i NetworkObject in-scene sono registrati automaticamente\n" +
            "   da NGO al load della scena. Non serve aggiungerli alla NetworkPrefab list.",
            MessageType.Info);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void CreatePuzzle()
    {
        Undo.SetCurrentGroupName("Create Cathedral Puzzle");
        int group = Undo.GetCurrentGroup();

        // Root container
        var root = CreateEmpty("PuzzleRoot", _parentRoom, Vector3.zero);

        // ── PuzzleRoomManager ─────────────────────────────────────────────
        var managerGo = CreateEmpty("PuzzleRoomManager", root, Vector3.zero);
        Undo.AddComponent<NetworkObject>(managerGo);
        var mgr = Undo.AddComponent<PuzzleRoomManager>(managerGo);

        // ── PuzzleEmitter ─────────────────────────────────────────────────
        var emitterGo = CreateEmpty("PuzzleEmitter", root, _emitterPos);
        Undo.AddComponent<NetworkObject>(emitterGo);
        var emitter = Undo.AddComponent<PuzzleEmitter3D>(emitterGo);

        // ── Pedane ────────────────────────────────────────────────────────
        var pedA = BuildPedestal("Pedestal_A", root, _pedestalAPos, emitter);
        var pedB = BuildPedestal("Pedestal_B", root, _pedestalBPos, emitter);
        SetSerializedRef(pedA.GetComponent<BeamPedestal3D>(), "otherPedestal", pedB.GetComponent<BeamPedestal3D>());
        SetSerializedRef(pedB.GetComponent<BeamPedestal3D>(), "otherPedestal", pedA.GetComponent<BeamPedestal3D>());

        // ── Prismi ────────────────────────────────────────────────────────
        float halfSpan = (_prismCount - 1) * _prismSpacing * 0.5f;
        for (int i = 0; i < _prismCount; i++)
        {
            Vector3 pos = new Vector3(-halfSpan + i * _prismSpacing, 0f, _emitterPos.z + 2f);
            BuildPrism($"Prism_{i + 1:00}", root, pos);
        }

        // ── LightReceiver ─────────────────────────────────────────────────
        var receiverGo = CreateEmpty("LightReceiver", root, _receiverPos);
        var rcol = Undo.AddComponent<BoxCollider>(receiverGo);
        rcol.size = new Vector3(0.6f, 2f, 0.6f);
        var receiver = Undo.AddComponent<LightReceiver3D>(receiverGo);

        var rVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(rVis, "Receiver Visual");
        rVis.transform.SetParent(receiverGo.transform, false);
        rVis.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        Object.DestroyImmediate(rVis.GetComponent<Collider>());
        SetSerializedRef(receiver, "receiverRenderer", rVis.GetComponent<Renderer>());

        // ── Wiring manager → receivers3D ──────────────────────────────────
        var soMgr = new SerializedObject(mgr);
        var arr = soMgr.FindProperty("receivers3D");
        arr.arraySize = 1;
        arr.GetArrayElementAtIndex(0).objectReferenceValue = receiver;
        soMgr.ApplyModifiedPropertiesWithoutUndo();

        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = root;
        Debug.Log("[CathedralPuzzleSetup] Struttura puzzle creata in " + _parentRoom.name);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private GameObject BuildPedestal(string name, GameObject parent, Vector3 localPos, PuzzleEmitter3D emitter)
    {
        var go = CreateEmpty(name, parent, localPos);
        var ped = Undo.AddComponent<BeamPedestal3D>(go);
        SetSerializedRef(ped, "emitter", emitter);

        var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Undo.RegisterCreatedObjectUndo(cyl, name + " Mesh");
        cyl.transform.SetParent(go.transform, false);
        cyl.transform.localScale    = new Vector3(1.5f, 0.05f, 1.5f);
        cyl.transform.localPosition = new Vector3(0f, -0.05f, 0f);
        Object.DestroyImmediate(cyl.GetComponent<Collider>());
        SetSerializedRef(ped, "visual", cyl.GetComponent<Renderer>());

        return go;
    }

    private void BuildPrism(string name, GameObject parent, Vector3 localPos)
    {
        var go = CreateEmpty(name, parent, localPos);
        Undo.AddComponent<NetworkObject>(go);
        var prism = Undo.AddComponent<PrismReflector3D>(go);
        var col   = Undo.AddComponent<BoxCollider>(go);
        col.size  = new Vector3(0.3f, 2f, 1f);

        var railStart = CreateEmpty("RailStart", go, new Vector3(-2f, 0f, 0f));
        var railEnd   = CreateEmpty("RailEnd",   go, new Vector3( 2f, 0f, 0f));

        var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(vis, name + " Visual");
        vis.transform.SetParent(go.transform, false);
        vis.transform.localScale = new Vector3(0.3f, 0.8f, 0.8f);
        Object.DestroyImmediate(vis.GetComponent<Collider>());

        var so = new SerializedObject(prism);
        so.FindProperty("railStart").objectReferenceValue     = railStart.transform;
        so.FindProperty("railEnd").objectReferenceValue       = railEnd.transform;
        so.FindProperty("prismRenderer").objectReferenceValue = vis.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static void SetSerializedRef(Object component, string field, Object value)
    {
        var so = new SerializedObject(component);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject CreateEmpty(string name, GameObject parent, Vector3 localPos)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        if (parent != null)
            go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        return go;
    }
}
