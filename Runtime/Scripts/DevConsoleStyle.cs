using UnityEngine;

namespace Jerbo.DevConsole {
    
    [System.Serializable]
    public class DevConsoleStyle : ScriptableObject {
        
        [Header("Core")] 
        [SerializeField] public GUISkin console_skin;
        [SerializeField] public KeyCode[] open_console_key = { KeyCode.T };
        // [SerializeField] public Texture2D console_icon;
        // [SerializeField] public Vector2Int console_icon_frames = new (4,1);
        // [SerializeField] public float console_icon_anim_speed = 1f;
        // [SerializeField] public bool show_icon = true;
        
        [Header("Console Colors")]
        [SerializeField] public Color color_background     = new (0.2561905f, 0.3f, 0.18f, 1f);
        [SerializeField] public Color color_text_default   = new (0.5660378f, 0.5660378f, 0.5660378f, 1f);
        [SerializeField] public Color color_text_selected  = new (1f, 0.8588235f, 0.3333333f, 1f);
        [SerializeField] public Color color_text_valid_cmd = new (1f, 1f, 1f, 1f);
        [SerializeField] public Color color_outline_macro  = new (1f, 0.3f, 0f, 1f);
        
        [Header("Layout")]
        [SerializeField] public float console_text_size = 36f;
        // [SerializeField] public int console_icon_size = 26;
        
        
        [Header("Animations")]
        [SerializeField] public float select_hint_bump_offset_amount = 6f;
        [SerializeField] public float select_hint_bump_speed = 24f;
        // [SerializeField] public float arg_help_bump_offset_amount = -6f;
        // [SerializeField] public float arg_help_bump_speed = 8f;
        // [SerializeField] public float arg_help_width_padding = 8f;
    }
}