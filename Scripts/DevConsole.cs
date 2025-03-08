using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = System.Object;

namespace Jerbo.Tools
{
    public class DevConsole : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod]
        public static void Init() {
            GameObject consoleContainer = new ("- Dev Console -");
            consoleContainer.AddComponent<DevConsole>();
        }
        

        /*
         * Static
         */

        
        
        
        /*
         * Const
         */

        const BindingFlags BASE_FLAGS = BindingFlags.Default | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags INSTANCED_BINDING_FLAGS = BASE_FLAGS | BindingFlags.Instance;
        const BindingFlags STATIC_BINDING_FLAGS = BASE_FLAGS | BindingFlags.Static;

        
        const string CONSOLE_INPUT_FIELD_ID = "Console Input Field";
        const float SCREEN_HEIGHT_PERCENTAGE = 0.05f;
        const float WIDTH_SPACING = 8f;
        const float HEIGHT_SPACING = 8f;
        const float HINT_HEIGHT_TEXT_PADDING = 2f;
        const char SPACE = ' ';

        
        
        /*
         * Instanced
         */
        

        // Core
        readonly CommandData[] commands = new CommandData[256];
        Type[] assemblyTypes;
        int totalCommandCount;
        int staticCommandCount;
        bool hasBeenInitialized;
        
        
        // Input
        string consoleInputString = string.Empty;
        int selected;
        int moveToEnd;
        bool isActive;
        int setFocus;

        // Drawing
        GUISkin consoleSkin;
        float consoleWidth;
        float consoleHeight;
        Vector2 consoleInputDrawPos;
        Vector2 consoleInputSize;



        
        /*
         * Core console functionality
         */
        
        void InitializeConsole() {
            consoleSkin = Resources.Load<GUISkin>("Dev Console Skin");
            assemblyTypes = Assembly.GetExecutingAssembly().GetTypes();
        }
        
        void LoadStaticCommands() {
            foreach (Type loadedType in assemblyTypes) {
                MethodInfo[] methodsInType = loadedType.GetMethods(STATIC_BINDING_FLAGS);
                foreach (MethodInfo methodInfo in methodsInType) {
                    DevCommand devCommand = methodInfo.GetCustomAttribute<DevCommand>();
                    if (devCommand == null) continue;
                    
                    CommandData cmdData = new ();
                    cmdData.AssignCommand(devCommand, methodInfo);
                    commands[totalCommandCount++] = cmdData;
                }
            }

            staticCommandCount = totalCommandCount;
        }
        
        void LoadInstanceCommands() {
            MonoBehaviour[] monoBehavioursInScene = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (MonoBehaviour scriptBase in monoBehavioursInScene) {
                MethodInfo[] methodsInType = scriptBase.GetType().GetMethods(INSTANCED_BINDING_FLAGS);
                foreach (MethodInfo methodInfo in methodsInType) {
                    DevCommand devCommand = methodInfo.GetCustomAttribute<DevCommand>();
                    if (devCommand == null) continue;
                    
                    if (HasFoundInstancedCommand(methodInfo, out int index)) {
                        commands[index].AddTarget(scriptBase);
                    }
                    else {
                        CommandData cmdData = new ();
                        cmdData.AssignCommand(devCommand, methodInfo);
                        commands[totalCommandCount++] = cmdData;
                    }
                }
            }
        }
        
        bool HasFoundInstancedCommand(MethodInfo methodInfo, out int index) {
            for (int i = staticCommandCount; i < totalCommandCount; i++) {
                if (commands[i].IsMethod(methodInfo)) {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }
        
        
        
        /*
         * Console Actions
         */
        
        
        void OpenConsole() {
            isActive = true;
            setFocus = 1;
            consoleInputString = string.Empty;

            if (hasBeenInitialized == false) {
                hasBeenInitialized = true;
                InitializeConsole();
                LoadStaticCommands();
            }
            
            LoadInstanceCommands();
        }

        void CloseConsole() {
            totalCommandCount = staticCommandCount;
            isActive = false;
            consoleInputString = string.Empty;
            selected = -1;
            GUI.FocusControl(null);
        }
        
        
        
        
        /*
         * Main logic flow
         */
        
        
        void OnGUI() {
            Event e = Event.current;
            if (isActive == false) {
                if (e.KeyUp(KeyCode.F1)) OpenConsole();
                return;
            }

            /*
             * Console is active
             */
            
            
            if (e.KeyUp(KeyCode.Escape) || e.KeyUp(KeyCode.F1)) CloseConsole();
            
            GUISkin skin = GUI.skin;
            GUI.skin = consoleSkin;
            GUI.backgroundColor = Color.grey;
        
            DrawConsole();
        
            GUI.skin = skin;
        }


        
        
        void DrawConsole() {
            float width = Screen.width;
            float height = Screen.height;
            Event e = Event.current;
            bool hasInputText = consoleInputString.Length > 0;
            
            
            /*
            * Fuzzy search for matching commands
            */
            
            List<CommandData> matchingCommands = new ();
            if (hasInputText) {
                string[] slicedInputString = consoleInputString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (CommandData cmd in commands) {
                    bool matchesAll = true;
                    foreach (string inputSlice in slicedInputString) {
                        if (cmd.GetDisplayName().Contains(inputSlice, StringComparison.OrdinalIgnoreCase) == false) {
                            matchesAll = false;
                            break;
                        }
                    }
                    if (matchesAll) matchingCommands.Add(cmd);
                }
            }
            

            
            /*
             * Hint menu navigation
             */

            if (hasInputText == false || matchingCommands.Count == 0) {
                selected = -1;
            }
            
            if (GUI.GetNameOfFocusedControl() == CONSOLE_INPUT_FIELD_ID) {
                if (selected != -1) {
                    selected = Mathf.Clamp(selected, 0, matchingCommands.Count - 1);

                    if (e.KeyDown(KeyCode.KeypadEnter) || e.KeyDown(KeyCode.Return) || e.KeyDown(KeyCode.Tab)) {
                        consoleInputString = matchingCommands[selected].GetDisplayName() + " ";
                        moveToEnd = 2;
                    }
                    
                }
                
                if (e.KeyDown(KeyCode.DownArrow)) {
                    selected -= 1;
                    selected %= matchingCommands.Count;
                }
                else if (e.KeyDown(KeyCode.UpArrow)) {
                    selected += 1;
                    if (selected < 0) selected = matchingCommands.Count - 1;
                }
            }
            
            
            
            
             
                
            /*
             * draw console input area
             */
            
            consoleInputDrawPos = new Vector2(WIDTH_SPACING, height - (HEIGHT_SPACING + height * SCREEN_HEIGHT_PERCENTAGE));
            consoleInputSize = new Vector2(width - WIDTH_SPACING * 2f, height * SCREEN_HEIGHT_PERCENTAGE);

            GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);
            Rect inputWindowRect = new (consoleInputDrawPos, consoleInputSize);
            consoleInputString = GUI.TextField(inputWindowRect, consoleInputString);
            hasInputText = consoleInputString.Length > 0;


            
            
            
            /*
             * Inputs regarding movement inside the hint window
             */
            
            bool shouldDrawHintWindow = hasInputText && matchingCommands.Count > 0;
            if (shouldDrawHintWindow) {
                DrawHintWindow(matchingCommands);
            }
            else {
                selected = -1;
            }

            
 
            
            
            /*
             * Set focus back to input field
             */
            
            if (setFocus > 0) {
                --setFocus;
                GUI.FocusControl(CONSOLE_INPUT_FIELD_ID);
            }
            
            if (moveToEnd > 0) {
                --moveToEnd;
                TextEditor text = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                text.MoveTextEnd();
            }
            
            GUI.enabled = true;
        }



        void DrawHintWindow(List<CommandData> matchingCommands) {
            
            /*
             * Draw Command Hints
             */
            
            float maximumWidth = 0;
            float maximumHeight = 0;
            GUIContent sizeHelper = new ();
            foreach (CommandData cmd in matchingCommands) {
                sizeHelper.text = cmd.GetDisplayName();
                Vector2 size = consoleSkin.label.CalcSize(sizeHelper);
                maximumWidth = Mathf.Max(size.x, maximumWidth);
                maximumHeight += size.y + HINT_HEIGHT_TEXT_PADDING;
            }
            
            
            Rect hintBackgroundRect = new (consoleInputDrawPos - new Vector2(0, maximumHeight - 2), new Vector2(maximumWidth, maximumHeight));
            GUI.Box(hintBackgroundRect, "");
            Vector2 hintStartPos = hintBackgroundRect.position + new Vector2(0, maximumHeight);
            float stepHeight = maximumHeight / matchingCommands.Count;
            for (int i = 0; i < matchingCommands.Count; i++) {
                CommandData cmd = matchingCommands[i];
                Vector2 pos = hintStartPos - new Vector2(0, (i+1) * stepHeight);
                
                GUI.enabled = i == selected;
                GUI.Label(new Rect(pos, new Vector2(maximumWidth, stepHeight)), cmd.GetDisplayName());
            }
            
        }
        
        
        
        
        class InputCommand {
            internal string inputText;
            internal DevCommand selectedCommand;
            internal List<CommandArgument> commandArguments = new (6);
            
            internal void ResetCommand() {
                selectedCommand = null;
                commandArguments.Clear();
            }
        }


        class CommandArgument {
            internal readonly string displayName;
            internal readonly object argValue;
            
            public CommandArgument(string displayName, object argValue) {
                this.displayName = displayName;
                this.argValue = argValue;
            }
        }

        
        struct CommandData {
            string displayName;
            string hintText;
            MethodInfo method;
            ParameterInfo[] parameters;
            readonly List<Object> targets;

            internal void AssignCommand(DevCommand devCommand, MethodInfo methodInfo) {
                method = methodInfo;
                displayName = devCommand.displayName ?? method.Name;
            
                parameters = method.GetParameters();
                StringBuilder sb = new (84);
                sb.Append(displayName);
                sb.Append(SPACE);
                foreach (ParameterInfo param in parameters) {
                    sb.Append(param.Name);
                    sb.Append(SPACE);
                }
            
                hintText = sb.ToString();
            }

            internal void AddTarget(Object target) => targets.Add(target);
            internal string GetDisplayName() => displayName;
            internal string GetHint() => hintText;
            public bool IsMethod(MethodInfo methodInfo) => method == methodInfo;
        }


        struct HintData {
            public readonly string displayString;
            public readonly string rawValue;
            
            public HintData(string displayString, string rawValue) {
                this.displayString = displayString;
                this.rawValue = rawValue;
            }
        }
    }
}