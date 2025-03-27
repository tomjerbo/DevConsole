/*
 * Enable this for projects with URP
 */

//#define URP_ENABLED


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;


/*
 * ----------- TODO LIST ----------------
 * Check how generic parameters are handled
 * Check how override methods are handled
 * Add toast menu for executed commands
 *
 *
 * Make container ScriptableObject for DevConsole, have it spawn in console & hold references to data objects
 * so unity doesn't ignore them when building and it also removes need to load stuff from resources!
 */



namespace Jerbo.Tools
{
public class DevConsole : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
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
    static readonly object[] HintValue = new object[MAX_HINTS];
    static readonly GUIContent[] HintContent = new GUIContent[MAX_HINTS];
    static readonly Type SO_TYPE = typeof(ScriptableObject);
    static readonly Type COMMAND_TYPE = typeof(CommandData);
    static History CommandHistoryState;
    static int StaticCommandCount;
    int hintsToDisplay;
    int totalCommandCount;

    
    
    // Input
    /*
     * Try to replace strings with textbuilder
     * TextBuilder.Remove(index, length)
     * make char[] and just slice into it?
     * use helper methods to manipulate char[] without adding memory
     */
    static readonly StringBuilder TextBuilder = new (256);
    static readonly List<HistoryCommand> HistoryCommands = new (32);
    readonly InputCommand inputCommand = new ();
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
    bool hasUnparsedHistoryCommands;
    static float argumentHintBump;
    
    
    
    /*
     * Core console functionality
     *
     */
    
