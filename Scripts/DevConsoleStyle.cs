using Jerbo.Inspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace Jerbo.Tools {
    public class DevConsoleStyle : ScriptableObject {
        public const string ASSET_PATH = "Dev Console Style";
        
        [Tab("Console Colors", Tab.Color.Yellow)]
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
        public float HintWindowHeightOffset = 0f;
        public float SelectionBumpOffsetAmount = 12f;
        public float SelectionBumpSpeed = 8f;
        public float ArgumentTypeOffsetAmount = 12f;
        public float ArgumentTypeSpeed = 8f;
        public float ArgumentTypeHintSpacing = 8f;
        public AnimationCurve SelectionBumpCurve;
        public AnimationCurve ArgumentTypeBumpCurve;
    }
}