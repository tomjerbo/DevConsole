using UnityEngine;

namespace Jerbo.Tools { 
// [CreateAssetMenu]
public class DevConsoleCache : ScriptableObject
{
    
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod, DevCommand("LoadCache")]
    static void CacheAssetReferences() {
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

        // Debug.Log($"DevConsole Cached -> {cache.AssetReferences.Length} ScriptableObjects & {cache.SceneNames.Length} Scenes");
    }
#endif

    static string[] SEARCH_FOLDERS = { "Assets" };
    public const string ASSET_PATH = "Dev Console Cache";
    [HideInInspector] public ScriptableObject[] AssetReferences;
    [HideInInspector] public string[] AssetNames;
}
}
