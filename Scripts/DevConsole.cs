using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

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
        const char TOGGLE_CONSOLE_KEY = '§';
        const float SCREEN_HEIGHT_PERCENTAGE = 0.05f;
        const float INPUT_BORDER_SIZE = 2f;
        const float WIDTH_SPACING = 8f;
        const float HEIGHT_SPACING = 8f;
        
        readonly List<ConsoleCommand> commands = new(100);
        readonly List<ConsoleCommand> staticCommands = new(100);
        GUISkin consoleSkin;
        string consoleInputString;
        int selected;
        bool isVisible;



        
        void Start() {
            consoleSkin = Resources.Load<GUISkin>("Dev Console Skin");
            const BindingFlags BINDING_FLAGS = BindingFlags.Default | BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.InvokeMethod;
            
            Type[] assemblyTypes = Assembly.GetExecutingAssembly().GetTypes();
            Type commandAttribute = typeof(DevCommand);
            
            
            List<MemberInfo> memberInfoCollection = new (24);
            foreach (Type loadedType in assemblyTypes)
            {
                memberInfoCollection.AddRange(loadedType.GetMethods(BINDING_FLAGS).Where(info => Attribute.IsDefined(info, commandAttribute)));

                foreach (MemberInfo info in memberInfoCollection)
                {
                    commands.Add(new ConsoleCommand(info));
                }
                memberInfoCollection.Clear();
            }
        }

        
        void DrawConsole()
        {
            if (isVisible == false && GUI.GetNameOfFocusedControl() == CONSOLE_INPUT_FIELD) {
                GUI.FocusControl(null);
                return;
            }
            float width = Screen.width;
            float height = Screen.height;

            consoleSkin.textField.fontSize = Mathf.RoundToInt(height * SCREEN_HEIGHT_PERCENTAGE - INPUT_BORDER_SIZE * 2);
            GUI.skin = consoleSkin;
            
            
            /*
             * draw console input area
             */

            Rect inputWindowRect = new (
                WIDTH_SPACING, height - (HEIGHT_SPACING + height * SCREEN_HEIGHT_PERCENTAGE), 
                width - WIDTH_SPACING * 2f, height * SCREEN_HEIGHT_PERCENTAGE);
            
            
            GUI.SetNextControlName(CONSOLE_INPUT_FIELD);
            string newInputString = GUI.TextField(inputWindowRect, consoleInputString);
            GUI.FocusControl(CONSOLE_INPUT_FIELD);
            TextEditor text = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            text.MoveTextEnd();

            
            bool hasUpdatedString = false;
            if (newInputString != consoleInputString) {
                consoleInputString = newInputString;
                hasUpdatedString = true;
            }
            bool hasInputText = consoleInputString.Length > 0;
            
            
            /*
             * handle core console inputs, on/off etc
             */
            
            // Closes console
            if (hasInputText && consoleInputString[^1] == TOGGLE_CONSOLE_KEY) {
                isVisible = false;
                return;
            }
            
            
            
            

            
            /*
             * if we are typing, show matching commands
             *
             * highlight index 0 of hints
             * press arrows to navigate
             * 
             * fuzzy search for best match
             */

            if (hasInputText) {
                selected += DevInput.NavigateVertical();
                selected = Mathf.Clamp(selected,0, commands.Count - 1);
                
                
                string possibleCommands = "";
                for (int i = commands.Count - 1; i >= 0; i--) {
                    if (selected == i) {
                        possibleCommands += "> ";
                    }
                    possibleCommands += commands[i].GetCommandInfo();
                    if (i < commands.Count - 1) possibleCommands += "\n";
                }
                
                
                
                GUIContent helpContent = new (possibleCommands);
                Vector2 size = consoleSkin.textArea.CalcSize(helpContent);
                Rect helpRect = new (inputWindowRect);
                helpRect.y -= 2 + size.y;
                helpRect.width = Mathf.Clamp(helpRect.width, 0, inputWindowRect.width);
                helpRect.size = size;
                GUI.enabled = false;
                GUI.TextArea(helpRect, possibleCommands);
                GUI.enabled = true;
                
            }
            else {
                selected = 0;
            }
            
            
            
            
        }
        
        
        
        

        void ResetConsoleVariables() {
            consoleInputString = string.Empty;
            selected = 0;
        }

        void OnGUI()
        {
            if (isVisible) DrawConsole();
        }

        void Update() {
            
            if (DevInput.ToggleConsole())
            {
                isVisible = !isVisible;
                if (isVisible) {
                    ResetConsoleVariables();
                }
            }

            
            if (Input.GetKeyDown(KeyCode.Space))
            {
                object target = GetComponent<TestScript>();
                foreach (ConsoleCommand cmd in commands)
                {
                    switch (cmd.info)
                    {
                        case FieldInfo info:
                            Debug.Log($"Calling: {info.Name} - {info} - {info.GetType()}");
                            object value = info.GetValue(target);
                            
                            
                            if (value is Transform tr) info.SetValue(target, Camera.main.transform);
                            else if (value is UnityEvent unityEvent) unityEvent.Invoke();
                            else if (value is Action action)
                            {
                                action.Invoke();
                            }
                            
                            break;
                        
                        case MethodInfo info:
                            Debug.Log($"Calling: {info.Name} - {info} - {info.GetType()}");
                            break;
                        
                        case PropertyInfo info: 
                            Debug.Log($"Calling: {info.Name} - {info} - {info.GetType()}");
                            break;
                        
                        case EventInfo info:
                            Debug.Log($"Calling: {info.Name} - {info} - {info.GetType()}");
                            
                            
                            // Retrieve the delegate from the EventInfo
                            var eventDelegate = info.GetRaiseMethod()?.Invoke(target, null) as Delegate;

                            // Alternative way to get the delegate using reflection, if GetRaiseMethod is not available
                            if (eventDelegate == null)
                            {
                                var fieldInfo = target.GetType().GetField(info.Name, BindingFlags.NonPublic | BindingFlags.Instance);
                                eventDelegate = fieldInfo?.GetValue(target) as Delegate;
                            }

                            // Invoke the event if the delegate is found
                            if (eventDelegate != null)
                            {
                                Type[] args = eventDelegate.Method.GetGenericArguments();
                                
                                eventDelegate.DynamicInvoke(); // Pass any necessary arguments here
                            }
                            else
                            {
                                Debug.LogWarning($"Event {info.Name} has no subscribers.");
                            }

                            break;
                    }
                }
            }
        }


        class ConsoleCommand
        {
            internal ConsoleCommand(MemberInfo info)
            {
                this.info = info;
            }
            internal MemberInfo info;


            internal string GetCommandInfo()
            {
                string description = $"{info.Name}";

                switch (info)
                {
                    case FieldInfo fieldInfo:
                        description += $" (Field, Type: {fieldInfo.FieldType.FullName})";
                        break;
                
                    case MethodInfo methodInfo:
                        var methodParams = methodInfo.GetParameters()
                            .Select(p => $"{p.ParameterType.FullName} {p.Name}");
                        description += $" (Method, Return Type: {methodInfo.ReturnType.FullName}, Parameters: {string.Join(", ", methodParams)})";
                        break;
                
                    case PropertyInfo propertyInfo:
                        description += $" (Property, Type: {propertyInfo.PropertyType.Name})";
                        break;
                
                    case EventInfo eventInfo:
                        var handlerType = eventInfo.EventHandlerType;
                        var invokeMethod = handlerType?.GetMethod("Invoke");
                        var eventParams = invokeMethod?.GetParameters()
                            .Select(p => $"{p.ParameterType.FullName} {p.Name}") ?? Enumerable.Empty<string>();
                        description += $" (Event, Handler Type: {handlerType?.FullName}, Parameters: {string.Join(", ", eventParams)})";
                        break;
                
                    default:
                        description += " (Unknown Member Type)";
                        break;
                }

                return description;
            }
        }
        
    }
}