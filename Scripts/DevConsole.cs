using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    static void SpawnConsoleInScene() {
        GameObject consoleContainer = new ("- Dev Console -");
        consoleContainer.AddComponent<DevConsole>();
        DontDestroyOnLoad(consoleContainer);
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    static void CacheAssetReferences() {
        /*
         * Cant use assetdatabase in builds, need a way to load/cache assets
         */
    
        
        string[] assetGuids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(ScriptableObject)}");
        AssetReferences = new ScriptableObject[assetGuids.Length];
        AssetNames = new string[assetGuids.Length];
        
        for (int i = 0; i < assetGuids.Length; i++) {
            string guid = assetGuids[i];
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            AssetReferences[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            AssetNames[i] = AssetReferences[i].name;
        }
        
        

        assetGuids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(Scene)}");
        SceneNames = new string[assetGuids.Length];
        ScenePaths = new string[assetGuids.Length];

        for (int i = 0; i < assetGuids.Length; i++) {
            string guid = assetGuids[i];
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            // Example path: Assets/Spawn Points/Map 1/Docks In Water.asset
            //                              Split -> [ assetName.extension ]
            //                              Split -> [ assetName ]

            string nameFromPath = path.Split('/')[^1]; // Last split is assetName.extension
            string nameWithoutExtension = nameFromPath.Split('.')[0]; // First split is assetName
            SceneNames[i] = nameWithoutExtension;
            ScenePaths[i] = path;
        }

        Debug.Log($"DevConsole Cached -> {AssetReferences.Length} ScriptableObjects & {SceneNames.Length} Scenes");
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
    const char STRING_MARKER = '"';


    
    
    /*
     * Instanced
     */
    

    // Core
    bool hasConsoleBeenInitialized;
    static readonly CommandData[] Commands = new CommandData[256];
    static int StaticCommandCount;
    int totalCommandCount;
    
    static ScriptableObject[] AssetReferences;
    static string[] AssetNames;
    static string[] SceneNames;
    static string[] ScenePaths;

    
    
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

        StaticCommandCount = totalCommandCount;
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
        for (int i = StaticCommandCount; i < totalCommandCount; i++) {
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
        totalCommandCount = StaticCommandCount;
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
        
        
        

        int hintAmount = GenerateSuggestionHints();
        
        
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
         * Execute command
         */

        
        if (inputCommand.HasCommand() && (e.KeyDown(KeyCode.KeypadEnter, false) || e.KeyDown(KeyCode.Return, false))) {
            ParseInputForCommandsAndArguments(true);
            if (inputCommand.TryExecuteCommand()) {
                e.Use();
                inputCommand.Clear();
                moveMarkerToEnd = 2;
                hintAmount = 0;
                selectedHint = -1;
            }
        }
        
        
        
        
         
            
        /*
         * draw console input area
         */
        
        consoleInputDrawPos = new Vector2(WIDTH_SPACING, height - (HEIGHT_SPACING + height * SCREEN_HEIGHT_PERCENTAGE));
        consoleInputSize = new Vector2(width - WIDTH_SPACING * 2f, height * SCREEN_HEIGHT_PERCENTAGE);

        
        /*
         * TODO change to box with text drawn inside it to control look better, ex upcoming parameter names
         */
        GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);
        Rect inputWindowRect = new (consoleInputDrawPos, consoleInputSize);
        inputCommand.inputText = GUI.TextField(inputWindowRect, inputCommand.inputText);
        ParseInputForCommandsAndArguments(false);
        
        
        
        
        
        
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
            
            /*
             * better visual selection, only highlight the part that is relevant
             */
            GUI.enabled = i == selectedHint;
            GUI.Label(new Rect(pos, new Vector2(maximumWidth, stepHeight)), hint.displayString);
        }
        
        GUI.enabled = true;
    }

    

    int GenerateSuggestionHints() {
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

            return hintsFound;
        }


        /*
         * check command for what the next argument type is and parse the appropriate part of input accordingly
         * 
         */


        int argumentCount = inputCommand.GetArgumentCount();
        int commandIndex = inputCommand.GetCommandIndex();

        if (inputCommand.HasCommand() == false || argumentCount >= Commands[commandIndex].GetParameterCount()) {
            return hintsFound;
        }

        

        // Ugly string stuff
        string inputText = inputCommand.inputText;
        inputText = inputText.Remove(0, Commands[commandIndex].GetDisplayName().Length).TrimStart(SPACE);
        for (int i = 0; i < argumentCount; i++) {
            inputText = inputText.Remove(0, inputCommand.GetArgumentByIndex(i).displayName.Length).TrimStart(SPACE);
        }
        
        string[] inputWithoutMatches = inputText.Split(SPACE, StringSplitOptions.RemoveEmptyEntries);
        
        
        Type argumentType = Commands[commandIndex].GetParameterType(argumentCount);

        /*
         * Bool
         */

        if (argumentType == typeof(bool)) {
            TextBuilder.Clear();
            TextBuilder.Append(bool.TrueString);
            inputHints[hintsFound++].SetHint(TextBuilder);

            TextBuilder.Clear();
            TextBuilder.Append(bool.FalseString);
            inputHints[hintsFound++].SetHint(TextBuilder);
            return hintsFound;
        }

        
        
        
        
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
                    inputHints[hintsFound++].SetHint(TextBuilder);
                }
            }

            return hintsFound;
        }


        
        
        /*
         * Scenes
         */

        if (argumentType == typeof(Scene)) {
            for (int i = 0; i < SceneNames.Length; i++) {
                string sceneName = SceneNames[i];
                bool containsWord = true;
                foreach (string word in inputWithoutMatches) {
                    if (sceneName.Contains(word, StringComparison.InvariantCultureIgnoreCase)) continue;

                    containsWord = false;
                    break;
                }

                if (containsWord) {
                    TextBuilder.Clear();
                    TextBuilder.Append(sceneName);
                    inputHints[hintsFound++].SetHint(TextBuilder);
                }
            }

            return hintsFound;
        }


        
        
        /*
         * ScriptableObjects
         */

        if (typeof(ScriptableObject).IsAssignableFrom(argumentType)) {
            foreach (ScriptableObject asset in AssetReferences) {
                
                /*
                 * Asset is scriptableObject but has wrong inheritance type
                 */
                if (argumentType.IsAssignableFrom(asset.GetType()) == false) continue;
                
                bool containsWord = true;
                foreach (string word in inputWithoutMatches) {
                    if (asset.name.Contains(word, StringComparison.InvariantCultureIgnoreCase)) continue;

                    containsWord = false;
                    break;
                }

                if (containsWord) {
                    TextBuilder.Clear();
                    TextBuilder.Append(asset.name);
                    inputHints[hintsFound++].SetHint(TextBuilder);
                }
            }

            return hintsFound;
        }

        /*
         * prob wanna display what command and name of argument it's related too as well, but also not inside the hint box
         */
        // TextBuilder.Clear();
        // TextBuilder.Append($"Parameters -> '{argumentType}'({argumentType.Name}) is not supported!");
        // TextBuilder.Append(SPACE);
        // inputHints[hintsFound++].SetHint(TextBuilder);
        return hintsFound;
    }




    void ParseInputForCommandsAndArguments(bool ignoreSpacingRequirement) {
        inputCommand.RemoveSelection();
        if (inputCommand.HasText() == false) return;
        string inputText = inputCommand.inputText;
        
        
        /*
         * Commands
         */

        int matchingCommandIndex = -1;
        int longestCommandName = -1;

        for (int i = 0; i < totalCommandCount; i++) {
             if (inputText.StartsWith(Commands[i].GetDisplayName(), StringComparison.InvariantCultureIgnoreCase) == false)
                 continue;
             int lengthOfInput = Commands[i].GetDisplayName().Length;
             if (lengthOfInput > longestCommandName) {
                 matchingCommandIndex = i;
                 longestCommandName = lengthOfInput;
             }
        }

        /*
         * this will select a command if you have 2 with the same name and one is longer
         * ex. LoadScene -> LoadSceneTwo
         * the first one will get selected, maybe add something that waits until whitespace to hide hint menu?
         */
        if (matchingCommandIndex != -1) {
            /*
             * Only apply command if we have a space afterward marking the input as done
             */
            if (inputText.Length > longestCommandName && inputText[longestCommandName] == SPACE) {
                inputText = inputText.Remove(0, longestCommandName);
                inputCommand.SelectCommand(matchingCommandIndex);
            }
        }



        
        
        
        /*
         * Arguments
         */

        if (inputCommand.HasCommand() == false) return;
        /*
         * if we reach this, then 'matchingCommandIndex' will be set
         * Look into how to parse string, would be nice to have " or ' encapsulation 
         */

        
        int paramCount = Commands[matchingCommandIndex].GetParameterCount();
        for (int i = 0; i < paramCount; i++) {
            // We remove parts of the string for matching arguments so if we get to a loop when it's empty, were done!
            if (string.IsNullOrEmpty(inputText)) break;
            inputText = inputText.TrimStart(SPACE);
            Type argumentType = Commands[matchingCommandIndex].GetParameterType(i);

            /*
             * Bool
             */
            
            if (argumentType == typeof(bool)) {
                if (inputText.StartsWith(bool.TrueString + (ignoreSpacingRequirement ? string.Empty : SPACE), StringComparison.InvariantCultureIgnoreCase)) {
                    inputText = inputText.Remove(0, bool.TrueString.Length);
                    inputCommand.SetArgument(bool.TrueString, true);
                }
                else if (inputText.StartsWith(bool.FalseString + (ignoreSpacingRequirement ? string.Empty : SPACE), StringComparison.InvariantCultureIgnoreCase)) {
                    inputText = inputText.Remove(0, bool.FalseString.Length);
                    inputCommand.SetArgument(bool.FalseString, false);
                }
                
                continue;
            }
            
            
            
            
            /*
             * Enums
             */


            if (argumentType.IsEnum) {
                string[] namesInsideEnum = argumentType.GetEnumNames();
                int longestEnumMatch = -1;
                int matchingEnumIndex = -1;

                for (int enumIndex = 0; enumIndex < namesInsideEnum.Length; enumIndex++) {
                    if (inputText.StartsWith(namesInsideEnum[enumIndex] + (ignoreSpacingRequirement ? string.Empty : SPACE), StringComparison.InvariantCultureIgnoreCase) == false) 
                        continue;
                    
                    int enumLength = namesInsideEnum[enumIndex].Length;
                    if (enumLength > longestEnumMatch) {
                        longestEnumMatch = enumLength;
                        matchingEnumIndex = enumIndex;
                    }
                }

                if (matchingEnumIndex != -1) {
                    inputText = inputText.Remove(0, longestEnumMatch);
                    inputCommand.SetArgument(namesInsideEnum[matchingEnumIndex], argumentType.GetEnumValues().GetValue(matchingEnumIndex));
                }
                
                continue;
            }
            
            
            
            
            
            /*
             * Scenes
             */

            if (argumentType == typeof(Scene)) {
                int longestSceneMatch = -1;
                int matchingSceneIndex = -1;
                for (int sceneIndex = 0; sceneIndex < SceneNames.Length; sceneIndex++) {
                    if (inputText.StartsWith(SceneNames[sceneIndex] + (ignoreSpacingRequirement ? string.Empty : SPACE), StringComparison.InvariantCultureIgnoreCase) == false) 
                        continue;
                    
                    int nameLength = SceneNames[sceneIndex].Length;
                    if (nameLength > longestSceneMatch) {
                        longestSceneMatch = nameLength;
                        matchingSceneIndex = sceneIndex;
                    }
                }

                if (matchingSceneIndex != -1) {
                    inputText = inputText.Remove(0, longestSceneMatch);
                    inputCommand.SetArgument(SceneNames[matchingSceneIndex], ScenePaths[matchingSceneIndex]);
                }
                
                continue;
            }
            
            
            
            
            /*
             * ScriptableObjects
             */

            if (typeof(ScriptableObject).IsAssignableFrom(argumentType)) {
                int longestAssetMatch = -1;
                int matchingAssetIndex = -1;

                for (int assetIndex = 0; assetIndex < AssetReferences.Length; assetIndex++) {
                    if (inputText.StartsWith(AssetNames[assetIndex] + (ignoreSpacingRequirement ? string.Empty : SPACE), StringComparison.InvariantCultureIgnoreCase) == false)
                        continue;

                    int nameLength = AssetNames[assetIndex].Length;
                    if (nameLength > longestAssetMatch) {
                        longestAssetMatch = nameLength;
                        matchingAssetIndex = assetIndex;
                    }
                }

                if (matchingAssetIndex != -1) {
                    inputText = inputText.Remove(0, longestAssetMatch);
                    inputCommand.SetArgument(AssetNames[matchingAssetIndex], AssetReferences[matchingAssetIndex]);
                }
                
                continue;
            }
            
            
            
            /*
             * Strings
             * Look into how to parse string, would be nice to have " or ' encapsulation
             */


            if (argumentType == typeof(string)) {
                int markersFound = 0;
                int validStringLength = -1;
                
                for (int strLen = 0; strLen < inputText.Length; strLen++) {
                    bool isMarker = inputText[strLen] == STRING_MARKER;
                    if (markersFound == 0 && isMarker == false) {
                        break; 
                    }

                    if (isMarker) {
                        markersFound++;
                        if (markersFound == 2) {
                            validStringLength = strLen+1;
                            break;
                        }
                    }
                    else if (markersFound == 0) {
                        break; // string doesnt start with marker
                    }
                }

                if (validStringLength != -1) {
                    if (ignoreSpacingRequirement || (inputText.Length > validStringLength && inputText[validStringLength] == SPACE)) {
                        string argumentString = inputText[..validStringLength];
                        inputText = inputText.Remove(0, validStringLength);
                        inputCommand.SetArgument(argumentString, argumentString[1..^1]);
                    }
                }
                
                
                continue;
            }
            
            
            
            
            /*
             * All other that can be converted from string
             * int, float, byte
             */
            
            TypeConverter typeConverter = TypeDescriptor.GetConverter(argumentType);
            if (typeConverter.CanConvertFrom(typeof(string))) {
                string firstPossibleValue = inputText.Split(SPACE)[0];

                object stringToValue = null;
                try {
                    stringToValue = typeConverter.ConvertFromString(firstPossibleValue);
                }
                catch {
                    // ignored
                }

                if (stringToValue != null) {
                    if (ignoreSpacingRequirement || (inputText.Length > firstPossibleValue.Length && inputText[firstPossibleValue.Length] == SPACE)) {
                        inputText = inputText.Remove(0, firstPossibleValue.Length);
                        inputCommand.SetArgument(firstPossibleValue, stringToValue);
                    }
                }
                
                continue;
            }
            
            
            break; // input value doesn't match any argument type, stop looking since we're doing them in order 
        }
    }
    
    

    class InputCommand {
        internal string inputText;
        int selectedCommand;
        readonly InputArgument[] inputArguments = new InputArgument[12];
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
        internal void SelectCommand(int matchingCommandIndex) => selectedCommand = matchingCommandIndex;

        /*
         * Not doing overload for times when display value and argument value is the same to
         * make it obvious what you are doing from the outside 
         */
        internal void SetArgument(string displayValue, object argumentValue) {
            inputArguments[argumentsAssigned++].Set(displayValue, argumentValue);
        }
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
                    TextBuilder.Append($"{inputArguments[i].displayName} ");
                }
            }
            
            TextBuilder.Append($"{inputHint.outputString} ");
            inputText = TextBuilder.ToString();
        }

        internal int GetCommandIndex() => selectedCommand;
        internal int GetArgumentCount() => argumentsAssigned;
        internal InputArgument GetArgumentByIndex(int index) => inputArguments[index];

        internal bool TryExecuteCommand() {
            List<Object> target = Commands[selectedCommand].GetTargets();
            ParameterInfo[] parameters = Commands[selectedCommand].GetParameters();
            object[] argumentValues = new object[parameters.Length];
            for (int i = 0; i < argumentValues.Length; i++) {
                if (i < argumentsAssigned) {
                    if (parameters[i].ParameterType == typeof(Scene)) {
                        /*
                         * Scenes are wacky, you can only really load scenes that are active or inside build list
                         */
                        argumentValues[i] = SceneManager.GetSceneByPath((string)inputArguments[i].argumentValue);
                    }
                    else {
                        argumentValues[i] = inputArguments[i].argumentValue;
                    }
                }
                else {
                    if (parameters[i].HasDefaultValue == false) 
                        return false;
                    
                    argumentValues[i] = parameters[i].DefaultValue;
                }
            }

            for (int i = 0; i < target.Count; i++) {
                if (selectedCommand > StaticCommandCount && target[i] == null) {
                    continue;
                }
                Commands[selectedCommand].GetMethod().Invoke(target[i], argumentValues);
            }

            return true;
        }
    }


    struct InputArgument {
        internal string displayName;
        internal object argumentValue;

        internal void Set(string displayName, object argumentValue) {
            this.displayName = displayName;
            this.argumentValue = argumentValue;
        }
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
        internal bool IsSameMethod(MethodInfo methodInfo) => method == methodInfo;
        internal int GetParameterCount() => parameterCount;
        internal Type GetParameterType(int index) => parameters[index].ParameterType;
        internal ParameterInfo[] GetParameters() => parameters;
        internal List<Object> GetTargets() => targets;
    }


    struct InputHint {
        internal string displayString;
        internal string outputString;

        // Having 2 strings for displaying commands, the visual adds the parameter names, for arguments both are the same
        internal void SetHint(StringBuilder builder, string outputValue) {
            displayString = builder.ToString();
            outputString = outputValue;
        }

        internal void SetHint(StringBuilder builder) {
            displayString = builder.ToString();
            outputString = builder.ToString();
        }
    }
}

}