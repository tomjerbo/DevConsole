using System;
using UnityEngine;
using Object = UnityEngine.Object;

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
        
        public static T[] LoadAllOfType<T>() where T : Object {
            string[] assetGuids = UnityEditor.AssetDatabase.FindAssets( $"t:{typeof(T)}", new[] { "Assets/" } );
            if (assetGuids == null || assetGuids.Length == 0) {
                Debug.LogError($"Could not find any assets of type {typeof(T)}!");
                return Array.Empty<T>();
            }

            T[] assets = new T[assetGuids.Length];
            for (int idx = 0; idx < assetGuids.Length; idx++) {
                string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuids[idx]);
                assets[idx] = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);
            }

            return assets;
        }


        public static bool inside(this Vector2 pos, Vector2 min, Vector2 max) {
            return pos.x >= min.x && pos.x <= max.x && pos.y >= min.y && pos.y <= max.y;
        }
        
#endif
    }
}