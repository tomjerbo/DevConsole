using UnityEngine;

namespace Jerbo.DevConsole { 
public class DevConsoleCache : ScriptableObject
{
    [SerializeField, Tooltip("Including folders that has editor assets can result in errors during builds!")] 
    string[] ScriptableObjectFolder = { "Assets" };
    [SerializeField] public ScriptableObject[] AssetReferences;
    [SerializeField] public string[] AssetNames;

    [DevCommand]
    public void PrintCache() {
        if (AssetNames == null) {
            Debug.LogError("- DevConsoleCache is null! - ");
            return;
        }
        
        if (AssetNames.Length == 0) {
            Debug.LogError("- DevConsoleCache is empty! -");
            return;
        }
        
        Debug.Log($"- DevConsoleCache Assets({AssetNames.Length}) -");
        for (int i = 0; i < AssetNames.Length; i++) {
            Debug.LogError($"{AssetNames[i]}");
        }

        Debug.LogError("- End of cache -");
    }

#if UNITY_EDITOR
    
    /*
     * Different caching spots
     * enter play mode
     * builds
     */
    
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnEnterGame() {
        return; // Disabling runtime caching to lighten load
        Debug.Log("Caching from runtime init");
        CacheAssetReferences_Editor();
    }
    
    
    /*
     * Caching method
     * Make loading assets async!
     * Cache commands if possible
     */
    
    [DevCommand("RebuildCache")]
    public static void CacheAssetReferences_Editor() {
        DevConsoleCache cache = Util.LoadFirstAsset<DevConsoleCache>();
        
        string[] assetGuids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(ScriptableObject)}", cache.ScriptableObjectFolder);
        cache.AssetReferences = new ScriptableObject[assetGuids.Length];
        cache.AssetNames = new string[assetGuids.Length];
        
        for (int i = 0; i < assetGuids.Length; i++) {
            string guid = assetGuids[i];
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            cache.AssetReferences[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            cache.AssetNames[i] = cache.AssetReferences[i].name;
        }

        Debug.Log($"DevConsole Cached -> {cache.AssetReferences.Length} ScriptableObjects");
        UnityEditor.EditorUtility.SetDirty(cache);
    }

    public void RebuildCache_Editor() {
        CacheAssetReferences_Editor();
    }
    
#endif
}
}