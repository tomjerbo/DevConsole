using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Jerbo.Tools
{
    public class DevConsole : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod]
        public static void SpawnConsoleInScene() {
            GameObject consoleContainer = new ("- Dev Console -");
            consoleContainer.AddComponent<DevConsole>();
            consoleContainer.hideFlags = HideFlags.HideAndDontSave;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void CacheAssetReferences() {
            /*
             * Cant use assetdatabase in builds, need a way to load/cache assets
             */
        
            List<ScriptableObject> scriptableObjects = new ();
            string[] assetGuids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(ScriptableObject)}");
            foreach (string guid in assetGuids) {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                scriptableObjects.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(path));
            }
            SoAssets = scriptableObjects.ToArray();
        
        
        
            List<string> sceneAssetNames = new ();
            assetGuids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(Scene)}");
            foreach (string guid in assetGuids) {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                // Example path: Assets/Spawn Points/Map 1/Docks In Water.asset
                //                              Split -> [ assetName.extension ]
                //                              Split -> [ assetName ]
            
                string nameFromPath = path.Split('/')[^1]; // Last split is assetName.extension
                string nameWithoutExtension = nameFromPath.Split('.')[0]; // First split is assetName
                sceneAssetNames.Add(nameWithoutExtension);
            }
            SceneNames = sceneAssetNames.ToArray();
        }
