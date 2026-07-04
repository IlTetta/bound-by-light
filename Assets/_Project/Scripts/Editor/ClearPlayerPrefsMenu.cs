using UnityEditor;
using UnityEngine;

public static class ClearPlayerPrefsMenu
{
    [MenuItem("Tools/PlayerPrefs/Clear All")]
    private static void ClearAll()
    {
        PlayerPrefs.DeleteAll();
        Debug.Log("[ClearPlayerPrefsMenu] PlayerPrefs cancellati.");
    }
}
