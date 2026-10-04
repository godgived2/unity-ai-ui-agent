using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AI.Discovery
{
    /// <summary>
    /// Central, thread-safe registry of every discovered MCP tool.
    ///
    /// MCPDiscovery finds tools, ToolRegistry stores them, ToolSelector and
    /// PromptBuilder read from here.
    ///
    /// Backed by <see cref="ConcurrentDictionary{TKey,TValue}"/> rather than a plain
    /// Dictionary so that registration (which can happen on assembly reload, a
    /// background discovery scan, or a live MCP handshake) can never race with a
    /// read from the prompt-building or tool-selection path. <see cref="Tools"/>
    /// returns a point-in-time snapshot, so callers enumerating it are never
    /// affected by a concurrent Register/Unregister.
    /// </summary>
    public static class ToolRegistry
    {
        private static readonly ConcurrentDictionary<string, ToolMetadata> _tools =
            new ConcurrentDictionary<string, ToolMetadata>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Optional diagnostic sink (duplicate registrations, subscriber failures).
        /// No-op by default so this assembly stays free of any logging dependency;
        /// wire it to Debug.Log / a file / whatever the host wants.
        /// </summary>
        public static Action<string> Logger { get; set; }

        /// <summary>Fired after a tool is newly added or an existing one is overwritten.</summary>
        public static event Action<ToolMetadata> ToolRegistered;

        /// <summary>Fired after a tool is removed.</summary>
        public static event Action<string> ToolUnregistered;

        /// <summary>Fired after any mutation (register, unregister, clear) — convenient for cache invalidation.</summary>
        public static event Action RegistryChanged;

        // =====================================
        // TOOLS
        // =====================================

        /// <summary>A point-in-time snapshot of all registered tools.</summary>
        public static IReadOnlyCollection<ToolMetadata> Tools => _tools.Values.ToArray();

        public static int Count => _tools.Count;

        // =====================================
        // AVAILABLE TOOLS
        // =====================================

        public static IList<ToolMetadata> GetAvailableTools()
        {
            return _tools.Values.ToList();
        }

        // =====================================
        // REGISTER
        // =====================================

        /// <summary>
        /// Registers a tool, overwriting any existing entry with the same name
        /// (case-insensitive). Overwrites are logged via <see cref="Logger"/> if set.
        /// </summary>
        public static void Register(ToolMetadata tool)
        {
            if (tool == null || string.IsNullOrWhiteSpace(tool.Name))
                return;

            bool wasUpdate = false;

            _tools.AddOrUpdate(
                tool.Name,
                addValueFactory: _ => tool,
                updateValueFactory: (_, __) =>
                {
                    wasUpdate = true;
                    return tool;
                });

            if (wasUpdate)
                Logger?.Invoke($"[ToolRegistry] '{tool.Name}' was already registered — overwriting with new definition.");

            RaiseToolRegistered(tool);
            RaiseRegistryChanged();
        }

        /// <summary>
        /// Registers a tool only if no tool with that name already exists.
        /// Use this instead of <see cref="Register"/> when a silent overwrite would
        /// be a bug rather than expected behavior (e.g. two discovery sources
        /// racing to register the same name).
        /// </summary>
        /// <returns>True if the tool was newly added, false if a tool with that name already existed.</returns>
        public static bool TryRegisterNew(ToolMetadata tool)
        {
            if (tool == null || string.IsNullOrWhiteSpace(tool.Name))
                return false;

            bool added = _tools.TryAdd(tool.Name, tool);
            if (added)
            {
                RaiseToolRegistered(tool);
                RaiseRegistryChanged();
            }
            else
            {
                Logger?.Invoke($"[ToolRegistry] TryRegisterNew('{tool.Name}') skipped — already registered.");
            }

            return added;
        }

        public static void RegisterRange(IEnumerable<ToolMetadata> tools)
        {
            if (tools == null)
                return;

            foreach (var tool in tools)
            {
                Register(tool);
            }
        }

        // =====================================
        // REMOVE
        // =====================================

        public static bool Unregister(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            bool removed = _tools.TryRemove(toolName, out _);
            if (removed)
            {
                RaiseToolUnregistered(toolName);
                RaiseRegistryChanged();
            }

            return removed;
        }

        public static void Clear()
        {
            if (_tools.IsEmpty)
                return;

            _tools.Clear();
            RaiseRegistryChanged();
        }

        // =====================================
        // GET
        // =====================================

        public static ToolMetadata Get(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return null;

            _tools.TryGetValue(toolName, out var tool);
            return tool;
        }

        public static bool Contains(string toolName)
        {
            return !string.IsNullOrWhiteSpace(toolName) && _tools.ContainsKey(toolName);
        }

        // =====================================
        // CATEGORY
        // =====================================

        public static List<ToolMetadata> GetByCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return new List<ToolMetadata>();

            return _tools.Values
                .Where(tool => !string.IsNullOrEmpty(tool.Category) &&
                               tool.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // =====================================
        // ENABLED TOOLS
        // =====================================

        public static List<ToolMetadata> GetEnabled()
        {
            return _tools.Values.Where(tool => tool.Enabled).ToList();
        }

        // =====================================
        // SEARCH
        // =====================================

        public static List<ToolMetadata> Search(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new List<ToolMetadata>();

            string keywordLower = keyword.ToLowerInvariant();

            return _tools.Values
                .Where(tool =>
                    FieldContains(tool.Name, keywordLower) ||
                    FieldContains(tool.Description, keywordLower) ||
                    FieldContains(tool.Category, keywordLower) ||
                    AnyContains(tool.Keywords, keywordLower) ||
                    AnyContains(tool.Capabilities, keywordLower))
                .ToList();
        }

        private static bool FieldContains(string field, string keywordLower)
        {
            return !string.IsNullOrEmpty(field) && field.ToLowerInvariant().Contains(keywordLower);
        }

        private static bool AnyContains(IEnumerable<string> values, string keywordLower)
        {
            return values != null && values.Any(v => !string.IsNullOrEmpty(v) && v.ToLowerInvariant().Contains(keywordLower));
        }

        // =====================================
        // DEBUG
        // =====================================

        public static string GetSummary()
        {
            int enabledCount = _tools.Values.Count(t => t.Enabled);
            return $"Registered MCP Tools: {_tools.Count} ({enabledCount} enabled, {_tools.Count - enabledCount} disabled)";
        }

        // =====================================
        // EVENTS — isolated so one bad subscriber can't break registration
        // =====================================

        private static void RaiseToolRegistered(ToolMetadata tool)
        {
            try { ToolRegistered?.Invoke(tool); }
            catch (Exception ex) { Logger?.Invoke($"[ToolRegistry] ToolRegistered subscriber threw: {ex.Message}"); }
        }

        private static void RaiseToolUnregistered(string toolName)
        {
            try { ToolUnregistered?.Invoke(toolName); }
            catch (Exception ex) { Logger?.Invoke($"[ToolRegistry] ToolUnregistered subscriber threw: {ex.Message}"); }
        }

        private static void RaiseRegistryChanged()
        {
            try { RegistryChanged?.Invoke(); }
            catch (Exception ex) { Logger?.Invoke($"[ToolRegistry] RegistryChanged subscriber threw: {ex.Message}"); }
        }
    }
}