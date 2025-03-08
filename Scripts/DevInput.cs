using UnityEngine;

namespace Jerbo.Tools {
    public static class DevInput
    {

        public static bool KeyUp(this Event e, KeyCode key, bool useOnTrue = true) {
            if (e.isKey && e.keyCode == key && e.type == EventType.KeyUp) {
                if (useOnTrue) e.Use();
                return true;
            }

            return false;
        }
        
        public static bool KeyDown(this Event e, KeyCode key, bool useOnTrue = true) {
            if (e.isKey && e.keyCode == key && e.type == EventType.KeyDown) {
                if (useOnTrue) e.Use();
                return true;
            }

            return false;
        }
        
#if ENABLE_LEGACY_INPUT_MANAGER
        
        static KeyCode openKey = KeyCode.BackQuote;
        static KeyCode exitKey = KeyCode.Escape;
        static KeyCode navigateUp = KeyCode.UpArrow;
        static KeyCode navigateDown = KeyCode.DownArrow;
        
        public static bool OpenConsole() {
            return Input.GetKeyDown(openKey);
        }

        public static int NavigateVertical() {
            if (Input.GetKeyDown(navigateUp)) {
                return 1;
            }
            if (Input.GetKeyDown(navigateDown)) {
                return -1;
            }

            return 0;
        }

        public static bool CloseConsole() {
            return Input.GetKeyDown(exitKey);
        }
#elif ENABLE_INPUT_SYSTEM
        
        static DevConsoleInputs consoleActions = new();
        
        static void ValidateInputIsEnabled() {
            if (consoleActions.DevConsole.enabled == false)
                consoleActions.DevConsole.Enable(); 
        }
        
        public static bool ToggleConsole() {
            ValidateInputIsEnabled();
            return consoleActions.DevConsole.ToggleConsole.WasPerformedThisFrame();
        }

        public static int NavigateVertical() {
            ValidateInputIsEnabled();
            if (consoleActions.DevConsole.NavigateUp.WasPerformedThisFrame()) {
                return 1;
            }
            if (consoleActions.DevConsole.NavigateDown.WasPerformedThisFrame()) {
                return -1;
            }

            return 0;
        }
        
#endif
        
    }
}