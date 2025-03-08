using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Jerbo.Tools
{
    public class DevConsole : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod]
        public static void Init() {
            GameObject consoleContainer = new ("- Dev Console -");
            consoleContainer.AddComponent<DevConsole>();
        }

        
        const string CONSOLE_INPUT_FIELD = "Console Input Field";
        const float SCREEN_HEIGHT_PERCENTAGE = 0.05f;
        const float WIDTH_SPACING = 8f;
        const float HEIGHT_SPACING = 8f;
        const float HINT_HEIGHT_TEXT_PADDING = 2f;

        
        readonly List<ConsoleCommand> commands = new(100);
        readonly List<ConsoleCommand> staticCommands = new(100);
        GUISkin consoleSkin;
        string consoleInputString = string.Empty;
        int selected;
        bool isActive;
        int setFocus;


        float consoleWidth;
        float consoleHeight;
        Vector2 consoleInputDrawPos;
        Vector2 consoleInputSize;


        
        void Start() {
            consoleSkin = Resources.Load<GUISkin>("Dev Console Skin");
            const BindingFlags BINDING_FLAGS = BindingFlags.Default | BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.InvokeMethod;
            
            Type[] assemblyTypes = Assembly.GetExecutingAssembly().GetTypes();
            Type commandAttribute = typeof(DevCommand);
            
            
            List<MethodInfo> memberInfoCollection = new (24);
            foreach (Type loadedType in assemblyTypes)
            {
                memberInfoCollection.AddRange(loadedType.GetMethods(BINDING_FLAGS).Where(info => Attribute.IsDefined(info, commandAttribute)));

                foreach (MethodInfo info in memberInfoCollection)
                {
                    commands.Add(new ConsoleCommand(info));
                }
                memberInfoCollection.Clear();
            }
        }
        
        
        void OnGUI() {
            Event e = Event.current;
            if (isActive == false) {
                if (e.KeyUp(KeyCode.F1)) OpenConsole();
                return;
            }

            
            // Is Active
            if (e.KeyUp(KeyCode.Escape) || e.KeyUp(KeyCode.F1)) CloseConsole();
            
            GUISkin skin = GUI.skin;
            GUI.skin = consoleSkin;
            GUI.backgroundColor = Color.red;
        
            DrawHintWindow();
            DrawConsole();
        
            GUI.skin = skin;
        }


        void OpenConsole() {
            isActive = true;
            setFocus = 1;
            consoleInputString = string.Empty;
        }

        void CloseConsole() {
            isActive = false;
            consoleInputString = string.Empty;
            selected = -1;
            GUI.FocusControl(null);
        }
        
        
        void DrawConsole() {
            float width = Screen.width;
            float height = Screen.height;
            
            /*
             * draw console input area
             */
            consoleInputDrawPos = new Vector2(WIDTH_SPACING, height - (HEIGHT_SPACING + height * SCREEN_HEIGHT_PERCENTAGE));
            consoleInputSize = new Vector2(width - WIDTH_SPACING * 2f, height * SCREEN_HEIGHT_PERCENTAGE);

            GUI.SetNextControlName(CONSOLE_INPUT_FIELD);
            Rect inputWindowRect = new (consoleInputDrawPos, consoleInputSize);
            consoleInputString = GUI.TextField(inputWindowRect, consoleInputString);


            if (setFocus > 0) {
                --setFocus;
                GUI.FocusControl(CONSOLE_INPUT_FIELD);
            }
        }


        void DrawHintWindow() {
            bool hasInputText = consoleInputString.Length > 0;
            if (hasInputText == false) {
                selected = -1;
                return;
            }

            
            
            // Search for matches
            string[] slicedInputString = consoleInputString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            List<ConsoleCommand> matchingCommands = new ();
            foreach (ConsoleCommand cmd in commands) {
                bool matchesAll = true;
                foreach (string inputSlice in slicedInputString) {
                    if (cmd.GetCommandName().Contains(inputSlice, StringComparison.OrdinalIgnoreCase) == false) {
                        matchesAll = false;
                        break;
                    }
                }
                if (matchesAll) matchingCommands.Add(cmd);
            }


            if (matchingCommands.Count == 0) {
                selected = -1;
                return;
            }
            
            
            /*
             * Inputs regarding movement inside the hint window
             */
            Event e = Event.current;

            bool updatedSelection = false;
            if (selected != -1) {
                selected = Mathf.Clamp(selected, 0, matchingCommands.Count - 1);
            }
            
            if (e.KeyDown(KeyCode.DownArrow)) {
                selected -= 1;
                selected %= matchingCommands.Count;
                updatedSelection = true;
                setFocus = 1;
            }
            else if (e.KeyDown(KeyCode.UpArrow)) {
                selected += 1;
                if (selected < 0) selected = matchingCommands.Count - 1;
                updatedSelection = true;
                setFocus = 1;
            }
            
            if (updatedSelection) {
                TextEditor text = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                text.MoveTextEnd();
                text.SelectNone();
            }
            
            float maximumWidth = 0;
            float maximumHeight = 0;
            GUIContent sizeHelper = new ();
            foreach (ConsoleCommand cmd in matchingCommands) {
                sizeHelper.text = cmd.GetCommandName();
                Vector2 size = consoleSkin.label.CalcSize(sizeHelper);
                maximumWidth = Mathf.Max(size.x, maximumWidth);
                maximumHeight += size.y + HINT_HEIGHT_TEXT_PADDING;
            }
            
            
            Rect hintBackgroundRect = new (consoleInputDrawPos - new Vector2(0, maximumHeight - 2), new Vector2(maximumWidth, maximumHeight));
            GUI.Box(hintBackgroundRect, "");
            Vector2 hintStartPos = hintBackgroundRect.position + new Vector2(0, maximumHeight);
            float stepHeight = maximumHeight / matchingCommands.Count;
            for (int i = 0; i < matchingCommands.Count; i++) {
                ConsoleCommand cmd = matchingCommands[i];
                Vector2 pos = hintStartPos - new Vector2(0, (i+1) * stepHeight);
                
                GUI.enabled = i == selected;
                GUI.Label(new Rect(pos, new Vector2(maximumWidth, stepHeight)), cmd.GetCommandName());
            }
            
            
            GUI.enabled = true;
        }



        
        
        // void Update() {
        //     
        //     if (DevInput.ToggleConsole())
        //     {
        //         isActive = !isActive;
        //         if (isActive) {
        //             ResetConsoleVariables();
        //         }
        //     }
        //
        //     
        //     if (Input.GetKeyDown(KeyCode.Space))
        //     {
        //         object target = GetComponent<TestScript>();
        //         foreach (ConsoleCommand cmd in commands)
        //         {
        //             switch (cmd.info)
        //             {
        //                 case FieldInfo info:
        //                     Debug.Log($"Calling: {info.Name} - {info} - {info.GetType()}");
        //                     object value = info.GetValue(target);
        //                     
        //                     
        //                     if (value is Transform tr) info.SetValue(target, Camera.main.transform);
        //                     else if (value is UnityEvent unityEvent) unityEvent.Invoke();
        //                     else if (value is Action action)
        //                     {
        //                         action.Invoke();
        //                     }
        //                     
        //                     break;
        //                 
        //                 case MethodInfo info:
        //                     Debug.Log($"Calling: {info.Name} - {info} - {info.GetType()}");
        //                     break;
        //                 
        //                 case PropertyInfo info: 
        //                     Debug.Log($"Calling: {info.Name} - {info} - {info.GetType()}");
        //                     break;
        //                 
        //                 case EventInfo info:
        //                     Debug.Log($"Calling: {info.Name} - {info} - {info.GetType()}");
        //                     
        //                     
        //                     // Retrieve the delegate from the EventInfo
        //                     var eventDelegate = info.GetRaiseMethod()?.Invoke(target, null) as Delegate;
        //
        //                     // Alternative way to get the delegate using reflection, if GetRaiseMethod is not available
        //                     if (eventDelegate == null)
        //                     {
        //                         var fieldInfo = target.GetType().GetField(info.Name, BindingFlags.NonPublic | BindingFlags.Instance);
        //                         eventDelegate = fieldInfo?.GetValue(target) as Delegate;
        //                     }
        //
        //                     // Invoke the event if the delegate is found
        //                     if (eventDelegate != null)
        //                     {
        //                         Type[] args = eventDelegate.Method.GetGenericArguments();
        //                         
        //                         eventDelegate.DynamicInvoke(); // Pass any necessary arguments here
        //                     }
        //                     else
        //                     {
        //                         Debug.LogWarning($"Event {info.Name} has no subscribers.");
        //                     }
        //
        //                     break;
        //             }
        //         }
        //     }
        // }


        class ConsoleCommand
        {
            internal ConsoleCommand(MethodInfo info)
            {
                this.info = info;
            }
            MethodInfo info;
            
            internal string GetCommandName()
            {
                return info.Name;
            }
        }
        
    }
}