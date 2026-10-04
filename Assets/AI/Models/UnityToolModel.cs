using System;
using System.Collections.Generic;

namespace AI.Models
{
    [Serializable]
    public class UnityToolModel
    {
        // Tool Identity
        public string Name;
        public string DisplayName;
        public string Description;
        public string Category;

        // Source
        public string Provider;          // MCP, UnityAPI, Reflection, BuiltIn
        public string AssemblyName;
        public string ClassName;
        public string MethodName;

        // Capabilities
        public bool IsEnabled = true;
        public bool IsExperimental;
        public bool RequiresPlayMode;
        public bool RequiresSelection;
        public bool RequiresConfirmation;
        public bool SupportsUndo = true;

        // Parameters
        public List<UnityToolParameter> Parameters = new();

        // Metadata
        public List<string> Tags = new();
        public List<string> RequiredPackages = new();

        // Runtime
        public int UsageCount;
        public DateTime LastUsed;
        public double AverageExecutionTime;

        public override string ToString()
        {
            return $"{Name} ({Category})";
        }
    }

    [Serializable]
    public class UnityToolParameter
    {
        public string Name;
        public string Type;
        public string Description;

        public bool Required;
        public object DefaultValue;

        public List<string> AllowedValues = new();
    }
}