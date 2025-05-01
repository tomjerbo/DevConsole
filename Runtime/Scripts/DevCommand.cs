using System;

namespace Jerbo.DevConsole {
    
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Event)]
    public class DevCommand : Attribute {
        public readonly string displayName;

        public DevCommand(string displayName) {
            this.displayName = displayName;
        }
        
        public DevCommand() {
            displayName = string.Empty;
        }
    }
}
