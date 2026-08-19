using System.Threading.Tasks;
using UnityEngine;

namespace Jerbo.DevConsole {
public static class CreatePluginAssets
{
    
    [UnityEditor.InitializeOnLoadMethod]
    static async void create_package_folder() {
        
        if (System.IO.Directory.Exists(DevConsole.PLUGINS_FOLDER_PATH) == false) {
            System.IO.Directory.CreateDirectory(DevConsole.PLUGINS_FOLDER_PATH);
        }
        bool should_save_assets = false;

        await Task.Delay(100); // Fixes some strange issue where it doesn't find the assets when importing the package..
        
        /*
         * Cache
         */
        DevConsoleCache console_cache = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleCache>(DevConsole.DEV_CONSOLE_CACHE_PATH);
        if (console_cache == null) {
            Debug.Log($"Could not find {nameof(DevConsoleCache)} at path '{DevConsole.DEV_CONSOLE_CACHE_PATH}'! Creating new.");
            
            console_cache = ScriptableObject.CreateInstance<DevConsoleCache>();
            console_cache.name = nameof(DevConsoleCache);
            UnityEditor.AssetDatabase.CreateAsset(console_cache, DevConsole.DEV_CONSOLE_CACHE_PATH);
            console_cache.RebuildCache_Editor();
            should_save_assets = true;
        }
        
        /*
         * Style & Skin
         */
        DevConsoleStyle console_style = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleStyle>(DevConsole.DEV_CONSOLE_STYLE_PATH);
        if (console_style == null) {
            Debug.Log($"Could not find {nameof(DevConsoleStyle)} at path '{DevConsole.DEV_CONSOLE_STYLE_PATH}'! Creating new.");
            
            GUISkin base_gui_skin = Resources.Load<GUISkin>("Base_Dev Console Skin");
            GUISkin new_skin = Object.Instantiate(base_gui_skin);
            new_skin.name = "DevConsoleSkin";
            UnityEditor.AssetDatabase.CreateAsset(new_skin, DevConsole.DEV_CONSOLE_SKIN_PATH);
            
            
            console_style = ScriptableObject.CreateInstance<DevConsoleStyle>();
            console_style.name = nameof(DevConsoleStyle);
            console_style.console_skin = new_skin;
            UnityEditor.AssetDatabase.CreateAsset(console_style, DevConsole.DEV_CONSOLE_STYLE_PATH);
            
            should_save_assets = true;
        }
        

        if (should_save_assets) {
            UnityEditor.AssetDatabase.SaveAssets();
        }
    }
}

}