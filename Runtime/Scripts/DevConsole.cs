/*
 * Enable this for projects with URP
 */

// #define URP_ENABLED
// #define CONSOLE_DEBUG


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/*
 * ----------- TODO LIST ----------------
 * Check how generic parameters are handled
 * Check how override methods are handled
 * Add toast menu for executed commands
 * URP define easily editable, want to have a settings object with a bool
 * Cache not being saved between sessions when stored in package folder, create folder for assets
 * inside Assets/Plugins to store in
 * 
 * Load/Save location for builds is not the same as editor
 *
 */


namespace Jerbo.DevConsole
{
public class DevConsole : MonoBehaviour
{
    
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void SpawnConsoleInScene() {
        if (FindAnyObjectByType<DevConsole>() != null) {
            Debug.Log("Dev console already exists, not creating a new one!");
            return;
        }
        GameObject consoleContainer = new ("- Dev Console (Editor) -");
        DevConsole console = consoleContainer.AddComponent<DevConsole>();
        DontDestroyOnLoad(console);
        IsOpen = false;
    }
#endif
    
    
    [Conditional("CONSOLE_DEBUG")]
    void Log(object message, Object context = null) {
        Debug.Log(message.ToString(), context);
    }

    [Conditional("CONSOLE_DEBUG")]
    void LogError(string message, Object context = null) {
        Debug.LogError(message, context);
    }

    [DevCommand]
    void PrintCache() {
        if (hasConsoleBeenInitialized == false)
            return;
        
        Cache.PrintCache();
    }
    
    /*
     * Const
     */
    
    
    const BindingFlags BASE_FLAGS = BindingFlags.Default | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags INSTANCED_BINDING_FLAGS = BASE_FLAGS | BindingFlags.Instance;
    const BindingFlags STATIC_BINDING_FLAGS = BASE_FLAGS | BindingFlags.Static;

    /*
     * Assets
     */

    public const string DEV_CONSOLE_SKIN_PATH = PLUGINS_FOLDER_PATH + "DevConsoleSkin.asset";
    public const string DEV_CONSOLE_CACHE_PATH = PLUGINS_FOLDER_PATH + "DevConsoleCache.asset";
    public const string DEV_CONSOLE_STYLE_PATH = PLUGINS_FOLDER_PATH + "DevConsoleStyle.asset";
    public const string PLUGINS_FOLDER_PATH = "Assets/Plugins/DevConsole/";
    const string HISTORY_COMMAND_FILE_VERSION = "FileVersion 0.1";
    const string MACRO_COMMAND_FILE_VERSION = "FileVersion 0.1";

    
    /*
     * Console
     */
    const string CONSOLE_INPUT_FIELD_ID = "Console Input Field";
    const int MAX_COMMANDS = 256;
    const int MAX_HINTS = 32;
    const float WIDTH_SPACING = 8f;
    const float HEIGHT_SPACING = 8f;
    const char SPACE = ' ';


    
    /*
     * Instanced
     */
    
    

    // Core
    bool hasConsoleBeenInitialized;
    [SerializeField] DevConsoleCache Cache;
    [SerializeField] DevConsoleStyle Style;
    public void SetupRefsForBuild(DevConsoleCache cache, DevConsoleStyle style) {
        Cache = cache;
        Style = style;
    }

    
    
    string CommandHistoryPath => Path.Combine(Application.persistentDataPath, "DevConsole-CommandHistory.txt");
    string DevMacroPath => Path.Combine(Application.persistentDataPath, "DevConsole-Macros.txt");
    static readonly CommandData[] Commands = new CommandData[MAX_COMMANDS];
    static readonly int[] HintIndex = new int[MAX_HINTS];
    static readonly object[] HintValue = new object[MAX_HINTS];
    static readonly GUIContent[] HintContent = new GUIContent[MAX_HINTS];
    static readonly Type SO_TYPE = typeof(ScriptableObject);
    static readonly Type COMMAND_TYPE = typeof(CommandData);
    static History CommandHistoryState;
    static int StaticCommandCount;
    int hintsToDisplay;
    int hint_display_index_start;
    int totalCommandCount;
    
    
    
    // Input
    /*
     * Try to replace strings with textbuilder
     * TextBuilder.Remove(index, length)
     * make char[] and just slice into it?
     * use helper methods to manipulate char[] without adding memory
     */
    
    
    public static bool IsOpen { get; private set; }
    static readonly StringBuilder TextBuilder = new (256);
    static List<HistoryCommand> HistoryCommands = new (32);
    readonly InputCommand inputCommand = new ();
    readonly List<MacroCommand> macroCommands = new();
    MacroCommand activeMacro;
    int moveMarkerToEnd;
    int selectedHint;
    int setFocus;

    enum History {
        HIDE,
        WAIT_FOR_INPUT,
        SHOW,
    }
    


    class MacroCommand {
        public KeyCode key;
        public List<HistoryCommand> commands = new();
    }

    
    // Drawing
    Vector2 consoleInputDrawPos;
    Vector2 consoleInputSize;
    float selectionBump;
    bool hasUnparsedHistoryCommands;
    bool hasUnparsedMacroCommands;
    static float argumentHintBump;

