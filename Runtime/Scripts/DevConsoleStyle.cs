using UnityEngine;

namespace Jerbo.DevConsole {
    
    [System.Serializable]
    public class DevConsoleStyle : ScriptableObject {
        
        [Header("Core")] 
        // [SerializeField] public Texture2D console_icon;
        // [SerializeField] public Vector2Int console_icon_frames = new (4,1);
        // [SerializeField] public float consol_icon_anim_speed = 1f;
        [SerializeField] public GUISkin console_skin;
        // [SerializeField] public bool show_icon = true;
        [SerializeField] public KeyCode[] open_console_key;
        
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
        // [SerializeField] public int console_icon_size = 26;
        
        
        [Header("Animations")]
        [SerializeField] public float select_hint_bump_offset_amount = 6f;
        [SerializeField] public float select_hint_bump_speed = 12f;
        [SerializeField] public float arg_help_bump_offset_amount = -6f;
        [SerializeField] public float arg_help_bump_speed = 8f;
        [SerializeField] public float arg_help_width_padding = 8f;
        [SerializeField] public AnimationCurve selection_bump_curve;
        [SerializeField] public AnimationCurve argument_type_bump_curve;
    }
}