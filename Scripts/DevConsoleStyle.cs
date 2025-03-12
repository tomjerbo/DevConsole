using Jerbo.Inspector;
using UnityEngine;

namespace Jerbo.Tools {
    // [CreateAssetMenu]
    public class DevConsoleStyle : ScriptableObject {
        public const string ASSET_PATH = "Dev Console Style";
        
        [Tab("Colors", Tab.Color.Yellow)]
        public Color BorderColor = Color.white;
        public Color InputTextColor = Color.white;
        public Color HintTextColorDefault = Color.white;
        public Color HintTextColorSelected = Color.white;

        [Tab("Layout", Tab.Color.Pink)]
        public float ConsoleWindowHeight = 36f;
        public float SelectionBumpOffsetAmount = 12f;
        public float SelectionBumpSpeed = 8f;
        public AnimationCurve SelectionBumpCurve;
    }
}