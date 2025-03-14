using System;
using UnityEngine;

namespace Jerbo.Tools {
    
    [AttributeUsage(AttributeTargets.Method)]
    public class DevCommand : Attribute {
        public readonly string displayName;

        public DevCommand(string displayName) {
            if (string.IsNullOrEmpty(displayName) == false && displayName.Contains(' ')) {
                Debug.LogError("DevCommand -> Method aliases with space is not supported!");
            }
            this.displayName = displayName;
        }
        
        public DevCommand() {
            displayName = string.Empty;
        }
    }
}