    /*
     * Add more shortcuts to make it more obvious
     */
    GUIStyle BoxBorderSkin() => Style.ConsoleSkin.customStyles[0];
    
    
    /*
     * Core console functionality
     */

    void Awake() {
        DontDestroyOnLoad(this);
    }

    void InitializeConsole() {

#if UNITY_EDITOR
        Cache = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleCache>(DEV_CONSOLE_CACHE_PATH);
        Style = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleStyle>(DEV_CONSOLE_STYLE_PATH);
#endif
        
        Array.Fill(HintValue, COMMAND_TYPE);
        for (int i = 0; i < HintContent.Length; i++) {
            HintContent[i] = new GUIContent();
        }
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
                
                Commands[totalCommandCount++].AssignMethod(devCommand, methodInfo, null);
            }
            
            
            FieldInfo[] fieldsInType = loadedType.GetFields(STATIC_BINDING_FLAGS);
            foreach (FieldInfo fieldInfo in fieldsInType) {
                DevCommand devCommand = fieldInfo.GetCustomAttribute<DevCommand>();
                if (devCommand == null) continue;
                
                Commands[totalCommandCount++].AssignField(devCommand, fieldInfo, null);
            }
        }
        
        StaticCommandCount = totalCommandCount;
    }
    
    void LoadInstanceCommands() {
        totalCommandCount = StaticCommandCount;
        MonoBehaviour[] monoBehavioursInScene = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (MonoBehaviour scriptBase in monoBehavioursInScene) {
            
            MethodInfo[] methodsInType = scriptBase.GetType().GetMethods(INSTANCED_BINDING_FLAGS);
            foreach (MethodInfo methodInfo in methodsInType) {
                DevCommand devCommand = methodInfo.GetCustomAttribute<DevCommand>();
                if (devCommand == null) continue;
                
                if (HasFoundInstancedCommand(methodInfo, out int index)) {
                    Commands[index].targets.Add(scriptBase);
                }
                else {
                    Commands[totalCommandCount++].AssignMethod(devCommand, methodInfo, scriptBase);
                }
            }
            
            FieldInfo[] fieldsInType = scriptBase.GetType().GetFields(INSTANCED_BINDING_FLAGS);
            foreach (FieldInfo fieldInfo in fieldsInType) {
                DevCommand devCommand = fieldInfo.GetCustomAttribute<DevCommand>();
                if (devCommand == null) continue;
                
                if (HasFoundInstancedCommand(fieldInfo, out int index)) {
                    Commands[index].targets.Add(scriptBase);
                }
                else {
                    Commands[totalCommandCount++].AssignField(devCommand, fieldInfo, scriptBase);
                }
            }
        }
    }
    
    bool HasFoundInstancedCommand(object commandTarget, out int index) {
        bool isTargetMethod = commandTarget is MethodInfo;
        MethodInfo methodInfo = commandTarget as MethodInfo;
        FieldInfo fieldInfo = commandTarget as FieldInfo;
        
        for (int i = StaticCommandCount; i < totalCommandCount; i++) {
            
            if (isTargetMethod) {
                if (Commands[i].commandType == CommandData.CommandType.METHOD) {
                    if (Commands[i].method == methodInfo) {
                        index = i;
                        return true;
                    }
                }
            }
            else {
                if (Commands[i].field == fieldInfo) {
                    index = i;
                    return true;
                }
            }
        }

        index = -1;
        return false;
    }

    void OnDestroy() {
        SaveHistoryCommands();
        SaveMacroCommands();
        IsOpen = false;
    }
    
    [DevCommand]
    void SaveHistoryCommands() {
        TextBuilder.Clear();
        TextBuilder.EnsureCapacity(4096);
        TextBuilder.AppendLine(HISTORY_COMMAND_FILE_VERSION);
        
        foreach (HistoryCommand cmd in HistoryCommands) {
            TextBuilder.AppendLine((cmd.argumentValues.Length + 2).ToString());
            TextBuilder.AppendLine(cmd.displayString);
            TextBuilder.AppendLine(cmd.commandDisplayName);
            foreach (string argName in cmd.argumentDisplayName) {
                TextBuilder.AppendLine(argName);
            }
        }
        
        File.WriteAllText(CommandHistoryPath, TextBuilder.ToString());
    }
    
    [DevCommand]
    void ClearCommandHistory() {
        HistoryCommands.Clear();
        SaveHistoryCommands();
    }
    
    [DevCommand]
    void LoadHistoryCommands() {
        if (File.Exists(CommandHistoryPath) == false) return;
        
        HistoryCommands.Clear(); // only clear if it works?
        hasUnparsedHistoryCommands = false;
        string[] historyTextFile = File.ReadAllLines(CommandHistoryPath);
        if (historyTextFile[0] == HISTORY_COMMAND_FILE_VERSION) {
            
        }
        else {
            /*
             * Handle versions
             */
            ClearCommandHistory();
            Debug.LogError("Invalid version of CommandHistory save file found!");
            return;
        }
        
        int sliceStart = 1;
        int sliceEnd = historyTextFile.Length;
        ParseHistoryCommands(ref HistoryCommands, ref historyTextFile, sliceStart, sliceEnd);
        foreach (HistoryCommand cmd in HistoryCommands) {
            if (cmd.historyCommandState != 2) {
                hasUnparsedHistoryCommands = true;
                break;
            }
        }
    }

    void ParseHistoryCommands(ref List<HistoryCommand> history, ref string[] historyTextFile, int sliceStart, int sliceEnd) {
        int currentReadIndex = sliceStart;
        while (currentReadIndex < sliceEnd) {
            if (int.TryParse(historyTextFile[currentReadIndex++], out int linesOfCommand) == false) {
                LogError("Error parsing command history! Try clearing history file to remove invalid values!");
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
                if (string.Equals(Commands[i].displayName, historyTextFile[currentReadIndex], StringComparison.OrdinalIgnoreCase)) {
                    cmd.commandIndex = i;
                    cmd.historyCommandState = argumentCount > 0 ? 1 : 2;
                    cmd.commandDisplayName = Commands[i].displayName;
                    break;
                }
            }
            ++currentReadIndex;
            

            int validArgsFound = 0;
            if (cmd.historyCommandState == 1) {
                for (int i = 0; i < argumentCount; i++) {
                    cmd.argumentDisplayName[i] = historyTextFile[currentReadIndex + i];
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
            
            history.Add(cmd);
            currentReadIndex += argumentCount;
        }
    }
    
    /*
     * Shortcuts
     * Macros where you run a series of commands to record them onto a keycode
     * StartMacro / EndMacro command
     *
     * AddShortcut taking in the key and what command
     *
     * Saving the argument
     */

    [DevCommand]
    void StartMacro(KeyCode key) {
        if (activeMacro != null)
            return;
        
        foreach (MacroCommand macroCommand in macroCommands) {
            if (macroCommand.key == key) {
                Debug.LogError($"DevConsole macro with key ({key}) already exists!");
                return;
            }
        }
        
        activeMacro = new MacroCommand() {
            key = key
        };
    }
    
    [DevCommand]
    void SaveMacroCommands() {
        TextBuilder.Clear();
        TextBuilder.EnsureCapacity(4096);
        TextBuilder.AppendLine(MACRO_COMMAND_FILE_VERSION);
        int macroStartIndex = TextBuilder.Length;

        foreach (MacroCommand macroCommand in macroCommands) {
            int lines = 0;
            foreach (HistoryCommand cmd in macroCommand.commands) {
                TextBuilder.AppendLine((cmd.argumentValues.Length + 2).ToString());
                TextBuilder.AppendLine(cmd.displayString);
                TextBuilder.AppendLine(cmd.commandDisplayName);
                lines += 3;
                foreach (string argName in cmd.argumentDisplayName) {
                    TextBuilder.AppendLine(argName);
                    ++lines;
                }
            }

            lines += 2;
            TextBuilder.Insert(macroStartIndex, $"{macroCommand.key}\n");
            TextBuilder.Insert(macroStartIndex, $"{lines}\n");
            macroStartIndex = TextBuilder.Length;
        }
        
        File.WriteAllText(DevMacroPath, TextBuilder.ToString());
    }
    
    [DevCommand]
    void LoadMacroCommands() {
        if (File.Exists(DevMacroPath) == false) return;
        
        macroCommands.Clear(); // only clear if it works?
        string[] historyTextFile = File.ReadAllLines(DevMacroPath);
        if (historyTextFile[0] == MACRO_COMMAND_FILE_VERSION) {
            
        }
        else {
            /*
             * Handle versions
             */
            ClearAllMacros();
            Debug.LogError("Invalid version of MacroCommand save file found!");
            return;
        }
        
        int i = 1;
        while (i < historyTextFile.Length) {
            int lines = int.Parse(historyTextFile[i]);
            MacroCommand macroCommand = new MacroCommand {
                key = Enum.Parse<KeyCode>(historyTextFile[i+1])
            };
            int sliceStart = i + 2;
            int sliceEnd = i + lines;
            ParseHistoryCommands(ref macroCommand.commands, ref historyTextFile, sliceStart, sliceEnd);
            macroCommands.Add(macroCommand);
            i += lines;
        }
    }

    [DevCommand]
    void ClearAllMacros() {
        macroCommands.Clear();
    }
    
    [DevCommand]
    void RemoveMacro(KeyCode key) {
        for (int i = macroCommands.Count - 1; i >= 0; i--) {
            if (macroCommands[i].key == key) {
                macroCommands.RemoveAt(i);
                return;
            } 
        }
    }

    [DevCommand]
    void ShowMacros(bool printCommandNames) {
        foreach (MacroCommand macroCommand in macroCommands) {
            Debug.Log($"Macro -> ({macroCommand.key})");
            if (printCommandNames == false)
                continue;
            foreach (HistoryCommand command in macroCommand.commands) {
                if (command.historyCommandState == 2) {
                    Debug.Log($"Command: {command.commandDisplayName}");
                }
                else {
                    Debug.Log($"Command [Not Parsed]: '{command.displayString}'");
                }
            }
            Debug.Log("------");
        }
    }
    
    object TryGetArgumentValue(ref string argumentString, int commandIndex, int argumentIndex) {
        
        /*
         * Bool
         */
        Type argumentType = Commands[commandIndex].parameterTypes[argumentIndex];

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
        
        
        /*
         * Vectors
         */
        
        bool isVec2 = argumentType == typeof(Vector2);
        bool isVec3 = argumentType == typeof(Vector3);
        bool isVec4 = argumentType == typeof(Vector4);
        if (isVec2 || isVec3 || isVec4) {
            string[] numbers = argumentString.Split(SPACE);
            int count = numbers.Length;
            if (count < 2 || count > 4)
                return null;
            
            TypeConverter floatConverter = TypeDescriptor.GetConverter(typeof(float));
            object[] values = new object[count];
            for (int i = 0; i < count; i++) {
                try {
                    values[i] = floatConverter.ConvertFromString(numbers[i]);
                }
                catch {
                    return null;
                }
            }

            
            if (isVec2 && count == 2) {
                return new Vector2((float)values[0], (float)values[1]);
            }
            
            if (isVec3 && count == 3) {
                return new Vector3((float)values[0], (float)values[1],  (float)values[2]);
            }
            
            if (isVec4 && count == 4) {
                return new Vector4((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
            }
        }
        
        
        return null;
    }

    bool ConnectHistoryCommand(ref List<HistoryCommand> commands) {
        bool hasCommandsToConnect = false;
        for (int i = 0; i < commands.Count; i++) {
            int validArgsFound = 0;
            HistoryCommand cmd = commands[i];
            if (cmd.historyCommandState == 0) {
                for (int k = 0; k < totalCommandCount; k++) {
                    if (string.Equals(cmd.commandDisplayName, Commands[k].displayName, StringComparison.OrdinalIgnoreCase)) {
                        cmd.commandIndex = k;
                        cmd.historyCommandState = 1;
                        cmd.commandDisplayName = Commands[k].displayName;
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
            
            if (cmd.historyCommandState < 2) {
                if (validArgsFound == argumentCount) {
                   cmd.historyCommandState = 2;
                   Log($"Successfully connected command -> {cmd.displayString}");
                }
                else {
                    hasCommandsToConnect = true;
                }
            }
            
            commands[i] = cmd;
        }

        return hasCommandsToConnect;
    }
    
    [DevCommand]
    void OpenSaveFolder() {
        Application.OpenURL(Application.persistentDataPath);
    }
    
    
    /*
     * Console Actions
     */
    
    
    void OpenConsole() {
        IsOpen = true;
        setFocus = 2;
        inputCommand.Clear();
        CommandHistoryState = History.WAIT_FOR_INPUT;
        
#if URP_ENABLED
        UnityEngine.Rendering..DebugManager.instance.enableRuntimeUI = false;
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
            LoadHistoryCommands();
            LoadMacroCommands();

            
            int validH = 0;
            int validM = 0;
            int invalidH = 0;
            int invalidM = 0;
            foreach (var cmd in HistoryCommands) {
                if (cmd.historyCommandState == 2) {
                    validH++;
                }
                else {
                    invalidH++;
                }
            }

            foreach (var cmd in macroCommands.SelectMany(macroCommand => macroCommand.commands)) {
                if (cmd.historyCommandState == 2) {
                    validM++;
                }
                else {
                    invalidM++;
                }
            }

            Log($"Parsing success: H->{validH}/{invalidH + validH} | M->{validM}/{invalidM + validM}");
            
        }
        else {
            LoadInstanceCommands();
            if (hasUnparsedHistoryCommands) {
                hasUnparsedHistoryCommands = ConnectHistoryCommand(ref HistoryCommands);
            }
            
            if (hasUnparsedMacroCommands) {
                for (int i = 0; i < macroCommands.Count; i++) {
                    bool hasUnparsedCommands = ConnectHistoryCommand(ref macroCommands[i].commands);
                    if (hasUnparsedCommands) {
                        hasUnparsedMacroCommands = true;
                    }
                }
            }
            
            int validH = 0;
            int validM = 0;
            int invalidH = 0;
            int invalidM = 0;
            foreach (var cmd in HistoryCommands) {
                if (cmd.historyCommandState == 2) {
                    validH++;
                }
                else {
                    invalidH++;
                }
            }

            foreach (var cmd in macroCommands.SelectMany(macroCommand => macroCommand.commands)) {
                if (cmd.historyCommandState == 2) {
                    validM++;
                }
                else {
                    invalidM++;
                }
            }

            Log($"Parsing success: H->{validH}/{invalidH + validH} | M->{validM}/{invalidM + validM}");
        }
    }
    
    void CloseConsole() {
        IsOpen = false;
        selectedHint = -1;
        if (activeMacro != null) {
            if (activeMacro.commands.Count > 0)
                macroCommands.Add(activeMacro);
            activeMacro = null;
        }
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
        if (IsOpen == false) {
            if (inputEvent.OpenConsole(overrideKeys:Style.openConsoleKey)) {
                OpenConsole();
            }
            else {
                foreach (var macro in macroCommands) {
                    if (inputEvent.KeyDown(macro.key)) {
                        HintIndex[0] = 0;
                        CommandHistoryState = History.SHOW;
                        foreach (var cmd in macro.commands) {
                            HistoryCommands.Insert(0, cmd);
                            inputCommand.UseHint(0);
                            if (inputCommand.CanExecuteCommand()) {
                                inputCommand.ExecuteCommand();
                                inputCommand.Clear();
                            }
                            HistoryCommands.RemoveAt(0);
                        }

                        CommandHistoryState = History.WAIT_FOR_INPUT;
                    }
                }
            }
            return;
        }

        /*
         * Console is active
         */


        if (inputEvent.CloseConsole()) {
            CloseConsole();
        }
        else {
            DrawConsole();
        }
    }
    
    
    void DrawConsole() {
        float width = Screen.width;
        float height = Screen.height;
        Event inputEvent = Event.current;
        Style.ConsoleSkin.label.fontSize = (int)(Style.ConsoleTextSize - HEIGHT_SPACING);
        Style.ConsoleSkin.textField.fontSize = (int)(Style.ConsoleTextSize - HEIGHT_SPACING);
        GUI.skin = Style.ConsoleSkin;

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
                    if (CommandHistoryState == History.SHOW && HistoryCommands[HintIndex[selectedHint]].historyCommandState != 2) {
                        selectionBump = 0;
                    }
                    else {
                        inputCommand.UseHint(selectedHint);
                        CommandHistoryState = History.HIDE;
                        moveMarkerToEnd = 2;
                        selectedHint = -1;    
                    }
                }
            }


            if (inputCommand.HasText() == false && inputEvent.Backspace()) {
                if (inputCommand.argumentCount > 0) {
                    --inputCommand.argumentCount;
                    if (inputEvent.control == false) {
                        inputCommand.inputContent.text = inputCommand.inputArgumentName[inputCommand.argumentCount].text;
                    }
                    moveMarkerToEnd = 2;
                    argumentHintBump = 0;
                    selectedHint = 0;
                }
                else if (inputCommand.commandIndex != -1) {
                    if (inputEvent.control == false) {
                        inputCommand.inputContent.text = inputCommand.commandContent.text;
                    }
                    inputCommand.commandIndex = -1;
                    moveMarkerToEnd = 2;
                    argumentHintBump = 0;
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


        /*
         * should creating a macro run the command? probably not
         * 
         */
        if (inputCommand.CanExecuteCommand() && inputEvent.ExecuteCommand()) {
            inputCommand.GenerateHistoryCommand(out HistoryCommand historyCommand);
            if (activeMacro == null) {
                inputCommand.ExecuteCommand();
            }
            else {
                activeMacro.commands.Add(historyCommand);
            }
        
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

            if (activeMacro == null && Style.keepConsoleOpenAfterCommand == false) {
                CloseConsole();
            }
        }
        

        
        /*
         * draw console input area
         */

        consoleInputDrawPos = new Vector2(WIDTH_SPACING, height - (HEIGHT_SPACING * 2f + Style.ConsoleTextSize));
        consoleInputSize = new Vector2(width - WIDTH_SPACING * 2f, Style.ConsoleTextSize);
        
        Rect consoleInputBackground = new(consoleInputDrawPos, consoleInputSize);
        GUI.backgroundColor = activeMacro == null ? Style.BackgroundColor : Style.RecordMacroColor;
        GUI.Box(consoleInputBackground, string.Empty, BoxBorderSkin());
        GUI.backgroundColor = Style.BackgroundColor;
        GUI.Box(consoleInputBackground, string.Empty);
        
        
        /*
         * icon
         */
        Vector2 iconSize = Vector2.one * Style.ConsoleIconSize;
        Vector2 iconOffset = (Style.ConsoleTextSize - Style.ConsoleIconSize) * 0.5f * Vector2.one;
        iconOffset.x = WIDTH_SPACING;
        
        Rect consoleIconRect = new Rect(consoleInputDrawPos + iconOffset, iconSize);
        int frameCount = Style.ConsoleIconFrames.x * Style.ConsoleIconFrames.y;
        float frameSpeed = frameCount * Style.ConsolIconAnimSpeed;
        int currentFrame = Mathf.FloorToInt(Time.unscaledTime * frameSpeed % frameCount);
        int frameX = currentFrame % Style.ConsoleIconFrames.x;
        int frameY = currentFrame / Style.ConsoleIconFrames.x;
        float frameWidth = 1.0f / Style.ConsoleIconFrames.x;
        float frameHeight = 1.0f / Style.ConsoleIconFrames.y;
        
        Rect textureCoords = new Rect(frameWidth * frameX, frameHeight * frameY, frameWidth, frameHeight);
        GUI.DrawTextureWithTexCoords(consoleIconRect, Style.ConsoleIcon, textureCoords, true);
        
        GUI.backgroundColor = Color.clear;
        
        float inputFieldPosX = consoleInputBackground.x + consoleIconRect.width + WIDTH_SPACING;
        float inputFieldXmax = consoleInputBackground.xMax - consoleIconRect.width;
        float inputFieldHeight = consoleInputBackground.height;
        
        if (inputCommand.commandIndex != -1) {
            Rect commandRect = new () {
                width = Mathf.Clamp(
                    Style.ConsoleSkin.label.CalcSize(inputCommand.commandContent).x, 
                    0, 
                    inputFieldXmax),
                height = inputFieldHeight,
                position = new Vector2(inputFieldPosX, consoleInputDrawPos.y)
            };
            GUI.contentColor = inputCommand.CanExecuteCommand() ? Style.ValidCommand : Style.SelectedCommand;
            GUI.Label(commandRect, inputCommand.commandContent);
            inputFieldPosX = commandRect.xMax - WIDTH_SPACING;
            
            GUI.contentColor = inputCommand.CanExecuteCommand() ? Style.ValidCommand : Style.SelectedArgument;
            for (int i = 0; i < inputCommand.argumentCount; i++) {
                Rect argRect = new (commandRect) {
                    width = Style.ConsoleSkin.label.CalcSize(inputCommand.inputArgumentName[i]).x,
                    height = inputFieldHeight,
                    x = inputFieldPosX
                };
                GUI.Label(argRect, inputCommand.inputArgumentName[i]);
                inputFieldPosX = argRect.xMax - WIDTH_SPACING;
            }
        }
        
        
        /*
         * Console text input
         */
        GUI.backgroundColor = Color.clear;
        GUI.contentColor = Style.InputTextDefault;
        GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);
        Rect inputFieldRect = new (consoleInputBackground) {
            x = inputFieldPosX,
            width = consoleInputBackground.xMax - inputFieldPosX,
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
                // int nameLenght = Commands[inputCommand.commandIndex].parameterNames[inputCommand.argumentCount].Length;
                TextBuilder.Append($"({Commands[inputCommand.commandIndex].parameterNames[inputCommand.argumentCount]})");
                
                GUIContent argumentHint = new (TextBuilder.ToString());
                Vector2 argumentHintSize = Style.ConsoleSkin.label.CalcSize(argumentHint);
                Rect argumentHintRect = new (inputFieldRect) {
                    x = inputFieldRect.x + Style.ConsoleSkin.textField.CalcSize(inputCommand.inputContent).x,
                    width = argumentHintSize.x,
                };
                argumentHintRect.position += new Vector2(Style.ArgHelpWidthPadding, Style.ArgumentTypeBumpCurve.Evaluate(argumentHintBump) * Style.ArgHelpBumpOffsetAmount);

                // Middle
                // TextBuilder.Insert(nameLenght + 4, COLOR_END_TAG);
                // TextBuilder.Insert(nameLenght + 3, colorTag);
                
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
         * DrawHintBox
         */

        if (hintsToDisplay > 0 && CommandHistoryState != History.WAIT_FOR_INPUT) {
            float maximumWidth = 0;
            float maxHintHeight = consoleInputDrawPos.y - Style.HintBoxBottomPadding - HEIGHT_SPACING * 2 - Style.HintBoxHeightOffset;
            float heightPerLine = Style.ConsoleSkin.label.CalcSize(HintContent[0]).y;
            int hintsToDraw = Mathf.Clamp(Mathf.RoundToInt(maxHintHeight / heightPerLine), 1, hintsToDisplay);
            float maximumHeight = hintsToDraw * heightPerLine;
            
            if (selectedHint < hint_display_index_start) {
                hint_display_index_start = selectedHint;
            } else if (selectedHint >= hint_display_index_start + hintsToDraw) {
                hint_display_index_start = selectedHint - hintsToDraw + 1;
            }
            hint_display_index_start = Mathf.Clamp(hint_display_index_start, 0, Mathf.Max(hintsToDisplay - hintsToDraw, 0));
            
            for (int i = 0; i < hintsToDraw; i++) {
                Vector2 hintTextSize = Style.ConsoleSkin.label.CalcSize(HintContent[hint_display_index_start + i]);
                maximumWidth = Mathf.Clamp(Mathf.Max(hintTextSize.x, maximumWidth), 0, Screen.width - WIDTH_SPACING * 2f);
            }
            
            
            Rect hintBackground = new (inputFieldRect) {
                width = maximumWidth,
                height = maximumHeight + Style.HintBoxBottomPadding,
                y = consoleInputDrawPos.y - Style.HintBoxBottomPadding - maximumHeight - Style.HintBoxHeightOffset,
            };

            GUI.backgroundColor = Style.BackgroundColor;
            GUI.Box(hintBackground, string.Empty);
            GUI.Box(hintBackground, string.Empty, BoxBorderSkin());
            
            Vector2 hintStartPos = hintBackground.position;
            for (int i = 0; i < hintsToDraw; i++) {
                bool isSelected = (hint_display_index_start + i) == selectedHint;
                
                float offsetDst = isSelected ? Style.SelectionBumpCurve.Evaluate(selectionBump) * Style.SelectHintBumpOffsetAmount : 0;
                Vector2 pos = hintStartPos + new Vector2(offsetDst, maximumHeight - (i+1) * heightPerLine);
                
                GUI.contentColor = isSelected ? Style.HintTextColorSelected : Style.HintTextColorDefault;

                if (CommandHistoryState == History.SHOW) {
                    if (HistoryCommands[hint_display_index_start + i].historyCommandState != 2) {
                        GUI.enabled = false;
                    }
                }
                GUI.Label(new Rect(pos, new Vector2(maximumWidth, heightPerLine)), HintContent[hint_display_index_start + i]);
                
                if (GUI.enabled == false) {
                    GUI.enabled = true;
                }
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
            inputCommand.Clear();
        }
        
        if (moveMarkerToEnd > 0) {
            --moveMarkerToEnd;
            TextEditor text = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            text.MoveTextEnd();
        }
        
        
        
        
        
        
#if CONSOLE_DEBUG
        /*
         * drawdebug box
         */
        GUI.backgroundColor = Style.BackgroundColor;
        GUI.contentColor = Style.InputTextDefault;
        GUI.enabled = true;
        GUIContent debug = new () {
            text = $"Selected Hint Index: {selectedHint}\n" +
                   $"Command Index: {inputCommand.commandIndex}\n" +
                   // $"Color string: {ColorUtility.ToHtmlStringRGBA(Style.HintTextColorDefault)}\n" +
                   // $"CommandHistoryState: {CommandHistoryState}\n" + 
                   $"HistoryCount: {HistoryCommands.Count}\n" +
                   // $"Hints to draw: {hintsToDraw}\n" + 
                   // $"Height of hints: {maximumHeight}\n" +
                   // $"History Index: "
                   
                   // $"\n" +
                   "",
        };

        if (CommandHistoryState == History.SHOW && selectedHint != -1) {
            HistoryCommand cmd = HistoryCommands[selectedHint];
            string text = $"-- History command #{selectedHint} --\n";
            text += $"commandIndex: {cmd.commandIndex}\n";
            text += $"historyState: {cmd.historyCommandState}\n";
            text += $"displayString: {cmd.displayString}\n";
            text += $"displayName: {cmd.commandDisplayName}\n";
            foreach (var str in cmd.argumentDisplayName) {
                text += $"arg: {str}\n";
            }

            debug.text += text;
        }
        
        Vector2 size = Style.ConsoleSkin.box.CalcSize(debug);
        GUI.Box(new Rect(Screen.width - size.x - WIDTH_SPACING, HEIGHT_SPACING, size.x,size.y + HEIGHT_SPACING), debug);
#endif
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
                    if (Commands[i].displayName.Contains(word, StringComparison.InvariantCultureIgnoreCase) == false) {
                        matchingHint = false;
                        break;
                    }
                }

                if (matchingHint) {
                    HintContent[hintsFound].text = Commands[i].hintText;
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
        Type argumentType = Commands[commandIndex].parameterTypes[argumentCount];

        
        
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
         * Vectors
         */
        bool isVec2 = argumentType == typeof(Vector2);
        bool isVec3 = argumentType == typeof(Vector3);
        bool isVec4 = argumentType == typeof(Vector4);
        if (isVec2 || isVec3 || isVec4) {
            string[] numbers = inputCommand.inputContent.text.Split(SPACE);
            int count = numbers.Length;
            if (count < 2 || count > 4)
                return hintsFound;
            
            TypeConverter floatConverter = TypeDescriptor.GetConverter(typeof(float));
            object[] values = new object[count];
            for (int i = 0; i < count; i++) {
                try {
                    values[i] = floatConverter.ConvertFromString(numbers[i]);
                }
                catch {
                    return hintsFound;
                }
            }

            
            if (isVec2 && count == 2) {
                Vector2 v = new ((float)values[0], (float)values[1]);
                HintContent[hintsFound].text = inputCommand.inputContent.text;
                HintValue[hintsFound] = v;
                
                hintsFound++;
                return hintsFound;
            }
            
            if (isVec3 && count == 3) {
                Vector3 v = new ((float)values[0], (float)values[1],  (float)values[2]);
                HintContent[hintsFound].text = inputCommand.inputContent.text;
                HintValue[hintsFound] = v;
                
                hintsFound++;
                return hintsFound;
            }
            
            if (isVec4 && count == 4) {
                Vector4 v = new ((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
                HintContent[hintsFound].text = inputCommand.inputContent.text;
                HintValue[hintsFound] = v;
                
                hintsFound++;
                return hintsFound;
            }
        }
        
        
        
        return hintsFound;
    }


    class InputCommand {
        internal readonly GUIContent inputContent = new ();
        internal readonly GUIContent commandContent = new ();
        internal readonly GUIContent[] inputArgumentName = new GUIContent[12];
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
                commandContent.text = historyCommand.commandDisplayName;
                
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
                commandContent.text = Commands[commandIndex].displayName;
                return;
            }

            
            
            /*
             * When applying argument hint
             */
            object argumentValue = HintValue[indexOfHint];
            
            
            /*
             * Vectors
             */
            
            if (argumentValue is Vector2 || argumentValue is Vector3 || argumentValue is Vector4) {
                inputArgumentName[argumentCount].text = HintContent[indexOfHint].text;
                inputArgumentValue[argumentCount] = argumentValue;
                argumentCount++;
                return;
            }
            
            /*
             * ScriptableObjects and everything else
             */
            
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
                if (Commands[commandIndex].parameterHasDefault[i] == false) return false;
            }
            return true;
        }
        internal void GenerateHistoryCommand(out HistoryCommand historyCommand) {
            historyCommand = new HistoryCommand();

            TextBuilder.Clear();
            TextBuilder.Append($"{Commands[commandIndex].displayName}{SPACE}");
            historyCommand.commandDisplayName = Commands[commandIndex].displayName;
            historyCommand.historyCommandState = 2;
            historyCommand.commandIndex = commandIndex;
            historyCommand.argumentValues = inputArgumentValue[..argumentCount];
            historyCommand.argumentDisplayName = new string[argumentCount];
            
            for (int i = 0; i < argumentCount; i++) {
                historyCommand.argumentDisplayName[i] = inputArgumentName[i].text;
            }
            
            for (int i = 0; i < inputArgumentName.Length; i++) {
                TextBuilder.Append($"{inputArgumentName[i].text}{SPACE}");
            }
            
            historyCommand.displayString = TextBuilder.ToString();
        }
        internal void ExecuteCommand() {
            object[] argumentValues = new object[Commands[commandIndex].parameterCount];
            for (int i = 0; i < argumentValues.Length; i++) {
                if (i < argumentCount) {
                    argumentValues[i] = inputArgumentValue[i];
                }
                else {
                    argumentValues[i] = Commands[commandIndex].defaultParamValue[i];
                }
            }

            bool isMethod = Commands[commandIndex].commandType == CommandData.CommandType.METHOD;
            for (int i = 0; i < Commands[commandIndex].targets.Count; i++) {
                if (commandIndex > StaticCommandCount && Commands[commandIndex].targets[i] == null)
                    continue;
                
                if (isMethod) { 
                    Commands[commandIndex].method.Invoke(Commands[commandIndex].targets[i], argumentValues);
                }
                else {
                    /*
                     * Actions will be null if no one is subscribed to them, unity events will still be valid
                     * Not doing null check on them since I'm not sure what I want the behaviour to be if it is
                     * Logging it might be really annoying..
                     */
                    if (Commands[commandIndex].field.FieldType == typeof(UnityEvent)) {
                        UnityEvent unityEvent = Commands[commandIndex].field.GetValue(Commands[commandIndex].targets[i]) as UnityEvent;
                        unityEvent?.Invoke();
                    }
                    else if (Commands[commandIndex].field.FieldType == typeof(Action)) {
                        Action action = Commands[commandIndex].field.GetValue(Commands[commandIndex].targets[i]) as Action;
                        if (action == null) {
                            Debug.LogError($"Action is null -> Could be caused by {Commands[commandIndex].field.Name} having no subscribers!");
                        }
                        else {
                            action.Invoke();
                        }
                    }
                    else {
                        Commands[commandIndex].field.SetValue(Commands[commandIndex].targets[i], argumentValues[0]);
                    }
                }
            }
        }
    }
    
    struct CommandData {
        public List<Object> targets;
        public string displayName;
        public string hintText;
        public Type[] parameterTypes;
        public string[] parameterNames;
        public bool[] parameterHasDefault;
        public object[] defaultParamValue;
        public int parameterCount;
        public CommandType commandType;
        public enum CommandType {
            METHOD,
            FIELD,
        }
        internal MethodInfo method;
        internal FieldInfo field;

        public void AssignMethod(DevCommand devCommand, object commandReference, Object target) {
            commandType = CommandType.METHOD;
            method = commandReference as MethodInfo;
            if (method == null) {
                Debug.LogError($"Error trying to assign {CommandType.METHOD}!");
                return;
            }

            displayName = string.IsNullOrEmpty(devCommand.displayName) ? method.Name : devCommand.displayName;
            
            ParameterInfo[] args = method.GetParameters();
            parameterCount = args.Length;
            parameterTypes = new Type[parameterCount];
            parameterNames = new string[parameterCount];
            parameterHasDefault = new bool[parameterCount];
            defaultParamValue = new object[parameterCount];


            TextBuilder.Clear();
            TextBuilder.Append($"{displayName} ");
            for (int i = 0; i < args.Length; i++) {
                ParameterInfo param = args[i];
                TextBuilder.Append($"<{param.Name}> ");
                parameterTypes[i] = param.ParameterType;
                parameterNames[i] = param.Name;
                parameterHasDefault[i] = param.HasDefaultValue;
                defaultParamValue[i] = param.DefaultValue;
            }

            hintText = TextBuilder.ToString();
        
            
            if (targets == null) targets = new List<Object>();
            else targets.Clear();
            targets.Add(target);
        }
        
        public void AssignField(DevCommand devCommand, object commandReference, Object target) {
            commandType = CommandType.FIELD;
            field = commandReference as FieldInfo;
            if (field == null) {
                Debug.LogError($"Error trying to assign {CommandType.FIELD}!");
                return;
            }
            
            displayName = string.IsNullOrEmpty(devCommand.displayName) ? field.Name : devCommand.displayName;


            
            parameterCount = 1;
            parameterTypes = new Type[parameterCount];
            parameterNames = new string[parameterCount];
            parameterHasDefault = new bool[parameterCount];
            defaultParamValue = new object[parameterCount];
            
            if (field.FieldType == typeof(UnityEvent) || field.FieldType == typeof(Action)) {
                parameterCount = 0;
            }

            TextBuilder.Clear();
            TextBuilder.Append($"{displayName} ");
            TextBuilder.Append($"<{field.Name}> ");
            parameterTypes[0] = field.FieldType;
            parameterNames[0] = field.Name;
            parameterHasDefault[0] = false;
            defaultParamValue[0] = null;
            hintText = TextBuilder.ToString();
            

            
            if (targets == null) targets = new List<Object>();
            else targets.Clear();
            targets.Add(target);
        }
    }

    struct HistoryCommand {
        internal int historyCommandState; // 0 not parsed, 1 parsed command, 2 parsed command and args
        internal int commandIndex;
        internal string displayString;
        internal string commandDisplayName;
        internal object[] argumentValues;
        internal string[] argumentDisplayName;
    }
}

}