/*
 * Enable this for projects with URP
 */

//#define URP_ENABLED


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;


/*
 * ----------- TODO LIST ----------------
 * Check how generic parameters are handled
 * 
 */



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
    
    
    /*
     * Const
     */

    const BindingFlags BASE_FLAGS = BindingFlags.Default | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags INSTANCED_BINDING_FLAGS = BASE_FLAGS | BindingFlags.Instance;
    const BindingFlags STATIC_BINDING_FLAGS = BASE_FLAGS | BindingFlags.Static;

    
    const string DEV_CONSOLE_SKIN_PATH = "Dev Console Skin";
    const string CONSOLE_INPUT_FIELD_ID = "Console Input Field";
    const float WIDTH_SPACING = 8f;
    const float HEIGHT_SPACING = 8f;
    const float HINT_HEIGHT_TEXT_PADDING = 2f;
    const char SPACE = ' ';
    const char STRING_MARKER = '"';


    const string HIGHLIGHT_TEXT_CODE = "#FFFFFFFF";

    
    
    
    
    
    /*
     * Instanced
     */
    

    // Core
    bool hasConsoleBeenInitialized;
    static DevConsoleCache Cache;
    static DevConsoleStyle Style;
    static readonly CommandData[] Commands = new CommandData[256];
    static readonly int[] hintIndex = new int[32];
    static readonly Type[] hintType = new Type[32];
    static readonly GUIContent[] hintContent = new GUIContent[32];
    static readonly Type SO_TYPE = typeof(ScriptableObject);
    static readonly Type COMMAND_TYPE = typeof(CommandData);
    static int StaticCommandCount;
    int hintsToDisplay;
    int totalCommandCount;

    
    
    // Input
    /*
     * Try to replace strings with textbuilder
     * TextBuilder.Remove(index, length)
     */
    static readonly StringBuilder TextBuilder = new (256);
    readonly InputCommand inputCommand = new ();
    int moveMarkerToEnd;
    int selectedHint;
    bool isActive;
    int setFocus;

    
    // Drawing
    GUISkin consoleSkin;
    Vector2 consoleInputDrawPos;
    Vector2 consoleInputSize;
    float selectionBump;
    
    
    
    /*
     * Core console functionality
     *
     */
    
    
    
    void InitializeConsole() {
        consoleSkin = Resources.Load<GUISkin>(DEV_CONSOLE_SKIN_PATH);
        Cache = Resources.Load<DevConsoleCache>(DevConsoleCache.ASSET_PATH);
        Style = Resources.Load<DevConsoleStyle>(DevConsoleStyle.ASSET_PATH);
        Array.Fill(hintType, COMMAND_TYPE);
        for (int i = 0; i < hintContent.Length; i++) {
            hintContent[i] = new GUIContent();
        }
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
    
    
    /*
     * Reload instance commands when loading scene
     */
    void LoadInstanceCommands() {
        totalCommandCount = StaticCommandCount;
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
    
    void OnSceneChanged(Scene scene, LoadSceneMode loadSceneMode) {
        if (isActive == false) return;
        LoadInstanceCommands();
    }


    /*
     * Not sure if i like having this callback here, maybe just add it onto recompile callback
     */
    void Awake() {
        SceneManager.sceneLoaded += OnSceneChanged;
    }

    void OnDestroy() {
        SceneManager.sceneLoaded -= OnSceneChanged;
    }

    

    /*
     * Console Actions
     */
    
    
    void OpenConsole() {
        isActive = true;
        setFocus = 1;
        inputCommand.Clear();
        
#if URP_ENABLED
        UnityEngine.Rendering.DebugManager.instance.enableRuntimeUI = false;
#endif

        if (hasConsoleBeenInitialized == false) {
            hasConsoleBeenInitialized = true;
            
            /*
             * Can this be async? there is no reasonable situation where you should be able to
             * execute a command before the async is done.
             * even then i can just queue the command and pop them once it's loaded and done if you are using history
             */
            InitializeConsole();
            LoadStaticCommands();
            LoadInstanceCommands();
        }
        else {
            LoadInstanceCommands();
        }
    }

    
    void CloseConsole() {
        isActive = false;
        selectedHint = -1;
        GUI.FocusControl(null);
        
#if URP_ENABLED
        UnityEngine.Rendering.DebugManager.instance.enableRuntimeUI = true;
#endif
    }
    
    
    
    /*
     * Main logic flow
     */

    
    void OnGUI() {
        Event inputEvent = Event.current;
        if (isActive == false) {
            if (inputEvent.OpenConsole()) OpenConsole();
            return;
        }

        /*
         * Console is active
         */
        
        
        if (inputEvent.CloseConsole()) CloseConsole();
        
        
        DrawConsole();
    }

    
    void DrawConsole() {
        float width = Screen.width;
        float height = Screen.height;
        Event inputEvent = Event.current;
        
        GUISkin skin = GUI.skin;
        GUI.skin = consoleSkin;
        selectionBump = Mathf.Lerp(selectionBump, 1, Style.SelectionBumpSpeed * Time.unscaledDeltaTime);
        bool windowHasFocus = GUI.GetNameOfFocusedControl() == CONSOLE_INPUT_FIELD_ID;
        
        /*
         * Hint menu navigation
         * don't actually have to apply the input here, only need to capture it before textfields eats it
         * can also generate hints after input field to get the most up to date one
         *
         * Input -> Execute Command 
         * Input -> Hints & Navigation
         * Draw -> Console Field
         * If Changed -> Generate hints
         * Draw -> Hints
         * Input -> Execute navigation
         */
        
        hintsToDisplay = ParseInputForHints();
        if (hintsToDisplay > 1) {
            /*
             * how do i sort the array when i have 2 more that needs to follow the same index layout?
             */
            // Array.Sort(hintContent, 0, hintsToDisplay);
        }
        
        if (inputCommand.HasText() == false || hintsToDisplay == 0) {
            selectedHint = -1;
        }

        if (windowHasFocus && hintsToDisplay > 0) {
            if (selectedHint != -1) {
                selectedHint = Mathf.Clamp(selectedHint, 0, hintsToDisplay - 1);

                if (inputEvent.InsertHint()) {
                    ParseInputForCommandsAndArguments();
                    inputCommand.UseHint(selectedHint);
                    moveMarkerToEnd = 2;
                }
            }

            if (inputEvent.NavigateDown()) {
                selectedHint -= 1;
                selectionBump = 0;
                if (selectedHint < 0) selectedHint = hintsToDisplay - 1;
            }
            else if (inputEvent.NavigateUp()) {
                selectedHint += 1;
                selectionBump = 0;
                selectedHint %= hintsToDisplay;
            }
        }





        if (inputCommand.HasCommand() && inputEvent.ExecuteCommand(false)) {
            if (inputCommand.TryExecuteCommand()) {
                inputEvent.Use();
                inputCommand.Clear();
                moveMarkerToEnd = 2;
                hintsToDisplay = 0;
                selectedHint = -1;
            }
        }
        
        /*
         * Execute command
         * Could maybe avoid extra command parsing if we do this after textinput
         * it wouldn't account for space after argument/command though..
         * not sure if I like how that is all setup atm
         */

        
        
        
        
        
        /*
         * draw console input area
         */
        
        consoleInputDrawPos = new Vector2(WIDTH_SPACING, height - (HEIGHT_SPACING + Style.ConsoleWindowHeight));
        consoleInputSize = new Vector2(width - WIDTH_SPACING * 2f, Style.ConsoleWindowHeight);

        
        /*
         * 
         * doesn't need to update hints & parse commands if input hasn't changed
         */
        GUI.backgroundColor = Style.BorderColor;
        
        GUI.contentColor = inputCommand.CanExecuteCommand() ? Style.InputValidCommand : Style.InputTextDefault;
        
        Rect inputFieldRect = new (consoleInputDrawPos, consoleInputSize);
        GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);
        
        
        inputCommand.inputText = GUI.TextField(inputFieldRect, inputCommand.inputText); 
        ParseInputForCommandsAndArguments();
        
        
        
        
        if (inputCommand.HasText() && hintsToDisplay > 0) {
            DrawHintWindow(hintsToDisplay);
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
        
        /*
         * Reset gui skin
         */
        GUI.skin = skin;
    }


    void DrawHintWindow(int hintAmount) {
        /*
         * Draw Command Hints
         */
        
        float maximumWidth = 0;
        float maximumHeight = 0;
        
        for (int i = 0; i < hintAmount; i++) {
            Vector2 size = consoleSkin.label.CalcSize(hintContent[i]);
            maximumWidth = Mathf.Max(size.x, maximumWidth);
            maximumHeight += size.y + HINT_HEIGHT_TEXT_PADDING;
        }

        maximumWidth += Style.SelectionBumpOffsetAmount;
        GUI.backgroundColor = Style.BorderColor;
        Rect hintBackgroundRect = new (consoleInputDrawPos - new Vector2(0, maximumHeight + HEIGHT_SPACING), new Vector2(maximumWidth, maximumHeight));
        GUI.Box(hintBackgroundRect, "");
        
        Vector2 hintStartPos = hintBackgroundRect.position;
        float stepHeight = maximumHeight / hintAmount;
        for (int i = 0; i < hintAmount; i++) {
            bool isSelected = i == selectedHint;
            
            float offsetDst = isSelected ? Style.SelectionBumpCurve.Evaluate(selectionBump) * Style.SelectionBumpOffsetAmount : 0;
            Vector2 pos = hintStartPos + new Vector2(offsetDst, maximumHeight - (i+1) * stepHeight);
            
            /*
             * better visual selection, only highlight the part that is relevant
             */
            GUI.contentColor = isSelected ? Style.HintTextColorSelected : Style.HintTextColorDefault;
            GUI.Label(new Rect(pos, new Vector2(maximumWidth, stepHeight)), hintContent[i]);
        }
    } 

    
    int ParseInputForHints() {
        int hintsFound = 0;

        if (inputCommand.HasText() == false) {
            return hintsFound;
        }
        
        // TODO wanna add sorting based on how many matches we have, best result at the top
        // Fill with commands that match
        if (inputCommand.DisplayCommandHints()) {
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
                    hintContent[hintsFound].text = Commands[i].GetFullHint();
                    hintIndex[hintsFound] = i;
                    hintType[hintsFound] = COMMAND_TYPE;
                    hintsFound++;
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
            hintContent[hintsFound].text = bool.TrueString;
            hintType[hintsFound] = argumentType;
            hintsFound++;
            
            hintContent[hintsFound].text = bool.FalseString;
            hintType[hintsFound] = argumentType;
            hintsFound++;
            return hintsFound;
        }

        
        
        
        
        /*
         * Enums
         */

        if (argumentType.IsEnum) {
            string[] namesInsideEnum = argumentType.GetEnumNames();
            for (int i = 0; i < namesInsideEnum.Length; i++) {
                bool containsWord = true;
                for (int wordIndex = 0; wordIndex < inputWithoutMatches.Length; wordIndex++) {
                    if (namesInsideEnum[i].Contains(inputWithoutMatches[wordIndex], StringComparison.InvariantCultureIgnoreCase) == false) {
                        containsWord = false;
                        break;
                    }
                }

                if (containsWord) {
                    hintContent[hintsFound].text = namesInsideEnum[i];
                    hintIndex[hintsFound] = i;
                    hintType[hintsFound] = argumentType;
                    hintsFound++;
                }
            }

            return hintsFound;
        }
        
        
        /*
         * ScriptableObjects
         */

        if (SO_TYPE.IsAssignableFrom(argumentType)) {
            for (int i = 0; i < Cache.AssetReferences.Length; i++) {
                ScriptableObject asset = Cache.AssetReferences[i];
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
                    hintContent[hintsFound].text = asset.name;
                    hintIndex[hintsFound] = i;
                    hintType[hintsFound] = SO_TYPE;
                    hintsFound++;
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
    

    
    /*
     * TODO remove 'ignoreSpacingRequirement' bool, it was used to keep hints active after first matching command was found but
     * the logic has been changed to hints checking if they should keep displaying or not
     * 03-12 -> logic inside InputCommand.DisplayCommandHints()
     */
    
    void ParseInputForCommandsAndArguments() {
        inputCommand.RemoveSelection();
        if (inputCommand.HasText() == false) return;
        /*
         * int checkIndex = 0;
         * can remove string and use [checkIndex..^1]
         */
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
            if (inputText.Length >= longestCommandName) {
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
                if (inputText.StartsWith(bool.TrueString, StringComparison.InvariantCultureIgnoreCase)) {
                    inputText = inputText.Remove(0, bool.TrueString.Length);
                    inputCommand.SetArgument(bool.TrueString, true);
                }
                else if (inputText.StartsWith(bool.FalseString, StringComparison.InvariantCultureIgnoreCase)) {
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
                    if (inputText.StartsWith(namesInsideEnum[enumIndex], StringComparison.InvariantCultureIgnoreCase) == false) 
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
             * ScriptableObjects
             */

            if (SO_TYPE.IsAssignableFrom(argumentType)) {
                int longestAssetMatch = -1;
                int matchingAssetIndex = -1;

                for (int assetIndex = 0; assetIndex < Cache.AssetReferences.Length; assetIndex++) {
                    if (inputText.StartsWith(Cache.AssetNames[assetIndex], StringComparison.InvariantCultureIgnoreCase) == false)
                        continue;

                    int nameLength = Cache.AssetNames[assetIndex].Length;
                    if (nameLength > longestAssetMatch) {
                        longestAssetMatch = nameLength;
                        matchingAssetIndex = assetIndex;
                    }
                }

                if (matchingAssetIndex != -1) {
                    inputText = inputText.Remove(0, longestAssetMatch);
                    inputCommand.SetArgument(Cache.AssetNames[matchingAssetIndex], Cache.AssetReferences[matchingAssetIndex]);
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
                    if (inputText.Length >= validStringLength) {
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
                    if (inputText.Length >= firstPossibleValue.Length) {
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
        internal bool DisplayCommandHints() {
            return selectedCommand == -1 || Commands[selectedCommand].GetDisplayName().Length == inputText.Length;
        }
        
        /*
         * Don't break existing text when applying hint!
         * Applying the visual string, assigning matching command + argument is done later in the update loop
         * so we are not assigning the actual command or argument here, only inputting a string that the parser will
         * recognize later!
         */
        internal void UseHint(int indexOfHint) {
            TextBuilder.Clear();
            
            /*
             * applying command hint
             */
            if (hintType[indexOfHint] == COMMAND_TYPE) {
                TextBuilder.Append($"{Commands[hintIndex[indexOfHint]].GetDisplayName()}{SPACE}");
                inputText = TextBuilder.ToString();
                return;
            }

            
            /*
             * applying argument hint
             */
            if (HasCommand()) {
                TextBuilder.Append($"{Commands[selectedCommand].GetDisplayName()}{SPACE}");
                
                if (argumentsAssigned == 0) {
                    TextBuilder.Append($"{hintContent[indexOfHint].text}{SPACE}");
                    inputText = TextBuilder.ToString();
                    return;
                }
                
                /*
                 * Check if last argument is same type, if true then check if value matches
                 * if match, only write hint value? not sure which one atm
                 * if NO match, write the argument + hint value
                 */
                
                for (int i = 0; i < argumentsAssigned - 1; i++) {
                    TextBuilder.Append($"{inputArguments[i].displayName}{SPACE}");
                }

                if (inputText[^1] == SPACE) {
                    TextBuilder.Append($"{inputArguments[argumentsAssigned-1].displayName}{SPACE}");
                }

                TextBuilder.Append($"{hintContent[indexOfHint].text}{SPACE}");
                inputText = TextBuilder.ToString();
            }
            else {
                // Must be a command hint
                TextBuilder.Append($"{Commands[hintIndex[indexOfHint]].GetDisplayName()}{SPACE}");
                inputText = TextBuilder.ToString(); 
            }
        }

        internal int GetCommandIndex() => selectedCommand;
        internal int GetArgumentCount() => argumentsAssigned;
        internal InputArgument GetArgumentByIndex(int index) => inputArguments[index];
        internal bool CanExecuteCommand() {
            if (HasCommand() == false) return false;
            
            ParameterInfo[] arguments = Commands[selectedCommand].GetParameters();
            for (int i = argumentsAssigned; i < arguments.Length; i++) {
                if (arguments[i].HasDefaultValue == false) return false;
            }
            return true;
        }
        internal bool TryExecuteCommand() {
            List<Object> target = Commands[selectedCommand].GetTargets();
            ParameterInfo[] parameters = Commands[selectedCommand].GetParameters();
            object[] argumentValues = new object[parameters.Length];
            for (int i = 0; i < argumentValues.Length; i++) {
                if (i < argumentsAssigned) {
                    argumentValues[i] = inputArguments[i].argumentValue;
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


    /*
     * make into arrays instead of structs
     */
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
}

}