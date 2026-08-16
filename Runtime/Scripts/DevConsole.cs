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
using System.Threading;
using System.Threading.Tasks;
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
        is_console_open = false;
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
    
    
    const BindingFlags DEV_COMMAND_BINDING_FLAGS = BindingFlags.Default | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

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
    const int MAX_COMMANDS = 512;
    const int MAX_HISTORY = 32;
    const int MAX_HINTS = 32;
    const int MAX_ARGS = 16;
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
    // static readonly CommandData[] Commands = new CommandData[MAX_COMMANDS];
    static readonly Type TYPE_SO = typeof(ScriptableObject);
    static readonly Type TYPE_COMMAND_DATA = typeof(CommandData);
    static History CommandHistoryState;
    static int StaticCommandCount;
    int hint_display_index_start;
    int active_cmd_count;


    // Input
    /*
     * Try to replace strings with textbuilder
     * TextBuilder.Remove(index, length)
     * make char[] and just slice into it?
     * use helper methods to manipulate char[] without adding memory
     */
    
    
    public static bool is_console_open { get; private set; }
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

    async void Awake() {
        DontDestroyOnLoad(this);
        
        // Cache & Style gets assigned during build step!
#if UNITY_EDITOR
        Cache = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleCache>(DEV_CONSOLE_CACHE_PATH);
        Style = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleStyle>(DEV_CONSOLE_STYLE_PATH);
#endif
	    Debug.Log("loading commands - STARTED");
	    Stopwatch sw = Stopwatch.StartNew();
	    await Task.Run(load_dev_commands, destroyCancellationToken);
	    sw.Stop();
	    Debug.Log($"loading command - COMPLETED! -> {sw.ElapsedMilliseconds}ms");
    }

    void InitializeConsole() {
        Array.Fill(hint_value, TYPE_COMMAND_DATA);
        for (int i = 0; i < hint_content.Length; i++) {
            hint_content[i] = new GUIContent();
        }
        for (int i = 0; i < inputCommand.inputArgumentName.Length; i++) {
            inputCommand.inputArgumentName[i] = new GUIContent();
        }
    }

    void OnDestroy() {
        SaveHistoryCommands();
        SaveMacroCommands();
        is_console_open = false;
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
        // int currentReadIndex = sliceStart;
        // while (currentReadIndex < sliceEnd) {
        //     if (int.TryParse(historyTextFile[currentReadIndex++], out int linesOfCommand) == false) {
        //         LogError("Error parsing command history! Try clearing history file to remove invalid values!");
        //         break;
        //     }
        //     int argumentCount = linesOfCommand - 2;
        //     HistoryCommand cmd = new () {
        //         commandIndex = -1,
        //         argumentDisplayName = new string[argumentCount],
        //         argumentValues = new object[argumentCount],
        //         displayString = historyTextFile[currentReadIndex++],
        //     };
        //     
        //     /*
        //      * try find command
        //      */
        //
        //
        //     for (int i = 0; i < active_cmd_count; i++) {
        //         if (string.Equals(Commands[i].displayName, historyTextFile[currentReadIndex], StringComparison.OrdinalIgnoreCase)) {
        //             cmd.commandIndex = i;
        //             cmd.historyCommandState = argumentCount > 0 ? 1 : 2;
        //             cmd.commandDisplayName = Commands[i].displayName;
        //             break;
        //         }
        //     }
        //     ++currentReadIndex;
        //     
        //
        //     int validArgsFound = 0;
        //     if (cmd.historyCommandState == 1) {
        //         for (int i = 0; i < argumentCount; i++) {
        //             cmd.argumentDisplayName[i] = historyTextFile[currentReadIndex + i];
        //             object argumentValue = TryGetArgumentValue(ref historyTextFile[currentReadIndex + i], cmd.commandIndex, i);
        //             if (argumentValue != null) {
        //                 cmd.argumentValues[i] = argumentValue;
        //                 ++validArgsFound;
        //             }
        //         }
        //     }
        //     
        //     if (validArgsFound == argumentCount) {
        //         cmd.historyCommandState = 2;
        //     }
        //     
        //     history.Add(cmd);
        //     currentReadIndex += argumentCount;
        // }
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
    

    bool ConnectHistoryCommand(ref List<HistoryCommand> commands) {
        bool hasCommandsToConnect = false;
        // for (int i = 0; i < commands.Count; i++) {
        //     int validArgsFound = 0;
        //     HistoryCommand cmd = commands[i];
        //     if (cmd.historyCommandState == 0) {
        //         for (int k = 0; k < active_cmd_count; k++) {
        //             if (string.Equals(cmd.commandDisplayName, Commands[k].displayName, StringComparison.OrdinalIgnoreCase)) {
        //                 cmd.commandIndex = k;
        //                 cmd.historyCommandState = 1;
        //                 cmd.commandDisplayName = Commands[k].displayName;
        //                 break;
        //             }
        //         }
        //     }
        //
        //     int argumentCount = cmd.argumentDisplayName.Length;
        //     for (int k = 0; k < argumentCount; k++) {
        //         if (cmd.historyCommandState == 1) {
        //             object argumentValue = TryGetArgumentValue(ref cmd.argumentDisplayName[k], cmd.commandIndex, k);
        //             if (argumentValue != null) {
        //                 cmd.argumentValues[k] = argumentValue;
        //                 ++validArgsFound;
        //             }
        //         }
        //     }
        //     
        //     if (cmd.historyCommandState < 2) {
        //         if (validArgsFound == argumentCount) {
        //            cmd.historyCommandState = 2;
        //            Log($"Successfully connected command -> {cmd.displayString}");
        //         }
        //         else {
        //             hasCommandsToConnect = true;
        //         }
        //     }
        //     
        //     commands[i] = cmd;
        // }

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
        is_console_open = true;
        setFocus = 2;
        inputCommand.Clear();
        // Event.current.Use();
        
        reset_console_state();
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
            // LoadStaticCommands();
            // LoadInstanceCommands();
            // LoadHistoryCommands();
            // LoadMacroCommands();

            
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
            // LoadInstanceCommands();
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
        is_console_open = false;
        selected_hint_idx = -1;
        EndMacro();
        GUI.FocusControl(null);
        
        // DebugManager.instance.enableRuntimeUI = true;
    }
    
    /*
    * Main logic flow
    */

    void OnGUI() {
        Event input_event = Event.current;
        if (is_console_open == false) {
            KeyCode[] open_console_keys = Style != null ? Style.openConsoleKey : Array.Empty<KeyCode>();
            if (input_event.OpenConsole(overrideKeys:open_console_keys)) {
                OpenConsole();
            }
            else {
	            // TODO refactor triggering macros
                foreach (var macro in macroCommands) {
                    if (input_event.KeyDown(macro.key)) {
                        hint_index[0] = 0;
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


        if (input_event.CloseConsole()) {
            CloseConsole();
        }
        else {
	        // TODO seperate logic & repaint actions
	        draw_console_window(input_event);
        }
    }

	

    /*
     * TODO
     * #### THIS IS THE UP TO DATE STUFF ####
     */
    readonly int[] hint_index = new int[MAX_HINTS];
    readonly object[] hint_value = new object[MAX_HINTS];
    readonly object[] arg_value = new object[MAX_ARGS];
    readonly dev_command[] dev_commands = new dev_command[MAX_COMMANDS];
    readonly GUIContent[] hint_content = new GUIContent[MAX_HINTS];
    readonly List<string> cmd_history = new List<string>(MAX_HISTORY);
    
    readonly Rect[] hint_rect = new Rect[MAX_HINTS];
	Rect hint_background_rect = new ();
	Rect input_field_rect = new ();
	Rect input_field_background_rect = new ();
    
    
    
    device current_device;
    console_input_state console_state;
    string console_input_text;
    bool move_selection_to_end;
    
    float hint_height_per_line;
    int num_hints_on_screen;

    int selected_command_idx = -1;
    int history_selected_idx;
    
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
    
    void reset_console_state() {
	    parse_arg_result.reset();
	    selected_command_idx = -1;
	    selected_hint_idx = -1;
	    console_input_text = string.Empty;
	    mouse_pos = Event.current.mousePosition;
	    CommandHistoryState = History.WAIT_FOR_INPUT;
	    current_device = device.keyboard;
    }


	Task load_dev_commands() {
	     Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
	     foreach (Assembly assembly in assemblies) {
		     if (assembly.FullName.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
		         assembly.FullName.StartsWith("Jerbo", StringComparison.OrdinalIgnoreCase)) {
			     Type[] assembly_types = assembly.GetTypes();
			     foreach (Type loaded_type in assembly_types) {
				     MethodInfo[] methods_in_type = loaded_type.GetMethods(DEV_COMMAND_BINDING_FLAGS);
				     foreach (MethodInfo method_info in methods_in_type) {
					     DevCommand cmd = method_info.GetCustomAttribute<DevCommand>();
					     if (cmd != null) {
						     dev_commands[active_cmd_count++].set_method(cmd, method_info, loaded_type);
					     }
				     }


				     FieldInfo[] fields_in_type = loaded_type.GetFields(DEV_COMMAND_BINDING_FLAGS);
				     foreach (FieldInfo field_info in fields_in_type) {
					     DevCommand cmd = field_info.GetCustomAttribute<DevCommand>();
					     if (cmd != null) {
						     if (field_info.FieldType == typeof(Action)) {
							     dev_commands[active_cmd_count++].set_action(cmd, field_info, loaded_type);
						     }
						     else if (field_info.FieldType == typeof(UnityEvent)) {
							     dev_commands[active_cmd_count++].set_unity_event(cmd, field_info, loaded_type);
						     }
						     else {
							     dev_commands[active_cmd_count++].set_field(cmd, field_info, loaded_type);
						     }
					     }
				     }
			     }

			     // Log($"--- <Assembly Commands ({assembly.GetName().Name}) : {active_cmd_count} > ---");
			     // for (int i = 0; i < active_cmd_count; i++) {
			     //  Log($"CMD: {dev_commands[i].cmd_display_name}");
			     // }
			     // Log($"--- </Assembly Commands ({assembly.GetName().Name}) > ---");
		     }
	     }
	    
	    return Task.CompletedTask;
     }
    
    
    void draw_console_window(Event input_event) {
	    float width = Screen.width;
	    float height = Screen.height;
	    Style.ConsoleSkin.label.fontSize = (int)(Style.console_text_size - HEIGHT_SPACING);
	    Style.ConsoleSkin.textField.fontSize = (int)(Style.console_text_size - HEIGHT_SPACING);
	    GUI.skin = Style.ConsoleSkin;

	    selectionBump = Mathf.Lerp(selectionBump, 1, Style.SelectHintBumpSpeed * Time.unscaledDeltaTime);
	    argumentHintBump = Mathf.Lerp(argumentHintBump, 1, Style.ArgHelpBumpSpeed * Time.unscaledDeltaTime);
	    



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
	    
	    
	    if (input_event.isKey) {
		    current_device = device.keyboard;
	    }
	    else if (input_event.isMouse || input_event.mousePosition != mouse_pos) {
		    current_device = device.mouse;
		    mouse_pos = input_event.mousePosition;
	    }
	    
	    bool mouse_clicked_hint = parse_arg_result.num_hints > 0 && mouse_pos.inside(hint_background_rect.min, hint_background_rect.max) && input_event.mouse_down(KeyCode.Mouse0, false);
	    bool insert_hint_pressed = input_event.InsertHint(false);
	    
	    bool up = input_event.NavigateUp();
	    bool down = input_event.NavigateDown();
	    switch (console_state) {
		    case console_input_state.WAITING_FOR_INPUT:
			    if (up || down) {
				    console_state = console_input_state.HISTORY;
				    input_event.Use();
				    // select first or last history command
				    parse_arg_result.num_hints = cmd_history.Count;
				    selected_hint_idx = down ? cmd_history.Count - 1 : 0;
				    for (int idx = 0; idx < cmd_history.Count; idx++) {
					    hint_content[idx].text = cmd_history[idx];
				    }
				    return;
			    }
			    break;
		    
		    case console_input_state.HISTORY:
		    case console_input_state.COMMAND:
			    if (up) {
				    if (parse_arg_result.num_hints > 0) {
				    	selected_hint_idx++;
				    	if (selected_hint_idx >= parse_arg_result.num_hints) {
						    selected_hint_idx = -1;
				    	}
				    }
				    input_event.Use();
				    return;
			    }
			    if (down) {
				    if (parse_arg_result.num_hints > 0) {
					    selected_hint_idx--;
				    	if (selected_hint_idx < -1) {
						    selected_hint_idx = parse_arg_result.num_hints - 1;
				    	}
				    }

				    input_event.Use();
				    return;
			    }

			    
			    // execute command
			    bool execute_cmd_pressed = input_event.ExecuteCommand(false);
			    if (execute_cmd_pressed && selected_hint_idx == -1 && selected_command_idx != -1 && parse_arg_result.valid_args >= dev_commands[selected_command_idx].num_args_required) {
				    input_event.Use();
				    execute_command();
				    parse_arg_result.reset();
				    selected_command_idx = -1;
				    selected_hint_idx = -1;
				    console_input_text = string.Empty;
				    console_state = console_input_state.WAITING_FOR_INPUT;
				    return;
			    }
			    

			    // TODO applying history hint ?
			    // apply hints
			    bool insert_hint = (insert_hint_pressed || mouse_clicked_hint) && selected_hint_idx != -1 && parse_arg_result.num_hints > 0;
			    if (insert_hint) {
				    if (selected_command_idx == -1) {
					    if (console_state == console_input_state.COMMAND) {
					    	string hint_text = dev_commands[hint_index[selected_hint_idx]].cmd_display_name;
					    	string_section cmd_section_without_args = parse_string_for_section(ref hint_text, 0);
					    	apply_selected_hint(ref console_input_text, hint_text.AsSpan(cmd_section_without_args.start_idx, cmd_section_without_args.length), 0, true);
					    }
					    else if (console_state == console_input_state.HISTORY) {
						    ReadOnlySpan<char> hint_text = hint_content[selected_hint_idx].text.AsSpan();
					    	apply_selected_hint(ref console_input_text, hint_text, 0, false);
					    }
				    }
				    else {
					    apply_selected_hint(ref console_input_text, hint_content[selected_hint_idx].text.AsSpan(), parse_arg_result.next_idx, true);
				    }
				    selected_hint_idx = -1;
					
				    // trigger re-parse manually, input string won't notice the change, so won't trigger re-parse
				    parse_input_string();
				    move_selection_to_end = true;
				    input_event.Use();
				    return;
			    }
			    
			    
			    break;
		    
		    default:
			    throw new ArgumentOutOfRangeException();
	    }
	    
	    
	    
	    /*
	     * Draw Console background
	     */
	    
	    input_field_background_rect.Set(WIDTH_SPACING, height - (HEIGHT_SPACING * 2f + Style.console_text_size), width - WIDTH_SPACING * 2f, Style.console_text_size);
	    
	    Color outline_color = Style.color_background;
	    if (activeMacro != null) {
		    outline_color = Style.color_outline_macro;
	    }
	    
	    GUI.backgroundColor = outline_color;
	    GUI.Box(input_field_background_rect, string.Empty, BoxBorderSkin());
	    GUI.backgroundColor = Style.color_background;
	    GUI.Box(input_field_background_rect, string.Empty);
	    
	    
	    
	    /*
	     * Draw Console text input
	     */
	    
	    GUI.backgroundColor = Color.clear;
	    GUI.contentColor = Style.color_text_default;
	    input_field_rect.Set(
		    input_field_background_rect.x + WIDTH_SPACING, 
		    input_field_background_rect.y, 
		    input_field_background_rect.width - WIDTH_SPACING * 2, 
		    input_field_background_rect.height
		    );
	    GUIContent input_content = new (console_input_text);
	    
	    GUI.SetNextControlName(CONSOLE_INPUT_FIELD_ID);
	    string input_text = GUI.TextField(input_field_rect, console_input_text);
	    GUI.FocusControl(CONSOLE_INPUT_FIELD_ID);

	    if (move_selection_to_end) {
		    move_selection_to_end = false;
		    
		    TextEditor editor = (TextEditor) GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
		    editor?.MoveTextEnd();
	    }
	    
	    if (console_input_text != input_text) {
		    if (input_text.Length == 0) {
			    console_state = console_input_state.WAITING_FOR_INPUT;
			    console_input_text = string.Empty;
			    selected_command_idx = -1;
			    parse_arg_result.reset();
		    }
		    else {
			    console_state = console_input_state.COMMAND;
				console_input_text = input_text;
				parse_input_string();
		    }
	    }
	    
	    
	    // input type guide
	    float input_text_width = Style.ConsoleSkin.textField.CalcSize(input_content).x;
	    Rect input_type_guide = new Rect(input_field_rect);
	    input_type_guide.x += input_text_width + 2;
	    input_type_guide.width -= input_text_width + 2;

	    if (selected_command_idx == -1) {
			GUI.Label(input_type_guide, $"<size=100%><alpha=#66><DevCommand>");
	    }
	    else {
		    dev_command selected_command = dev_commands[selected_command_idx];
		    if (parse_arg_result.valid_args < selected_command.num_args) {
			    string arg_name = selected_command.arg_names[parse_arg_result.valid_args];
			    Type arg_type = selected_command.arg_types[parse_arg_result.valid_args];
			    
				GUI.Label(input_type_guide, $"<size=100%><alpha=#66>{arg_name} <{arg_type.Name}>");
		    }
	    }
	    
	    /*
	     * draw hints
	     */

	    if (parse_arg_result.num_hints > 0) {
		    if (selected_command_idx == -1 || parse_arg_result.valid_args < dev_commands[selected_command_idx].num_args) {
				calculate_and_draw_hints();
		    }
	    }
	    else {
		    selected_hint_idx = -1;
	    }
	    
	    
	    
	    
	    
	    
	    
	    
	    
	    
	    
	    
	    

#if DEVCONSOLE_DEBUG
	    /*
	     * drawdebug box
	     */
	    GUI.backgroundColor = Style.color_background;
	    GUI.contentColor = Style.color_text_default;
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
		           $"[State] Selected cmd idx: {selected_command_idx}\n" +
		           $"[State] Selected cmd name: {(selected_command_idx != -1 ? dev_commands[selected_command_idx].cmd_display_name : string.Empty)}\n" +
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
  //
	 //    
  //
		// if (false) {
  //
  //
  //

	 //    if (windowHasFocus) {
		//     
  //
  //
  //
  //
  //
	 //    if (inputCommand.CanExecuteCommand() && inputEvent.ExecuteCommand()) {
		//     inputCommand.GenerateHistoryCommand(out HistoryCommand historyCommand);
		//     if (activeMacro == null) {
		// 	    ToastMessages.Add(new GUIContent(inputCommand.CreateCompleteCommandString()));
		// 	    inputCommand.ExecuteCommand();
		//     }
		//     else {
		// 	    if (Commands[inputCommand.commandIndex].method.Name == nameof(EndMacro)) {
		// 		    EndMacro();
		// 	    }
		// 	    else {
		// 		    activeMacro.commands.Add(historyCommand);
		// 		    ToastMessages.Add(
		// 			    new GUIContent($"[Macro '{activeMacro.key}'] + {historyCommand.commandDisplayName}"));
		// 	    }
		//     }

		//     if (activeMacro == null && Style.keepConsoleOpenAfterCommand == false) {
		// 	    CloseConsole();
		//     }
	 //    }
  //
  //
  //
  //
  
	    /*
	     * icon
	     */
	    // Vector2 iconSize = Vector2.one * Style.ConsoleIconSize;
	    // Vector2 iconOffset = (Style.ConsoleTextSize - Style.ConsoleIconSize) * 0.5f * Vector2.one;
	    // iconOffset.x = WIDTH_SPACING;
     //
	    // Rect consoleIconRect = new Rect(input_field_rect.position + iconOffset, iconSize);
	    // int frameCount = Style.ConsoleIconFrames.x * Style.ConsoleIconFrames.y;
	    // float frameSpeed = frameCount * Style.ConsolIconAnimSpeed;
	    // int currentFrame = Mathf.FloorToInt(Time.unscaledTime * frameSpeed % frameCount);
	    // int frameX = currentFrame % Style.ConsoleIconFrames.x;
	    // int frameY = currentFrame / Style.ConsoleIconFrames.x;
	    // float frameWidth = 1.0f / Style.ConsoleIconFrames.x;
	    // float frameHeight = 1.0f / Style.ConsoleIconFrames.y;
     //
	    // Rect textureCoords = new Rect(frameWidth * frameX, frameHeight * frameY, frameWidth, frameHeight);
	    // GUI.DrawTextureWithTexCoords(consoleIconRect, Style.ConsoleIcon, textureCoords, true);
     //
	    // GUI.backgroundColor = Color.clear;
     //
	    // float inputFieldPosX = input_field_background_rect.x + consoleIconRect.width + WIDTH_SPACING;
	    // float inputFieldXmax = input_field_background_rect.xMax - consoleIconRect.width;
	    // float inputFieldHeight = input_field_background_rect.height;
     //
	    // if (inputCommand.commandIndex != -1) {
		   //  Rect commandRect = new () {
			  //   width = Mathf.Clamp(
				 //    Style.ConsoleSkin.label.CalcSize(inputCommand.commandContent).x,
				 //    0,
				 //    inputFieldXmax),
			  //   height = inputFieldHeight,
			  //   position = new Vector2(inputFieldPosX, input_field_rect.y)
		   //  };
		   //  GUI.contentColor = inputCommand.CanExecuteCommand() ? Style.color_outline_valid_cmd : Style.color_text_selected;
		   //  GUI.Label(commandRect, inputCommand.commandContent);
		   //  inputFieldPosX = commandRect.xMax - WIDTH_SPACING;
     //
		   //  GUI.contentColor = inputCommand.CanExecuteCommand() ? Style.color_outline_valid_cmd : Style.color_text_selected;
		   //  for (int i = 0; i < inputCommand.argumentCount; i++) {
			  //   Rect argRect = new (commandRect) {
				 //    width = Style.ConsoleSkin.label.CalcSize(inputCommand.inputArgumentName[i]).x,
				 //    height = inputFieldHeight,
				 //    x = inputFieldPosX
			  //   };
			  //   GUI.Label(argRect, inputCommand.inputArgumentName[i]);
			  //   inputFieldPosX = argRect.xMax - WIDTH_SPACING;
		   //  }
	    // }
  //
  //
  //
  //
  //
	 //    /*
	 //     * Draw Toast Messages
	 //     */
  //
	 //    if (ToastMessages.Count > 0) {
  //
		//     float maximumWidth = console_input_size.x;
		//     float maxLines = console_input_draw_pos.y - Style.HintBoxBottomPadding - HEIGHT_SPACING * 2 -
		//                      Style.HintBoxHeightOffset;
		//     float heightPerLine = Style.ConsoleSkin.label.CalcSize(ToastMessages[0]).y;
		//     int messagesToDraw = Mathf.Clamp(Mathf.RoundToInt(maxLines / heightPerLine), 1, ToastMessages.Count);
		//     float maximumHeight = messagesToDraw * heightPerLine;
  //
  //
  //
		//     Rect toastWindow = new (inputFieldRect) {
		// 	    width = maximumWidth,
		// 	    height = maximumHeight + Style.HintBoxBottomPadding,
		// 	    x = console_input_draw_pos.x,
		// 	    y = console_input_draw_pos.y - Style.HintBoxBottomPadding - maximumHeight - Style.HintBoxHeightOffset,
		//     };
  //
		//     GUI.backgroundColor = Style.BackgroundColor * 0.6f;
		//     GUI.Box(toastWindow, string.Empty);
  //
		//     GUI.contentColor = Style.HintTextColorDefault;
		//     Vector2 hintStartPos = toastWindow.position;
		//     for (int i = 0; i < messagesToDraw; i++) {
		// 	    Vector2 pos = hintStartPos + new Vector2(0, maximumHeight - (i + 1) * heightPerLine);
		// 	    GUI.Label(new Rect(pos, new Vector2(maximumWidth, heightPerLine)),
		// 		    ToastMessages[(messagesToDraw - 1) - i]);
		//     }
	 //    }
  //
  //
  //
  //
  //
	 //    /*
	 //     * Draw argument hint box
	 //     */
	 //    if (inputCommand.commandIndex != -1) {
		//     if (inputCommand.argumentCount < Commands[inputCommand.commandIndex].parameterCount) {
		// 	    TextBuilder.Clear();
		// 	    const string COLOR_END_TAG = "</color>";
		// 	    string colorTag = $"<color=#{ColorUtility.ToHtmlStringRGBA(Style.InputArgumentTypeBorder)}>";
		// 	    // int nameLenght = Commands[inputCommand.commandIndex].parameterNames[inputCommand.argumentCount].Length;
		// 	    TextBuilder.Append(
		// 		    $"({Commands[inputCommand.commandIndex].parameterNames[inputCommand.argumentCount]})");
  //
		// 	    GUIContent argumentHint = new (TextBuilder.ToString());
		// 	    Vector2 argumentHintSize = Style.ConsoleSkin.label.CalcSize(argumentHint);
		// 	    Rect argumentHintRect = new (inputFieldRect) {
		// 		    x = inputFieldRect.x + Style.ConsoleSkin.textField.CalcSize(inputCommand.inputContent).x,
		// 		    width = argumentHintSize.x,
		// 	    };
		// 	    argumentHintRect.position += new Vector2(Style.ArgHelpWidthPadding,
		// 		    Style.ArgumentTypeBumpCurve.Evaluate(argumentHintBump) * Style.ArgHelpBumpOffsetAmount);
  //
		// 	    // Middle
		// 	    // TextBuilder.Insert(nameLenght + 4, COLOR_END_TAG);
		// 	    // TextBuilder.Insert(nameLenght + 3, colorTag);
  //
		// 	    // Start
		// 	    TextBuilder.Insert(1, COLOR_END_TAG);
		// 	    TextBuilder.Insert(0, colorTag);
  //
		// 	    // End
		// 	    TextBuilder.Insert(TextBuilder.Length - 1, colorTag);
		// 	    TextBuilder.Append(COLOR_END_TAG);
  //
  //
		// 	    GUI.contentColor = Style.InputArgumentType;
		// 	    argumentHint.text = TextBuilder.ToString();
		// 	    GUI.Label(argumentHintRect, argumentHint);
		//     }
	 //    }
  //
	 //    /*
	 //     * Set focus back to input field
	 //     */
  //
	 //    if (setFocus > 0) {
		//     --setFocus;
		//     GUI.FocusControl(CONSOLE_INPUT_FIELD_ID);
		//     inputCommand.Clear();
	 //    }
  //
	 //    if (moveMarkerToEnd > 0) {
		//     --moveMarkerToEnd;
		//     TextEditor text = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
		//     text.MoveTextEnd();
	 //    }
  //
  //   }
	    #endregion
		
    }

    
    void execute_command() {

	    dev_command cmd = dev_commands[selected_command_idx];
	    int num_args = cmd.num_args;
	    int num_valig_args = parse_arg_result.valid_args;
	    
	    object[] cmd_args = new object[num_args];
        for (int idx = 0; idx < num_args; idx++) {
            if (idx < num_valig_args) {
	            cmd_args[idx] = arg_value[idx];
            }
            else {
	            cmd_args[idx] = cmd.arg_default_value[idx];
            }
        }

        List<object> targets = new (32);
        if (cmd.cmd_is_static) {
	        targets.Add(null);
        }
        else {
	        targets.AddRange(FindObjectsByType(cmd.target_type, FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        for (int idx = 0; idx < targets.Count; idx++) {
        	switch (cmd.cmd_type) {
	    	    case dev_command.command_type.method:
			        cmd.method.Invoke(targets[idx], cmd_args);
			        break;
			        
	    	    case dev_command.command_type.field:
			        // TODO enum fields dont work
			        cmd.field.SetValue(targets[idx], cmd_args);
			        break;
			        
	    	    case dev_command.command_type.action:
			        Action cmd_action = cmd.field.GetValue(targets[idx]) as Action;
			        cmd_action?.Invoke();
			        break;

		        case dev_command.command_type.unity_event:
			        UnityEvent cmd_unity_event = cmd.field.GetValue(targets[idx]) as UnityEvent;
			        cmd_unity_event?.Invoke();
			        break;
		        
		        default: throw new ArgumentOutOfRangeException();
        	}
        }

        ReadOnlySpan<char> input_cleaned = console_input_text.AsSpan();
        for (int idx = cmd_history.Count - 1; idx >= 0; idx--) {
	        if (cmd_history[idx].AsSpan().CompareTo(input_cleaned, StringComparison.OrdinalIgnoreCase) == 0) {
				cmd_history.RemoveAt(idx);
	        }
        }
        
		cmd_history.Insert(0, input_cleaned.ToString());
		if (cmd_history.Count > MAX_HISTORY) {
			cmd_history.RemoveAt(cmd_history.Count - 1);
		}
    }

    void parse_input_string() {
	    string_section cmd_section = parse_string_for_section(ref console_input_text, 0);
	    selected_command_idx = parse_for_command(ref console_input_text, ref cmd_section, dev_commands, active_cmd_count);
	    if (selected_command_idx != -1 && has_space_at_index(ref console_input_text, cmd_section.end, true)) {
		    parse_arg_result = parse_for_arguments(ref console_input_text, cmd_section.end, dev_commands[selected_command_idx]);
		    if (parse_arg_result.valid_args == dev_commands[selected_command_idx].num_args) {
			    parse_arg_result.num_hints = 0;
		    }
	    }
	    else {
		    parse_arg_result.num_hints = parse_for_command_hints(ref console_input_text, dev_commands);
	    }
    }

    void calculate_and_draw_hints() {
	    
	    /*
	     * TODO move hint box horizontally to start of next argument 
	     */
	    
	    
	    float maximum_width = 0;
	    float width_offset = 0;
	    float height_offset = 2;
	    float bottom_padding = 0;
	    
	    float max_hint_height = input_field_background_rect.y - bottom_padding - HEIGHT_SPACING * 2 - height_offset;
	    hint_height_per_line = Style.ConsoleSkin.label.CalcSize(hint_content[0]).y;
	    num_hints_on_screen = Mathf.Clamp(Mathf.RoundToInt(max_hint_height / hint_height_per_line), 1, parse_arg_result.num_hints);
	    float maximum_height = num_hints_on_screen * hint_height_per_line;

	    if (selected_hint_idx < hint_display_index_start) {
		    hint_display_index_start = selected_hint_idx;
	    }
	    else if (selected_hint_idx >= hint_display_index_start + num_hints_on_screen) {
		    hint_display_index_start = selected_hint_idx - num_hints_on_screen + 1;
	    }

	    hint_display_index_start = Mathf.Clamp(hint_display_index_start, 0, Mathf.Max(parse_arg_result.num_hints - num_hints_on_screen, 0));

	    // TODO this can be calculated once, lots of CalcSize is expensive
		float width_screen_buffer = Screen.width - WIDTH_SPACING * 2f;
	    for (int i = 0; i < num_hints_on_screen; i++) {
		    Vector2 hint_text_size = Style.ConsoleSkin.label.CalcSize(hint_content[hint_display_index_start + i]);
		    maximum_width = Mathf.Clamp(Mathf.Max(hint_text_size.x, maximum_width), 0, width_screen_buffer);
	    }

	    // TODO needs to handle cmd only, next_idx will be old when going from first arg -> cmd selection again
	    // donno if i even want this 
	    // Vector2 input_text_size = Style.ConsoleSkin.textField.CalcSize(new GUIContent(console_input_text[..parse_arg_result.next_idx]));
	    // width_offset = input_text_size.x;
	    
	    hint_background_rect = new Rect(input_field_background_rect) {
		    x = input_field_background_rect.x + width_offset,
		    y = input_field_background_rect.y - bottom_padding - maximum_height - height_offset,
		    width = maximum_width,
		    height = maximum_height + bottom_padding,
	    };

	    GUI.backgroundColor = Style.color_background;
	    GUI.Box(hint_background_rect, string.Empty);
	    GUI.Box(hint_background_rect, string.Empty, BoxBorderSkin());

	    Vector2 hint_starting_pos = hint_background_rect.position;

	    if (current_device == device.mouse && mouse_pos.inside(hint_background_rect.min, hint_background_rect.max)) {
		    float mouse_y = mouse_pos.y - hint_background_rect.y;
		    int hovered_hint = Mathf.FloorToInt(mouse_y / hint_height_per_line);
		    hovered_hint = Mathf.Clamp(hovered_hint, 0, num_hints_on_screen - 1);
		    selected_hint_idx = hint_display_index_start + (num_hints_on_screen - 1) - hovered_hint;
	    }
	    

	    for (int idx = 0; idx < num_hints_on_screen; idx++) {
		    // drawing from the bottom and up so that index matches selection
		    Vector2 pos = hint_starting_pos + new Vector2(0, maximum_height - (idx + 1) * hint_height_per_line);
		    hint_rect[idx] = new Rect(pos.x, pos.y, maximum_width, hint_height_per_line);

		    bool is_selected = (hint_display_index_start + idx) == selected_hint_idx;
		    if (is_selected) {
			    hint_rect[idx].x += Style.SelectionBumpCurve.Evaluate(selectionBump) * Style.SelectHintBumpOffsetAmount;
			    GUI.contentColor = Style.color_text_selected;
		    }
		    else {
			    GUI.contentColor = Style.color_text_default;
		    }


		    if (CommandHistoryState == History.SHOW) {
			    if (HistoryCommands[hint_display_index_start + idx].historyCommandState != 2) {
				    GUI.enabled = false;
			    }
		    }

		    GUI.Label(hint_rect[idx], hint_content[hint_display_index_start + idx]);

		    if (GUI.enabled == false) {
			    GUI.enabled = true;
		    }
	    }
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
	int parse_for_command(ref string input, ref string_section section, dev_command[] commands, int num_commands) {
		int command_idx = -1;
	    
		for (int idx = 0; idx < num_commands; idx++) {
			if (commands[idx].cmd_display_name.Length != section.length) {
				continue;
			}
		    
			if (string.Compare(input, section.start_idx, commands[idx].cmd_display_name, 0, section.length, StringComparison.OrdinalIgnoreCase) == 0) {
				command_idx = idx;
				break;
			}
		}

		return command_idx;
	}


	int parse_for_command_hints(ref string input, dev_command[] commands) {
		string_section[] remaining_segments = parse_string_for_remaining_sections(ref input, 0);
		int num_hints = 0;
	    
		for (int cmd_idx = 0; cmd_idx < commands.Length; cmd_idx++) {
		    
			ReadOnlySpan<char> cmd_name = commands[cmd_idx].cmd_display_name.AsSpan();
			bool display_as_hint = true;
		    
			for (int section_idx = 0; section_idx < remaining_segments.Length; section_idx++) {
				ReadOnlySpan<char> segment_span = input.AsSpan(remaining_segments[section_idx].start_idx, remaining_segments[section_idx].length);
				if (cmd_name.IndexOf(segment_span, StringComparison.OrdinalIgnoreCase) == -1) {
					display_as_hint = false;
					break;
				}
			}

			if (display_as_hint) {
				hint_content[num_hints].text = commands[cmd_idx].hint_text;
				hint_index[num_hints] = cmd_idx;
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
    
	parse_arg parse_for_arguments(ref string input, int start_idx, dev_command command) {
		/*
		 * if we find valid matches for all args in command, ignore rest of string
		 */
		parse_arg parse_result = new () { next_idx = start_idx };
		int valid_args = 0;
     	    
		for (int arg_idx = 0; arg_idx < command.num_args; arg_idx++) {
			bool require_space_after_arg = arg_idx + 1 < command.num_args;
			parse_result = parse_hints_for_arg_type(ref input, arg_idx, parse_result.next_idx, command.arg_types[arg_idx], require_space_after_arg);
     		    
			// 0 = invalid arg, 1 = valid arg
			if (parse_result.valid_args == 0) {
				break;
			}
     
			if (arg_idx + 1 < command.num_args) {
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
         
	parse_arg parse_hints_for_arg_type(ref string input, int arg_idx, int start_idx, Type arg_type, bool require_space_after_arg) {
        
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
						arg_value[arg_idx] = true;
					}
				}
				else if (remaining_segments[0].length == bool.FalseString.Length) {
					if (string.Compare(input, remaining_segments[0].start_idx, bool.FalseString, 0, remaining_segments[0].length, StringComparison.OrdinalIgnoreCase) == 0 ) 
					{
						parse_result.next_idx = remaining_segments[0].end;
						parse_result.valid_args++;
						arg_value[arg_idx] = false;
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
				hint_content[parse_result.num_hints].text = bool.TrueString;
				hint_value[parse_result.num_hints++]      = true;
			}
 
			if (show_hint_false) {
				hint_content[parse_result.num_hints].text = bool.FalseString;
				hint_value[parse_result.num_hints++]      = false;
			}
		}
        
        
		/*
		 * TODO
		 * #### ENUM ####
		 */
		else if (arg_type.IsEnum) {
     	    
			// check if valid arg
			string[] enum_names = arg_type.GetEnumNames();
			Array enum_values = arg_type.GetEnumValues();
			
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
				arg_value[arg_idx] = enum_values.GetValue(idx_best_match);
			}
     	    
     	    
			// get hints
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
					hint_content[parse_result.num_hints].text = enum_names[enum_idx];
					hint_value[parse_result.num_hints++] = enum_values.GetValue(enum_idx);
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
				arg_value[arg_idx] = Cache.AssetReferences[idx_best_match];
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
					hint_content[parse_result.num_hints].text = asset.name;
					hint_value[parse_result.num_hints++] = asset;
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
						arg_value[arg_idx] = string_to_value;
					}
				}
			}
		}
        
        
 
		return parse_result;
	}


	// TODO this whole thing is kinda ugly
	void apply_selected_hint(ref string input, ReadOnlySpan<char> hint_segment, int next_idx, bool append_space) {
		int first_char_idx = first_character_idx(ref input);
		ReadOnlySpan<char> segment_to_keep = input.AsSpan(first_char_idx, next_idx);
		StringBuilder builder = new (segment_to_keep.Length + hint_segment.Length);
		builder.Append(segment_to_keep.TrimStart(CHAR.SPACE).TrimEnd(CHAR.SPACE));
		if (builder.Length > 0) {
			builder.Append(CHAR.SPACE);
		}
		builder.Append(hint_segment);
		if (append_space) {
			builder.Append(CHAR.SPACE);
		}
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
            // inputContent.text = string.Empty;
            // argumentHintBump = 0;
            //
            // /*
            //  * Are we applying from history?
            //  */
            // if (CommandHistoryState == History.SHOW) {
            //     HistoryCommand historyCommand = HistoryCommands[hint_index[indexOfHint]];
            //     commandIndex = historyCommand.commandIndex;
            //     commandContent.text = historyCommand.commandDisplayName;
            //     
            //     argumentCount = historyCommand.argumentValues.Length;
            //     for (int i = 0; i < argumentCount; i++) {
            //         inputArgumentValue[i] = historyCommand.argumentValues[i];
            //         inputArgumentName[i].text = historyCommand.argumentDisplayName[i];
            //     }
            //     inputContent.text = string.Empty;
            //     return;
            // }
            //
            //
            // /*
            //  * When applying command hint
            //  */
            // if (hint_value[indexOfHint].GetType() == TYPE_COMMAND_DATA) { 
            //     commandIndex = hint_index[indexOfHint];
            //     commandContent.text = dev_commands[commandIndex].cmd_display_name;
            //     return;
            // }
            //
            //
            //
            // /*
            //  * When applying argument hint
            //  */
            // object argumentValue = hint_value[indexOfHint];
            //
            //
            // /*
            //  * Vectors
            //  */
            //
            // if (argumentValue is Vector2 || argumentValue is Vector3 || argumentValue is Vector4) {
            //     inputArgumentName[argumentCount].text = hint_content[indexOfHint].text;
            //     inputArgumentValue[argumentCount] = argumentValue;
            //     argumentCount++;
            //     return;
            // }
            //
            // /*
            //  * ScriptableObjects and everything else
            //  */
            //
            // if (TYPE_SO.IsAssignableFrom(argumentValue.GetType())) {
            //     inputArgumentName[argumentCount].text = hint_content[indexOfHint].text;
            // }
            // else {
            //     inputArgumentName[argumentCount].text = argumentValue.ToString();
            // }
            // inputArgumentValue[argumentCount] = argumentValue;
            //
            // argumentCount++;
        }
        internal bool CanExecuteCommand() {
            // if (commandIndex == -1) return false;
            //
            // for (int i = argumentCount; i < dev_commands[commandIndex].num_args; i++) {
            //     if (dev_commands[commandIndex].parameterHasDefault[i] == false) return false;
            // }
            return true;
        }
        internal void GenerateHistoryCommand(out HistoryCommand historyCommand) {
            historyCommand = new HistoryCommand();
            //
            // TextBuilder.Clear();
            // TextBuilder.Append($"{Commands[commandIndex].displayName}{CHAR.SPACE}");
            // historyCommand.commandDisplayName = Commands[commandIndex].displayName;
            // historyCommand.historyCommandState = 2;
            // historyCommand.commandIndex = commandIndex;
            // historyCommand.argumentValues = inputArgumentValue[..argumentCount];
            // historyCommand.argumentDisplayName = new string[argumentCount];
            //
            // for (int i = 0; i < argumentCount; i++) {
            //     historyCommand.argumentDisplayName[i] = inputArgumentName[i].text;
            // }
            //
            // for (int i = 0; i < inputArgumentName.Length; i++) {
            //     TextBuilder.Append($"{inputArgumentName[i].text}{CHAR.SPACE}");
            // }
            //
            // historyCommand.displayString = TextBuilder.ToString();
        }
        
        internal void ExecuteCommand() {
            // object[] argumentValues = new object[Commands[commandIndex].parameterCount];
            // for (int i = 0; i < argumentValues.Length; i++) {
            //     if (i < argumentCount) {
            //         argumentValues[i] = inputArgumentValue[i];
            //     }
            //     else {
            //         argumentValues[i] = Commands[commandIndex].defaultParamValue[i];
            //     }
            // }
            //
            // bool isMethod = Commands[commandIndex].commandType == CommandData.CommandType.METHOD;
            // for (int i = 0; i < Commands[commandIndex].targets.Count; i++) {
            //     if (commandIndex > StaticCommandCount && Commands[commandIndex].targets[i] == null)
            //         continue;
            //     
            //     if (isMethod) { 
            //         Commands[commandIndex].method.Invoke(Commands[commandIndex].targets[i], argumentValues);
            //     }
            //     else {
            //         /*
            //          * Actions will be null if no one is subscribed to them, unity events will still be valid
            //          * Not doing null check on them since I'm not sure what I want the behaviour to be if it is
            //          * Logging it might be really annoying..
            //          */
            //         if (Commands[commandIndex].field.FieldType == typeof(UnityEvent)) {
            //             UnityEvent unityEvent = Commands[commandIndex].field.GetValue(Commands[commandIndex].targets[i]) as UnityEvent;
            //             unityEvent?.Invoke();
            //         }
            //         else if (Commands[commandIndex].field.FieldType == typeof(Action)) {
            //             Action action = Commands[commandIndex].field.GetValue(Commands[commandIndex].targets[i]) as Action;
            //             if (action == null) {
            //                 Debug.LogError($"Action is null -> Could be caused by {Commands[commandIndex].field.Name} having no subscribers!");
            //             }
            //             else {
            //                 action.Invoke();
            //             }
            //         }
            //         else {
            //             Commands[commandIndex].field.SetValue(Commands[commandIndex].targets[i], argumentValues[0]);
            //         }
            //     }
            // }
        }

        internal string CreateCompleteCommandString() {
            // string fullCommandString = Commands[commandIndex].displayName;
            // for (int i = 0; i < Commands[commandIndex].parameterCount; i++) {
            //     if (i < argumentCount) {
            //         fullCommandString += " " + inputArgumentValue[i];
            //     }
            //     else {
            //         fullCommandString += " " + Commands[commandIndex].defaultParamValue[i];
            //     }
            // }
            //
            // return fullCommandString;
            return "";
        }
    }


	struct dev_command {
		public string cmd_display_name;
		public string hint_text;
		public int num_args;
		public int num_args_required;
		public string[] arg_names;
		public Type[] arg_types;
		public bool[] arg_has_default;
		public object[] arg_default_value;
		public bool cmd_is_static;
		public command_type cmd_type;
		public MethodInfo method;
		public FieldInfo field;
		public Type target_type;
		

		public enum command_type {
			method,
			field,
			action,
			unity_event,
		}

		public void set_method(DevCommand command, MethodInfo method_info, Type found_on_type) {
			cmd_type = command_type.method;
			method = method_info;
			cmd_is_static = method.IsStatic;
			target_type = found_on_type;
			
			cmd_display_name = string.IsNullOrEmpty(command.display_name) ? method.Name : command.display_name;
			
			
			ParameterInfo[] args = method.GetParameters();
			num_args = args.Length;
			num_args_required = num_args;
			arg_types = new Type[num_args];
			arg_names = new string[num_args];
			arg_has_default = new bool[num_args];
			arg_default_value = new object[num_args];


			TextBuilder.Clear();
			TextBuilder.Append($"{cmd_display_name} ");
			for (int i = 0; i < args.Length; i++) {
				ParameterInfo param = args[i];
				TextBuilder.Append($"<{param.Name}> ");
				
				arg_types[i] = param.ParameterType;
				arg_names[i] = param.Name;
				arg_has_default[i] = param.HasDefaultValue;
				arg_default_value[i] = param.DefaultValue;
				
				if (param.HasDefaultValue) {
					num_args_required--;
				}
			}

			hint_text = TextBuilder.ToString();
		}
		
		public void set_field(DevCommand command, FieldInfo field_info, Type found_on_type) {
			cmd_type = command_type.field;
			field = field_info;
			cmd_is_static = field.IsStatic;
			target_type = found_on_type;
			
			num_args = 1;
			num_args_required = num_args;
			arg_types = new Type[] { field.FieldType };
			arg_names = new string[]{ field.Name };
			
			
			cmd_display_name = string.IsNullOrEmpty(command.display_name) ? field.Name : command.display_name;
			hint_text = $"{cmd_display_name} <{field.Name}>";
		}

		public void set_action(DevCommand command, FieldInfo field_info, Type found_on_type) {
			cmd_type = command_type.action;
			field = field_info;
			cmd_is_static = field.IsStatic;
			target_type = found_on_type;
			
			num_args = 0;
			num_args_required = num_args;
			arg_types = Type.EmptyTypes;
			arg_names = Array.Empty<string>();
			
			cmd_display_name = string.IsNullOrEmpty(command.display_name) ? field.Name : command.display_name;
			hint_text = $"{cmd_display_name}";
		}
		
		public void set_unity_event(DevCommand command, FieldInfo field_info, Type found_on_type) {
			cmd_type = command_type.unity_event;
			field = field_info;
			cmd_is_static = field.IsStatic;
			target_type = found_on_type;
			
			num_args = 0;
			num_args_required = num_args;
			arg_types = Type.EmptyTypes;
			arg_names = Array.Empty<string>();
			
			cmd_display_name = string.IsNullOrEmpty(command.display_name) ? field.Name : command.display_name;
			hint_text = $"{cmd_display_name}";
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

            displayName = string.IsNullOrEmpty(devCommand.display_name) ? method.Name : devCommand.display_name;
            
            ParameterInfo[] args = method.GetParameters();
            parameterCount = args.Length;
            num_required_args = parameterCount;
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
                if (param.HasDefaultValue) {
	                num_required_args--;
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
            
            displayName = string.IsNullOrEmpty(devCommand.display_name) ? field.Name : devCommand.display_name;


            
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