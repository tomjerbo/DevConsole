using UnityEngine;

namespace Jerbo.DevConsole {
    public class DevConsoleStyle : ScriptableObject {
            
#if JERBO_INSPECTOR
        [Jerbo.Inspector.Tab("Core", Jerbo.Inspector.Tab.Color.Red)]
#else
        [Header("Core")] 
#endif
        public Texture2D ConsoleIcon;
        public Vector2Int ConsoleIconFrames = new (4,1);
        public float ConsolIconAnimSpeed = 1.5f;
        public GUISkin ConsoleSkin;
        
#if JERBO_INSPECTOR
        [Jerbo.Inspector.Tab("Console Colors", Jerbo.Inspector.Tab.Color.Pink)]
#else
        [Header("Console Colors")]
#endif
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

#if JERBO_INSPECTOR
        [Jerbo.Inspector.Tab("Layout", Jerbo.Inspector.Tab.Color.Blue)]
#else
        [Header("Layout")]
#endif
        public float ConsoleTextSize = 36f;
        public float HintBoxBottomPadding = 6;
        public float HintBoxHeightOffset = 0;
        public int ConsoleIconSize = 26;
        
#if JERBO_INSPECTOR
        [Jerbo.Inspector.Tab("Animations", Jerbo.Inspector.Tab.Color.Yellow)]
#else
        [Header("Animations")]
#endif
        public float SelectHintBumpOffsetAmount = 12f;
        public float SelectHintBumpSpeed = 8f;
        public float ArgHelpBumpOffsetAmount = 12f;
        public float ArgHelpBumpSpeed = 8f;
        public float ArgHelpWidthPadding = 8f;
        public AnimationCurve SelectionBumpCurve;
        public AnimationCurve ArgumentTypeBumpCurve;
    }
}