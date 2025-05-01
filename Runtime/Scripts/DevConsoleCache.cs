using UnityEngine;

namespace Jerbo.DevConsole { 
public class DevConsoleCache : ScriptableObject
{
    public const string ASSET_PATH = "Dev Console Cache";
    static string[] SEARCH_FOLDERS = { "Assets" };
    [HideInInspector, SerializeField] public ScriptableObject[] AssetReferences;
    [HideInInspector, SerializeField] public string[] AssetNames;
    
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
     */
    
    [DevCommand("RebuildCache")]
    internal static void CacheAssetReferences() {
        DevConsoleCache cache = Resources.Load<DevConsoleCache>(ASSET_PATH);
    
        
        string[] assetGuids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(ScriptableObject)}", SEARCH_FOLDERS);
        cache.AssetReferences = new ScriptableObject[assetGuids.Length];
        cache.AssetNames = new string[assetGuids.Length];
        
        for (int i = 0; i < assetGuids.Length; i++) {
            string guid = assetGuids[i];
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            cache.AssetReferences[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            cache.AssetNames[i] = cache.AssetReferences[i].name;
        }
        
        Debug.Log($"DevConsole Cached -> {cache.AssetReferences.Length} ScriptableObjects");
    }
    
#endif
}

/*
 * Callback when triggering a build
 */
#if UNITY_EDITOR
public class DevConsoleCacheBuildCallback : UnityEditor.Build.IPreprocessBuildWithReport {
    public int callbackOrder => 0;
    public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) {
        Debug.Log("Caching from build");
        DevConsoleCache.CacheAssetReferences();
    }
}
#endif
}
