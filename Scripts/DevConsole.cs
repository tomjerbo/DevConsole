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
using Object = UnityEngine.Object;


/*
 * ----------- TODO LIST ----------------
 * Check how generic parameters are handled
 * Check how override methods are handled
 * Hard select commands and arguments, segment input into each part
 * Offset hint menu inline with argument position
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
    const string VALID_VALUE_HINT = "[Valid Input]";
    const string INVALID_VALUE_HINT = "[In-Valid Input]";
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
        LoadCommandHistory();
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
            if (Commands[i].IsSameMethod(methodInfo)) {
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
        hintsToDisplay = ParseHints();
        if (hintsToDisplay > 1) {
            /*
             * how do i sort the array when i have multiple that needs to follow the same index layout?
             * custom sort method?
             */
            // Array.Sort(hintContent, 0, hintsToDisplay);
        }

        if (inputCommand.commandIndex == -1 && inputCommand.HasText() == false && CommandHistoryState == History.HIDE) {
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


            if (inputEvent.Backspace(false) && inputCommand.HasText() == false) {
                if (inputCommand.argumentCount > 0) {
                    --inputCommand.argumentCount;
                    inputCommand.inputContent.text = inputCommand.inputArgumentName[inputCommand.argumentCount].text;
                    moveMarkerToEnd = 2;
                    selectionBump = 0;
                    inputEvent.Use();
                }
                else if (inputCommand.commandIndex != -1) {
                    inputCommand.inputContent.text = inputCommand.commandContent.text;
                    inputCommand.commandIndex = -1;
                    moveMarkerToEnd = 2;
                    selectionBump = 0;
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
            if (inputCommand.TryExecuteCommand()) {
                inputEvent.Use();

                HistoryCommands.Remove(inputCommand.inputContent.text);
                HistoryCommands.Insert(0, inputCommand.inputContent.text);
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
        
        
        float dg_cmdRectPos = 0;
        float[] db_argDrawPos = new float[inputCommand.argumentCount];
        
        GUI.backgroundColor = Color.clear;
        float drawPosX = consoleInputBackground.x;
        if (inputCommand.commandIndex != -1) {
            
            Rect commandRect = new (consoleInputBackground) {
                width = consoleSkin.label.CalcSize(inputCommand.commandContent).x
            };
            GUI.contentColor = Style.SelectedCommand;
            GUI.Label(commandRect, inputCommand.commandContent);
            drawPosX = commandRect.xMax - WIDTH_SPACING;
            dg_cmdRectPos = commandRect.x;
            
            GUI.contentColor = Style.SelectedArgument;
            for (int i = 0; i < inputCommand.argumentCount; i++) {
                Rect argRect = new (commandRect) {
                    width = consoleSkin.label.CalcSize(inputCommand.inputArgumentName[i]).x,
                    x = drawPosX
                };
                GUI.Label(argRect, inputCommand.inputArgumentName[i]);
                drawPosX = argRect.xMax - WIDTH_SPACING;
                db_argDrawPos[i] = argRect.x;
            }
        }
        
        
        GUI.contentColor = Style.InputTextDefault;
        GUIContent inputFieldText = new (inputCommand.inputContent);
        GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);
        Rect inputFieldRect = new (consoleInputBackground) {
            x = drawPosX,
            width = consoleSkin.textField.CalcSize(inputCommand.inputContent).x + WIDTH_SPACING
        };
        string inputText = GUI.TextField(inputFieldRect, inputFieldText.text);
        if (inputText != inputCommand.inputContent.text) {
            CommandHistoryState = History.HIDE;
        }
        inputCommand.inputContent.text = inputText;
        
        
        /*
         * Draw argument hint box
         */
        if (inputCommand.commandIndex != -1) {
            ParameterInfo[] methodParameters = Commands[inputCommand.commandIndex].GetParameters();
            if (inputCommand.argumentCount < methodParameters.Length) {
                TextBuilder.Clear();
                TextBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGBA(Style.InputArgumentTypeBorder)}><</color>");
                TextBuilder.Append($"{methodParameters[inputCommand.argumentCount].ParameterType.Name}");
                TextBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGBA(Style.InputArgumentTypeBorder)}>></color>");
                
                
                GUIContent argumentHint = new ($"< {methodParameters[inputCommand.argumentCount].ParameterType.Name} >");
                Vector2 argumentHintSize = consoleSkin.label.CalcSize(argumentHint);
                Rect argumentHintRect = new (inputFieldRect) {
                    x = inputFieldRect.xMax,
                    width = argumentHintSize.x,
                };
                
                argumentHintRect.position += new Vector2(Style.ArgumentTypeHintSpacing, Style.ArgumentTypeBumpCurve.Evaluate(argumentHintBump) * Style.ArgumentTypeOffsetAmount);
                
                GUI.contentColor = Style.InputArgumentType;
                argumentHint.text = TextBuilder.ToString();
                GUI.Label(argumentHintRect, argumentHint);
            }
        }
        
        
        
        /*
         * debug box
         */
        GUI.contentColor = Color.yellow;
        GUIContent debug = new () {
            text = $"Selected Hint Index: {selectedHint}\n" +
                   $"Color string: {ColorUtility.ToHtmlStringRGBA(Style.HintTextColorDefault)}\n" +
                   $"Command Draw Pos: {dg_cmdRectPos}\n"
        };

        for (int i = 0; i < inputCommand.argumentCount; i++) {
            debug.text += $"Arg {i} Draw Pos: {db_argDrawPos[i]}\n";
        }
        Vector2 size = consoleSkin.box.CalcSize(debug);
        if (true) {
            GUI.Box(new Rect(Screen.width - size.x - WIDTH_SPACING, HEIGHT_SPACING, size.x,size.y + HEIGHT_SPACING), debug);
        }
        
        
        
        
        /*
         * Draw Hint Box
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
            
            
            GUI.backgroundColor = inputCommand.CanExecuteCommand() ? Style.ValidCommand : Style.BorderColor;
            Rect hintBackground = new (inputFieldRect) {
                width = maximumWidth,
                height = maximumHeight,
                y = consoleInputDrawPos.y + 1 - maximumHeight,
            };
            
            
            GUI.Box(hintBackground, string.Empty, consoleSkin.customStyles[0]);
            Vector2 hintStartPos = hintBackground.position;
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
    }


    int ParseHints() {
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
        if (argumentCount >= Commands[commandIndex].GetParameterCount()) {
            return hintsFound;
        }

        
        /*
         * make input text a char[] and index for how long to avoid allocating each input
         * trim/remove just moves temp index
         * how to handle the gui.textfield inputs tho?
         */
        
        string[] inputWithoutMatches = inputCommand.inputContent.text.Split(SPACE, StringSplitOptions.RemoveEmptyEntries);
        Type argumentType = Commands[commandIndex].GetParameterType(argumentCount);

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
        internal int commandIndex;
        internal GUIContent[] inputArgumentName = new GUIContent[12];
        readonly object[] inputArgumentValue = new object[12];
        internal int argumentCount;
        internal GUIContent commandContent = new ();
        
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
             * Are we doing a history command?
             * TODO change this to just input a history type value, wanna avoid string stuff
             */
            if (CommandHistoryState == History.SHOW) {
                inputContent.text = string.Empty;
                Debug.LogError("History not implemented yet");
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
                inputArgumentValue[argumentCount] = argumentValue;
            }
            else {
                inputArgumentName[argumentCount].text = argumentValue.ToString();
                inputArgumentValue[argumentCount] = argumentValue;
            }
            
            argumentCount++;
        }
        internal bool CanExecuteCommand() {
            if (commandIndex == -1) return false;
            
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