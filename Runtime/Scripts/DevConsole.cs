/*
 * Enable this for projects with URP
 */

// #define URP_ENABLED
#define DEVCONSOLE_DEBUG


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
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



/* 
 * TODO Rework ====
 *
 * Custom parsing for savefiles with named values, maybe use something like json, must be tiny and without external dependencies!
 * Treat input as string always, having to select the command/argument is very tedious, editing text is much faster to use
 * Optional placement of having it on the top vs bottom of screen? help menu adjusted accordingly both visually and index wise with directional inputs
 * Toast menu to show output & messages, clear command
 * scrolling through history commands should replace input text field to it's quick to select+use, maybe same for hints?
 * Look into using my ui system for handling navigation
 *
 *
 *
 * have toggle for displaying arguments in hint view, when in command mode
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
    
    
    [Conditional("DEVCONSOLE_DEBUG")]
    void Log(object message, Object context = null) {
        Debug.Log(message.ToString(), context);
    }

    [Conditional("DEVCONSOLE_DEBUG")]
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

    public struct CHAR {
        internal const char SPACE = ' ';
        internal const char EMPTY = '\0';
    }
    
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
    static readonly Type TYPE_SO = typeof(ScriptableObject);
    static readonly Type TYPE_COMMAND_DATA = typeof(CommandData);
    static History CommandHistoryState;
    static int StaticCommandCount;
    int hintsToDisplay;
    int hint_display_index_start;
    int active_cmd_count;
    int selected_command_idx = -1;


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
    readonly List<MacroCommand> macroCommands = new(12);
    static readonly List<GUIContent> ToastMessages = new(128);
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
        
        // Cache & Style gets assigned during build step!
#if UNITY_EDITOR
        Cache = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleCache>(DEV_CONSOLE_CACHE_PATH);
        Style = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleStyle>(DEV_CONSOLE_STYLE_PATH);
#endif
    }

    void InitializeConsole() {
        Array.Fill(HintValue, TYPE_COMMAND_DATA);
        for (int i = 0; i < HintContent.Length; i++) {
            HintContent[i] = new GUIContent();
        }
        for (int i = 0; i < inputCommand.inputArgumentName.Length; i++) {
            inputCommand.inputArgumentName[i] = new GUIContent();
        }
    }
    
    void LoadStaticCommands() {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (Assembly assembly in assemblies) {
            if (assembly.FullName.StartsWith("Assembly-CSharp", StringComparison.Ordinal)
                || assembly.FullName.StartsWith("Jerbo", StringComparison.Ordinal)) {
                CheckAssemblyForStaticCommands(assembly);
            }
        }
        StaticCommandCount = active_cmd_count;
    }

    void CheckAssemblyForStaticCommands(Assembly assembly) {
        Type[] assemblyTypes = assembly.GetTypes();
        foreach (Type loadedType in assemblyTypes) {
            MethodInfo[] methodsInType = loadedType.GetMethods(STATIC_BINDING_FLAGS);
            foreach (MethodInfo methodInfo in methodsInType) {
                DevCommand devCommand = methodInfo.GetCustomAttribute<DevCommand>();
                if (devCommand == null) continue;
                    
                Commands[active_cmd_count++].AssignMethod(devCommand, methodInfo, null);
            }
                
                
            FieldInfo[] fieldsInType = loadedType.GetFields(STATIC_BINDING_FLAGS);
            foreach (FieldInfo fieldInfo in fieldsInType) {
                DevCommand devCommand = fieldInfo.GetCustomAttribute<DevCommand>();
                if (devCommand == null) continue;
                    
                Commands[active_cmd_count++].AssignField(devCommand, fieldInfo, null);
            }
        }
        
        Log($"--- <Assembly Commands ({assembly.GetName().Name}) : {active_cmd_count} > ---");
        for (int i = 0; i < active_cmd_count; i++) {
            Log($"Static Command: {Commands[i].displayName}");
        }
        Log($"--- </Assembly Commands ({assembly.GetName().Name}) > ---");
    }
    
    void LoadInstanceCommands() {
        active_cmd_count = StaticCommandCount;
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
                    Commands[active_cmd_count++].AssignMethod(devCommand, methodInfo, scriptBase);
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
                    Commands[active_cmd_count++].AssignField(devCommand, fieldInfo, scriptBase);
                }
            }
        }
        
        Log($"Loaded Instance Commands: {active_cmd_count-StaticCommandCount} from -> {monoBehavioursInScene.Length} components.");
        for (int i = StaticCommandCount; i < active_cmd_count; i++) {
            Log($"Instance Command: {Commands[i].displayName}");
        }
        Log("--- Instanced Commands ---");
    }
    
    bool HasFoundInstancedCommand(object commandTarget, out int index) {
        bool isTargetMethod = commandTarget is MethodInfo;
        MethodInfo methodInfo = commandTarget as MethodInfo;
        FieldInfo fieldInfo = commandTarget as FieldInfo;
        
        for (int i = StaticCommandCount; i < active_cmd_count; i++) {
            
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


            for (int i = 0; i < active_cmd_count; i++) {
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
    void StartMacro(KeyCode key, bool writeOverShortcutKey = false) {
        if (activeMacro != null)
            return;

        for (var i = 0; i < macroCommands.Count; i++) {
            var macroCommand = macroCommands[i];
            if (macroCommand.key == key) {
                if (writeOverShortcutKey) {
                    macroCommands.RemoveAt(i);
                    break;
                }
                else {
                    Debug.LogError($"DevConsole macro with key ({key}) already exists!");
                    return;
                }
                    
            }
        }

        activeMacro = new MacroCommand() {
            key = key
        };
    }

    [DevCommand]
    void EndMacro() {
        if (activeMacro == null) return;
        if (activeMacro.commands.Count > 0) {
            macroCommands.Add(activeMacro);
        }

        ToastMessages.Add(new GUIContent($"Ended Macro: Key [{activeMacro.key}] -> # of Commands {activeMacro.commands.Count}."));
        activeMacro = null;
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

    [DevCommand]
    void Clear() {
        ToastMessages.Clear();
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

        if (TYPE_SO.IsAssignableFrom(argumentType)) {
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
            string[] numbers = argumentString.Split(CHAR.SPACE);
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
                for (int k = 0; k < active_cmd_count; k++) {
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
        Event.current.Use();

        Cache.AssetReferences = Util.LoadAllOfType<ScriptableObject>();
        
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
        EndMacro();
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
            KeyCode[] openKeys = Style != null ? Style.openConsoleKey : Array.Empty<KeyCode>();
            if (inputEvent.OpenConsole(overrideKeys:openKeys)) {
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
                                ToastMessages.Add(new GUIContent($"Macro '{macro.key}'"));
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
	        draw_console_window();
        }
    }

	

    console_input_state console_state = console_input_state.WAITING_FOR_INPUT;

    const int MAX_ARGUMENTS = 16;
    string console_input_text;
    int[] command_argument_idx = new int[MAX_ARGUMENTS];
    int command_selected_idx;
    int history_selected_idx;
    int valid_args;
    
    
    enum console_input_state {
	    WAITING_FOR_INPUT,
	    HISTORY,
	    COMMAND,
    }

    void draw_console_window() {
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
	     * Commands are always treated as raw strings!
	     *
	     *
	     * States:
	     *	History
	     *	Writing
	     *
	     * History:
	     *	display list of history commands
	     *	immediately replace input string with history value, allowing 'arrow up -> enter' to quickly execute command
	     *
	     * Writing:
	     *  start with saving a copy of the input string, find matching command,
	     *		if found skip forward to after the commands length and parse arguments,
	     *		for each argument found, skip forward again and repeat.
	     *		if no full match is found, show suggestions in order of best->worst match.
	     *		early out for argument hints if string is empty, accept 'default/null'
	     *	Match start of input string vs command names
	     *	if match found, save index to command and start index of arguments in input string
	     *	step through input string and try and match arguments, if fail, return what argument index to gather hints for
	     *
	     */


	    
	    
	    
	    /*
	     * decide what state we're in for the console input
	     */

	    

	    /*
	     * if user tries to navigate up or down, intention is to scroll through history
	     * enter history state and start displaying history with first history selected
	     */
	    if (console_state == console_input_state.WAITING_FOR_INPUT) {
		    Event e = Event.current;
		    bool up = e.NavigateUp(false);
		    bool down = e.NavigateUp(false);
		    if (up || down) {
			    console_state = console_input_state.HISTORY;
			    e.Use();
			    // select first or last history command
			    history_selected_idx = down ? HistoryCommands.Count - 1 : 0;
			    
			    // TODO add all history commands to hint list
		    }
	    }

	    
	    
	    // if (console_state == console_input_state.COMMAND) {
		   //  // Reset
		   //  console_input_text = string.Empty;
	    //
		   //  try_find_command_match(ref input_data);
		   //  if (input_data.has_command()) {
			  //   if (Commands[input_data.idx_command].parameterCount > 0) {
				 //    try_match_arguments(ref input_data);
			  //   }
		   //  }
	    // }
	    












	    /*
	     * Does the input have any text in it? if so, try and parse it for a command
	     * parse command or find hints first? same thing?
	     * don't do both at the same time, the check is the expensive part
	     *
	     *
	     *
	     */



	    // Parse input string for command
	    // ReadOnlySpan<char> consoleInputSpan = console_input_text.AsSpan();
	    // int matchingCommandIndex = ParseStringForCommand(ref consoleInputSpan, Commands, active_cmd_count);
	    // bool hasValidCommand = matchingCommandIndex != -1;
	    // bool hasAllArguments = hasValidCommand && Commands[matchingCommandIndex].parameterCount == 0;
	    // if (hasValidCommand && hasAllArguments) {
		   //  // Find arguments
		   //  // ParseStringForArgument()
		   //  // inputCommand.commandIndex = matchingCommandIndex;
	    //
	    // }

	    
	    
	    {
		    /*
		     * Draw Console background
		     */
		    consoleInputDrawPos = new Vector2(WIDTH_SPACING, height - (HEIGHT_SPACING * 2f + Style.ConsoleTextSize));
		    consoleInputSize = new Vector2(width - WIDTH_SPACING * 2f, Style.ConsoleTextSize);

		    Rect consoleInputBackground = new (consoleInputDrawPos, consoleInputSize);
		    GUI.backgroundColor = activeMacro == null ? Style.BackgroundColor : Style.RecordMacroColor;
		    GUI.Box(consoleInputBackground, string.Empty, BoxBorderSkin());
		    GUI.backgroundColor = Style.BackgroundColor;
		    GUI.Box(consoleInputBackground, string.Empty);
		    
		    
		    /*
		     * Draw Console text input
		     */
		    GUI.backgroundColor = Color.clear;
		    GUI.contentColor = Style.InputTextDefault;
		    GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);
		    Rect inputFieldRect = new (consoleInputBackground) {
			    x = 12,
			    width = consoleInputBackground.xMax - 12,
		    };
		    string inputText = GUI.TextField(inputFieldRect, console_input_text);
		    
		    // TODO only focus when opening?
			GUI.FocusControl(CONSOLE_INPUT_FIELD_ID);
		    
		    if (console_input_text != inputText) {
			    if (inputText.Length == 0) {
				    console_state = console_input_state.WAITING_FOR_INPUT;
				    console_input_text = string.Empty;
				    command_selected_idx = -1;
				    hintsToDisplay = 0;
			    }
			    else {
				    console_state = console_input_state.COMMAND;
					console_input_text = inputText;
					
					string_section cmd_section = parse_string_for_section(ref console_input_text, 0);
					command_selected_idx = parse_for_command(ref console_input_text, ref cmd_section, Commands, active_cmd_count);
					if (command_selected_idx != -1) {
						(int matching_args, int hints_to_display) parse_arg_result = parse_for_arguments(ref console_input_text, cmd_section.end, Commands[command_selected_idx]);
						valid_args = parse_arg_result.matching_args;
						if (parse_arg_result.matching_args >= Commands[command_selected_idx].num_required_args) {
							// can be executed
						}
						hintsToDisplay = parse_arg_result.hints_to_display;
					}
					else {
						hintsToDisplay = ParseHints();
					}
			    }
		    }
		    
	    
	    
	    
	    /*
	     * DrawHintBox
	     *
	     * extract this into method that takes in current selected index, array of things to display
	     */

	    if (hintsToDisplay > 0) {
		    float maximumWidth = 0;
		    float maxHintHeight = consoleInputDrawPos.y - Style.HintBoxBottomPadding - HEIGHT_SPACING * 2 - Style.HintBoxHeightOffset;
		    float heightPerLine = Style.ConsoleSkin.label.CalcSize(HintContent[0]).y;
		    int hintsToDraw = Mathf.Clamp(Mathf.RoundToInt(maxHintHeight / heightPerLine), 1, hintsToDisplay);
		    float maximumHeight = hintsToDraw * heightPerLine;

		    if (selectedHint < hint_display_index_start) {
			    hint_display_index_start = selectedHint;
		    }
		    else if (selectedHint >= hint_display_index_start + hintsToDraw) {
			    hint_display_index_start = selectedHint - hintsToDraw + 1;
		    }

		    hint_display_index_start = Mathf.Clamp(hint_display_index_start, 0, Mathf.Max(hintsToDisplay - hintsToDraw, 0));

		    for (int i = 0; i < hintsToDraw; i++) {
			    Vector2 hintTextSize = Style.ConsoleSkin.label.CalcSize(HintContent[hint_display_index_start + i]);
			    maximumWidth = Mathf.Clamp(Mathf.Max(hintTextSize.x, maximumWidth), 0,
				    Screen.width - WIDTH_SPACING * 2f);
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

			    float offsetDst = isSelected
				    ? Style.SelectionBumpCurve.Evaluate(selectionBump) * Style.SelectHintBumpOffsetAmount
				    : 0;
			    Vector2 pos = hintStartPos + new Vector2(offsetDst, maximumHeight - (i + 1) * heightPerLine);

			    GUI.contentColor = isSelected ? Style.HintTextColorSelected : Style.HintTextColorDefault;

			    if (CommandHistoryState == History.SHOW) {
				    if (HistoryCommands[hint_display_index_start + i].historyCommandState != 2) {
					    GUI.enabled = false;
				    }
			    }

			    GUI.Label(new Rect(pos, new Vector2(maximumWidth, heightPerLine)),
				    HintContent[hint_display_index_start + i]);

			    if (GUI.enabled == false) {
				    GUI.enabled = true;
			    }
		    }
	    }
	    else {
		    selectedHint = -1;
	    }
	    
	    
	    }

	    
	    
	    
	    
	    
	    
	    
	    
	    
	    
	    

