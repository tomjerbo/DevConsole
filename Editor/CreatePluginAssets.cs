using System.Threading.Tasks;
using UnityEngine;

namespace Jerbo.DevConsole {
public static class CreatePluginAssets
{
    [UnityEditor.InitializeOnLoadMethod]
    static async void CreatePackageFolder() {
        
        if (System.IO.Directory.Exists(DevConsole.PLUGINS_FOLDER_PATH) == false) {
            System.IO.Directory.CreateDirectory(DevConsole.PLUGINS_FOLDER_PATH);
        }
        bool shouldSaveAssets = false;

        await Task.Delay(100); // Fixes some strange issue where it doesn't find the assets when importing the package..
        
        /*
         * Cache
         */
        DevConsoleCache consoleCache = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleCache>(DevConsole.DEV_CONSOLE_CACHE_PATH);
        if (consoleCache == null) {
            Debug.LogError($"Could not find {nameof(DevConsoleCache)} at path '{DevConsole.DEV_CONSOLE_CACHE_PATH}'! Creating new.");
            
            consoleCache = ScriptableObject.CreateInstance<DevConsoleCache>();
            consoleCache.name = nameof(DevConsoleCache);
            UnityEditor.AssetDatabase.CreateAsset(consoleCache, DevConsole.DEV_CONSOLE_CACHE_PATH);
            shouldSaveAssets = true;
        }
        
        /*
         * Style
         */
        DevConsoleStyle consoleStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleStyle>(DevConsole.DEV_CONSOLE_STYLE_PATH);
        if (consoleStyle == null) {
            Debug.LogError($"Could not find {nameof(DevConsoleStyle)} at path '{DevConsole.DEV_CONSOLE_STYLE_PATH}'! Creating new.");
            
            GUISkin baseGuiSkin = Resources.Load<GUISkin>("Base_Dev Console Skin");
            GUISkin newSkin = Object.Instantiate(baseGuiSkin);
            newSkin.name = "DevConsoleSkin";
            UnityEditor.AssetDatabase.CreateAsset(newSkin, DevConsole.DEV_CONSOLE_SKIN_PATH);
            
            
            DevConsoleStyle baseStyleAsset = Resources.Load<DevConsoleStyle>("Base_Dev Console Style");
            DevConsoleStyle newStyle = Object.Instantiate(baseStyleAsset);
            consoleStyle = newStyle;
            consoleStyle.name = nameof(DevConsoleStyle);
            consoleStyle.ConsoleSkin = newSkin;
            UnityEditor.AssetDatabase.CreateAsset(consoleStyle, DevConsole.DEV_CONSOLE_STYLE_PATH);
            
            shouldSaveAssets = true;
        }
        

        if (shouldSaveAssets) {
            UnityEditor.AssetDatabase.SaveAssets();
        }
    }
}

}