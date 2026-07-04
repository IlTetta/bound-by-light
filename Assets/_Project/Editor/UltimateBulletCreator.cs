using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → BoundByLight → Create Ultimate Bullet Prefab
///
/// Crea Bullet_Ultimate.prefab in Assets/_Project/Prefabs/Weapon/:
///   • Sfera dorata emissiva (visivamente diversa dai proiettili normali)
///   • SphereCollider trigger, Rigidbody kinematic
///   • Projectile3D configurato (velocità, lifetime)
///   • NetworkObject
///   • TrailRenderer per la scia
///
/// Dopo la creazione assegna il prefab al campo
/// "Ultimate Projectile Prefab" di TetherCombat.
/// </summary>
public static class UltimateBulletCreator
{
    [MenuItem("Tools/BoundByLight/Create Ultimate Bullet Prefab")]
    public static void Create()
    {
        // ── Materiale ─────────────────────────────────────────────────────────
        const string matPath = "Assets/_Project/Materials/BulletUltimate.mat";

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.05f, 1f));   // oro
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(1f, 0.6f, 0f) * 3f);  // glow arancio
        mat.SetFloat("_Smoothness", 0.8f);

        AssetDatabase.CreateAsset(mat, matPath);

        // Materiale scia
        const string trailMatPath = "Assets/_Project/Materials/BulletUltimateTrail.mat";
        var trailMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        trailMat.SetColor("_BaseColor", new Color(1f, 0.5f, 0f, 0.6f));
        AssetDatabase.CreateAsset(trailMat, trailMatPath);

        // ── Root GameObject ────────────────────────────────────────────────────
        var root = new GameObject("Bullet_Ultimate");

        // Rigidbody — kinematic, no gravity, freeze Y pos e tutte le rotazioni
        var rb                    = root.AddComponent<Rigidbody>();
        rb.useGravity             = false;
        rb.isKinematic            = true;
        rb.freezeRotation         = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Collider trigger — sferico, più grande dei proiettili normali
        var col       = root.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius    = 0.22f;

        // NetworkObject — obbligatorio per spawn in rete
        root.AddComponent<NetworkObject>();

        // Projectile3D — parametri distinti da Bullet_Light
        var proj                           = root.AddComponent<Projectile3D>();
        proj.Speed                         = 18f;
        proj.Acceleration                  = 0f;
        proj.MaxSpeed                      = 18f;
        proj.FaceMovement                  = false;   // sfera: non ha verso
        proj.InitialInvulnerabilityDuration = 0.03f;
        proj.LifeTime                      = 6f;
        proj.DamageOwner                   = false;
        proj.Damage                        = 30;

        // ── Visual: sfera dorata ───────────────────────────────────────────────
        var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        vis.name = "Visual";
        vis.transform.SetParent(root.transform, false);
        vis.transform.localScale = Vector3.one * 0.38f;
        Object.DestroyImmediate(vis.GetComponent<Collider>());
        vis.GetComponent<Renderer>().sharedMaterial = mat;

        // ── TrailRenderer: scia arancione ─────────────────────────────────────
        var trail                = root.AddComponent<TrailRenderer>();
        trail.time               = 0.25f;
        trail.startWidth         = 0.3f;
        trail.endWidth           = 0f;
        trail.minVertexDistance  = 0.05f;
        trail.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.sharedMaterial     = trailMat;

        // ── Salva prefab ───────────────────────────────────────────────────────
        const string prefabPath = "Assets/_Project/Prefabs/Weapon/Bullet_Ultimate.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[UltimateBulletCreator] Prefab creato: " + prefabPath);
        EditorGUIUtility.PingObject(prefab);
        Selection.activeObject = prefab;
    }
}
