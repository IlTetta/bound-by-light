using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools → BoundByLight → Create HolyBolt Prefab
///
/// Crea HolyBolt.prefab in Assets/_Project/Prefabs/Weapon/ con:
///   • Sfera emissiva oro sacra (visivamente distinta dai proiettili player)
///   • SphereCollider trigger, Rigidbody kinematic, ContinuousDynamic
///   • HolyBolt script (estende Projectile3D) preconfigurato
///   • NetworkObject
///   • TrailRenderer scia dorata
///   • Point Light per glow locale
///
/// Dopo la creazione:
///   1. Assegna il prefab al campo "Projectile Prefab" di NetworkProjectileWeapon
///      sul root del MiniBoss.
///   2. (Opzionale) Assegna un prefab Impact VFX al campo "_impactVfxPrefab"
///      e un evento FMOD al campo "_impactSfx".
///   3. Registra il prefab nei Network Prefabs del NetworkManager.
/// </summary>
public static class HolyBoltCreator
{
    private const string PrefabPath  = "Assets/_Project/Prefabs/Weapon/HolyBolt.prefab";
    private const string MatPath     = "Assets/_Project/Materials/HolyBolt.mat";
    private const string TrailMatPath = "Assets/_Project/Materials/HolyBoltTrail.mat";

    [MenuItem("Tools/BoundByLight/Create HolyBolt Prefab")]
    public static void Create()
    {
        EnsureDirectoryExists("Assets/_Project/Prefabs/Weapon");
        EnsureDirectoryExists("Assets/_Project/Materials");

        // ── Materiale corpo ───────────────────────────────────────────────────
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(1f, 0.92f, 0.4f, 1f));   // oro chiaro
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.1f) * 4f); // glow oro intenso
            mat.SetFloat("_Smoothness", 0.85f);
            mat.SetFloat("_Metallic", 0.3f);
            AssetDatabase.CreateAsset(mat, MatPath);
        }

        // ── Materiale scia ────────────────────────────────────────────────────
        var trailMat = AssetDatabase.LoadAssetAtPath<Material>(TrailMatPath);
        if (trailMat == null)
        {
            trailMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            trailMat.SetColor("_BaseColor", new Color(1f, 0.75f, 0.1f, 0.7f)); // oro semi-trasparente
            AssetDatabase.CreateAsset(trailMat, TrailMatPath);
        }

        // ── Root ──────────────────────────────────────────────────────────────
        var root = new GameObject("HolyBolt");

        // Rigidbody — kinematic, nessuna gravità, nessuna rotazione
        var rb                    = root.AddComponent<Rigidbody>();
        rb.useGravity             = false;
        rb.isKinematic            = true;
        rb.freezeRotation         = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // SphereCollider trigger (abilitato da Projectile3D.Initialize)
        var col       = root.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius    = 0.25f;
        col.enabled   = false; // Projectile3D lo abilita dopo InitialInvulnerabilityDuration

        // NetworkObject — spawn in rete
        root.AddComponent<NetworkObject>();

        // HolyBolt script — configura i campi pubblici ereditati da Projectile3D
        var bolt = root.AddComponent<HolyBolt>();
        bolt.Speed                          = 14f;
        bolt.Acceleration                   = 0f;
        bolt.MaxSpeed                       = 14f;
        bolt.FaceMovement                   = true;    // orienta la sfera nella direzione di volo
        bolt.InitialInvulnerabilityDuration = 0.05f;
        bolt.LifeTime                       = 4f;
        bolt.DamageOwner                    = false;
        bolt.Damage                         = 15;      // sovrascritto dal Brain via SetDamageOverride
        bolt.BlockingLayers                 = LayerMask.GetMask("Wall", "Obstacle");

        // Campi privati di HolyBolt tramite SerializedObject
        var boltSo = new SerializedObject(bolt);
        boltSo.FindProperty("_impactVfxDuration").floatValue = 1.5f;
        // _impactVfxPrefab e _impactSfx rimangono null/vuoti: assegnali manualmente dopo la creazione
        boltSo.ApplyModifiedPropertiesWithoutUndo();

        // ── TrailRenderer: scia dorata ────────────────────────────────────────
        var trail               = root.AddComponent<TrailRenderer>();
        trail.time              = 0.18f;
        trail.startWidth        = 0.22f;
        trail.endWidth          = 0f;
        trail.minVertexDistance = 0.04f;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows    = false;
        trail.sharedMaterial    = trailMat;

        // ── Visual: sfera emissiva dorata ─────────────────────────────────────
        var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        vis.name = "Visual";
        vis.transform.SetParent(root.transform, false);
        vis.transform.localScale    = Vector3.one * 0.28f;
        vis.transform.localPosition = Vector3.zero;
        Object.DestroyImmediate(vis.GetComponent<Collider>()); // collider già sul root
        vis.GetComponent<Renderer>().sharedMaterial    = mat;
        vis.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        vis.GetComponent<Renderer>().receiveShadows    = false;

        // ── Point Light: glow locale ──────────────────────────────────────────
        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(vis.transform, false);
        var light        = lightGo.AddComponent<Light>();
        light.type       = LightType.Point;
        light.color      = new Color(1f, 0.85f, 0.2f);
        light.intensity  = 2.5f;
        light.range      = 3.5f;
        light.shadows    = LightShadows.None;

        // ── Salva prefab ───────────────────────────────────────────────────────
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[HolyBoltCreator] Prefab creato: {PrefabPath}\n" +
                  "Ricorda di:\n" +
                  "  1. Assegnare _impactVfxPrefab (effetto impatto) nell'Inspector\n" +
                  "  2. Assegnare _impactSfx (evento FMOD) nell'Inspector\n" +
                  "  3. Registrare il prefab nei Network Prefabs del NetworkManager");

        EditorGUIUtility.PingObject(prefab);
        Selection.activeObject = prefab;
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    /// <summary>Crea la cartella (e le eventuali parent) se non esiste già.</summary>
    private static void EnsureDirectoryExists(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string folder = System.IO.Path.GetFileName(path);

        EnsureDirectoryExists(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }
}
