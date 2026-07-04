using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → BoundByLight → Create Ultimate Pickup Prefab
///
/// Crea UltimatePickup.prefab in Assets/_Project/Prefabs/:
///   • Sfera oro/viola emissiva (visivamente diversa da tutto il resto)
///   • SphereCollider trigger, layer Pickup
///   • NetworkObject + UltimatePickup script
///   • GemFloating per l'animazione fluttuante
///
/// Dopo la creazione:
///   1. Trascina il prefab nella scena in RoomUnderground/Props.
///   2. Assegna il campo "Visual" sull'UltimatePickup all'oggetto Visual figlio.
///   3. Verifica che i player abbiano il Tag "Player".
/// </summary>
public static class UltimatePickupCreator
{
    [MenuItem("Tools/BoundByLight/Create Ultimate Pickup Prefab")]
    public static void Create()
    {
        // ── Materiale ─────────────────────────────────────────────────────────
        const string matPath = "Assets/_Project/Materials/UltimatePickup.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.6f, 0.1f, 1f, 1f));   // viola
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.8f, 0.3f, 1f) * 4f); // glow viola intenso
            mat.SetFloat("_Smoothness", 0.9f);
            AssetDatabase.CreateAsset(mat, matPath);
        }

        // ── Root ──────────────────────────────────────────────────────────────
        var root = new GameObject("UltimatePickup");

        // Layer Pickup
        int pickupLayer = LayerMask.NameToLayer("Pickup");
        if (pickupLayer >= 0) root.layer = pickupLayer;

        // SphereCollider trigger
        var col       = root.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius    = 0.8f;

        // NetworkObject
        root.AddComponent<NetworkObject>();

        // UltimatePickup script
        root.AddComponent<UltimatePickup>();

        // ── Visual: sfera viola fluttuante ────────────────────────────────────
        var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        vis.name = "Visual";
        vis.transform.SetParent(root.transform, false);
        vis.transform.localScale    = Vector3.one * 0.55f;
        vis.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        Object.DestroyImmediate(vis.GetComponent<Collider>());
        vis.GetComponent<Renderer>().sharedMaterial = mat;
        if (pickupLayer >= 0) vis.layer = pickupLayer;

        // ── Wiring UltimatePickup → visual ────────────────────────────────────
        var so = new SerializedObject(root.GetComponent<UltimatePickup>());
        so.FindProperty("visual").objectReferenceValue = vis;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ── Salva prefab ──────────────────────────────────────────────────────
        const string prefabPath = "Assets/_Project/Prefabs/UltimatePickup.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[UltimatePickupCreator] Prefab creato: " + prefabPath);
        EditorGUIUtility.PingObject(prefab);
        Selection.activeObject = prefab;
    }
}
