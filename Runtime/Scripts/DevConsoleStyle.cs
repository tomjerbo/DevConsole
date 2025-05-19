using UnityEngine;

namespace Jerbo.DevConsole {
    public class DevConsoleStyle : ScriptableObject {
        public const string BASE_ASSET_PATH = "Base_Dev Console Style";
        
        [Header("Console Colors")]
        public Color BackgroundColor = Color.white;
        public Color InputTextDefault = Color.white;
        public Color SelectedCommand = Color.white;
        public Color SelectedArgument = Color.white;
        
        [Space(12)]
        public Color ValidCommand = Color.white;
        public Color InputArgumentType = Color.white;
        public Color InputArgumentTypeBorder = Color.white;
        public Color RecordMacroColor = Color.white;
        
        [Space(12)]
        public Color HintTextColorDefault = Color.white;
        public Color HintTextColorSelected = Color.white;

        
        [Header("Layout")]
        public float ConsoleTextSize = 36f;
        public float HintBoxBottomPadding = 6;
        public float HintBoxHeightOffset = 0;
        public int ConsoleIconSize = 26;
        
        
        [Header("Animations")]
        public float SelectHintBumpOffsetAmount = 12f;
        public float SelectHintBumpSpeed = 8f;
        public float ArgHelpBumpOffsetAmount = 12f;
        public float ArgHelpBumpSpeed = 8f;
        public float ArgHelpWidthPadding = 8f;
        public AnimationCurve SelectionBumpCurve;
        public AnimationCurve ArgumentTypeBumpCurve;
        
        [Header("Textures")] 
        public Texture2D ConsoleIcon;
    }
}