    [DevCommand]
    void LogCache() {
        Debug.LogError($"- Cached Assets({Cache.AssetNames.Length}) -");
        for (int i = 0; i < Cache.AssetNames.Length; i++) {
            Debug.LogError($"{Cache.AssetNames[i]} -> {Cache.AssetReferences[i].GetInstanceID()}");
        }
    }
    
    
    void InitializeConsole() {
        consoleSkin = Resources.Load<GUISkin>(DEV_CONSOLE_SKIN_PATH);
        Cache = Resources.Load<DevConsoleCache>(DevConsoleCache.ASSET_PATH);
        Style = Resources.Load<DevConsoleStyle>(DevConsoleStyle.ASSET_PATH);
        Array.Fill(HintValue, COMMAND_TYPE);
        for (int i = 0; i < HintContent.Length; i++) {
            HintContent[i] = new GUIContent();
        }
/*
 * TODO move this somewhere else or destroy inputcommand object
 */
        for (int i = 0; i < inputCommand.inputArgumentName.Length; i++) {
            inputCommand.inputArgumentName[i] = new GUIContent();
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
            if (Commands[i].method == methodInfo) {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    void OnDestroy() {
        SaveCommandHistory();
    }

    
    [DevCommand]
    void SaveCommandHistory() {
        if (HistoryCommands.Count == 0) return;
        
        TextBuilder.Clear();
        TextBuilder.EnsureCapacity(4096*2);
        
        foreach (HistoryCommand cmd in HistoryCommands) {
            TextBuilder.AppendLine((cmd.argumentValues.Length + 2).ToString());
            TextBuilder.AppendLine(cmd.displayString);
            TextBuilder.AppendLine(Commands[cmd.commandIndex].GetDisplayName());
            foreach (string argName in cmd.argumentDisplayName) {
                TextBuilder.AppendLine(argName);
            }
        }
        
        File.WriteAllText(CommandHistoryPath, TextBuilder.ToString());
    }
    
    [DevCommand]
    void ClearCommandHistory(bool saveToFile) {
        HistoryCommands.Clear();
        if (saveToFile) SaveCommandHistory();
    }
    
    [DevCommand]
    void LoadCommandHistory() {
        if (File.Exists(CommandHistoryPath) == false) return;
        
        HistoryCommands.Clear(); // only clear if it works?
        hasUnparsedHistoryCommands = false;
        string[] historyTextFile = File.ReadAllLines(CommandHistoryPath);
        
        int currentReadIndex = 0;
        while (currentReadIndex < historyTextFile.Length) {
            if (int.TryParse(historyTextFile[currentReadIndex++], out int linesOfCommand) == false) {
                Debug.LogError("Error parsing command history! Try clearing history file to remove invalid values!");
                break;
            }
            int argumentCount = linesOfCommand - 2;
            HistoryCommand cmd = new () {
                commandIndex = -1,
                argumentDisplayName = new string[argumentCount],
                argumentValues = new object[argumentCount],
                displayString = historyTextFile[currentReadIndex++],
            };
            
            
            /*
             * try find command
             */


            for (int i = 0; i < totalCommandCount; i++) {
                if (string.Equals(Commands[i].GetDisplayName(), historyTextFile[currentReadIndex], StringComparison.OrdinalIgnoreCase)) {
                    cmd.commandIndex = i;
                    cmd.historyCommandState = 1;
                    cmd.commandDisplayName = Commands[i].GetDisplayName();
                    break;
                }
            }
            ++currentReadIndex;


            int validArgsFound = 0;
            for (int i = 0; i < argumentCount; i++) {
                cmd.argumentDisplayName[i] = historyTextFile[currentReadIndex + i];
                
                if (cmd.historyCommandState == 1) {
                    object argumentValue = TryGetArgumentValue(ref historyTextFile[currentReadIndex + i], cmd.commandIndex, i);
                    if (argumentValue != null) {
                        cmd.argumentValues[i] = argumentValue;
                        ++validArgsFound;
                    }
                }
            }
            
            if (validArgsFound == argumentCount) {
                cmd.historyCommandState = 2;
            }
            else {
                hasUnparsedHistoryCommands = true;
            }
            
            HistoryCommands.Insert(0,cmd);
            currentReadIndex += argumentCount;
        }
    }

    object TryGetArgumentValue(ref string argumentString, int commandIndex, int argumentIndex) {
        
        /*
         * Bool
         */
        Type argumentType = Commands[commandIndex].parameters[argumentIndex].ParameterType;

        if (argumentType == typeof(bool)) {
            if (string.Equals(argumentString, bool.TrueString, StringComparison.OrdinalIgnoreCase)) {
                return true;
            } 
            if (string.Equals(argumentString, bool.FalseString, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            return null;
        }
        
        
        /*
         * Enums
         */

        if (argumentType.IsEnum) {
            string[] namesInsideEnum = argumentType.GetEnumNames();
            for (int i = 0; i < namesInsideEnum.Length; i++) {
                if (string.Equals(argumentString, namesInsideEnum[i], StringComparison.OrdinalIgnoreCase)) {
                    return argumentType.GetEnumValues().GetValue(i);
                }
            }
            
            return null;
        }
        
        
        /*
         * ScriptableObjects
         */

        if (SO_TYPE.IsAssignableFrom(argumentType)) {
            for (int i = 0; i < Cache.AssetReferences.Length; i++) {
                ScriptableObject asset = Cache.AssetReferences[i];
                if (argumentType.IsAssignableFrom(asset.GetType()) == false) continue;
                
                if (string.Equals(argumentString, asset.name, StringComparison.OrdinalIgnoreCase)) {
                    return asset;
                }
            }

            return null;
        }

        
        /*
         * try parse string to argument type and display "Apply Value" hint if its valid, and always select the hint
         */
        
        TypeConverter typeConverter = TypeDescriptor.GetConverter(argumentType);
        if (typeConverter.CanConvertFrom(typeof(string))) {
            object stringToValue = null;
            try {
                stringToValue = typeConverter.ConvertFromString(argumentString);
            }
            catch {
                // ignored
            }

            return stringToValue;
        }

        return null;
    }

    void ParseHistoryCommands() {
        hasUnparsedHistoryCommands = false;
        for (int i = 0; i < HistoryCommands.Count; i++) {
            int validArgsFound = 0;
            HistoryCommand cmd = HistoryCommands[i];
            if (cmd.historyCommandState == 0) {
                for (int k = 0; k < totalCommandCount; k++) {
                    if (string.Equals(cmd.commandDisplayName, Commands[k].GetDisplayName(), StringComparison.OrdinalIgnoreCase)) {
                        cmd.commandIndex = i;
                        cmd.historyCommandState = 1;
                        cmd.commandDisplayName = Commands[k].GetDisplayName();
                        break;
                    }
                }
            }

            int argumentCount = cmd.argumentDisplayName.Length;
            for (int k = 0; k < argumentCount; k++) {
                if (cmd.historyCommandState == 1) {
                    object argumentValue = TryGetArgumentValue(ref cmd.argumentDisplayName[k], cmd.commandIndex, k);
                    if (argumentValue != null) {
                        cmd.argumentValues[k] = argumentValue;
                        ++validArgsFound;
                    }
                }
            }
            
            if (validArgsFound == argumentCount) {
                cmd.historyCommandState = 2;
                Debug.Log($"Successfully parsed command -> {cmd.displayString}");
            }
            else {
                hasUnparsedHistoryCommands = true;
            }
        }
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
        CommandHistoryState = History.WAIT_FOR_INPUT;
        
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
            LoadCommandHistory();
        }
        else {
            LoadInstanceCommands();
            if (hasUnparsedHistoryCommands) {
                ParseHistoryCommands();
            }
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
        GUI.skin = consoleSkin;
        
        selectionBump = Mathf.Lerp(selectionBump, 1, Style.SelectHintBumpSpeed * Time.unscaledDeltaTime);
        argumentHintBump = Mathf.Lerp(argumentHintBump, 1, Style.ArgHelpBumpSpeed * Time.unscaledDeltaTime);
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
        hintsToDisplay = ParseHints();
        if (hintsToDisplay > 1) {
            /*
             * how do i sort the array when i have multiple that needs to follow the same index layout?
             * custom sort method?
             */
            // Array.Sort(hintContent, 0, hintsToDisplay);
        }

        if (inputCommand.commandIndex == -1 && inputCommand.HasText() == false) {
            if (CommandHistoryState == History.HIDE) 
                CommandHistoryState = History.WAIT_FOR_INPUT;
        }
        else {
            CommandHistoryState = History.HIDE;
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


            if (inputEvent.Backspace(false) && inputCommand.HasText() == false) {
                if (inputCommand.argumentCount > 0) {
                    --inputCommand.argumentCount;
                    inputCommand.inputContent.text = inputCommand.inputArgumentName[inputCommand.argumentCount].text;
                    moveMarkerToEnd = 2;
                    argumentHintBump = 0;
                    selectedHint = 0;
                    inputEvent.Use();
                }
                else if (inputCommand.commandIndex != -1) {
                    inputCommand.inputContent.text = inputCommand.commandContent.text;
                    inputCommand.commandIndex = -1;
                    moveMarkerToEnd = 2;
                    argumentHintBump = 0;
                    inputEvent.Use();
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

            selectedHint = Mathf.Clamp(selectedHint, -1, hintsToDisplay - 1);
        }


        
        if (inputCommand.CanExecuteCommand() && inputEvent.ExecuteCommand(false)) {
            if (inputCommand.TryExecuteCommand(out HistoryCommand historyCommand)) {
                inputEvent.Use();

                for (int i = 0; i < HistoryCommands.Count; i++) {
                    if (historyCommand.commandIndex != HistoryCommands[i].commandIndex) continue;
                    if (historyCommand.argumentValues.Length != HistoryCommands[i].argumentValues.Length) continue;
                    bool hasSameArguments = true;
                    for (int k = 0; k < historyCommand.argumentValues.Length; k++) {
                        if (historyCommand.argumentValues[k] != HistoryCommands[i].argumentValues[k]) {
                            hasSameArguments = false;
                            break;
                        }
                    }

                    if (hasSameArguments) {
                        HistoryCommands.RemoveAt(i);
                        break;
                    }
                }
                
                HistoryCommands.Insert(0, historyCommand);
                if (HistoryCommands.Count > 32) {
                    HistoryCommands.RemoveAt(HistoryCommands.Count - 1);
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
        
        GUI.backgroundColor = inputCommand.CanExecuteCommand() ? Style.ValidCommand : Style.BorderColor;
        Rect consoleInputBackground = new(consoleInputDrawPos, consoleInputSize);
        GUI.Box(consoleInputBackground, string.Empty);
        
        
        GUI.backgroundColor = Color.clear;
        float drawPosX = consoleInputBackground.x;
        if (inputCommand.commandIndex != -1) {
            
            Rect commandRect = new (consoleInputBackground) {
                width = consoleSkin.label.CalcSize(inputCommand.commandContent).x
            };
            GUI.contentColor = Style.SelectedCommand;
            GUI.Label(commandRect, inputCommand.commandContent);
            drawPosX = commandRect.xMax - WIDTH_SPACING;
            
            GUI.contentColor = Style.SelectedArgument;
            for (int i = 0; i < inputCommand.argumentCount; i++) {
                Rect argRect = new (commandRect) {
                    width = consoleSkin.label.CalcSize(inputCommand.inputArgumentName[i]).x,
                    x = drawPosX
                };
                GUI.Label(argRect, inputCommand.inputArgumentName[i]);
                drawPosX = argRect.xMax - WIDTH_SPACING;
            }
        }
        
        
        GUI.contentColor = Style.InputTextDefault;
        GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);
        Rect inputFieldRect = new (consoleInputBackground) {
            x = drawPosX,
            width = consoleInputBackground.width - (drawPosX - consoleInputBackground.x)
        };
        string inputText = GUI.TextField(inputFieldRect, inputCommand.inputContent.text);
        inputCommand.inputContent.text = inputText;
        
        
        /*
         * Draw argument hint box
         */
        if (inputCommand.commandIndex != -1) {
            if (inputCommand.argumentCount < Commands[inputCommand.commandIndex].parameterCount) {
                TextBuilder.Clear();
                const string COLOR_END_TAG = "</color>";
                string colorTag = $"<color=#{ColorUtility.ToHtmlStringRGBA(Style.InputArgumentTypeBorder)}>";
                int nameLenght = Commands[inputCommand.commandIndex].parameters[inputCommand.argumentCount].Name.Length;
                TextBuilder.Append($"< {Commands[inputCommand.commandIndex].parameters[inputCommand.argumentCount].Name} | {Commands[inputCommand.commandIndex].parameters[inputCommand.argumentCount].ParameterType.Name} >");
                
                GUIContent argumentHint = new (TextBuilder.ToString());
                Vector2 argumentHintSize = consoleSkin.label.CalcSize(argumentHint);
                Rect argumentHintRect = new (inputFieldRect) {
                    x = inputFieldRect.x + consoleSkin.textField.CalcSize(inputCommand.inputContent).x,
                    width = argumentHintSize.x,
                };
                argumentHintRect.position += new Vector2(Style.ArgHelpWidthPadding, Style.ArgumentTypeBumpCurve.Evaluate(argumentHintBump) * Style.ArgHelpBumpOffsetAmount);

                // Middle
                TextBuilder.Insert(nameLenght + 4, COLOR_END_TAG);
                TextBuilder.Insert(nameLenght + 3, colorTag);
                
                // Start
                TextBuilder.Insert(1, COLOR_END_TAG);
                TextBuilder.Insert(0, colorTag);
                
                // End
                TextBuilder.Insert(TextBuilder.Length-1, colorTag);
                TextBuilder.Append(COLOR_END_TAG);


                GUI.contentColor = Style.InputArgumentType;
                argumentHint.text = TextBuilder.ToString();
                GUI.Label(argumentHintRect, argumentHint);
            }
        }
        
        
        
        /*
         * drawdebug box
         */
        GUI.contentColor = Color.red;
        GUI.backgroundColor = Style.BorderColor;
        GUIContent debug = new () {
            text = $"Selected Hint Index: {selectedHint}\n" +
                   $"Color string: {ColorUtility.ToHtmlStringRGBA(Style.HintTextColorDefault)}\n" +
                   $"CommandHistoryState: {CommandHistoryState}\n" + 
                   $"HistoryCount: {HistoryCommands.Count}"
        };


        Vector2 size = consoleSkin.box.CalcSize(debug);
        if (true) {
            GUI.Box(new Rect(Screen.width - size.x - WIDTH_SPACING, HEIGHT_SPACING, size.x,size.y + HEIGHT_SPACING), debug);
        }
        
        
        
        
        /*
         * DrawHintBox
         */
        if (hintsToDisplay > 0 && CommandHistoryState != History.WAIT_FOR_INPUT) {
            float maximumWidth = 0;
            float maximumHeight = 0;
        
            for (int i = 0; i < hintsToDisplay; i++) {
                Vector2 hintTextSize = consoleSkin.label.CalcSize(HintContent[i]);
                maximumWidth = Mathf.Max(hintTextSize.x, maximumWidth);
                maximumHeight += hintTextSize.y + HINT_HEIGHT_TEXT_PADDING;
            }
            // maximumWidth += Style.SelectHintBumpOffsetAmount;
            
            
            GUI.backgroundColor = inputCommand.CanExecuteCommand() ? Style.ValidCommand : Style.BorderColor;
            Rect hintBackground = new (inputFieldRect) {
                width = maximumWidth,
                height = maximumHeight + Style.HintBoxHeightPadding,
                y = consoleInputDrawPos.y - Style.HintBoxHeightPadding - maximumHeight + 2,
            };
            
            /*
             * TODO make unparsed history commands not selectable and show up as grey'd out
             */
            GUI.Box(hintBackground, string.Empty, consoleSkin.customStyles[0]);
            Vector2 hintStartPos = hintBackground.position;
            float stepHeight = maximumHeight / hintsToDisplay;
            for (int i = 0; i < hintsToDisplay; i++) {
                bool isSelected = i == selectedHint;
                
                float offsetDst = isSelected ? Style.SelectionBumpCurve.Evaluate(selectionBump) * Style.SelectHintBumpOffsetAmount : 0;
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
    }


    int ParseHints() {
        int hintsFound = 0;
        
        /*
         * Command history hints
         */
        if (CommandHistoryState != History.HIDE) {
            for (int i = 0; i < HistoryCommands.Count; i++) {
                if (hintsFound == MAX_HINTS) break;

                HintContent[hintsFound].text = HistoryCommands[i].displayString;
                HintIndex[hintsFound] = i;
                hintsFound++;
            }

            return hintsFound;
        }
        
        
        // TODO wanna add sorting based on how many matches we have, best result at the top
        // Fill with commands that match
        if (inputCommand.commandIndex == -1) {
            string[] inputWords = inputCommand.inputContent.text.Split(SPACE, StringSplitOptions.RemoveEmptyEntries);
           
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
                    HintValue[hintsFound] = Commands[i];
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
        if (argumentCount >= Commands[commandIndex].parameterCount) {
            return hintsFound;
        }

        
        /*
         * make input text a char[] and index for how long to avoid allocating each input
         * trim/remove just moves temp index
         * how to handle the gui.textfield inputs tho?
         */
        
        string[] inputWithoutMatches = inputCommand.inputContent.text.Split(SPACE, StringSplitOptions.RemoveEmptyEntries);
        Type argumentType = Commands[commandIndex].parameters[argumentCount].ParameterType;

        
        
        /*
         * Bool
         */

        if (argumentType == typeof(bool)) {
            HintContent[hintsFound].text = bool.TrueString;
            HintValue[hintsFound] = true;
            hintsFound++;
                    
            HintContent[hintsFound].text = bool.FalseString;
            HintValue[hintsFound] = false;
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
                    HintValue[hintsFound] = argumentType.GetEnumValues().GetValue(i);
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
                    HintValue[hintsFound] = asset;
                    hintsFound++;
                }
            }

            return hintsFound;
        }

        
        
        /*
         * try parse string to argument type and display "Apply Value" hint if its valid, and always select the hint
         */
        
        TypeConverter typeConverter = TypeDescriptor.GetConverter(argumentType);
        if (typeConverter.CanConvertFrom(typeof(string))) {
            object stringToValue = null;
            try {
                stringToValue = typeConverter.ConvertFromString(inputCommand.inputContent.text);
            }
            catch {
                // ignored
            }
            
            HintContent[hintsFound].text = inputCommand.inputContent.text;
            HintValue[hintsFound] = stringToValue;
            hintsFound++;
            
            if (stringToValue != null) {
                selectedHint = 0;
            }
            else {
                selectedHint = -1;
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


    class InputCommand {
        internal GUIContent inputContent = new ();
        internal GUIContent commandContent = new ();
        internal GUIContent[] inputArgumentName = new GUIContent[12];
        readonly object[] inputArgumentValue = new object[12];
        internal int commandIndex;
        internal int argumentCount;
        
        internal void Clear() {
            inputContent.text = string.Empty;
            argumentCount = 0;
            commandIndex = -1;
        }
        internal bool HasText() => string.IsNullOrEmpty(inputContent.text) == false;
        internal void UseHint(int indexOfHint) {
            inputContent.text = string.Empty;
            argumentHintBump = 0;
            
            /*
             * Are we applying from history?
             */
            
            if (CommandHistoryState == History.SHOW) {
                HistoryCommand historyCommand = HistoryCommands[HintIndex[indexOfHint]];
                commandIndex = historyCommand.commandIndex;
                commandContent.text = Commands[commandIndex].GetDisplayName();
                
                argumentCount = historyCommand.argumentValues.Length;
                for (int i = 0; i < argumentCount; i++) {
                    inputArgumentValue[i] = historyCommand.argumentValues[i];
                    inputArgumentName[i].text = historyCommand.argumentDisplayName[i];
                }
                inputContent.text = string.Empty;
                return;
            }
            
            
            
            /*
             * When applying command hint
             */
            if (HintValue[indexOfHint].GetType() == COMMAND_TYPE) { 
                commandIndex = HintIndex[indexOfHint];
                commandContent.text = Commands[commandIndex].GetDisplayName();
                return;
            }

            
            
            /*
             * When applying argument hint
             */

            object argumentValue = HintValue[indexOfHint];
            if (SO_TYPE.IsAssignableFrom(argumentValue.GetType())) {
                inputArgumentName[argumentCount].text = HintContent[indexOfHint].text;
            }
            else {
                inputArgumentName[argumentCount].text = argumentValue.ToString();
            }
            inputArgumentValue[argumentCount] = argumentValue;
            
            argumentCount++;
        }
        internal bool CanExecuteCommand() {
            if (commandIndex == -1) return false;
            
            for (int i = argumentCount; i < Commands[commandIndex].parameterCount; i++) {
                if (Commands[commandIndex].parameters[i].HasDefaultValue == false) return false;
            }
            return true;
        }
        internal bool TryExecuteCommand(out HistoryCommand historyCommand) {
            historyCommand = new HistoryCommand();

            TextBuilder.Clear();
            TextBuilder.Append($"{Commands[commandIndex].GetDisplayName()}{SPACE}");
            historyCommand.commandDisplayName = Commands[commandIndex].GetDisplayName();
            
            List<Object> target = Commands[commandIndex].GetTargets();
            
            object[] argumentValues = new object[Commands[commandIndex].parameterCount];
            for (int i = 0; i < argumentValues.Length; i++) {
                if (i < argumentCount) {
                    argumentValues[i] = inputArgumentValue[i];
                    TextBuilder.Append($"{inputArgumentName[i].text}{SPACE}");
                }
                else {
                    if (Commands[commandIndex].parameters[i].HasDefaultValue == false) 
                        return false;
                    
                    argumentValues[i] = Commands[commandIndex].parameters[i].DefaultValue;
                }
            }

            for (int i = 0; i < target.Count; i++) {
                if (commandIndex > StaticCommandCount && target[i] == null) {
                    continue;
                }
                Commands[commandIndex].method.Invoke(target[i], argumentValues);
            }

            historyCommand.commandIndex = commandIndex;
            historyCommand.argumentValues = inputArgumentValue[..argumentCount];
            historyCommand.argumentDisplayName = new string[argumentCount];
            for (int i = 0; i < argumentCount; i++) {
                historyCommand.argumentDisplayName[i] = inputArgumentName[i].text;
            }
            historyCommand.displayString = TextBuilder.ToString();
            return true;
        }
    }
    
    
    struct CommandData {
        internal MethodInfo method;
        internal ParameterInfo[] parameters;
        internal int parameterCount;
        string displayName;
        string hintText;
        List<Object> targets;

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
        internal List<Object> GetTargets() => targets;
    }

    struct HistoryCommand {
        internal string displayString;
        internal int commandIndex;
        internal int historyCommandState; // 0 not parsed, 1 parsed command, 2 parsed command and args
        internal string commandDisplayName;
        internal object[] argumentValues;
        internal string[] argumentDisplayName;
    }
}

}