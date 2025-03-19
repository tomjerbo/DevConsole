/*
 * Enable this for projects with URP
 */

//#define URP_ENABLED


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;


/*
 * ----------- TODO LIST ----------------
 * Check how generic parameters are handled
 * Check how override methods are handled
 * Hard select commands and arguments, segment input into each part
 * Offset hint menu inline with argument position
 * Add toast menu for executed commands
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
    const int MAX_HINTS = 32;
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
    static DevConsoleCache Cache;
    static DevConsoleStyle Style;
    static readonly string CommandHistoryPath = Path.Combine(Application.persistentDataPath, "DevConsole-CommandHistory.txt");
    static readonly CommandData[] Commands = new CommandData[256];
    static readonly int[] HintIndex = new int[MAX_HINTS];
    static readonly Type[] HintType = new Type[MAX_HINTS];
    static readonly GUIContent[] HintContent = new GUIContent[MAX_HINTS];
    static readonly Type SO_TYPE = typeof(ScriptableObject);
    static readonly Type COMMAND_TYPE = typeof(CommandData);
    static History CommandHistoryState;
    static int HintArgumentIndex = -1;
    static int StaticCommandCount;
    int hintsToDisplay;
    int totalCommandCount;
    int historySelectionIndex = -1;

    
    
    // Input
    /*
     * Try to replace strings with textbuilder
     * TextBuilder.Remove(index, length)
     * make char[] and just slice into it?
     * use helper methods to manipulate char[] without adding memory
     */
    static readonly StringBuilder TextBuilder = new (256);
    readonly InputCommand inputCommand = new ();
    readonly List<string> HistoryCommands = new (32);
    bool isActive;
    int moveMarkerToEnd;
    int selectedHint;
    int setFocus;

    enum History {
        HIDE,
        WAIT_FOR_INPUT,
        SHOW,
    }

    
    // Drawing
    GUISkin consoleSkin;
    Vector2 consoleInputDrawPos;
    Vector2 consoleInputSize;
    float selectionBump;
    float argumentHintBump;
    
    
    
    /*
     * Core console functionality
     *
     */
    
    
    
    void InitializeConsole() {
        consoleSkin = Resources.Load<GUISkin>(DEV_CONSOLE_SKIN_PATH);
        Cache = Resources.Load<DevConsoleCache>(DevConsoleCache.ASSET_PATH);
        Style = Resources.Load<DevConsoleStyle>(DevConsoleStyle.ASSET_PATH);
        LoadCommandHistory();
        Array.Fill(HintType, COMMAND_TYPE);
        for (int i = 0; i < HintContent.Length; i++) {
            HintContent[i] = new GUIContent();
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
        SaveCommandHistory();
    }

    
    [DevCommand]
    void SaveCommandHistory() {
        File.WriteAllLines(CommandHistoryPath, HistoryCommands);
    }
    
    
    [DevCommand]
    void LoadCommandHistory() {
        HistoryCommands.Clear();
        HistoryCommands.AddRange(File.ReadAllLines(CommandHistoryPath));
        HistoryCommands.Reverse();
    }

    [DevCommand]
    void OpenCommandHistoryPath() {
        Application.OpenURL(Application.persistentDataPath);
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
        argumentHintBump = Mathf.Lerp(argumentHintBump, 1, Style.ArgumentTypeSpeed * Time.unscaledDeltaTime);
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
        
        
        /*
         * make it possible to show command history as hints, add new hint type
         */
        hintsToDisplay = ParseInputForHints();
        if (hintsToDisplay > 1) {
            /*
             * how do i sort the array when i have multiple that needs to follow the same index layout?
             * custom sort method?
             */
            // Array.Sort(hintContent, 0, hintsToDisplay);
        }

        if (inputCommand.HasText() == false && CommandHistoryState == History.HIDE) {
            CommandHistoryState = History.WAIT_FOR_INPUT;
        }

        if (windowHasFocus) {
            if (selectedHint != -1) {
                if (inputEvent.InsertHint()) {
                    inputCommand.UseHint(selectedHint);
                    CommandHistoryState = History.HIDE;
                    moveMarkerToEnd = 2;
                    selectedHint = -1;
                }
            }
            

            if (inputEvent.NavigateDown()) {
                selectedHint -= 1;
                selectionBump = 0;
                if (CommandHistoryState == History.WAIT_FOR_INPUT) {
                    CommandHistoryState = History.SHOW;
                }
                if (selectedHint < -1) selectedHint = hintsToDisplay - 1;
            }
            else if (inputEvent.NavigateUp()) {
                selectedHint += 1;
                selectionBump = 0;
                if (CommandHistoryState == History.WAIT_FOR_INPUT) {
                    CommandHistoryState = History.SHOW;
                }
                if (selectedHint >= hintsToDisplay) {
                    selectedHint = -1;
                }
            }
            
            selectedHint = Mathf.Clamp(selectedHint, -1, hintsToDisplay-1);
        }
        



        if (inputCommand.HasCommand() && inputEvent.ExecuteCommand(false)) {
            if (inputCommand.TryExecuteCommand()) {
                inputEvent.Use();
                
                HistoryCommands.Remove(inputCommand.inputText);
                HistoryCommands.Insert(0, inputCommand.inputText);
                if (HistoryCommands.Count > 32) {
                    HistoryCommands.RemoveAt(HistoryCommands.Count-1);
                }
                
                inputCommand.Clear();
                CommandHistoryState = History.WAIT_FOR_INPUT;
                moveMarkerToEnd = 2;
                hintsToDisplay = 0;
                selectedHint = -1;
                
                CloseConsole();
            }
        }
        
        
        
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
        GUIContent inputFieldText = new (inputCommand.inputText);
        GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);
        string inputText = GUI.TextField(inputFieldRect, inputFieldText.text);
        if (inputText != inputCommand.inputText) {
            CommandHistoryState = History.HIDE;
        }
        inputCommand.inputText = inputText;
        ParseInputForCommandsAndArguments();
        
        
        /*
         * Draw argument hint box
         */

        if (inputCommand.HasCommand() && HintArgumentIndex != -1) {
            ParameterInfo parameterInfo = Commands[inputCommand.commandIndex].GetParameters()[HintArgumentIndex];
            
            TextBuilder.Clear();
            TextBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGBA(Style.InputArgumentTypeBorder)}>< </color>");
            TextBuilder.Append($"{parameterInfo.ParameterType.Name}");
            TextBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGBA(Style.InputArgumentTypeBorder)}> ></color>");
            
            
            GUIContent argumentHint = new ($"< {parameterInfo.ParameterType.Name} >");
            Vector2 inputTextSize = consoleSkin.textField.CalcSize(inputFieldText);
            Vector2 argumentHintSize = consoleSkin.label.CalcSize(argumentHint);
            
            Rect argumentHintRect = new (consoleInputDrawPos, consoleInputSize);
            argumentHintRect.position += new Vector2(inputTextSize.x + Style.ArgumentTypeHintSpacing, Style.ArgumentTypeBumpCurve.Evaluate(argumentHintBump) * Style.ArgumentTypeOffsetAmount);
            argumentHintRect.width = Mathf.Clamp(argumentHintSize.x, 0, Mathf.Max(0, inputFieldRect.xMax - argumentHintRect.position.x));
            
            GUI.contentColor = Style.InputArgumentType;
            argumentHint.text = TextBuilder.ToString();
            GUI.Label(argumentHintRect, argumentHint);
        }
        
        
        /*
         * debug box
         */
        GUI.contentColor = Color.yellow;
        
        GUIContent debug = new () {
            text = $"Hint argument index: {HintArgumentIndex}\n" +
                   $"Hint type: {HintType[Mathf.Max(0,HintArgumentIndex)].Name}\n" +
                   $"Selected Hint Index: {selectedHint}\n" +
                   $"Color string: {ColorUtility.ToHtmlStringRGBA(Style.HintTextColorDefault)}"
        };
        Vector2 size = consoleSkin.box.CalcSize(debug);
        GUI.Box(new Rect(Screen.width - size.x - WIDTH_SPACING, HEIGHT_SPACING, size.x,size.y + HEIGHT_SPACING), debug);
        
        
        
        
        /*
         * Draw Command Hints
         */
        if (hintsToDisplay > 0 && CommandHistoryState != History.WAIT_FOR_INPUT) {
            float maximumWidth = 0;
            float maximumHeight = 0;
        
            for (int i = 0; i < hintsToDisplay; i++) {
                Vector2 hintTextSize = consoleSkin.label.CalcSize(HintContent[i]);
                maximumWidth = Mathf.Max(hintTextSize.x, maximumWidth);
                maximumHeight += hintTextSize.y + HINT_HEIGHT_TEXT_PADDING;
            }
            maximumWidth += Style.SelectionBumpOffsetAmount;


            float horizontalOffset = 0;
            /*
             * TODO figure out how to correctly get where to offset the hint menu
             */
            // if (inputCommand.HasCommand()) {
            //     int charCount = Commands[inputCommand.commandIndex].GetDisplayName().Length;
            //     for (int i = 0; i < HintArgumentIndex-1; i++) {
            //         charCount += inputCommand.inputArgumentName[i].Length;
            //     }
            //
            //     inputFieldText.text = inputFieldText.text[..charCount];
            //     horizontalOffset = consoleSkin.textField.CalcSize(inputFieldText).x;
            // }
            
            GUI.backgroundColor = Style.BorderColor;
            Rect hintBackgroundRect = new (consoleInputDrawPos + new Vector2(horizontalOffset, (maximumHeight + Style.HintWindowHeightOffset) * -1), new Vector2(maximumWidth, maximumHeight));
            GUI.Box(hintBackgroundRect, "");
            /*
             * make into scroll region?
             * or make a manual one
             */
            // GUI.BeginScrollView(hintBackgroundRect)
        
            Vector2 hintStartPos = hintBackgroundRect.position;
            float stepHeight = maximumHeight / hintsToDisplay;
            for (int i = 0; i < hintsToDisplay; i++) {
                bool isSelected = i == selectedHint;
            
                float offsetDst = isSelected ? Style.SelectionBumpCurve.Evaluate(selectionBump) * Style.SelectionBumpOffsetAmount : 0;
                Vector2 pos = hintStartPos + new Vector2(offsetDst, maximumHeight - (i+1) * stepHeight);
                
                GUI.contentColor = isSelected ? Style.HintTextColorSelected : Style.HintTextColorDefault;
                GUI.Label(new Rect(pos, new Vector2(maximumWidth, stepHeight)), HintContent[i]);
            }
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


    int ParseInputForHints() {
        int hintsFound = 0;
        
        /*
         * Command history hints
         */
        if (CommandHistoryState != History.HIDE) {
            for (int i = 0; i < HistoryCommands.Count; i++) {
                if (hintsFound == MAX_HINTS) break;

                HintContent[hintsFound].text = HistoryCommands[i];
                HintIndex[hintsFound] = i;
                hintsFound++;
            }

            return hintsFound;
        }
        
        
        if (inputCommand.HasText() == false) {
            return hintsFound;
        }
        
        // TODO wanna add sorting based on how many matches we have, best result at the top
        // Fill with commands that match
        if (inputCommand.DisplayCommandHints()) {
            HintArgumentIndex = -1;
            string[] inputWords = inputCommand.inputText.Split(SPACE, StringSplitOptions.RemoveEmptyEntries);
           
            for (int i = 0; i < totalCommandCount; i++) {
                if (hintsFound == MAX_HINTS) break;
                
                bool matchingHint = true; 
                foreach (string word in inputWords) {
                    if (Commands[i].GetDisplayName().Contains(word, StringComparison.InvariantCultureIgnoreCase) == false) {
                        matchingHint = false;
                        break;
                    }
                }

                if (matchingHint) {
                    HintContent[hintsFound].text = Commands[i].GetFullHint();
                    HintIndex[hintsFound] = i;
                    HintType[hintsFound] = COMMAND_TYPE;
                    hintsFound++;
                }
            }

            return hintsFound;
        }


        /*
         * check command for what the next argument type is and parse the appropriate part of input accordingly
         * 
         */


        int argumentCount = inputCommand.argumentCount;
        int commandIndex = inputCommand.commandIndex;

        /*
         * already found all the arguments possible
         */
        if (argumentCount >= Commands[commandIndex].GetParameterCount()) {
            return hintsFound;
        }

        
        /*
         * make input text a char[] and index for how long to avoid allocating each input
         * trim/remove just moves temp index
         * how to handle the gui.textfield inputs tho?
         */
        // Ugly string stuff
        string inputText = inputCommand.inputText;
        inputText = inputText.Remove(0, Commands[commandIndex].GetDisplayName().Length).TrimStart(SPACE);
        for (int i = 0; i < argumentCount; i++) {
            int argumentLength = inputCommand.inputArgumentName[i].Length;
            if (i == argumentCount - 1 && argumentLength == inputText.Length) {
                argumentCount--;
            }
            else {
                inputText = inputText.Remove(0, argumentLength).TrimStart(SPACE);
            }
        }

        if (HintArgumentIndex != argumentCount) {
            argumentHintBump = 0;
        }
        HintArgumentIndex = argumentCount;
        
        
        string[] inputWithoutMatches = inputText.Split(SPACE, StringSplitOptions.RemoveEmptyEntries);
        
        
        Type argumentType = Commands[commandIndex].GetParameterType(argumentCount);

        /*
         * Bool
         */

        if (argumentType == typeof(bool)) {
            HintContent[hintsFound].text = bool.TrueString;
            HintType[hintsFound] = argumentType;
            hintsFound++;
            
            HintContent[hintsFound].text = bool.FalseString;
            HintType[hintsFound] = argumentType;
            hintsFound++;
            return hintsFound;
        }

        
        
        
        
        /*
         * Enums
         */

        if (argumentType.IsEnum) {
            string[] namesInsideEnum = argumentType.GetEnumNames();
            for (int i = 0; i < namesInsideEnum.Length; i++) {
                if (hintsFound == MAX_HINTS) break;
                
                bool containsWord = true;
                for (int wordIndex = 0; wordIndex < inputWithoutMatches.Length; wordIndex++) {
                    if (namesInsideEnum[i].Contains(inputWithoutMatches[wordIndex], StringComparison.InvariantCultureIgnoreCase) == false) {
                        containsWord = false;
                        break;
                    }
                }

                if (containsWord) {
                    HintContent[hintsFound].text = namesInsideEnum[i];
                    HintIndex[hintsFound] = i;
                    HintType[hintsFound] = argumentType;
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
                if (hintsFound == MAX_HINTS) break;

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
                    HintContent[hintsFound].text = asset.name;
                    HintIndex[hintsFound] = i;
                    HintType[hintsFound] = SO_TYPE;
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
                    inputCommand.inputArgumentName[inputCommand.argumentCount] = bool.TrueString;
                    inputCommand.inputArgumentValue[inputCommand.argumentCount] = true;
                    inputCommand.argumentCount++;
                }
                else if (inputText.StartsWith(bool.FalseString, StringComparison.InvariantCultureIgnoreCase)) {
                    inputText = inputText.Remove(0, bool.FalseString.Length);
                    inputCommand.inputArgumentName[inputCommand.argumentCount] = bool.FalseString;
                    inputCommand.inputArgumentValue[inputCommand.argumentCount] = false;
                    inputCommand.argumentCount++;
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
                    /*
                     * TODO will sending a raw int to enum argument work?
                     */
                    inputCommand.inputArgumentName[inputCommand.argumentCount] = namesInsideEnum[matchingEnumIndex];
                    inputCommand.inputArgumentValue[inputCommand.argumentCount] = matchingEnumIndex;
                    inputCommand.argumentCount++;
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
                    inputCommand.inputArgumentName[inputCommand.argumentCount] = Cache.AssetNames[matchingAssetIndex];
                    inputCommand.inputArgumentValue[inputCommand.argumentCount] = Cache.AssetReferences[matchingAssetIndex];
                    inputCommand.argumentCount++;
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
                        inputCommand.inputArgumentName[inputCommand.argumentCount] = argumentString;
                        inputCommand.inputArgumentValue[inputCommand.argumentCount] = argumentString[1..^1];
                        inputCommand.argumentCount++;
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
                        inputCommand.inputArgumentName[inputCommand.argumentCount] = firstPossibleValue;
                        inputCommand.inputArgumentValue[inputCommand.argumentCount] = stringToValue;
                        inputCommand.argumentCount++;
                    }
                }
                
                continue;
            }
            
            
            break; // input value doesn't match any argument type, stop looking since we're doing them in order 
        }
    }
    
    

    class InputCommand {
        internal string inputText;
        internal int commandIndex;
        internal string[] inputArgumentName = new string[12];
        internal object[] inputArgumentValue = new object[12];
        internal int argumentCount;
        
        internal void Clear() {
            inputText = string.Empty;
            commandIndex = -1;
            argumentCount = 0;
        }
        internal void RemoveSelection() {
            commandIndex = -1;
            argumentCount = 0;
        }
        internal void SelectCommand(int matchingCommandIndex) => commandIndex = matchingCommandIndex;
        internal bool HasText() => string.IsNullOrEmpty(inputText) == false;
        internal bool HasCommand() => commandIndex != -1;
        internal bool DisplayCommandHints() {
            return commandIndex == -1 || Commands[commandIndex].GetDisplayName().Length == inputText.Length;
        }
        
        
 
        /*
         * something is wacky here..
         * we are showing hints for the previous argument if the inputText isnt longer than the pure cmd + arg strings
         * why doesnt 
         */
        internal void UseHint(int indexOfHint) {
            TextBuilder.Clear();
            
            /*
             * Are we doing a history command?
             */
            if (CommandHistoryState == History.SHOW) {
                TextBuilder.Append(HintContent[indexOfHint].text);
                inputText = TextBuilder.ToString();
                return;
            }
            
            
            
            /*
             * When applying command hint
             */
            if (HintType[indexOfHint] == COMMAND_TYPE) {
                TextBuilder.Append($"{Commands[HintIndex[indexOfHint]].GetDisplayName()}{SPACE}");
                inputText = TextBuilder.ToString();
                return;
            }

            
            /*
             * When applying argument hint
             */
        
            TextBuilder.Append($"{Commands[commandIndex].GetDisplayName()}{SPACE}");
            
            for (int i = 0; i < argumentCount; i++) {
                /*
                 * This break here is the fellow that is making sure we don't double write the
                 * last argument if we're replacing it instead of adding a new argument to the end
                 */
                if (HintArgumentIndex == i) break;
                TextBuilder.Append($"{inputArgumentName[i]}{SPACE}");
            }

            TextBuilder.Append($"{HintContent[indexOfHint].text}{SPACE}");
            inputText = TextBuilder.ToString();
        }
        
        internal bool CanExecuteCommand() {
            if (HasCommand() == false) return false;
            
            ParameterInfo[] arguments = Commands[commandIndex].GetParameters();
            for (int i = argumentCount; i < arguments.Length; i++) {
                if (arguments[i].HasDefaultValue == false) return false;
            }
            return true;
        }
        internal bool TryExecuteCommand() {
            List<Object> target = Commands[commandIndex].GetTargets();
            ParameterInfo[] parameters = Commands[commandIndex].GetParameters();
            object[] argumentValues = new object[parameters.Length];
            for (int i = 0; i < argumentValues.Length; i++) {
                if (i < argumentCount) {
                    argumentValues[i] = inputArgumentValue[i];
                }
                else {
                    if (parameters[i].HasDefaultValue == false) 
                        return false;
                    
                    argumentValues[i] = parameters[i].DefaultValue;
                }
            }

            for (int i = 0; i < target.Count; i++) {
                if (commandIndex > StaticCommandCount && target[i] == null) {
                    continue;
                }
                Commands[commandIndex].GetMethod().Invoke(target[i], argumentValues);
            }
            
            
            return true;
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