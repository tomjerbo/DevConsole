using UnityEngine;

namespace Jerbo.DevConsole {
    public static class DevConsoleInput
    {

        public static bool key_up(this Event e, KeyCode key, bool useOnTrue = true) {
            if (e.isKey && e.keyCode == key && e.type == EventType.KeyUp) {
                if (useOnTrue) e.Use();
                return true;
            }

            return false;
        }
        
        public static bool key_down(this Event e, KeyCode key, bool useOnTrue = true) {
            if (e.isKey && e.keyCode == key && e.type == EventType.KeyDown) {
                if (useOnTrue) e.Use();
                return true;
            }

            return false;
        }
        
        public static bool mouse_down(this Event e, KeyCode key, bool useOnTrue = true) {
            if (e.isMouse && e.keyCode == key && e.type == EventType.MouseDown) {
                if (useOnTrue) e.Use();
                return true;
            }

            return false;
        }
        
        public static bool mouse_up(this Event e, KeyCode key, bool useOnTrue = true) {
            if (e.isMouse && e.keyCode == key && e.type == EventType.MouseUp) {
                if (useOnTrue) e.Use();
                return true;
            }

            return false;
        }
        
        public static bool execute_command(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown) {
                foreach (KeyCode key in EXECUTE_COMMAND) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool close_console(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyUp) {
                foreach (KeyCode key in CLOSE_CONSOLE) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool open_console(this Event e, bool useOnSuccess = true, params KeyCode[] overrideKeys) {
            if (e.isKey && e.type == EventType.KeyUp) {
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
        
        public static bool insert_hint(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown) {
                foreach (KeyCode key in INSERT_HINT) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool navigate_up(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown) {
                foreach (KeyCode key in NAVIGATE_UP) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool navigate_down(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown) {
                foreach (KeyCode key in NAVIGATE_DOWN) {
                    if (e.keyCode != key) continue;
                    
                    if (useOnSuccess) e.Use();
                    return true;
                }
            }
            
            return false;
        }
        
        public static bool backspace(this Event e, bool useOnSuccess = true) {
            if (e.isKey && e.type == EventType.KeyDown && e.keyCode == BACKSPACE) {
                if (useOnSuccess) e.Use();
                return true;
            }
            
            return false;
        }
        
        
        static KeyCode[] EXECUTE_COMMAND = { KeyCode.KeypadEnter, KeyCode.Return, };
        static KeyCode[] CLOSE_CONSOLE = { KeyCode.Escape, };
        static KeyCode[] OPEN_CONSOLE = { KeyCode.T };
        static KeyCode[] INSERT_HINT = { KeyCode.KeypadEnter, KeyCode.Return, KeyCode.Tab };
        static KeyCode[] NAVIGATE_UP = { KeyCode.UpArrow, KeyCode.PageUp };
        static KeyCode[] NAVIGATE_DOWN = { KeyCode.DownArrow, KeyCode.PageDown };
        static KeyCode BACKSPACE = KeyCode.Backspace;
    }
}