using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace Jerbo.Tools
{
    public class DevConsole : MonoBehaviour
    {
        // [RuntimeInitializeOnLoadMethod]
        // public static void Init()
        // {
        //     Instance = new DevConsole();
        // }
        //
        // static DevConsole Instance;

        [SerializeField] GUISkin consoleSkin;
        string consoleInput = "";
        bool shouldFocusTextbox;
        const string CONSOLE_INPUT_FIELD = "Console Input Field";
        
        
        List<ConsoleCommand> commands = new();
        void Start()
        {
            const BindingFlags BINDING_FLAGS = BindingFlags.Default | BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetField |
                                       BindingFlags.GetProperty | BindingFlags.InvokeMethod;
            
            Type[] assemblyTypes = Assembly.GetExecutingAssembly().GetTypes();
            Type commandAttribute = typeof(AddCommandAttribute);
            
            
            List<MemberInfo> memberInfoCollection = new (24);
            foreach (Type loadedType in assemblyTypes)
            {
                memberInfoCollection.AddRange(loadedType.GetFields(BINDING_FLAGS).Where(info => Attribute.IsDefined(info, commandAttribute)));
                memberInfoCollection.AddRange(loadedType.GetProperties(BINDING_FLAGS).Where(info => Attribute.IsDefined(info, commandAttribute)));
                memberInfoCollection.AddRange(loadedType.GetMethods(BINDING_FLAGS).Where(info => Attribute.IsDefined(info, commandAttribute)));
                memberInfoCollection.AddRange(loadedType.GetEvents(BINDING_FLAGS).Where(info => Attribute.IsDefined(info, commandAttribute)));

                foreach (MemberInfo info in memberInfoCollection)
                {
                    commands.Add(new ConsoleCommand(info));
                }
                memberInfoCollection.Clear();
            }
        }

        int selected = 0;
        
        void DrawConsole()
        {
            GUI.skin = consoleSkin;
            Rect inputWindowRect = new Rect(24, Screen.height / 2f - 16, Screen.width - 48, 32);
            bool stopEditingTextfield = DevInput.ExitConsole();
            
            
            GUI.SetNextControlName(CONSOLE_INPUT_FIELD);
            consoleInput = GUI.TextField(inputWindowRect, consoleInput);
            
            if (shouldFocusTextbox == false && consoleInput.Length > 0 && consoleInput[^1] == '§')
            {
                consoleInput = consoleInput.Remove(consoleInput.Length - 1, 1);
                isVisible = false;
                GUI.FocusControl(null);
                return;
            }

            if (shouldFocusTextbox)
            {
                shouldFocusTextbox = false;
                GUI.FocusControl(CONSOLE_INPUT_FIELD);
            }
            
            if (stopEditingTextfield && GUI.GetNameOfFocusedControl() == CONSOLE_INPUT_FIELD)
            {
                GUI.FocusControl(null);
            }


            if (consoleInput.Length > 0)
            {
                selected += DevInput.NavigateVertical();
                selected = Mathf.Clamp(selected,0, commands.Count - 1);
                
                string possibleCommands = "";
                for (int i = commands.Count - 1; i >= 0; i--)
                {
                    if (selected == i)
                    {
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
            
            
            GUI.FocusControl(CONSOLE_INPUT_FIELD);
        }
        
        
        bool isVisible;

        void OnGUI()
        {
            if (isVisible) DrawConsole();
        }

        void Update()
        {
            if (DevInput.ToggleConsole())
            {
                isVisible = !isVisible;
                shouldFocusTextbox = isVisible;
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
                string description = $"{info.Name} -> {info.GetType()}";

                // switch (info)
                // {
                //     case FieldInfo fieldInfo:
                //         description += $" (Field, Type: {fieldInfo.FieldType.FullName})";
                //         break;
                //
                //     case MethodInfo methodInfo:
                //         var methodParams = methodInfo.GetParameters()
                //             .Select(p => $"{p.ParameterType.FullName} {p.Name}");
                //         description += $" (Method, Return Type: {methodInfo.ReturnType.FullName}, Parameters: {string.Join(", ", methodParams)})";
                //         break;
                //
                //     case PropertyInfo propertyInfo:
                //         description += $" (Property, Type: {propertyInfo.PropertyType.Name})";
                //         break;
                //
                //     case EventInfo eventInfo:
                //         var handlerType = eventInfo.EventHandlerType;
                //         var invokeMethod = handlerType?.GetMethod("Invoke");
                //         var eventParams = invokeMethod?.GetParameters()
                //             .Select(p => $"{p.ParameterType.FullName} {p.Name}") ?? Enumerable.Empty<string>();
                //         description += $" (Event, Handler Type: {handlerType?.FullName}, Parameters: {string.Join(", ", eventParams)})";
                //         break;
                //
                //     default:
                //         description += " (Unknown Member Type)";
                //         break;
                // }

                return description;
            }
        }
        
    }

    public static class DevInput
    {

        
#if ENABLE_LEGACY_INPUT_MANAGER
        
        static KeyCode toggleConsoleKey = KeyCode.BackQuote;
        static KeyCode navigateUp = KeyCode.UpArrow;
        static KeyCode navigateDown = KeyCode.DownArrow;
        static KeyCode exitKey = KeyCode.Escape;
        
        public static bool ToggleConsole() {
            return Input.GetKeyDown(toggleConsoleKey);
        }

        public static int NavigateVertical() {
            if (Input.GetKeyDown(navigateUp)) {
                return 1;
            }
            if (Input.GetKeyDown(navigateDown)) {
                return -1;
            }

            return 0;
        }

        public static bool ExitConsole() {
            return Input.GetKeyDown(exitKey);
        }
#elif ENABLE_INPUT_SYSTEM
        
        static DevConsoleInputs consoleActions = new();
        
        static void ValidateInputIsEnabled() {
            if (consoleActions.DevConsole.enabled == false)
                consoleActions.DevConsole.Enable(); 
        }
        
        public static bool ToggleConsole() {
            ValidateInputIsEnabled();
            return consoleActions.DevConsole.ToggleConsole.WasPerformedThisFrame();
        }

        public static int NavigateVertical() {
            ValidateInputIsEnabled();
            if (consoleActions.DevConsole.NavigateUp.WasPerformedThisFrame()) {
                return 1;
            }
            if (consoleActions.DevConsole.NavigateDown.WasPerformedThisFrame()) {
                return -1;
            }

            return 0;
        }
        
#endif
        
    }
}

[AttributeUsage(AttributeTargets.All)]
public class AddCommandAttribute : Attribute
{
    
}