#if DEVCONSOLE_DEBUG
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
		           // $"HistoryCount: {HistoryCommands.Count}\n" +
		           $"[STRUCT] input text: {console_input_text}\n" +
		           $"[State] Console state: {console_state}\n" +
		           $"[State] Selected cmd idx: {command_selected_idx}\n" +
		           $"[State] Selected cmd name: {(command_selected_idx != -1 ? Commands[command_selected_idx].displayName : string.Empty)}\n" +
		           $"[State] Valid args: {valid_args}\n" +
		           // $"[STRUCT] has cmd: {input_data.has_command()}\n" +
		           // $"[STRUCT] cmd match idx: {input_data.idx_command}\n" +
		           // $"[STRUCT] cmd match name: {(input_data.has_command() ? Commands[input_data.idx_command].displayName : "No match!")}\n" +
		           // $"[SPAN] cmd match idx: {matchingCommandIndex}\n" +
		           // $"[SPAN] cmd match name: {(matchingCommandIndex != -1 ? Commands[matchingCommandIndex].displayName : "No match!")}\n" +
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





	    #region old stuff

	    

		if (false)
	    {



	    hintsToDisplay = ParseHints();
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
				    if (CommandHistoryState == History.SHOW &&
				        HistoryCommands[HintIndex[selectedHint]].historyCommandState != 2) {
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
					    inputCommand.inputContent.text =
						    inputCommand.inputArgumentName[inputCommand.argumentCount].text;
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


	    if (inputCommand.CanExecuteCommand() && inputEvent.ExecuteCommand()) {
		    inputCommand.GenerateHistoryCommand(out HistoryCommand historyCommand);
		    if (activeMacro == null) {
			    ToastMessages.Add(new GUIContent(inputCommand.CreateCompleteCommandString()));
			    inputCommand.ExecuteCommand();
		    }
		    else {
			    if (Commands[inputCommand.commandIndex].method.Name == nameof(EndMacro)) {
				    EndMacro();
			    }
			    else {
				    activeMacro.commands.Add(historyCommand);
				    ToastMessages.Add(
					    new GUIContent($"[Macro '{activeMacro.key}'] + {historyCommand.commandDisplayName}"));
			    }
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

	    Rect consoleInputBackground = new (consoleInputDrawPos, consoleInputSize);
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
	     * Draw Console text input
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
	     * Draw Toast Messages
	     */

	    if (ToastMessages.Count > 0) {

		    float maximumWidth = consoleInputSize.x;
		    float maxLines = consoleInputDrawPos.y - Style.HintBoxBottomPadding - HEIGHT_SPACING * 2 -
		                     Style.HintBoxHeightOffset;
		    float heightPerLine = Style.ConsoleSkin.label.CalcSize(ToastMessages[0]).y;
		    int messagesToDraw = Mathf.Clamp(Mathf.RoundToInt(maxLines / heightPerLine), 1, ToastMessages.Count);
		    float maximumHeight = messagesToDraw * heightPerLine;



		    Rect toastWindow = new (inputFieldRect) {
			    width = maximumWidth,
			    height = maximumHeight + Style.HintBoxBottomPadding,
			    x = consoleInputDrawPos.x,
			    y = consoleInputDrawPos.y - Style.HintBoxBottomPadding - maximumHeight - Style.HintBoxHeightOffset,
		    };

		    GUI.backgroundColor = Style.BackgroundColor * 0.6f;
		    GUI.Box(toastWindow, string.Empty);

		    GUI.contentColor = Style.HintTextColorDefault;
		    Vector2 hintStartPos = toastWindow.position;
		    for (int i = 0; i < messagesToDraw; i++) {
			    Vector2 pos = hintStartPos + new Vector2(0, maximumHeight - (i + 1) * heightPerLine);
			    GUI.Label(new Rect(pos, new Vector2(maximumWidth, heightPerLine)),
				    ToastMessages[(messagesToDraw - 1) - i]);
		    }
	    }





	    /*
	     * Draw argument hint box
	     */
	    if (inputCommand.commandIndex != -1) {
		    if (inputCommand.argumentCount < Commands[inputCommand.commandIndex].parameterCount) {
			    TextBuilder.Clear();
			    const string COLOR_END_TAG = "</color>";
			    string colorTag = $"<color=#{ColorUtility.ToHtmlStringRGBA(Style.InputArgumentTypeBorder)}>";
			    // int nameLenght = Commands[inputCommand.commandIndex].parameterNames[inputCommand.argumentCount].Length;
			    TextBuilder.Append(
				    $"({Commands[inputCommand.commandIndex].parameterNames[inputCommand.argumentCount]})");

			    GUIContent argumentHint = new (TextBuilder.ToString());
			    Vector2 argumentHintSize = Style.ConsoleSkin.label.CalcSize(argumentHint);
			    Rect argumentHintRect = new (inputFieldRect) {
				    x = inputFieldRect.x + Style.ConsoleSkin.textField.CalcSize(inputCommand.inputContent).x,
				    width = argumentHintSize.x,
			    };
			    argumentHintRect.position += new Vector2(Style.ArgHelpWidthPadding,
				    Style.ArgumentTypeBumpCurve.Evaluate(argumentHintBump) * Style.ArgHelpBumpOffsetAmount);

			    // Middle
			    // TextBuilder.Insert(nameLenght + 4, COLOR_END_TAG);
			    // TextBuilder.Insert(nameLenght + 3, colorTag);

			    // Start
			    TextBuilder.Insert(1, COLOR_END_TAG);
			    TextBuilder.Insert(0, colorTag);

			    // End
			    TextBuilder.Insert(TextBuilder.Length - 1, colorTag);
			    TextBuilder.Append(COLOR_END_TAG);


			    GUI.contentColor = Style.InputArgumentType;
			    argumentHint.text = TextBuilder.ToString();
			    GUI.Label(argumentHintRect, argumentHint);
		    }
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

    }
	    #endregion
		
    }
    
    
    int ParseHints() {
        int hintsFound = 0;
        
        /*
         * Command history hints
         */
        // if (CommandHistoryState != History.HIDE) {
        //     for (int i = 0; i < HistoryCommands.Count; i++) {
        //         if (hintsFound == MAX_HINTS) break;
        //
        //         HintContent[hintsFound].text = HistoryCommands[i].displayString;
        //         HintIndex[hintsFound] = i;
        //         hintsFound++;
        //     }
        //
        //     return hintsFound;
        // }
        
        
        // TODO wanna add sorting based on how many matches we have, best result at the top
        // Fill with commands that match
        if (selected_command_idx == -1) {
            string[] inputWords = console_input_text.Split(CHAR.SPACE, StringSplitOptions.RemoveEmptyEntries);
           
            for (int i = 0; i < active_cmd_count; i++) {
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
        
        string[] inputWithoutMatches = inputCommand.inputContent.text.Split(CHAR.SPACE, StringSplitOptions.RemoveEmptyEntries);
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

        if (TYPE_SO.IsAssignableFrom(argumentType)) {
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



        return 0;
        /*
         * Vectors, ignore thses
         */
        bool isVec2 = argumentType == typeof(Vector2);
        bool isVec3 = argumentType == typeof(Vector3);
        bool isVec4 = argumentType == typeof(Vector4);
        if (isVec2 || isVec3 || isVec4) {
            string[] numbers = inputCommand.inputContent.text.Split(CHAR.SPACE);
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
    
    // void try_find_command_match(ref input_command_data input_data) {
	   //  input_data.idx_command = -1;
	   //  if (string.IsNullOrEmpty(input_data.input_text)) {
		  //   return;
	   //  }
	   //  
	   //  int idx_longest_match = -1;
	   //  int cmd_match_length = -1;
	   //  for (int i = 0; i < active_cmd_count; i++) {
		  //   int cmd_name_length = Commands[i].displayName.Length;
		  //   if (cmd_match_length < cmd_name_length && input_data.input_text.StartsWith(Commands[i].displayName, StringComparison.OrdinalIgnoreCase)) {
			 //    cmd_match_length = cmd_name_length;
			 //    idx_longest_match = i;
		  //   }
	   //  }
    //
	   //  input_data.idx_command = idx_longest_match;
    // }
    //
    // int try_match_arguments(ref input_command_data input_data) {
	   //  string string_of_arguments = input_data.input_text.Remove(0, Commands[input_data.idx_command].displayName.Length).Trim(CHAR.SPACE);
	   //  if (string.IsNullOrEmpty(string_of_arguments)) {
		  //   return 0;
	   //  }
    //
	   //  int idx_arg = 0;
	   //  CommandData target_command = Commands[input_data.idx_command];
	   //  for (int idx = 0; idx < target_command.parameterCount; idx++) {
		  //   bool found_match = false;
		  //   Type parameter_type = target_command.parameterTypes[idx];
		  //   if (has_valid_argument(ref string_of_arguments, parameter_type) == false) {
			 //    return idx;
		  //   }
	   //  }
    //
	   //  return -1;
    // }


    bool has_valid_argument(ref string argument_string, Type parameter_type) {
			/*
	         * Bool
	         */

	        if (parameter_type == typeof(bool)) {
		        
	            if (argument_string.StartsWith(bool.TrueString, StringComparison.OrdinalIgnoreCase)) {
		            argument_string.Remove(0, bool.TrueString.Length);
	                return true;
	            }
	            
	            if (argument_string.StartsWith(bool.FalseString, StringComparison.OrdinalIgnoreCase)) {
		            argument_string.Remove(0, bool.FalseString.Length);
		            return true;
	            }

	            return false;
	        }
	        
	        
	        /*
	         * Enums
	         */

	        if (parameter_type.IsEnum) {
	            string[] namesInsideEnum = parameter_type.GetEnumNames();
	            int length_of_match = -1;
	            int idx_best_match = -1;
	            for (int i = 0; i < namesInsideEnum.Length; i++) {
		            if (argument_string.StartsWith(namesInsideEnum[i], StringComparison.OrdinalIgnoreCase)) {
			            if (length_of_match < namesInsideEnum[i].Length) {
				            idx_best_match = i;
			            }
		            }
	            }

	            if (idx_best_match == -1) {
					return false;
	            }

	            argument_string.Remove(0, length_of_match);
	            return true;
	        }
	        
	        
	        /*
	         * ScriptableObjects
	         */

	        if (TYPE_SO.IsAssignableFrom(parameter_type)) {
	            for (int i = 0; i < Cache.AssetReferences.Length; i++) {
	                ScriptableObject asset = Cache.AssetReferences[i];
	                if (parameter_type.IsAssignableFrom(asset.GetType()) == false) continue;
	                
	                if (string.Equals(argument_string, asset.name, StringComparison.OrdinalIgnoreCase)) {
	                    return asset;
	                }
	            }

	            return false;
	        }
	        

	        
	        /*
	         * try parse string to argument type and display "Apply Value" hint if its valid, and always select the hint
	         */
	        
	        TypeConverter typeConverter = TypeDescriptor.GetConverter(parameter_type);
	        if (typeConverter.CanConvertFrom(typeof(string))) {
	            object stringToValue = null;
	            try {
	                stringToValue = typeConverter.ConvertFromString(argument_string);
	            }
	            catch {
	                // ignored
	            }

	            return false;
	            // return stringToValue;
	        }
	        
	        
	        /*
	         * Vectors
	         */
	        
	        bool isVec2 = parameter_type == typeof(Vector2);
	        bool isVec3 = parameter_type == typeof(Vector3);
	        bool isVec4 = parameter_type == typeof(Vector4);
	        if (isVec2 || isVec3 || isVec4) {
	            string[] numbers = argument_string.Split(CHAR.SPACE);
	            int count = numbers.Length;
	            if (count < 2 || count > 4)
		            return false;

	            
	            TypeConverter floatConverter = TypeDescriptor.GetConverter(typeof(float));
	            object[] values = new object[count];
	            for (int i = 0; i < count; i++) {
	                try {
	                    values[i] = floatConverter.ConvertFromString(numbers[i]);
	                }
	                catch {
		                return false;

	                }
	            }

	            
	            if (isVec2 && count == 2) {
		            
	                // return new Vector2((float)values[0], (float)values[1]);
	            }
	            
	            if (isVec3 && count == 3) {
	                // return new Vector3((float)values[0], (float)values[1],  (float)values[2]);
	            }
	            
	            if (isVec4 && count == 4) {
	                // return new Vector4((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
	            }
	        }

	        return false;
    }



    struct string_section {
	    public int start_idx;
	    public int length;
	    public int end => start_idx + length;
	    public bool found_start => length != 0;
    }


    string_section parse_string_for_section(ref string input_string, int start_idx) {
	    string_section section = new (); 

	    // TODO can be clearer
	    for (int idx = start_idx; idx < input_string.Length; idx++) {
		    if (section.found_start == false && input_string[idx] != CHAR.SPACE) {
			    section.start_idx = idx;
			    section.length++;
		    }
		    else if (section.found_start) {
			    if (input_string[idx] == CHAR.SPACE) {
				    break;
			    }

			    section.length++;
		    }
	    }

	    return section;
    }

    string_section[] parse_string_for_remaining_sections(ref string input_string, int start_idx) {

	    int next_idx = start_idx;
	    int section_count = count_sections_in_string(ref input_string, start_idx);
	    string_section[] sections = new string_section[section_count];
	    
	    for (int idx = 0; idx < section_count; idx++) {
		    sections[idx] = parse_string_for_section(ref input_string, next_idx);
		    next_idx = sections[idx].end;
	    }

	    return sections;
    }


    int count_sections_in_string(ref string input_string, int start_idx) {
	    int count = 0;
	    int next_idx = start_idx;

	    while (next_idx < input_string.Length) {
			string_section section = new ();
			
	    	for (int idx = next_idx; idx < input_string.Length; idx++) {
			    if (section.found_start == false && input_string[idx] != CHAR.SPACE) {
				    section.start_idx = idx;
				    section.length++;
			    }
			    else if (section.found_start) {
				    if (input_string[idx] == CHAR.SPACE) {
					    break;
				    }
			
				    section.length++;
			    }
	    	}

		    if (section.found_start) {
				next_idx = section.end;
				count++;
		    }
		    else {
			    break;
		    }
	    }

	    return count;
    }
    
    
    // TODO can make methods that gets start & length of cmd/arg string
    int parse_for_command(ref string input, ref string_section section, CommandData[] commands, int num_commands) {
	    int command_idx = -1;
	    
	    for (int idx = 0; idx < num_commands; idx++) {
		    if (commands[idx].displayName.Length != section.length) {
			    continue;
		    }
		    
		    if (string.Compare(input, section.start_idx, commands[idx].displayName, 0, section.length, StringComparison.OrdinalIgnoreCase) == 0) {
			    command_idx = idx;
			    break;
		    }
	    }

	    return command_idx;
    }
    
    /*
     * figure out if we have valid cmd with args or if we should display hints for current arg type
     */
    
    (int matching_args, int hints_to_display) parse_for_arguments(ref string input, int start_idx, CommandData command) {
	    /*
	     * if we find valid matches for all args in command, ignore rest of string
	     */
	    int hints_to_display = 0;
	    int valid_args_found = 0;
	    int next_idx = start_idx;
	    
	    for (int arg_idx = 0; arg_idx < command.parameterCount; arg_idx++) {
		    (bool valid_arg, int next_idx_or_num_hints) parsed_hint_result = parse_hints_for_arg_type(ref input, next_idx, command.parameterTypes[arg_idx]); 
		    if (parsed_hint_result.valid_arg) {
				next_idx = parsed_hint_result.next_idx_or_num_hints;
				valid_args_found++;
		    }
		    else {
			    hints_to_display = parsed_hint_result.next_idx_or_num_hints;
			    break;
		    }
	    }

	    return (valid_args_found, hints_to_display);
    }
    
    (bool valid_arg, int next_idx_or_num_hints) parse_hints_for_arg_type(ref string input, int start_idx, Type arg_type) {
	    int next_idx_or_num_hints = 0;
	    bool valid_arg = false;

	    int length_of_input_left = input.Length - start_idx;
		string_section[] remaining_segments = parse_string_for_remaining_sections(ref input, start_idx);
	    
		/*
		 * possible value must match with all remaining segments to be a valid hint 
		 */
		
		
		/*
		 * TODO #### BOOL ####
		 */
	    
	    if (arg_type == typeof(bool)) {
		    
		    if (remaining_segments.Length == 0) {
			    HintContent[next_idx_or_num_hints].text = bool.TrueString;
			    HintValue[next_idx_or_num_hints++]      = true;
				    
			    HintContent[next_idx_or_num_hints].text = bool.FalseString;
			    HintValue[next_idx_or_num_hints++]      = false;
		    }
		    else if (remaining_segments[0].length == bool.TrueString.Length) {
			    if (string.Compare(input, remaining_segments[0].start_idx, bool.TrueString, 0, remaining_segments[0].length, StringComparison.OrdinalIgnoreCase) == 0) {
				    next_idx_or_num_hints = remaining_segments[0].end;
				    valid_arg = true;
			    }
		    }
		    else if (remaining_segments[0].length == bool.FalseString.Length) {
			    if (string.Compare(input, remaining_segments[0].start_idx, bool.FalseString, 0, remaining_segments[0].length, StringComparison.OrdinalIgnoreCase) == 0) {
				    next_idx_or_num_hints = remaining_segments[0].end;
				    valid_arg = true;
			    }
		    }
		    else {
				// hints
			    bool show_hint_true = true;
			    bool show_hint_false = true;
			    
			    
			    for (int idx = 0; idx < remaining_segments.Length; idx++) {
				    ReadOnlySpan<char> segment = input.AsSpan(remaining_segments[idx].start_idx, remaining_segments[idx].length);
				    
				    if (show_hint_true && bool.TrueString.AsSpan().Contains(segment, StringComparison.OrdinalIgnoreCase) == false) {
						show_hint_true = false;
				    }
				
				    if (show_hint_false && bool.FalseString.AsSpan().Contains(segment, StringComparison.OrdinalIgnoreCase) == false) {
					    show_hint_false = false;
				    }
				
				    if (show_hint_false == false && show_hint_true == false) {
					    break;
				    }
			    }

			    if (show_hint_true) {
				    HintContent[next_idx_or_num_hints].text = bool.TrueString;
				    HintValue[next_idx_or_num_hints++]      = true;
			    }

			    if (show_hint_false) {
				    HintContent[next_idx_or_num_hints].text = bool.FalseString;
				    HintValue[next_idx_or_num_hints++]      = false;
			    }
		    }
	    }
	    
	    
	    /*
	     * TODO #### ENUM ####
	     */
	    else if (arg_type.IsEnum) {
		    
		    // check if valid arg
		    string[] enum_names = arg_type.GetEnumNames();
		    int length_of_match = -1;
		    int idx_best_match = -1;
		    
		    // longest match if any
		    for (int idx = 0; idx < enum_names.Length; idx++) {
			    if (enum_names[idx].Length > length_of_input_left || length_of_match > enum_names[idx].Length) {
				    continue;
			    }

			    if (string.Compare(input, start_idx, enum_names[idx], 0, enum_names[idx].Length, StringComparison.OrdinalIgnoreCase) == 0) {
				    length_of_match = enum_names[idx].Length;
				    idx_best_match = idx;
			    }
		    }

		    // is valid enum arg
		    if (idx_best_match != -1) {
			    valid_arg = true;
			    next_idx_or_num_hints = start_idx + length_of_match;
		    }
		    else {
			    
			    // not valid arg, get hints instead
			    Array enum_values = arg_type.GetEnumValues();
			    
			    for (int enum_idx = 0; enum_idx < enum_names.Length; enum_idx++) { 
				    bool display_as_hint = true;
				    for (int section_idx = 0; section_idx < remaining_segments.Length; section_idx++) {
					    if (remaining_segments[section_idx].length > enum_names[enum_idx].Length) {
						    display_as_hint = false;
						    break;
					    }

					    ReadOnlySpan<char> segment_span = input.AsSpan(remaining_segments[section_idx].start_idx, remaining_segments[section_idx].length);
					    if (enum_names[enum_idx].AsSpan().Contains(segment_span, StringComparison.OrdinalIgnoreCase) == false) {
						    display_as_hint = false;
						    break;
					    }
				    }
				    
				    if (display_as_hint) {
					    HintContent[next_idx_or_num_hints].text = enum_names[enum_idx];
					    HintValue[next_idx_or_num_hints++] = enum_values.GetValue(enum_idx);
				    }
				}
		    }
	    }
	    
	    
	    /*
	     * TODO #### SCRIPTABLE OBJECTS ####
	     */

	    
	    else if (TYPE_SO.IsAssignableFrom(arg_type)) {


		    int length_of_match = -1;
		    int idx_best_match = -1;
		    if (remaining_segments.Length > 0) {
		    	ReadOnlySpan<char> first_segment = input.AsSpan(remaining_segments[0].start_idx, remaining_segments[0].length);
		    	for (int idx = 0; idx < Cache.AssetReferences.Length; idx++) {
					ScriptableObject asset = Cache.AssetReferences[idx];
					if (arg_type.IsAssignableFrom(asset.GetType()) == false || asset.name.Length < length_of_match) {
						continue;
					}
					
					
					if (first_segment.CompareTo(asset.name.AsSpan(), StringComparison.OrdinalIgnoreCase) == 0) {
						length_of_match = asset.name.Length;
						idx_best_match = idx;
					}
				}
		    }
		    
		    if (idx_best_match != -1) {
			    valid_arg = true;
			    next_idx_or_num_hints = start_idx + length_of_match;
		    }
		    else {
			    
			    for (int asset_idx = 0; asset_idx < Cache.AssetReferences.Length; asset_idx++) {
				    ScriptableObject asset = Cache.AssetReferences[asset_idx];
				    if (arg_type.IsAssignableFrom(asset.GetType()) == false) {
					    continue;
				    }

				    bool display_as_hint = true;
				    for (int section_idx = 0; section_idx < remaining_segments.Length; section_idx++) {
					    ReadOnlySpan<char> segment_span = input.AsSpan(remaining_segments[section_idx].start_idx, remaining_segments[section_idx].length);
				    	if (asset.name.AsSpan().Contains(segment_span, StringComparison.OrdinalIgnoreCase) == false) {
						    display_as_hint = false;
						    break;
					    }
				    }

				    if (display_as_hint) {
					    HintContent[next_idx_or_num_hints].text = asset.name;
					    HintValue[next_idx_or_num_hints++] = asset;
				    }
			    }
			    
		    }
	    }
	    
	    
	    
	    /*
	     * TODO #### NUNMBERS AND OTHER ####
	     */

	    else {
		    TypeConverter type_converter = TypeDescriptor.GetConverter(arg_type);
		    if (type_converter.CanConvertFrom(typeof(string))) {
			    object string_to_value = null;
			    try {
				    string_to_value = type_converter.ConvertFromString(input[start_idx..]);
			    }
			    finally {
			    	if (string_to_value != null) {
						valid_arg = true;
						next_idx_or_num_hints = input.Length;
					}
			    }
		    }
	    }
	    
	    
	    

	    return (valid_arg, next_idx_or_num_hints);
    }
    
    

    int parse_string_for_arg_of_type(ref string input, int start_idx, Type arg_type) {
	    return 0;
	    
	    //     
	    //     /*
	    //      * ScriptableObjects
	    //      */
	    //
	    //     if (TYPE_SO.IsAssignableFrom(parameter_type)) {
	    //         for (int i = 0; i < Cache.AssetReferences.Length; i++) {
	    //             ScriptableObject asset = Cache.AssetReferences[i];
	    //             if (parameter_type.IsAssignableFrom(asset.GetType()) == false) continue;
	    //             
	    //             if (string.Equals(argument_string, asset.name, StringComparison.OrdinalIgnoreCase)) {
	    //                 return asset;
	    //             }
	    //         }
	    //
	    //         return false;
	    //     }
	    //     
	    //
	    //     
	    //     /*
	    //      * try parse string to argument type and display "Apply Value" hint if its valid, and always select the hint
	    //      */
	    //     
	    //     TypeConverter typeConverter = TypeDescriptor.GetConverter(parameter_type);
	    //     if (typeConverter.CanConvertFrom(typeof(string))) {
	    //         object stringToValue = null;
	    //         try {
	    //             stringToValue = typeConverter.ConvertFromString(argument_string);
	    //         }
	    //         catch {
	    //             // ignored
	    //         }
	    //
	    //         return false;
	    //         // return stringToValue;
	    //     }


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
            if (HintValue[indexOfHint].GetType() == TYPE_COMMAND_DATA) { 
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
            
            if (TYPE_SO.IsAssignableFrom(argumentValue.GetType())) {
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
            TextBuilder.Append($"{Commands[commandIndex].displayName}{CHAR.SPACE}");
            historyCommand.commandDisplayName = Commands[commandIndex].displayName;
            historyCommand.historyCommandState = 2;
            historyCommand.commandIndex = commandIndex;
            historyCommand.argumentValues = inputArgumentValue[..argumentCount];
            historyCommand.argumentDisplayName = new string[argumentCount];
            
            for (int i = 0; i < argumentCount; i++) {
                historyCommand.argumentDisplayName[i] = inputArgumentName[i].text;
            }
            
            for (int i = 0; i < inputArgumentName.Length; i++) {
                TextBuilder.Append($"{inputArgumentName[i].text}{CHAR.SPACE}");
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

        internal string CreateCompleteCommandString() {
            string fullCommandString = Commands[commandIndex].displayName;
            for (int i = 0; i < Commands[commandIndex].parameterCount; i++) {
                if (i < argumentCount) {
                    fullCommandString += " " + inputArgumentValue[i];
                }
                else {
                    fullCommandString += " " + Commands[commandIndex].defaultParamValue[i];
                }
            }

            return fullCommandString;
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
        public int num_required_args;
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
                if (param.HasDefaultValue == false) {
	                num_required_args++;
                }
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