using UnityEngine;

namespace Jerbo.DevConsole { 
public class DevConsoleCache : ScriptableObject
{
    static string[] SEARCH_FOLDERS = { "Assets" };
    [HideInInspector, SerializeField] public ScriptableObject[] AssetReferences;
    [HideInInspector, SerializeField] public string[] AssetNames;
    public int cacheSize;
    
    [DevCommand]
    void LogCache() {
        if (AssetNames == null) {
            Debug.Log("- DevConsle Cache is null! - ");
            return;
        }
        
        if (AssetNames.Length == 0) {
            Debug.Log("- DevConsole Cache is empty! -");
            return;
        }
        
        Debug.Log($"- DevConsole Cached Assets({AssetNames.Length}) -");
        for (int i = 0; i < AssetNames.Length; i++) {
            Debug.Log($"{AssetNames[i]}");
        }

        Debug.Log("- End of cache -");
    }

#if UNITY_EDITOR

    /*
     * Different caching spots
     * enter play mode
     * builds
     */
    
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnEnterGame() {
        Debug.Log("Caching from runtime init");
        CacheAssetReferences();
    }
    
    
    /*
     * Caching method
     * TODO make loading assets async!
     */
    
    [DevCommand("RebuildCache")]
    internal static void CacheAssetReferences() {
        DevConsoleCache[] cacheObjects = Resources.LoadAll<DevConsoleCache>("");

        if (cacheObjects == null || cacheObjects.Length == 0) {
            Debug.Log("Failed to load DevConsoleCache!");
            return;
        }
        DevConsoleCache cache = cacheObjects[0];

        string[] assetGuids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(ScriptableObject)}", SEARCH_FOLDERS);
        cache.AssetReferences = new ScriptableObject[assetGuids.Length];
        cache.AssetNames = new string[assetGuids.Length];
        
        for (int i = 0; i < assetGuids.Length; i++) {
            string guid = assetGuids[i];
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            cache.AssetReferences[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            cache.AssetNames[i] = cache.AssetReferences[i].name;
        }

        cache.cacheSize = assetGuids.Length; 
        Debug.Log($"({cache.name}) Cached -> {cache.AssetReferences.Length} ScriptableObjects");
    }
    
#endif
}

/*
 * Callback when triggering a build
 * Breaking some builds for some reason, disabling for now
 */
#if UNITY_EDITOR
// public class DevConsoleCacheBuildCallback : UnityEditor.Build.IPreprocessBuildWithReport {
//     public int callbackOrder => 0;
//     public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) {
//         Debug.Log("Caching from build");
//         DevConsoleCache.CacheAssetReferences();
//     }
// }
#endif
}
