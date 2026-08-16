using System;

namespace Jerbo.DevConsole {
    
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event)]
    public class DevCommand : Attribute {
        public readonly string display_name;

        public DevCommand(string displayName) {
            this.display_name = displayName.Replace(DevConsole.CHAR.SPACE, DevConsole.CHAR.EMPTY);
        }
        
        public DevCommand() {
            display_name = string.Empty;
        }
    }
}
