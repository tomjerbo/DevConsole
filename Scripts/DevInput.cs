using UnityEngine;

namespace Jerbo.Tools {
    public static class DevInput
    {

        
#if ENABLE_LEGACY_INPUT_MANAGER
        
        static KeyCode toggleConsoleKey = KeyCode.BackQuote;
        static KeyCode navigateUp = KeyCode.UpArrow;
        static KeyCode navigateDown = KeyCode.DownArrow;
        static KeyCode exitKey = KeyCode.Escape;
        
        public static bool ToggleConsole() {
            return Input.GetKeyDown(toggleConsoleKey);
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

        public static bool ExitConsole() {
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