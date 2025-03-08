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
        readonly InputCommand inputCommand = new ();
        readonly InputHint[] inputHints = new InputHint[32];
        readonly StringBuilder stringBuilder = new (256);
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
                    
                    commands[totalCommandCount++].AssignCommand(devCommand, methodInfo, null);
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
                        commands[totalCommandCount++].AssignCommand(devCommand, methodInfo, scriptBase);
                    }
                }
            }
        }
        
        bool HasFoundInstancedCommand(MethodInfo methodInfo, out int index) {
            for (int i = staticCommandCount; i < totalCommandCount; i++) {
                if (commands[i].IsSameMethod(methodInfo)) {
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
            inputCommand.Reset();

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
            
            

            int hintAmount = BuildHints();
            
            
            /*
             * Hint menu navigation
             */

            if (inputCommand.HasText() == false || hintAmount == 0) {
                selected = -1;
            }
            
            if (GUI.GetNameOfFocusedControl() == CONSOLE_INPUT_FIELD_ID && hintAmount > 0) {
                if (selected != -1) {
                    selected = Mathf.Clamp(selected, 0, hintAmount - 1);

                    if (e.KeyDown(KeyCode.KeypadEnter) || e.KeyDown(KeyCode.Return) || e.KeyDown(KeyCode.Tab)) {
                        
                        // Apply hint
                        moveToEnd = 2;
                    }
                }
                
                if (e.KeyDown(KeyCode.DownArrow)) {
                    selected -= 1;
                    if (selected < 0) selected = hintAmount - 1;
                }
                else if (e.KeyDown(KeyCode.UpArrow)) {
                    selected += 1;
                    selected %= hintAmount;
                }
            }
            
            
            
            
             
                
            /*
             * draw console input area
             */
            
            consoleInputDrawPos = new Vector2(WIDTH_SPACING, height - (HEIGHT_SPACING + height * SCREEN_HEIGHT_PERCENTAGE));
            consoleInputSize = new Vector2(width - WIDTH_SPACING * 2f, height * SCREEN_HEIGHT_PERCENTAGE);

            GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);
            Rect inputWindowRect = new (consoleInputDrawPos, consoleInputSize);
            inputCommand.inputText = GUI.TextField(inputWindowRect, inputCommand.inputText);


            
            
            /*
             * Inputs regarding movement inside the hint window
             */
            
            if (inputCommand.HasText() && hintAmount > 0) {
                DrawHintWindow(hintAmount);
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
            
        }



        void DrawHintWindow(int hintAmount) {
            /*
             * Draw Command Hints
             */
            
            float maximumWidth = 0;
            float maximumHeight = 0;
            GUIContent sizeHelper = new ();
            for (int i = 0; i < hintAmount; i++) {
                sizeHelper.text = inputHints[i].displayString;
                Vector2 size = consoleSkin.label.CalcSize(sizeHelper);
                maximumWidth = Mathf.Max(size.x, maximumWidth);
                maximumHeight += size.y + HINT_HEIGHT_TEXT_PADDING;
            }
            
            
            Rect hintBackgroundRect = new (consoleInputDrawPos - new Vector2(0, maximumHeight - 2), new Vector2(maximumWidth, maximumHeight));
            GUI.Box(hintBackgroundRect, "");
            Vector2 hintStartPos = hintBackgroundRect.position + new Vector2(0, maximumHeight);
            float stepHeight = maximumHeight / hintAmount;
            for (int i = 0; i < hintAmount; i++) {
                InputHint hint = inputHints[i];
                Vector2 pos = hintStartPos - new Vector2(0, (i+1) * stepHeight);
                
                GUI.enabled = i == selected;
                GUI.Label(new Rect(pos, new Vector2(maximumWidth, stepHeight)), hint.displayString);
            }
            
            GUI.enabled = true;
        }

        

        int BuildHints() {
            int hintsFound = 0;

            if (inputCommand.HasText() == false) {
                return hintsFound;
            }

            
            // TODO wanna add sorting based on how many matches we have, best result at the top
            // Fill with commands that match
            if (inputCommand.HasCommand() == false) {
                string[] inputWords = inputCommand.inputText.Split(SPACE, StringSplitOptions.RemoveEmptyEntries);
               
                for (int i = 0; i < totalCommandCount; i++) {
                    
                    bool matchingHint = true; 
                    foreach (string word in inputWords) {
                        if (commands[i].GetDisplayName().Contains(word, StringComparison.OrdinalIgnoreCase) == false) {
                            matchingHint = false;
                            break;
                        }
                    }

                    if (matchingHint) {
                        stringBuilder.Clear();
                        stringBuilder.Append(commands[i].GetFullHint());
                        stringBuilder.Append(SPACE);

                        inputHints[hintsFound++].SetHint(stringBuilder, commands[i].GetDisplayName());
                    }
                }
            }
            else { // Has command, look for arguments
                
            }

            return hintsFound;
        }
        
        
        
        
        
        
        
        
        
        class InputCommand {
            internal string inputText;
            internal DevCommand selectedCommand;
            internal List<InputArgument> commandArguments = new (6);
            
            internal void Reset() {
                inputText = string.Empty;
                selectedCommand = null;
                commandArguments.Clear();
            }

            internal void RemoveSelection() {
                selectedCommand = null;
                commandArguments.Clear();
            }

            internal bool HasText() => string.IsNullOrEmpty(inputText) == false;
            internal bool HasCommand() => selectedCommand != null;
        }


        class InputArgument {
            internal readonly string displayName;
            internal readonly object argValue;
            
            public InputArgument(string displayName, object argValue) {
                this.displayName = displayName;
                this.argValue = argValue;
            }
        }

        
        struct CommandData {
            string displayName;
            string hintText;
            MethodInfo method;
            ParameterInfo[] parameters;
            List<Object> targets;

            internal void AssignCommand(DevCommand devCommand, MethodInfo methodInfo, Object target) {
                method = methodInfo;
                displayName = string.IsNullOrEmpty(devCommand.displayName) ? method.Name : devCommand.displayName;
                
                parameters = method.GetParameters();
                StringBuilder sb = new (84);
                sb.Append(displayName);
                sb.Append(SPACE);
                foreach (ParameterInfo param in parameters) {
                    sb.Append(param.Name);
                    sb.Append(SPACE);
                }
                hintText = sb.ToString();
            
                
                if (targets == null) targets = new List<Object>();
                else targets.Clear();
                targets.Add(target);
            }

            internal void AddTarget(Object target) => targets.Add(target);
            internal string GetDisplayName() => displayName;
            internal string GetFullHint() => hintText;
            public bool IsSameMethod(MethodInfo methodInfo) => method == methodInfo;
        }


        struct InputHint {
            internal string displayString;
            internal string outputString;

            public void SetHint(StringBuilder builder, string outputValue) {
                displayString = builder.ToString();
                outputString = outputValue;
            }
        }
    }
}