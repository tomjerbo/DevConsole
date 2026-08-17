#define URP_ENABLED
#define DEVCONSOLE_DEBUG

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Application = UnityEngine.Application;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;



/* 
 * TODO
 * === Rework ====
 *
 * Optional placement of having it on the top vs bottom of screen? help menu adjusted accordingly both visually and index wise with directional inputs
 * scrolling through history commands should replace input text field to it's quick to select+use, maybe same for hints?
 * Look into using my ui system for handling navigation
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

    
    
    const BindingFlags DEV_COMMAND_BINDING_FLAGS = BindingFlags.Default | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public const string DEV_CONSOLE_SKIN_PATH = PLUGINS_FOLDER_PATH + "DevConsoleSkin.asset";
    public const string DEV_CONSOLE_CACHE_PATH = PLUGINS_FOLDER_PATH + "DevConsoleCache.asset";
    public const string DEV_CONSOLE_STYLE_PATH = PLUGINS_FOLDER_PATH + "DevConsoleStyle.asset";
    public const string PLUGINS_FOLDER_PATH = "Assets/Plugins/DevConsole/";
    const string macro_version = nameof(save_version_history.init_history);
    const string history_version = nameof(save_version_macros.init_macro);
    
    static readonly string path_history_cmd = Path.Combine(Application.persistentDataPath, "DevConsole-CommandHistory.txt");
    static readonly string path_macro_cmd = Path.Combine(Application.persistentDataPath, "DevConsole-Macros.txt");
    
    const string CONSOLE_INPUT_FIELD_ID = "Console Input Field";
    const int MAX_COMMANDS = 512;
    const int MAX_HISTORY = 32;
    const int MAX_HINTS = 32;
    const int MAX_TOAST_MESSAGES = 32;
    const int MAX_ARGS = 16;
    const float WIDTH_SPACING = 8f;
    const float HEIGHT_SPACING = 8f;
	
    
    public static bool is_console_open { get; private set; } // public so you can check it from the outside
    readonly StringBuilder text_builder = new (256);

    
    readonly List<GUIContent> toast_messages = new(MAX_TOAST_MESSAGES);
    readonly List<string> cmd_history = new (MAX_HISTORY);
    readonly GUIContent[] hint_content = new GUIContent[MAX_HINTS];
    readonly dev_command[] dev_commands = new dev_command[MAX_COMMANDS];
    readonly List<macro_cmd> macro_commands = new(16);
    readonly int[] hint_index = new int[MAX_HINTS];
    readonly object[] arg_value = new object[MAX_ARGS];
    
    readonly Rect[] hint_rect = new Rect[MAX_HINTS];
    Rect hint_background_rect = new ();
    Rect input_field_rect = new ();
    Rect input_field_background_rect = new ();
    

    macro_cmd macro_active;
    device current_device;
    console_input_state console_state;
    string console_input_text;
    bool move_selection_to_end;
    
    float hint_height_per_line;
    int num_commands;
    int num_hints_on_screen;
    int display_hint_start_idx;
    int selected_command_idx;
    int selected_history_idx;
    int selected_hint_idx;
    
    parse_arg parse_arg_result;
    Vector2 mouse_pos;
    
    // TODO animations
    float selectionBump;
    float argumentHintBump;
    
    GUIStyle box_border_skin() => Style.ConsoleSkin.customStyles[0];
    
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
    
    struct macro_cmd {
	    public KeyCode keybind;
	    public int num_commands;
	    public bool is_creating_macro;
	    public string[] cmd_strings;
    }
    
    public struct CHAR {
	    internal const char SPACE = ' ';
	    internal const char EMPTY = '\0';
	    internal const char NEW_LINE = '\n';
	    internal const char TAB = '\t';
	    internal const char SEPERATOR = ';';
    }
    
    enum console_input_state {
	    waiting_for_input,
	    using_history,
	    write_command,
    }

    enum device {
	    keyboard,
	    mouse
    }

    enum save_version_history {
	    init_history,
	    LAST_VERSION_PLUS_ONE,
    }
    enum save_version_macros {
	    init_macro,
	    LAST_VERSION_PLUS_ONE,
    }
    
    

    [SerializeField] DevConsoleCache Cache;
    [SerializeField] DevConsoleStyle Style;
    public void assign_refs_for_build(DevConsoleCache cache, DevConsoleStyle style) {
        Cache = cache;
        Style = style;
    }
    

	async void Awake() {
        DontDestroyOnLoad(this);
        
        // Cache & Style gets assigned during build step!
#if UNITY_EDITOR
        Cache = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleCache>(DEV_CONSOLE_CACHE_PATH);
        Style = UnityEditor.AssetDatabase.LoadAssetAtPath<DevConsoleStyle>(DEV_CONSOLE_STYLE_PATH);
#endif
		init_arrays();
		
	    Debug.Log("loading commands - STARTED");
	    Stopwatch sw = Stopwatch.StartNew();
	    await Task.WhenAll(
		      Task.Run(load_dev_commands, destroyCancellationToken)
		    , Task.Run(load_history_commands, destroyCancellationToken)
		    , Task.Run(load_macro_commands, destroyCancellationToken)
		    );
	    
	    reset_console_state();
	    sw.Stop();
	    Debug.Log($"loading command - COMPLETED! -> {sw.ElapsedMilliseconds}ms");
    }
	
	void init_arrays() {
	    for (int i = 0; i < hint_content.Length; i++) {
		    hint_content[i] = new GUIContent();
	    }

	    macro_active.cmd_strings = new string[32];
    }

    void OnDestroy() {
		save_history_commands();
        save_macro_commands();
        is_console_open = false;
    }
    
    async Task load_history_commands() {
	    if (File.Exists(path_history_cmd)) {
		    string[] history_commands = await File.ReadAllLinesAsync(path_history_cmd, destroyCancellationToken);
		    
		    // get version
		    if (Enum.TryParse(history_commands[^1], out save_version_history version)) {
			    Debug.Log($"history version -> {version}");
			    
			    // handle versioning when relevant
		    }
		    
			cmd_history.AddRange(history_commands);
			cmd_history.RemoveAt(history_commands.Length-1); // remove version line
	    }
    }

    void save_history_commands() {
	    cmd_history.Add(macro_version);
	    File.WriteAllLines(path_history_cmd, cmd_history);
    }

    [DevCommand]
    void macro_start(KeyCode key, bool replace_existing = false) {
	    if (macro_active.is_creating_macro) {
            return;
	    }

        for (var idx = 0; idx < macro_commands.Count; idx++) {
            if (macro_commands[idx].keybind == key) {
                if (replace_existing) {
                    macro_commands.RemoveAt(idx);
                    break;
                }
                
                toast_messages.Add(new GUIContent($"Macro already bound to '{key}'!"));
                return;
            }
        }
        
        macro_active.is_creating_macro = true;
        macro_active.keybind = key;
        macro_active.num_commands = 0;
    }

    [DevCommand]
    void macro_end() {
	    if (macro_active.is_creating_macro == false) {
		    return;
	    }
	    
        if (macro_active.num_commands > 0) {
	        macro_cmd macro = new () {
		        keybind = macro_active.keybind,
		        cmd_strings = macro_active.cmd_strings[..macro_active.num_commands],
		        num_commands = macro_active.num_commands,
	        };
	        macro_commands.Add(macro);
			toast_messages.Add(new GUIContent($"Macro created: '{macro_active.keybind}' - {macro_active.num_commands} commands assigned."));
        }
        else {
			toast_messages.Add(new GUIContent($"Macro stopped, no commands assigned!"));
        }

        macro_active.is_creating_macro = false;
    }
    
    void save_macro_commands() {
	    text_builder.Clear();
	    for (int idx = 0; idx < macro_commands.Count; idx++) {
		    text_builder.Append(macro_commands[idx].keybind);
			text_builder.Append(CHAR.SEPERATOR);
			text_builder.AppendJoin(CHAR.SEPERATOR, macro_commands[idx].cmd_strings);
			text_builder.Append(CHAR.NEW_LINE);
	    }
	    
	    text_builder.AppendLine(history_version);
	    File.WriteAllText(path_macro_cmd, text_builder.ToString());
    }
    
    async Task load_macro_commands() {
	    if (File.Exists(path_macro_cmd)) {
		    string[] saved_macro_lines = await File.ReadAllLinesAsync(path_macro_cmd, destroyCancellationToken);
		    
		    if (Enum.TryParse(saved_macro_lines[^1], out save_version_macros version)) {
			    Debug.Log($"Macro version -> {version}");
			    // handle versioning when relevant
		    }
		    
		    // skip version tag on last line
			int num_required_parts_for_macro = 2;
		    for (int idx = 0; idx < saved_macro_lines.Length - 1; idx++) {
			    string[] macro_parts = saved_macro_lines[idx].Split(CHAR.SEPERATOR);
			    
			    if (macro_parts != null && macro_parts.Length >= num_required_parts_for_macro) {
				    if (Enum.TryParse(macro_parts[0], out KeyCode keybind)) {
						macro_cmd macro = new () {
							keybind = keybind,
							num_commands = macro_parts.Length - 1,
							cmd_strings = macro_parts[1..],
						};
						macro_commands.Add(macro);
				    }
			    }
		    }
	    }
    }

    [DevCommand]
    void macro_clear_all() {
	    if (macro_commands.Count > 0) {
	        toast_messages.Add(new GUIContent($"Removed {macro_commands.Count} macros"));
	        macro_commands.Clear();
	    }
    }
    
    [DevCommand]
    void macro_remove(KeyCode key) {
        for (int i = macro_commands.Count - 1; i >= 0; i--) {
            if (macro_commands[i].keybind == key) {
                macro_commands.RemoveAt(i);
                toast_messages.Add(new GUIContent($"Removed macro with keybind -> '{key}'"));
                return;
            } 
        }
    }

    [DevCommand]
    void macro_display(bool only_print_keybinds = false) {
	    for (int idx = 0; idx < macro_commands.Count; idx++) {
		    toast_messages.Add(new GUIContent($"Keybind: {macro_commands[idx].keybind}"));
		    if (only_print_keybinds == false) {
			    for (int cmd_idx = 0; cmd_idx < macro_commands[idx].num_commands; cmd_idx++) {
					toast_messages.Add(new GUIContent($"{CHAR.TAB}{macro_commands[idx].cmd_strings[cmd_idx]}"));
			    }
		    }
	    }
    }

    [DevCommand]
    void clear() {
        toast_messages.Clear();
    }
    
    
    [DevCommand]
    void open_save_folder() {
        Application.OpenURL(Application.persistentDataPath);
    }
    
    
    /*
     * Console Actions
     */
    
    
    void console_open() {
        is_console_open = true;
        reset_console_state();

#if URP_ENABLED
	    UnityEngine.Rendering.DebugManager.instance.enableRuntimeUI = false;
#endif
    }
    
    void console_close() {
        is_console_open = false;
        macro_end();
        GUI.FocusControl(null);
        
#if URP_ENABLED
        UnityEngine.Rendering.DebugManager.instance.enableRuntimeUI = true;
#endif
    }

    void OnGUI() {
        Event input_event = Event.current;
        if (is_console_open == false) {
            KeyCode[] open_console_keys = Style != null ? Style.openConsoleKey : Array.Empty<KeyCode>();
            if (input_event.OpenConsole(overrideKeys:open_console_keys)) {
                console_open();
            }
            else {
                foreach (macro_cmd macro in macro_commands) {
                    if (input_event.KeyDown(macro.keybind)) {
                        foreach (string cmd in macro.cmd_strings) {

	                        console_input_text = cmd;
	                        parse_input_string();
	                        if (selected_command_idx != -1 && parse_arg_result.valid_args >= dev_commands[selected_command_idx].num_args_required) {
								execute_command();
	                        }
	                        reset_console_state();
                        }

                        return;
                    }
                }
            }
            return;
        }

        /*
         * Console is active
         */


        if (input_event.CloseConsole()) {
            console_close();
        }
        else {
	        // TODO seperate logic & repaint actions
	        draw_console_window(input_event);
        }
    }
    
    void reset_console_state() {
	    parse_arg_result.reset();
	    selected_command_idx = -1;
	    selected_hint_idx = -1;
	    console_input_text = string.Empty;
	    console_state = console_input_state.waiting_for_input;
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
						     dev_commands[num_commands++].set_method(cmd, method_info, loaded_type, text_builder);
					     }
				     }


				     FieldInfo[] fields_in_type = loaded_type.GetFields(DEV_COMMAND_BINDING_FLAGS);
				     foreach (FieldInfo field_info in fields_in_type) {
					     DevCommand cmd = field_info.GetCustomAttribute<DevCommand>();
					     if (cmd != null) {
						     if (field_info.FieldType == typeof(Action)) {
							     dev_commands[num_commands++].set_action(cmd, field_info, loaded_type);
						     }
						     else if (field_info.FieldType == typeof(UnityEvent)) {
							     dev_commands[num_commands++].set_unity_event(cmd, field_info, loaded_type);
						     }
						     else {
							     dev_commands[num_commands++].set_field(cmd, field_info, loaded_type);
						     }
					     }
				     }
				     
				     
				     PropertyInfo[] properties_in_type = loaded_type.GetProperties(DEV_COMMAND_BINDING_FLAGS);
				     foreach (PropertyInfo prop_info in properties_in_type) {
					     DevCommand cmd = prop_info.GetCustomAttribute<DevCommand>();
					     if (cmd != null) { 
						     dev_commands[num_commands++].set_method(cmd, prop_info.SetMethod, loaded_type, text_builder);
					     }
				     }
				     
				     // TODO fix events
				     // EventInfo[] events_in_type = loaded_type.GetEvents(DEV_COMMAND_BINDING_FLAGS);
				     // foreach (EventInfo event_info in events_in_type) {
					    //  DevCommand cmd = event_info.GetCustomAttribute<DevCommand>();
					    //  if (cmd != null) { 
						   //   dev_commands[active_cmd_count++].set_method(cmd, event_info.RaiseMethod, loaded_type);
					    //  }
				     // }
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
		    case console_input_state.waiting_for_input:
			    if (up || down) {
				    console_state = console_input_state.using_history;
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
		    
		    case console_input_state.using_history:
		    case console_input_state.write_command:
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
				    console_state = console_input_state.waiting_for_input;
				    return;
			    }
			    
			    
			    // apply hints
			    bool should_insert_hint = (insert_hint_pressed || mouse_clicked_hint) && selected_hint_idx != -1 && parse_arg_result.num_hints > 0;
			    if (should_insert_hint) {
				    if (selected_command_idx == -1) {
					    if (console_state == console_input_state.write_command) {
					    	string hint_text = dev_commands[hint_index[selected_hint_idx]].cmd_display_name;
					    	string_section cmd_section_without_args = parse_string_for_section(ref hint_text, 0);
					    	insert_hint(ref console_input_text, hint_text.AsSpan(cmd_section_without_args.start_idx, cmd_section_without_args.length), 0, true);
					    }
					    else if (console_state == console_input_state.using_history) {
						    ReadOnlySpan<char> hint_text = hint_content[selected_hint_idx].text.AsSpan();
					    	insert_hint(ref console_input_text, hint_text, 0, false);
					    }
				    }
				    else {
					    insert_hint(ref console_input_text, hint_content[selected_hint_idx].text.AsSpan(), parse_arg_result.next_idx, true);
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
	     * Draw Toast Messages
	     */

	    if (toast_messages.Count > 0) {
		    draw_toast_messages();
	    }
	    
	    
	    
	    /*
	     * Draw Console background
	     */
	    
	    input_field_background_rect.Set(WIDTH_SPACING, height - (HEIGHT_SPACING * 2f + Style.console_text_size), width - WIDTH_SPACING * 2f, Style.console_text_size);
	    
	    Color outline_color = Style.color_background;
	    if (macro_active.is_creating_macro) {
		    outline_color = Style.color_outline_macro;
	    }
	    
	    GUI.backgroundColor = outline_color;
	    GUI.Box(input_field_background_rect, string.Empty, box_border_skin());
	    GUI.backgroundColor = Style.color_background;
	    GUI.Box(input_field_background_rect, string.Empty);
	    
	    
	    
	    /*
	     * Draw Console text input
	     */
	    
	    GUI.backgroundColor = Color.clear;
	    bool has_valid_command = selected_command_idx != -1 && parse_arg_result.valid_args >= dev_commands[selected_command_idx].num_args_required;
	    GUI.contentColor = has_valid_command ? Style.color_text_valid_cmd : Style.color_text_default;
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
			    console_state = console_input_state.waiting_for_input;
			    console_input_text = string.Empty;
			    selected_command_idx = -1;
			    parse_arg_result.reset();
		    }
		    else {
			    console_state = console_input_state.write_command;
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
				draw_hints();
		    }
	    }
	    else {
		    selected_hint_idx = -1;
	    }
	    
	    
	    
	    
	    /*
	     * icon
	     */
	    // Vector2 iconSize = Vector2.one * Style.ConsoleIconSize;
	    // Vector2 iconOffset = (Style.console_text_size - Style.ConsoleIconSize) * 0.5f * Vector2.one;
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
	    
	    Vector2 size = Style.ConsoleSkin.box.CalcSize(debug);
	    GUI.Box(new Rect(Screen.width - size.x - WIDTH_SPACING, HEIGHT_SPACING, size.x,size.y + HEIGHT_SPACING), debug);
