using System;

namespace Jerbo.Tools {
    
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field)]
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
