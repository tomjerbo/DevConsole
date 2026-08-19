using System;

namespace Jerbo.DevConsole {
    
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event)]
    public class DevCommand : Attribute {
        public readonly string display_name;
        public readonly bool close_after_use;
        
        public DevCommand() {
            display_name = string.Empty;
            close_after_use = true;
        }

        public DevCommand(string display_name) {
            this.display_name = display_name.Replace(" ", string.Empty);
            close_after_use = true;
        }

        public DevCommand(bool close_after_use) {
            this.close_after_use = close_after_use;
        }

        public DevCommand(string display_name, bool close_after_use) {
            this.display_name = display_name.Replace(" ", string.Empty);
            this.close_after_use = close_after_use;
        }
    }
}
