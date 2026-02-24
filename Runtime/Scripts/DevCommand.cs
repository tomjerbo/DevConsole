using System;

namespace Jerbo.DevConsole {
    
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Event)]
    public class DevCommand : Attribute {
        public readonly string displayName;

        public DevCommand(string displayName) {
            this.displayName = displayName.Replace(DevConsole.CHAR.SPACE, DevConsole.CHAR.EMPTY);
        }
        
        public DevCommand() {
            displayName = string.Empty;
        }
    }
}
