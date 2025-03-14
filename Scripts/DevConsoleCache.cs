using UnityEngine;

namespace Jerbo.Tools { 
// [CreateAssetMenu]
public class DevConsoleCache : ScriptableObject
{
    
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod, DevCommand("LoadCache")]
    static void CacheAssetReferences() {
        /*
         * Cant use assetdatabase in builds, need a way to load/cache assets
         */
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
        
        

        // assetGuids = UnityEditor.AssetDatabase.FindAssets("t:Scene", SEARCH_FOLDERS);
        // cache.SceneNames = new string[assetGuids.Length];
        // cache.ScenePaths = new string[assetGuids.Length];
        //
        // for (int i = 0; i < assetGuids.Length; i++) {
        //     string guid = assetGuids[i];
        //     string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
        //     // Example path: Assets/Spawn Points/Map 1/Docks In Water.asset
        //     //                              Split -> [ assetName.extension ]
        //     //                              Split -> [ assetName ]
        //
        //     string nameFromPath = path.Split(PATH_SPLITTER)[^1]; // Last split is assetName.extension
        //     string nameWithoutExtension = nameFromPath.Split(ASSET_EXTENSION_SPLITTER)[0]; // First split is assetName
        //     cache.SceneNames[i] = nameWithoutExtension;
        //     cache.ScenePaths[i] = path;
        // }

        // Debug.Log($"DevConsole Cached -> {cache.AssetReferences.Length} ScriptableObjects & {cache.SceneNames.Length} Scenes");
    }
#endif

    static string[] SEARCH_FOLDERS = { "Assets" };
    public const string ASSET_PATH = "Dev Console Cache";
    [HideInInspector] public ScriptableObject[] AssetReferences;
    [HideInInspector] public string[] AssetNames;
    // [HideInInspector] public string[] SceneNames;
    // [HideInInspector] public string[] ScenePaths;
    
    
    // const char PATH_SPLITTER = '/';
    // const char ASSET_EXTENSION_SPLITTER = '.';
}
}
