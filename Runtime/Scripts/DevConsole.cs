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
using UnityEngine.Rendering;
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
    int selected_hint_idx;
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
    Vector2 console_input_draw_pos;
    Vector2 console_input_size;
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
        // Event.current.Use();
        mouse_pos = Event.current.mousePosition;
        command_selected_idx = -1;
        // DebugManager.instance.enableRuntimeUI = false;
        
        // TODO handle assets loading async
        Cache.AssetReferences = Util.LoadAllOfType<ScriptableObject>();
        

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
        selected_hint_idx = -1;
        EndMacro();
        GUI.FocusControl(null);
        
        // DebugManager.instance.enableRuntimeUI = true;
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

	

    /*
     * TODO
     * #### THIS IS THE UP TO DATE STUFF ####
     */
    console_input_state console_state = console_input_state.WAITING_FOR_INPUT;
    device current_device = device.keyboard;
    
    string console_input_text;
    int command_selected_idx = -1;
    int history_selected_idx;
    int force_move_cursor_to_end_ticks;
    parse_arg parse_arg_result;
    Vector2 mouse_pos;

    struct parse_arg {
	    public int valid_args;
	    public int next_idx;
	    public int num_hints;

	    public void reset() {
		    valid_args = 0;
		    next_idx = 0;
		    num_hints = 0;
	    }
    }
    
    struct string_section {
	    public int start_idx;
	    public int length;
	    public int end => start_idx + length;
	    public bool found_start => length != 0;
    }
    
    
    enum console_input_state {
	    WAITING_FOR_INPUT,
	    HISTORY,
	    COMMAND,
    }

    enum device {
	    keyboard,
	    mouse
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
	     * if user tries to navigate up or down, intention is to scroll through history
	     * enter history state and start displaying history with first history selected
	     */
	    
	    
	    Event input_action = Event.current;
	    if (input_action.isKey) {
		    current_device = device.keyboard;
	    }
	    else if (input_action.isMouse || input_action.mousePosition != mouse_pos) {
		    current_device = device.mouse;
		    mouse_pos = input_action.mousePosition;
	    }
	    bool up = input_action.NavigateUp();
	    bool down = input_action.NavigateDown();
	    switch (console_state) {
		    case console_input_state.WAITING_FOR_INPUT:
			    if (up || down) {
				    console_state = console_input_state.HISTORY;
				    input_action.Use();
				    // select first or last history command
				    selected_hint_idx = down ? HistoryCommands.Count - 1 : 0;
				    force_move_cursor_to_end_ticks = 4;
			    }
			    break;
		    
		    case console_input_state.HISTORY:
		    case console_input_state.COMMAND:
			    if (up) {
				    selected_hint_idx++;
				    if (selected_hint_idx >= parse_arg_result.num_hints) {
					    selected_hint_idx = 0;
				    }
				    input_action.Use();
				    force_move_cursor_to_end_ticks = 4;
			    }
			    else if (down) {
				    selected_hint_idx--;
				    if (selected_hint_idx < 0) {
					    selected_hint_idx = parse_arg_result.num_hints - 1;
				    }
				    input_action.Use();
				    force_move_cursor_to_end_ticks = 4;
			    }
			    break;
		    
		    default:
			    throw new ArgumentOutOfRangeException();
	    }
	    
	    
	    {
		    /*
		     * Draw Console background
		     */
		    console_input_draw_pos = new Vector2(WIDTH_SPACING, height - (HEIGHT_SPACING * 2f + Style.ConsoleTextSize));
		    console_input_size = new Vector2(width - WIDTH_SPACING * 2f, Style.ConsoleTextSize);

		    Rect console_input_background = new (console_input_draw_pos, console_input_size);
		    Color background_color = Style.BackgroundColor;
		    if (activeMacro != null) {
			    background_color = Style.RecordMacroColor;
		    } else if (command_selected_idx != -1 && parse_arg_result.valid_args >= Commands[command_selected_idx].num_required_args) {
			    background_color = Style.HintTextColorSelected;
		    }
		    GUI.backgroundColor = background_color;
		    GUI.Box(console_input_background, string.Empty, BoxBorderSkin());
		    GUI.backgroundColor = Style.BackgroundColor;
		    GUI.Box(console_input_background, string.Empty);
		    
		    
		    if (force_move_cursor_to_end_ticks > 0) {
			    TextEditor editor = (TextEditor) GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
			    editor?.MoveTextEnd();
		    }
		    
		    
		    /*
		     * Draw Console text input
		     */
		    GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);

		    // consume mouse clicks is outside text rect to allow selecting hints
		    bool mouse_clicked = console_input_background.Contains(mouse_pos) == false && input_action.mouse_down(KeyCode.Mouse0);
		    bool insert_hint_pressed = current_device == device.keyboard && input_action.InsertHint();
		    
		    GUI.backgroundColor = Color.clear;
		    GUI.contentColor = Style.InputTextDefault;
		    Rect input_field_rect = new (console_input_background) {
			    x = 12,
			    width = console_input_background.xMax - 12,
		    };
		    GUIContent input_content = new GUIContent(console_input_text);
		    string input_text = GUI.TextField(input_field_rect, console_input_text);
		    // TODO only focus when opening?
		    GUI.FocusControl(CONSOLE_INPUT_FIELD_ID);
		    
		    if (console_input_text != input_text) {
			    if (input_text.Length == 0) {
				    console_state = console_input_state.WAITING_FOR_INPUT;
				    console_input_text = string.Empty;
				    command_selected_idx = -1;
				    parse_arg_result.reset();
			    }
			    else {
				    console_state = console_input_state.COMMAND;
					console_input_text = input_text;
					
					string_section cmd_section = parse_string_for_section(ref console_input_text, 0);
					command_selected_idx = parse_for_command(ref console_input_text, ref cmd_section, Commands, active_cmd_count);
					if (command_selected_idx != -1 && has_space_at_index(ref console_input_text, cmd_section.end, true)) {
						parse_arg_result = parse_for_arguments(ref console_input_text, cmd_section.end, Commands[command_selected_idx]);
					}
					else {
						parse_arg_result.num_hints = parse_for_command_hints(ref console_input_text, Commands);
					}
			    }
		    }
		    
		    
		    // hint label
		    float input_text_width = Style.ConsoleSkin.textField.CalcSize(input_content).x;
		    Rect input_type_guide = new Rect(input_field_rect);
		    input_type_guide.x += input_text_width + 2;
		    input_type_guide.width -= input_text_width + 2;

		    if (command_selected_idx == -1) {
				GUI.Label(input_type_guide, $"<size=75%><alpha=#88><DevCommand>");
		    }
		    else {
			    CommandData selected_command = Commands[command_selected_idx];
			    if (parse_arg_result.valid_args < selected_command.parameterCount) {
				    string arg_name = selected_command.parameterNames[parse_arg_result.valid_args];
				    Type arg_type = selected_command.parameterTypes[parse_arg_result.valid_args];
				    
					GUI.Label(input_type_guide, $"<size=75%><alpha=#88>({arg_name})<color=grey><{arg_type.Name}>");
			    }
		    }
		    
		    
		    
		    
		    if (force_move_cursor_to_end_ticks > 0) {
			    force_move_cursor_to_end_ticks--;
			    TextEditor editor = (TextEditor) GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
			    editor?.MoveTextEnd();
		    }
		    
	    
	    
	    
	    /*
	     * DrawHintBox
	     *
	     * extract this into method that takes in current selected index, array of things to display
	     */

	    if (parse_arg_result.num_hints > 0) {
		    float maximum_width = 0;
		    float max_hint_height = console_input_draw_pos.y - Style.HintBoxBottomPadding - HEIGHT_SPACING * 2 - Style.HintBoxHeightOffset;
		    float height_per_line = Style.ConsoleSkin.label.CalcSize(HintContent[0]).y;
		    int hints_to_draw = Mathf.Clamp(Mathf.RoundToInt(max_hint_height / height_per_line), 1, parse_arg_result.num_hints);
		    float maximum_height = hints_to_draw * height_per_line;

		    if (selected_hint_idx < hint_display_index_start) {
			    hint_display_index_start = selected_hint_idx;
		    }
		    else if (selected_hint_idx >= hint_display_index_start + hints_to_draw) {
			    hint_display_index_start = selected_hint_idx - hints_to_draw + 1;
		    }

		    hint_display_index_start = Mathf.Clamp(hint_display_index_start, 0, Mathf.Max(parse_arg_result.num_hints - hints_to_draw, 0));

		    for (int i = 0; i < hints_to_draw; i++) {
			    Vector2 hint_text_size = Style.ConsoleSkin.label.CalcSize(HintContent[hint_display_index_start + i]);
			    maximum_width = Mathf.Clamp(Mathf.Max(hint_text_size.x, maximum_width), 0, Screen.width - WIDTH_SPACING * 2f);
		    }


		    Rect hint_background = new (input_field_rect) {
			    width = maximum_width,
			    height = maximum_height + Style.HintBoxBottomPadding,
			    y = console_input_draw_pos.y - Style.HintBoxBottomPadding - maximum_height - Style.HintBoxHeightOffset,
		    };

		    GUI.backgroundColor = Style.BackgroundColor;
		    GUI.Box(hint_background, string.Empty);
		    GUI.Box(hint_background, string.Empty, BoxBorderSkin());

		    Vector2 hint_starting_pos = hint_background.position;
		    
			// select with mouse
		    if (current_device == device.mouse && mouse_pos.inside(hint_background.min, hint_background.max)) {
			    float mouse_y = mouse_pos.y - hint_background.y;
			    int hovered_hint = Mathf.FloorToInt(mouse_y / height_per_line);
			    hovered_hint = Mathf.Clamp(hovered_hint, 0, hints_to_draw - 1);
			    selected_hint_idx = hint_display_index_start + (hints_to_draw - 1) - hovered_hint;
		    }
		    
		    
		    for (int idx = 0; idx < hints_to_draw; idx++) {
			    // drawing from the bottom and up so that index matches selection
			    Vector2 pos = hint_starting_pos + new Vector2(0, maximum_height - (idx + 1) * height_per_line); 
			    Rect hint_rect = new (pos, new Vector2(maximum_width, height_per_line));
			    
			    bool is_selected = (hint_display_index_start + idx) == selected_hint_idx;
			    if (is_selected) {
				    hint_rect.x += Style.SelectionBumpCurve.Evaluate(selectionBump) * Style.SelectHintBumpOffsetAmount;
					GUI.contentColor = Style.HintTextColorSelected;
			    }
			    else {
					GUI.contentColor = Style.HintTextColorDefault;
			    }
			    

			    if (CommandHistoryState == History.SHOW) {
				    if (HistoryCommands[hint_display_index_start + idx].historyCommandState != 2) {
					    GUI.enabled = false;
				    }
			    }

			    GUI.Label(hint_rect, HintContent[hint_display_index_start + idx]);

			    if (GUI.enabled == false) {
				    GUI.enabled = true;
			    }
		    }



		    // apply hints
		    bool insert_hint = insert_hint_pressed || (mouse_clicked && mouse_pos.inside(hint_background.min, hint_background.max));
		    if (insert_hint && selected_hint_idx != -1) {
			    if (selected_command_idx != -1) {
				    // has command
				    string hint_text = Commands[HintIndex[selected_hint_idx]].displayName;
				    string_section cmd_section_without_args = parse_string_for_section(ref hint_text, 0);
					apply_selected_hint(ref console_input_text, hint_text.AsSpan(cmd_section_without_args.start_idx, cmd_section_without_args.length), parse_arg_result.next_idx);
			    }
			    else {
					apply_selected_hint(ref console_input_text, HintContent[selected_hint_idx].text.AsSpan(), parse_arg_result.next_idx);
			    }
				selected_hint_idx = -1;
				force_move_cursor_to_end_ticks = 4;
				
				string_section cmd_section = parse_string_for_section(ref console_input_text, 0);
				command_selected_idx = parse_for_command(ref console_input_text, ref cmd_section, Commands, active_cmd_count);
				if (command_selected_idx != -1) {
					parse_arg_result = parse_for_arguments(ref console_input_text, cmd_section.end, Commands[command_selected_idx]);
				}
				else {
					parse_arg_result.num_hints = parse_for_command_hints(ref console_input_text, Commands);
				}
		    }
	    }
	    else {
		    selected_hint_idx = -1;
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
		    text = 
			       $"Selected Hint Index: {selected_hint_idx}\n" +
		           // $"Command Index: {inputCommand.commandIndex}\n" +
		           // $"Color string: {ColorUtility.ToHtmlStringRGBA(Style.HintTextColorDefault)}\n" +
		           // $"CommandHistoryState: {CommandHistoryState}\n" + 
		           // $"HistoryCount: {HistoryCommands.Count}\n" +
		           $"[STRUCT] input text: {console_input_text}\n" +
		           $"[State] Console state: {console_state}\n" +
		           $"[State] Selected cmd idx: {command_selected_idx}\n" +
		           $"[State] Selected cmd name: {(command_selected_idx != -1 ? Commands[command_selected_idx].displayName : string.Empty)}\n" +
		           $"[State] Valid Args: {parse_arg_result.valid_args}\n" +
		           $"[State] Num hints: {parse_arg_result.num_hints}\n" +
		           $"[State] Next idx: {parse_arg_result.next_idx}\n" +
		           $"[State] String Vis: {color_string_from_idx(console_input_text, parse_arg_result.next_idx)}\n" +
		           $"[State] Device: {current_device}\n" +
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

	    string color_string_from_idx(string str, int idx) {
		    if (string.IsNullOrEmpty(str) || idx < 0 || idx > str.Length) {
			    return "";
		    }
		    string result = "";
		    result += "<color=red>";
		    result += str[..idx];
		    result += "</color=red>";
		    result += "<color=green>";
		    result += str[idx..];
		    result += "</color=green>";
		    return result;
	    }

	    if (CommandHistoryState == History.SHOW && selected_hint_idx != -1) {
		    HistoryCommand cmd = HistoryCommands[selected_hint_idx];
		    string text = $"-- History command #{selected_hint_idx} --\n";
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

	    

		if (false) {



			parse_arg_result.num_hints = 0; //ParseHints();
	    if (inputCommand.commandIndex == -1 && inputCommand.HasText() == false) {
		    if (CommandHistoryState == History.HIDE)
			    CommandHistoryState = History.WAIT_FOR_INPUT;
	    }
	    else {
		    CommandHistoryState = History.HIDE;
	    }


	    if (windowHasFocus) {
		    if (selected_hint_idx != -1) {
			    if (inputEvent.InsertHint()) {
				    if (CommandHistoryState == History.SHOW &&
				        HistoryCommands[HintIndex[selected_hint_idx]].historyCommandState != 2) {
					    selectionBump = 0;
				    }
				    else {
					    inputCommand.UseHint(selected_hint_idx);
					    CommandHistoryState = History.HIDE;
					    moveMarkerToEnd = 2;
					    selected_hint_idx = -1;
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
				    selected_hint_idx = 0;
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
			    selected_hint_idx -= 1;
			    selectionBump = 0;
			    if (CommandHistoryState == History.WAIT_FOR_INPUT) {
				    CommandHistoryState = History.SHOW;
			    }

			    if (selected_hint_idx < -1) selected_hint_idx = parse_arg_result.num_hints - 1;
		    }
		    else if (inputEvent.NavigateUp()) {
			    selected_hint_idx += 1;
			    selectionBump = 0;
			    if (CommandHistoryState == History.WAIT_FOR_INPUT) {
				    CommandHistoryState = History.SHOW;
			    }

			    if (selected_hint_idx >= parse_arg_result.num_hints) {
				    selected_hint_idx = -1;
			    }
		    }

		    selected_hint_idx = Mathf.Clamp(selected_hint_idx, -1, parse_arg_result.num_hints - 1);
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
		    parse_arg_result.num_hints = 0;
		    selected_hint_idx = -1;

		    if (activeMacro == null && Style.keepConsoleOpenAfterCommand == false) {
			    CloseConsole();
		    }
	    }



	    /*
	     * draw console input area
	     */

	    console_input_draw_pos = new Vector2(WIDTH_SPACING, height - (HEIGHT_SPACING * 2f + Style.ConsoleTextSize));
	    console_input_size = new Vector2(width - WIDTH_SPACING * 2f, Style.ConsoleTextSize);

	    Rect consoleInputBackground = new (console_input_draw_pos, console_input_size);
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

	    Rect consoleIconRect = new Rect(console_input_draw_pos + iconOffset, iconSize);
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
			    position = new Vector2(inputFieldPosX, console_input_draw_pos.y)
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

		    float maximumWidth = console_input_size.x;
		    float maxLines = console_input_draw_pos.y - Style.HintBoxBottomPadding - HEIGHT_SPACING * 2 -
		                     Style.HintBoxHeightOffset;
		    float heightPerLine = Style.ConsoleSkin.label.CalcSize(ToastMessages[0]).y;
		    int messagesToDraw = Mathf.Clamp(Mathf.RoundToInt(maxLines / heightPerLine), 1, ToastMessages.Count);
		    float maximumHeight = messagesToDraw * heightPerLine;



		    Rect toastWindow = new (inputFieldRect) {
			    width = maximumWidth,
			    height = maximumHeight + Style.HintBoxBottomPadding,
			    x = console_input_draw_pos.x,
			    y = console_input_draw_pos.y - Style.HintBoxBottomPadding - maximumHeight - Style.HintBoxHeightOffset,
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


    int parse_for_command_hints(ref string input, CommandData[] commands) {
	    
	    string_section[] remaining_segments = parse_string_for_remaining_sections(ref input, 0);
	    int num_hints = 0;
	    
	    for (int cmd_idx = 0; cmd_idx < commands.Length; cmd_idx++) {
		    
		    ReadOnlySpan<char> cmd_name = commands[cmd_idx].displayName.AsSpan();
		    bool display_as_hint = true;
		    
		    for (int section_idx = 0; section_idx < remaining_segments.Length; section_idx++) {
			    ReadOnlySpan<char> segment_span = input.AsSpan(remaining_segments[section_idx].start_idx, remaining_segments[section_idx].length);
			    if (cmd_name.IndexOf(segment_span, StringComparison.OrdinalIgnoreCase) == -1) {
				    display_as_hint = false;
				    break;
			    }
		    }

		    if (display_as_hint) {
			    HintContent[num_hints].text = cmd_name.ToString();
			    HintValue[num_hints] = commands[cmd_idx];
			    HintIndex[num_hints] = cmd_idx;
			    num_hints++;
			    
			    if (num_hints >= MAX_HINTS) {
				    break;
			    }
		    }
	    }

	    return num_hints;
    }
    
    /*
     * figure out if we have valid cmd with args or if we should display hints for current arg type
     */
    
    parse_arg parse_for_arguments(ref string input, int start_idx, CommandData command) {
     	    /*
     	     * if we find valid matches for all args in command, ignore rest of string
     	     */
     	    parse_arg parse_result = new () { next_idx = start_idx };
     	    int valid_args = 0;
     	    
     	    for (int arg_idx = 0; arg_idx < command.parameterCount; arg_idx++) {
     		    bool require_space_after_arg = arg_idx + 1 < command.parameterCount;
     		    parse_result = parse_hints_for_arg_type(ref input, parse_result.next_idx, command.parameterTypes[arg_idx], require_space_after_arg);
     		    
     		    // 0 = invalid arg, 1 = valid arg
     		    if (parse_result.valid_args == 0) {
     			    break;
     		    }
     
     		    if (arg_idx + 1 < command.parameterCount) {
     			    if (parse_result.next_idx >= input.Length || input[parse_result.next_idx] != CHAR.SPACE) {
     				    break;
     			    }
     		    }
     		    
     			valid_args++;
     	    }
     
     	    parse_result.valid_args = valid_args;
     	    return parse_result;
         }
         
    bool has_space_at_index(ref string input, int start_idx, bool must_have_space) {
     	    if (must_have_space == false) {
     		    return true;
     	    }
     	    
     	    return start_idx < input.Length && input[start_idx] == CHAR.SPACE;
         }
         
    parse_arg parse_hints_for_arg_type(ref string input, int start_idx, Type arg_type, bool require_space_after_arg) {
        
        parse_arg parse_result = new (){ next_idx = start_idx };
        int length_of_input_left = input.Length - parse_result.next_idx;
        if (length_of_input_left == 0) {
     	    return parse_result;
        }
        
        // using first index in remaining segments start_idx so that i skip empty spaces
     	string_section[] remaining_segments = parse_string_for_remaining_sections(ref input, parse_result.next_idx);
     	bool has_segments = remaining_segments.Length > 0;
     	
     	
     	/*
     	 * TODO
     	 * #### BOOL ####
     	 */
        
        if (arg_type == typeof(bool)) {
 
     	    if (has_segments && has_space_at_index(ref input, remaining_segments[0].end, require_space_after_arg)) {
     		    if (remaining_segments[0].length == bool.TrueString.Length) {
     			    if (string.Compare(input, remaining_segments[0].start_idx, bool.TrueString, 0, remaining_segments[0].length, StringComparison.OrdinalIgnoreCase) == 0) 
     			    {
     				    parse_result.next_idx = remaining_segments[0].end;
     				    parse_result.valid_args++;
     			    }
     		    }
     		    else if (remaining_segments[0].length == bool.FalseString.Length) {
     			    if (string.Compare(input, remaining_segments[0].start_idx, bool.FalseString, 0, remaining_segments[0].length, StringComparison.OrdinalIgnoreCase) == 0 ) 
     			    {
     				    parse_result.next_idx = remaining_segments[0].end;
     				    parse_result.valid_args++;
     			    }
     		    }
     	    }
     	    
     		// hints
     	    bool show_hint_true = true;
     	    bool show_hint_false = true;
     	    
     	    
     	    for (int idx = 0; idx < remaining_segments.Length; idx++) {
     		    ReadOnlySpan<char> segment = input.AsSpan(remaining_segments[idx].start_idx, remaining_segments[idx].length);
     		    
     		    if (show_hint_true && bool.TrueString.AsSpan().IndexOf(segment, StringComparison.OrdinalIgnoreCase) == -1) {
     				show_hint_true = false;
     		    }
     		
     		    if (show_hint_false && bool.FalseString.AsSpan().IndexOf(segment, StringComparison.OrdinalIgnoreCase) == -1) {
     			    show_hint_false = false;
     		    }
     		
     		    if (show_hint_false == false && show_hint_true == false) {
     			    break;
     		    }
     	    }
 
     	    if (show_hint_true) {
     		    HintContent[parse_result.num_hints].text = bool.TrueString;
     		    HintValue[parse_result.num_hints++]      = true;
     	    }
 
     	    if (show_hint_false) {
     		    HintContent[parse_result.num_hints].text = bool.FalseString;
     		    HintValue[parse_result.num_hints++]      = false;
     	    }
        }
        
        
        /*
         * TODO
         * #### ENUM ####
         */
        else if (arg_type.IsEnum) {
     	    
     	    // check if valid arg
     	    string[] enum_names = arg_type.GetEnumNames();
     	    int length_of_match = -1;
     	    int idx_best_match = -1;
 
     	    if (has_segments && has_space_at_index(ref input, remaining_segments[0].end, require_space_after_arg)) {
     		    // longest match if any
     		    for (int idx = 0; idx < enum_names.Length; idx++) {
     			    if (enum_names[idx].Length > length_of_input_left || length_of_match > enum_names[idx].Length) {
     				    continue;
     			    }
     			
     			    if (string.Compare(input, remaining_segments[0].start_idx, enum_names[idx], 0, enum_names[idx].Length, StringComparison.OrdinalIgnoreCase) == 0) {
     				    length_of_match = enum_names[idx].Length;
     				    idx_best_match = idx;
     			    }
     		    }
     	    }
 
     	    // is valid enum arg
     	    if (idx_best_match != -1) {
     		    parse_result.next_idx = remaining_segments[0].start_idx + length_of_match;
     		    parse_result.valid_args++;
     	    }
     	    
     	    
     	    // get hints
     	    Array enum_values = arg_type.GetEnumValues();
     	    for (int enum_idx = 0; enum_idx < enum_names.Length; enum_idx++) { 
     		    bool display_as_hint = true;
     		    for (int section_idx = 0; section_idx < remaining_segments.Length; section_idx++) {
     			    if (remaining_segments[section_idx].length > enum_names[enum_idx].Length) {
     				    display_as_hint = false;
     				    break;
     			    }
 
     			    ReadOnlySpan<char> segment_span = input.AsSpan(remaining_segments[section_idx].start_idx, remaining_segments[section_idx].length);
     			    if (enum_names[enum_idx].AsSpan().IndexOf(segment_span, StringComparison.OrdinalIgnoreCase) == -1) {
     				    display_as_hint = false;
     				    break;
     			    }
     		    }
     		    
     		    if (display_as_hint) {
     			    HintContent[parse_result.num_hints].text = enum_names[enum_idx];
     			    HintValue[parse_result.num_hints++] = enum_values.GetValue(enum_idx);
     			    if (parse_result.num_hints >= MAX_HINTS) {
     				    break;
     			    }
     		    }
     		}
        }
        
        
        /*
         * TODO
         * #### SCRIPTABLE OBJECTS ####
         */
 
        
        else if (TYPE_SO.IsAssignableFrom(arg_type)) {
     	    
     	    int length_of_match = -1;
     	    int idx_best_match = -1;
     	    if (has_segments && has_space_at_index(ref input, remaining_segments[0].end, require_space_after_arg)) {
     		    ReadOnlySpan<char> full_arg_segment = input.AsSpan(remaining_segments[0].start_idx);
     		    for (int idx = 0; idx < Cache.AssetReferences.Length; idx++) {
     				ScriptableObject asset = Cache.AssetReferences[idx];
     				if (arg_type.IsAssignableFrom(asset.GetType()) == false || asset.name.Length < length_of_match) {
     					continue;
     				}
     				
     				if (full_arg_segment.StartsWith(asset.name.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
     					length_of_match = asset.name.Length;
     					idx_best_match = idx;
     				}
     			}
     	    }
     	    
     	    if (idx_best_match != -1) {
     		    parse_result.next_idx = remaining_segments[0].start_idx + length_of_match;
     		    parse_result.valid_args++;
     	    }
     	    
     	    
     	    for (int asset_idx = 0; asset_idx < Cache.AssetReferences.Length; asset_idx++) {
     		    ScriptableObject asset = Cache.AssetReferences[asset_idx];
     		    if (arg_type.IsAssignableFrom(asset.GetType()) == false) {
     			    continue;
     		    }
 
     		    bool display_as_hint = true;
     		    for (int section_idx = 0; section_idx < remaining_segments.Length; section_idx++) {
     			    ReadOnlySpan<char> segment_span = input.AsSpan(remaining_segments[section_idx].start_idx, remaining_segments[section_idx].length);
     			    if (asset.name.AsSpan().IndexOf(segment_span, StringComparison.OrdinalIgnoreCase) == -1) {
     				    display_as_hint = false;
     				    break;
     			    }
     		    }
 
     		    if (display_as_hint) {
     			    HintContent[parse_result.num_hints].text = asset.name;
     			    HintValue[parse_result.num_hints++] = asset;
     			    if (parse_result.num_hints >= MAX_HINTS) {
     				    break;
     			    }
     		    }
     	    }
     	    
     	    
        }
        
        
        
        /*
         * TODO
         * #### NUMBERS AND OTHER ####
         */
 
        else if (remaining_segments.Length > 0) {
     	    TypeConverter type_converter = TypeDescriptor.GetConverter(arg_type);
     	    if (type_converter.CanConvertFrom(typeof(string))) {
     		    object string_to_value = null;
     		    try {
     			    string_to_value = type_converter.ConvertFromString(input[remaining_segments[0].start_idx..remaining_segments[0].end]);
     		    }
     		    catch (Exception _) { } // ignore errors, idc
     		    finally {
     			    if (string_to_value != null && has_space_at_index(ref input, remaining_segments[0].end, require_space_after_arg)) {
     					parse_result.next_idx = input.Length;
     				    parse_result.valid_args++;
     				}
     		    }
     	    }
        }
        
        
 
        return parse_result;
     }


    // TODO this whole thing is kinda ugly
    void apply_selected_hint(ref string input, ReadOnlySpan<char> hint_segment, int next_idx) {
	    int first_char_idx = first_character_idx(ref input);
	    ReadOnlySpan<char> segment_to_keep = input.AsSpan(first_char_idx, next_idx);
	    StringBuilder builder = new (segment_to_keep.Length + hint_segment.Length);
	    builder.Append(segment_to_keep.TrimStart(CHAR.SPACE).TrimEnd(CHAR.SPACE));
	    if (builder.Length > 0) {
		    builder.Append(CHAR.SPACE);
	    }
	    builder.Append(hint_segment);
	    builder.Append(CHAR.SPACE);
	    input = builder.ToString();
    }

    
    int first_character_idx(ref string input) {
	    int first_char = 0;
	    for (int idx = 0; idx < input.Length; idx++) {
		    if (input[idx] != CHAR.SPACE) {
			    first_char = idx;
			    break;
		    }
	    }

	    return first_char;
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