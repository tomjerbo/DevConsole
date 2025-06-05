using UnityEngine;

namespace Jerbo.DevConsole {
    public static class Util {
#if UNITY_EDITOR
        public static T LoadFirstAsset<T>() where T : Object {
            string[] assetGuids = UnityEditor.AssetDatabase.FindAssets( $"t:{typeof(T)}", new[] { "Assets/" } );
            if (assetGuids == null || assetGuids.Length == 0) {
                Debug.LogError($"Could not find any assets of type {typeof(T)}!");
                return null;
            }
            
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuids[0]);
            T asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null) {
                Debug.LogError($"Loaded asset is null! Path: {assetPath}");
                return null;
            }
            
            return asset;
        }
#endif
    }
}