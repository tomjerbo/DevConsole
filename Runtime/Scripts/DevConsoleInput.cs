using UnityEngine;

namespace Jerbo.DevConsole {
    public static class DevConsoleInput
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
        
        
        public static bool ExecuteCommand(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown) {
                foreach (KeyCode key in EXECUTE_COMMAND) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool CloseConsole(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown) {
                foreach (KeyCode key in CLOSE_CONSOLE) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool OpenConsole(this Event e, bool useOnSuccess = true, params KeyCode[] overrideKeys) {
            if (e.isKey && e.type == EventType.KeyDown) {
                if (overrideKeys != null && overrideKeys.Length > 0) {
                    foreach (KeyCode key in overrideKeys) {
                        if (e.keyCode != key) continue;
                        
                        if (useOnSuccess) e.Use();
                        return true;
                    }

                    return false;
                }
                
                foreach (KeyCode key in OPEN_CONSOLE) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool InsertHint(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown) {
                foreach (KeyCode key in INSERT_HINT) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool NavigateUp(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown) {
                foreach (KeyCode key in NAVIGATE_UP) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool NavigateDown(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown) {
                foreach (KeyCode key in NAVIGATE_DOWN) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool Backspace(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown && e.keyCode == BACKSPACE) {
                if (useOnSuccess) e.Use();
                return true;
            }
            
            return false;
        }
        
        
        static KeyCode[] EXECUTE_COMMAND = { KeyCode.KeypadEnter, KeyCode.Return, };
        static KeyCode[] CLOSE_CONSOLE = { KeyCode.F1, KeyCode.Escape, };
        static KeyCode[] OPEN_CONSOLE = { KeyCode.T };
        static KeyCode[] INSERT_HINT = { KeyCode.KeypadEnter, KeyCode.Return, KeyCode.Tab };
        static KeyCode[] NAVIGATE_UP = { KeyCode.UpArrow, KeyCode.PageUp };
        static KeyCode[] NAVIGATE_DOWN = { KeyCode.DownArrow, KeyCode.PageDown };
        static KeyCode BACKSPACE = KeyCode.Backspace;

    }
}