#endif

    }
    
    void draw_toast_messages() {
	    float vertical_padding = 2;
	    float horizontal_padding = 2;
	    
	    float maximum_width = input_field_background_rect.width + horizontal_padding * 2;
	    float max_hint_height = input_field_background_rect.y - vertical_padding - HEIGHT_SPACING * 2;
	    float height_per_line = Style.ConsoleSkin.label.CalcSize(toast_messages[0]).y;
	    int messages_to_draw = Mathf.Clamp(Mathf.RoundToInt(max_hint_height / height_per_line), 1, toast_messages.Count);
	    float maximum_height = messages_to_draw * height_per_line;


	    Rect toast_window = new (input_field_background_rect) {
		    x = input_field_background_rect.x - horizontal_padding,
		    y = input_field_background_rect.y - maximum_height - vertical_padding,
		    width = maximum_width,
		    height = maximum_height + vertical_padding * 2,
	    };

	    Color color_background_faded = Style.color_background;
	    color_background_faded.a = 0.4f;	
	    GUI.backgroundColor = color_background_faded;
	    GUI.Box(toast_window, string.Empty);

	    GUI.contentColor = Style.color_text_default;
	    Vector2 hint_start_pos = toast_window.position + new Vector2(0, vertical_padding);
	    for (int i = 0; i < messages_to_draw; i++) {
		    Vector2 pos = hint_start_pos + new Vector2(0, i * height_per_line);
		    GUI.Label(new Rect(pos, new Vector2(maximum_width, height_per_line)), toast_messages[i]);
	    }
    
    }
    
    void draw_hints() {
	    float maximum_width = 0;
	    float width_offset = 0;
	    float spacing_from_input_rect = 2;
	    float vertical_padding = 2;
	    
	    float max_hint_height = input_field_background_rect.y - vertical_padding - HEIGHT_SPACING * 2 - spacing_from_input_rect;
	    hint_height_per_line = Style.ConsoleSkin.label.CalcSize(hint_content[0]).y;
	    num_hints_on_screen = Mathf.Clamp(Mathf.RoundToInt(max_hint_height / hint_height_per_line), 1, parse_arg_result.num_hints);
	    float maximum_height = num_hints_on_screen * hint_height_per_line;

	    if (selected_hint_idx < display_hint_start_idx) {
		    display_hint_start_idx = selected_hint_idx;
	    }
	    else if (selected_hint_idx >= display_hint_start_idx + num_hints_on_screen) {
		    display_hint_start_idx = selected_hint_idx - num_hints_on_screen + 1;
	    }

	    display_hint_start_idx = Mathf.Clamp(display_hint_start_idx, 0, Mathf.Max(parse_arg_result.num_hints - num_hints_on_screen, 0));

	    // TODO this can be calculated once, lots of CalcSize is expensive
		float width_screen_buffer = Screen.width - WIDTH_SPACING * 2f;
	    for (int i = 0; i < num_hints_on_screen; i++) {
		    Vector2 hint_text_size = Style.ConsoleSkin.label.CalcSize(hint_content[display_hint_start_idx + i]);
		    maximum_width = Mathf.Clamp(Mathf.Max(hint_text_size.x, maximum_width), 0, width_screen_buffer);
	    }

	    // TODO needs to handle cmd only, next_idx will be old when going from first arg -> cmd selection again
	    // donno if i even want this 
	    // Vector2 input_text_size = Style.ConsoleSkin.textField.CalcSize(new GUIContent(console_input_text[..parse_arg_result.next_idx]));
	    // width_offset = input_text_size.x;
	    
	    hint_background_rect = new Rect(input_field_background_rect) {
		    x = input_field_background_rect.x + width_offset,
		    y = input_field_background_rect.y - maximum_height - spacing_from_input_rect - vertical_padding * 2,
		    width = maximum_width,
		    height = maximum_height + vertical_padding * 2,
	    };

	    GUI.backgroundColor = Style.color_background;
	    GUI.Box(hint_background_rect, string.Empty);
	    GUI.Box(hint_background_rect, string.Empty, box_border_skin());

	    Vector2 hint_starting_pos = hint_background_rect.position + new Vector2(0, vertical_padding);

	    if (current_device == device.mouse && mouse_pos.inside(hint_background_rect.min, hint_background_rect.max)) {
		    float mouse_y = mouse_pos.y - hint_background_rect.y;
		    int hovered_hint = Mathf.FloorToInt(mouse_y / hint_height_per_line);
		    hovered_hint = Mathf.Clamp(hovered_hint, 0, num_hints_on_screen - 1);
		    selected_hint_idx = display_hint_start_idx + (num_hints_on_screen - 1) - hovered_hint;
	    }
	    

	    for (int idx = 0; idx < num_hints_on_screen; idx++) {
		    // drawing from the bottom and up so that index matches selection
		    Vector2 pos = hint_starting_pos + new Vector2(0, maximum_height - (idx + 1) * hint_height_per_line);
		    hint_rect[idx] = new Rect(pos.x, pos.y, maximum_width, hint_height_per_line);

		    bool is_selected = (display_hint_start_idx + idx) == selected_hint_idx;
		    if (is_selected) {
			    hint_rect[idx].x += Style.SelectionBumpCurve.Evaluate(selectionBump) * Style.SelectHintBumpOffsetAmount;
			    GUI.contentColor = Style.color_text_selected;
		    }
		    else {
			    GUI.contentColor = Style.color_text_default;
		    }

		    GUI.Label(hint_rect[idx], hint_content[display_hint_start_idx + idx]);
	    }
	}

    void execute_command() {
	    // TODO could combine duplicate and prepend 2x <console_input_text>
	    toast_messages.Add(new GUIContent(console_input_text));
	    while (toast_messages.Count > MAX_TOAST_MESSAGES) {
		    toast_messages.RemoveAt(0);
	    }
	    
	    dev_command cmd = dev_commands[selected_command_idx];
	    int num_args = cmd.num_args;
	    int num_valid_args = parse_arg_result.valid_args;

	    bool execute_command = true;
	    
	    if (macro_active.is_creating_macro && string.Compare(cmd.cmd_display_name, nameof(macro_end), StringComparison.OrdinalIgnoreCase) != 0) {
		    macro_active.cmd_strings[macro_active.num_commands++] = console_input_text;
		    execute_command = false;
	    } 
	    
	    if (execute_command) {
		    object[] cmd_args = new object[num_args];
	        for (int idx = 0; idx < num_args; idx++) {
	            if (idx < num_valid_args) {
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
				        if (num_args > 1) {
					        Debug.Log($"Cmd of type field has more than 1 args! -> {cmd.field.Name}, {num_args}");
				        }
				        cmd.field.SetValue(targets[idx], num_args == 1 ? cmd_args[0] : cmd_args);
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
	    }
        

        ReadOnlySpan<char> input_as_span = console_input_text.AsSpan();
        for (int idx = cmd_history.Count - 1; idx >= 0; idx--) {
	        if (cmd_history[idx].AsSpan().CompareTo(input_as_span, StringComparison.OrdinalIgnoreCase) == 0) {
				cmd_history.RemoveAt(idx);
	        }
        }
        
		cmd_history.Insert(0, console_input_text);
		if (cmd_history.Count > MAX_HISTORY) {
			cmd_history.RemoveAt(cmd_history.Count - 1);
		}
    }

    void parse_input_string() {
	    int selected_cmd_before = selected_command_idx;
	    int valid_args_before = parse_arg_result.valid_args;
	    
	    string_section cmd_section = parse_string_for_section(ref console_input_text, 0);
	    selected_command_idx = parse_for_command(ref console_input_text, ref cmd_section, dev_commands, num_commands);
	    if (selected_command_idx != -1 && has_space_at_index(ref console_input_text, cmd_section.end, true)) {
		    parse_arg_result = parse_for_arguments(ref console_input_text, cmd_section.end, dev_commands[selected_command_idx]);
		    if (parse_arg_result.valid_args == dev_commands[selected_command_idx].num_args) {
			    parse_arg_result.num_hints = 0;
		    }
	    }
	    else {
		    selected_command_idx = -1;
		    parse_arg_result.num_hints = parse_for_command_hints(ref console_input_text, dev_commands, num_commands);
	    }

	    if (selected_cmd_before != selected_command_idx || valid_args_before != parse_arg_result.valid_args) {
		    selected_hint_idx = -1;
	    }
    }

	string_section parse_string_for_section(ref string input_string, int start_idx) {
		string_section section = new (); 
		
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


	int parse_for_command_hints(ref string input, dev_command[] commands, int num_cmds) {
		string_section[] remaining_segments = parse_string_for_remaining_sections(ref input, 0);
		int num_hints = 0;
	    
		for (int cmd_idx = 0; cmd_idx < num_cmds; cmd_idx++) {
		    
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

			// arg is only valid if you have a space afterward, not needed for last arg
			bool not_last_arg = arg_idx + 1 < command.num_args; 
			if (not_last_arg) {
				if (parse_result.next_idx >= input.Length || input[parse_result.next_idx] != CHAR.SPACE) {
					break;
				}
			}
     		    
			valid_args++;
		}
     
		parse_result.valid_args = valid_args;
		return parse_result;
	}
         
	
	// having a bool that ignores the check is fucking stupid, but it's so convenient..
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
				hint_content[parse_result.num_hints++].text = bool.TrueString;
			}
 	
			if (show_hint_false) {
				hint_content[parse_result.num_hints++].text = bool.FalseString;
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
					hint_content[parse_result.num_hints++].text = enum_names[enum_idx];
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
 
        
		else if (typeof(ScriptableObject).IsAssignableFrom(arg_type)) {
     	    
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
					hint_content[parse_result.num_hints++].text = asset.name;
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
	void insert_hint(ref string input, ReadOnlySpan<char> hint_segment, int next_idx, bool append_space) {
		int first_char_idx = first_character_idx(ref input);
		ReadOnlySpan<char> segment_to_keep = input.AsSpan(first_char_idx, next_idx);
		text_builder.Clear();
		text_builder.Append(segment_to_keep.TrimStart(CHAR.SPACE).TrimEnd(CHAR.SPACE));
		if (text_builder.Length > 0) {
			text_builder.Append(CHAR.SPACE);
		}
		text_builder.Append(hint_segment);
		if (append_space) {
			text_builder.Append(CHAR.SPACE);
		}
		input = text_builder.ToString();
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


	struct dev_command {
		public string cmd_display_name;
		public string hint_text;
		public int num_args;
		public int num_args_required;
		public string[] arg_names;
		public Type[] arg_types;
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

		public void set_method(DevCommand command, MethodInfo method_info, Type found_on_type, StringBuilder builder) {
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
			arg_default_value = new object[num_args];


			builder.Clear();
			builder.Append($"{cmd_display_name} ");
			for (int i = 0; i < args.Length; i++) {
				ParameterInfo param = args[i];
				builder.Append($"<{param.Name}> ");
				
				arg_types[i] = param.ParameterType;
				arg_names[i] = param.Name;
				arg_default_value[i] = param.DefaultValue;
				
				if (param.HasDefaultValue) {
					num_args_required--;
				}
			}

			hint_text = builder.ToString();
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
	
    }
}