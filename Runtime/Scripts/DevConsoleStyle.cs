using UnityEngine;

namespace Jerbo.DevConsole {
    
    [System.Serializable]
    public class DevConsoleStyle : ScriptableObject {
        
        [Header("Core")] 
        [SerializeField] public Texture2D ConsoleIcon;
        [SerializeField] public Vector2Int ConsoleIconFrames = new (4,1);
        [SerializeField] public float ConsolIconAnimSpeed = 1f;
        [SerializeField] public GUISkin ConsoleSkin;
        [SerializeField] public bool show_icon = true;
        [SerializeField] public bool keepConsoleOpenAfterCommand;
        [SerializeField] public KeyCode[] openConsoleKey;
        
        /*
         * select #FFDB55
         * bg #232F0E
         */
        [Header("Console Colors")]
        [SerializeField] public Color color_background = Color.white;
        [SerializeField] public Color color_text_default = Color.white;
        [SerializeField] public Color color_text_selected = Color.white;
        [SerializeField] public Color color_text_valid_cmd = Color.white;
        [SerializeField] public Color color_outline_macro = Color.white;
        
        [Header("Layout")]
        [SerializeField] public float console_text_size = 26f;
        [SerializeField] public int ConsoleIconSize = 26;
        
        
        [Header("Animations")]
        [SerializeField] public float SelectHintBumpOffsetAmount = 12f;
        [SerializeField] public float SelectHintBumpSpeed = 8f;
        [SerializeField] public float ArgHelpBumpOffsetAmount = 12f;
        [SerializeField] public float ArgHelpBumpSpeed = 8f;
        [SerializeField] public float ArgHelpWidthPadding = 8f;
        [SerializeField] public AnimationCurve SelectionBumpCurve;
        [SerializeField] public AnimationCurve ArgumentTypeBumpCurve;
    }
}