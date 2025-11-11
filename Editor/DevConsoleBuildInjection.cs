using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jerbo.DevConsole {
    public class DevConsoleBuildInjection : UnityEditor.Build.IProcessSceneWithReport {
        
        const string CONSOLE_NAME = "- Dev Console (Build) -";
        public int callbackOrder => 0;
        
        public void OnProcessScene(Scene scene, BuildReport report) {
            if (UnityEditor.BuildPipeline.isBuildingPlayer == false) {
                return;
            }
            
            Debug.Log($"Processing -> {scene.name}, {scene.buildIndex}, {scene.path}");
            if (scene.buildIndex == 0) {
                DevConsole[] consoles = Object.FindObjectsByType<DevConsole>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (consoles != null && consoles.Length > 0) {
                    Debug.Log("Dev console already exist in scene!");
                    return;
                }
                
                GameObject consoleObject = new (CONSOLE_NAME);
                DevConsole devConsole = consoleObject.AddComponent<DevConsole>();
                
                DevConsoleCache cache = Util.LoadFirstAsset<DevConsoleCache>();
                DevConsoleStyle style = Util.LoadFirstAsset<DevConsoleStyle>();
                cache.RebuildCache_Editor();
                devConsole.SetupRefsForBuild(cache, style);
                UnityEditor.EditorUtility.SetDirty(cache);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(cache);
                
                Debug.Log("DevConsole added to build!");
            }
            else {
                DevConsole[] consoles = Object.FindObjectsByType<DevConsole>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (consoles != null) {
                    for (int i = 0; i < consoles.Length; i++) {
                        Object.DestroyImmediate(consoles[i].gameObject);
                        Debug.Log("Removed duplicate instance of DevConsole!");
                    }
                }
            }
        }
    }
}