#endif
        
        
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
        bool hasConsoleBeenInitialized;
        static readonly CommandData[] Commands = new CommandData[256];
        int totalCommandCount;
        int staticCommandCount;
        
        static ScriptableObject[] SoAssets;
        static string[] SceneNames;

        
        
        // Input
        static readonly StringBuilder TextBuilder = new (256);
        readonly InputCommand inputCommand = new ();
        readonly InputHint[] inputHints = new InputHint[32];
        int selectedHint;
        int moveMarkerToEnd;
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
        }
        
        void LoadStaticCommands() {
            Type[] assemblyTypes = Assembly.GetExecutingAssembly().GetTypes();
            foreach (Type loadedType in assemblyTypes) {
                MethodInfo[] methodsInType = loadedType.GetMethods(STATIC_BINDING_FLAGS);
                foreach (MethodInfo methodInfo in methodsInType) {
                    DevCommand devCommand = methodInfo.GetCustomAttribute<DevCommand>();
                    if (devCommand == null) continue;
                    
                    Commands[totalCommandCount++].AssignCommand(devCommand, methodInfo, null);
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
                        Commands[index].AddTarget(scriptBase);
                    }
                    else {
                        Commands[totalCommandCount++].AssignCommand(devCommand, methodInfo, scriptBase);
                    }
                }
            }
        }
        
        bool HasFoundInstancedCommand(MethodInfo methodInfo, out int index) {
            for (int i = staticCommandCount; i < totalCommandCount; i++) {
                if (Commands[i].IsSameMethod(methodInfo)) {
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
            inputCommand.Clear();

            if (hasConsoleBeenInitialized == false) {
                hasConsoleBeenInitialized = true;
                
                InitializeConsole();
                LoadStaticCommands();
                LoadInstanceCommands();
            }
            else {
                LoadInstanceCommands();
            }
        }

        
        void CloseConsole() {
            totalCommandCount = staticCommandCount;
            isActive = false;
            selectedHint = -1;
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
                selectedHint = -1;
            }
            
            if (GUI.GetNameOfFocusedControl() == CONSOLE_INPUT_FIELD_ID && hintAmount > 0) {
                if (selectedHint != -1) {
                    selectedHint = Mathf.Clamp(selectedHint, 0, hintAmount - 1);

                    if (e.KeyDown(KeyCode.KeypadEnter) || e.KeyDown(KeyCode.Return) || e.KeyDown(KeyCode.Tab)) {
                        inputCommand.UseHint(inputHints[selectedHint]);
                        moveMarkerToEnd = 2;
                    }
                }
                
                if (e.KeyDown(KeyCode.DownArrow)) {
                    selectedHint -= 1;
                    if (selectedHint < 0) selectedHint = hintAmount - 1;
                }
                else if (e.KeyDown(KeyCode.UpArrow)) {
                    selectedHint += 1;
                    selectedHint %= hintAmount;
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
            ParseInputForCommandsAndArguments();
            
            
            
            
            
            
            /*
             * Inputs regarding movement inside the hint window
             */
            
            if (inputCommand.HasText() && hintAmount > 0) {
                DrawHintWindow(hintAmount);
            }
            else {
                selectedHint = -1;
            }

            
            
            
            /*
             * Set focus back to input field
             */
            
            if (setFocus > 0) {
                --setFocus;
                GUI.FocusControl(CONSOLE_INPUT_FIELD_ID);
            }
            
            if (moveMarkerToEnd > 0) {
                --moveMarkerToEnd;
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
                
                GUI.enabled = i == selectedHint;
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
                        if (Commands[i].GetDisplayName().Contains(word, StringComparison.InvariantCultureIgnoreCase) == false) {
                            matchingHint = false;
                            break;
                        }
                    }

                    if (matchingHint) {
                        TextBuilder.Clear();
                        TextBuilder.Append(Commands[i].GetFullHint());
                        TextBuilder.Append(SPACE);

                        inputHints[hintsFound++].SetHint(TextBuilder, Commands[i].GetDisplayName());
                    }
                }
            }
            else { // Has command, look for arguments
                /*
                 * check command for what the next argument type is and parse the approriate part of input accordingly
                 * Look into how to parse string, would be nice to have " or ' encapsulation 
                 */
                
                // If is not asset type or has all arguments
                if (inputCommand.HasRequiredArguments()) {
                    return hintsFound;
                }
                
                Type argumentType = inputCommand.GetNextParameterType();
                
                /*
                 * Bool
                 */

                if (argumentType == typeof(bool)) {
                    TextBuilder.Clear();
                    TextBuilder.Append(bool.TrueString);
                    TextBuilder.Append(SPACE);
                    inputHints[hintsFound++].SetHint(TextBuilder, bool.TrueString);
                    
                    TextBuilder.Clear();
                    TextBuilder.Append(bool.FalseString);
                    TextBuilder.Append(SPACE);
                    inputHints[hintsFound++].SetHint(TextBuilder, bool.FalseString);
                    return hintsFound;
                }

                string[] inputWithoutMatches = inputCommand.GetInputWithoutMatches().Split(SPACE, StringSplitOptions.RemoveEmptyEntries);
                if (inputWithoutMatches.Length == 0) return hintsFound;
                
                /*
                 * Enums
                 */
                
                if (argumentType.IsEnum) {
                    string[] namesInsideEnum = argumentType.GetEnumNames();
                    foreach (string enumValueName in namesInsideEnum) {
                        bool containsWord = true;
                        foreach (string inputWord in inputWithoutMatches) {
                            if (enumValueName.Contains(inputWord, StringComparison.InvariantCultureIgnoreCase)) continue;
                            
                            containsWord = false;
                            break;
                        }

                        if (containsWord) {
                            TextBuilder.Clear();
                            TextBuilder.Append(enumValueName);
                            TextBuilder.Append(SPACE);
                            inputHints[hintsFound++].SetHint(TextBuilder, enumValueName);
                        }
                    }

                    return hintsFound;
                }
                
                
                
                /*
                 * Scenes
                 */
                
                if (argumentType == typeof(Scene)) {
                    
                }
                
                
                /*
                 * ScriptableObjects
                 */
                
                if (typeof(ScriptableObject).IsAssignableFrom(argumentType)) {
                    
                }
                
            }

            return hintsFound;
        }




        void ParseInputForCommandsAndArguments() {
            inputCommand.RemoveSelection();
            if (inputCommand.HasText() == false) return;
            
            /*
             * Commands
             */

            int matchingCommandIndex = -1;
            int longestMatch = 0;

            for (int i = 0; i < totalCommandCount; i++) {
                 if (inputCommand.inputText.StartsWith(Commands[i].GetDisplayName(), StringComparison.InvariantCultureIgnoreCase) == false) 
                     continue;
                 int lengthOfInput = Commands[i].GetDisplayName().Length;
                 if (lengthOfInput > longestMatch) {
                     matchingCommandIndex = i;
                     longestMatch = lengthOfInput;
                 }
            }

            if (matchingCommandIndex != -1) {
                inputCommand.SelectCommand(matchingCommandIndex);
            }



            /*
             * Arguments
             */

            if (inputCommand.HasCommand() == false) return;
            /*
             * Look into how to parse string, would be nice to have " or ' encapsulation 
             */
            
            
            
            
        }
        
        

        class InputCommand {
            internal string inputText;
            int selectedCommand;
            readonly InputArgument[] commandArguments = new InputArgument[12];
            int argumentsAssigned;
            
            internal void Clear() {
                inputText = string.Empty;
                selectedCommand = -1;
                argumentsAssigned = 0;
            }

            internal void RemoveSelection() {
                selectedCommand = -1;
                argumentsAssigned = 0;
            }
            public void SelectCommand(int matchingCommandIndex) => selectedCommand = matchingCommandIndex;
            internal bool HasText() => string.IsNullOrEmpty(inputText) == false;
            internal bool HasCommand() => selectedCommand != -1;

            /*
             * Don't break existing text when applying hint!
             * Applying the visual string, assigning matching command + argument is done later in the update loop
             * so we are not assigning the actual command or argument here, only inputting a string that the parser will
             * recognize later!
             */
            internal void UseHint(InputHint inputHint) {
                TextBuilder.Clear();
                if (HasCommand()) {
                    TextBuilder.Append($"{Commands[selectedCommand].GetDisplayName()} ");

                    for (int i = 0; i < argumentsAssigned; i++) {
                        TextBuilder.Append($"{commandArguments[i].displayName} ");
                    }
                }
                
                TextBuilder.Append($"{inputHint.outputString} ");
                inputText = TextBuilder.ToString();
            }

            internal bool HasRequiredArguments() {
                if (selectedCommand == -1) return false;
                // should we use '==' or '>=' .. not sure yet
                return argumentsAssigned >= Commands[selectedCommand].GetParameterCount();
            }
            
            // Assumes you have command set and isn't above parameter count, feels dumb to do this redirect
            public Type GetNextParameterType() {
                return Commands[selectedCommand].GetParameterByIndex(argumentsAssigned).GetType();
            }

            public string GetInputWithoutMatches() {
                TextBuilder.Clear();
                TextBuilder.Append(inputText);

                if (HasCommand() == false) return TextBuilder.ToString();
                int removeAmount = Commands[selectedCommand].GetDisplayName().Length;
                
                TextBuilder.Remove(0, removeAmount);
                if (TextBuilder[0] == SPACE) TextBuilder.Remove(0, 1); // can this be assumed to always be true if it's assigned?

                for (int i = 0; i < argumentsAssigned; i++) {
                    TextBuilder.Remove(0, commandArguments[i].displayName.Length);
                    if (TextBuilder[0] == SPACE) TextBuilder.Remove(0, 1);
                }

                return TextBuilder.ToString();
            }
        }


        struct InputArgument {
            internal readonly string displayName; // Not using getters here since i wanna re-create these often anyway
            internal readonly object argValue;
        }

        
        struct CommandData {
            string displayName;
            string hintText;
            MethodInfo method;
            ParameterInfo[] parameters;
            List<Object> targets;
            int parameterCount;

            internal void AssignCommand(DevCommand devCommand, MethodInfo methodInfo, Object target) {
                method = methodInfo;
                displayName = string.IsNullOrEmpty(devCommand.displayName) ? method.Name : devCommand.displayName;
                
                parameters = method.GetParameters();
                parameterCount = parameters.Length;

                TextBuilder.Clear();
                TextBuilder.Append($"{displayName} ");
                foreach (ParameterInfo param in parameters) {
                    TextBuilder.Append($"<{param.Name}> ");
                }
                hintText = TextBuilder.ToString();
            
                
                if (targets == null) targets = new List<Object>();
                else targets.Clear();
                targets.Add(target);
            }
            internal void AddTarget(Object target) => targets.Add(target);
            internal string GetDisplayName() => displayName; // Using getter to make it clear that 'displayName' only set via 'AssignCommand'
            internal string GetFullHint() => hintText;
            internal MethodInfo GetMethod() => method;
            public bool IsSameMethod(MethodInfo methodInfo) => method == methodInfo;
            internal int GetParameterCount() => parameterCount;
            internal ParameterInfo GetParameterByIndex(int index) => parameters[index];
        }


        struct InputHint {
            internal enum HintType {
                COMMAND,
                ARGUMENT,
            }
            internal string displayString;
            internal string outputString;

            public void SetHint(StringBuilder builder, string outputValue) {
                displayString = builder.ToString();
                outputString = outputValue;
            }
        }
    }
}