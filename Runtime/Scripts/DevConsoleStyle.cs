using Jerbo.Inspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace Jerbo.DevConsole {
    public class DevConsoleStyle : ScriptableObject {
        public const string ASSET_PATH = "Dev Console Style";
        
        [Tab("Console Colors", Tab.Color.Green)]
        public Color BorderColor = Color.white;
        public Color InputTextDefault = Color.white;
        public Color SelectedCommand = Color.white;
        public Color SelectedArgument = Color.white;
        
        [Space(12)]
        public Color ValidCommand = Color.white;
        public Color InputArgumentType = Color.white;
        public Color InputArgumentTypeBorder = Color.white;
        
        [Space(12)]
        public Color HintTextColorDefault = Color.white;
        public Color HintTextColorSelected = Color.white;

        
        [Tab("Layout", Tab.Color.Pink)]
        public float ConsoleWindowHeight = 36f;
        public float HintBoxHeightPadding = 12f;
        
        
        [Tab("Animations", Tab.Color.Blue)]
        public float SelectHintBumpOffsetAmount = 12f;
        public float SelectHintBumpSpeed = 8f;
        public float ArgHelpBumpOffsetAmount = 12f;
        public float ArgHelpBumpSpeed = 8f;
        public float ArgHelpWidthPadding = 8f;
        public AnimationCurve SelectionBumpCurve;
        public AnimationCurve ArgumentTypeBumpCurve;
    }
}