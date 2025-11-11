using UnityEngine;

namespace Jerbo.DevConsole {
    
    [System.Serializable]
    public class DevConsoleStyle : ScriptableObject {
        
        [Header("Core")] 
        [SerializeField] public Texture2D ConsoleIcon;
        [SerializeField] public Vector2Int ConsoleIconFrames = new (4,1);
        [SerializeField] public float ConsolIconAnimSpeed = 1f;
        [SerializeField] public GUISkin ConsoleSkin;
        [SerializeField] public bool keepConsoleOpenAfterCommand;
        [SerializeField] public KeyCode[] openConsoleKey;
        
        [Header("Console Colors")]
        [SerializeField] public Color BackgroundColor = Color.white;
        [SerializeField] public Color InputTextDefault = Color.white;
        [SerializeField] public Color SelectedCommand = Color.white;
        [SerializeField] public Color SelectedArgument = Color.white;
        
        [Space(12)]
        [SerializeField] public Color ValidCommand = Color.white;
        [SerializeField] public Color InputArgumentType = Color.white;
        [SerializeField] public Color InputArgumentTypeBorder = Color.white;
        [SerializeField] public Color RecordMacroColor = Color.white;
        
        [Space(12)]
        [SerializeField] public Color HintTextColorDefault = Color.white;
        [SerializeField] public Color HintTextColorSelected = Color.white;


        [Header("Layout")]
        [SerializeField] public float ConsoleTextSize = 36f;
        [SerializeField] public float HintBoxBottomPadding = 6;
        [SerializeField] public float HintBoxHeightOffset = 0;
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