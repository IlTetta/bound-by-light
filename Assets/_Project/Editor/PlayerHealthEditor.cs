using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PlayerHealth))]
public class PlayerHealthEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Disegna i campi normali dell'inspector
        DrawDefaultInspector();

        EditorGUILayout.Space();

        // La tua HelpBox di tipo Info
        EditorGUILayout.HelpBox(
            "Premi 9 per togliere 20 HP | Premi 0 per aggiungere 20 HP",
            MessageType.Info
        );

        if (GUI.changed)
            EditorUtility.SetDirty(target);
    }
}
