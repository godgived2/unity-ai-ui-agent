using System;
using System.Collections.Generic;
using System.Text;

namespace AI.Context
{
    /// <summary>
    /// Unity MCP Tool bilgilerini temsil eder.
    ///
    /// ÖNEMLÝ KAPSAM KURALI: modele sunulan resmî tool þemasý MCPToolDiscovery
    /// (BuildAgentSchemaFromList) tarafýndan üretilir. Bu sýnýf o þemanýn YERÝNE geçmez;
    /// editör tarafýnda hýzlý arama/lookup içindir.
    ///
    /// Bu yüzden GetSummary() bilerek ucuz tutulmuþtur (yalnýzca sayý): ToolContext'in
    /// tam listesi de prompt'a girerse ayný araç envanteri iki kez gider ve zaten dar
    /// olan baðlam penceresi boþa harcanýr. Ayrýntýlý liste isteniyorsa Render() açýkça
    /// çaðrýlmalýdýr.
    /// </summary>
    [Serializable]
    public class ToolContext
    {
        // ==============================
        // TOOL COUNT
        // ==============================

        public int ToolCount;

        // ==============================
        // TOOLS
        // ==============================

        public List<UnityToolContextInfo> Tools = new List<UnityToolContextInfo>();

        [NonSerialized]
        private Dictionary<string, UnityToolContextInfo> _byName;

        // ==============================
        // ADD TOOL
        // ==============================

        public void AddTool(UnityToolContextInfo tool)
        {
            if (tool == null || string.IsNullOrWhiteSpace(tool.Name))
                return;

            EnsureIndex();

            // Dedupe: ContextBuilder her Build() cagrisinda tool kesfini tekrarliyor.
            // Ayni ToolContext yeniden kullanildiginda liste eskiden katlanarak buyuyordu.
            if (_byName.ContainsKey(tool.Name))
                return;

            Tools.Add(tool);
            _byName[tool.Name] = tool;
            ToolCount = Tools.Count;
        }

        public void AddRange(IEnumerable<UnityToolContextInfo> tools)
        {
            if (tools == null)
                return;

            foreach (UnityToolContextInfo tool in tools)
                AddTool(tool);
        }

        private void EnsureIndex()
        {
            if (_byName != null)
                return;

            _byName = new Dictionary<string, UnityToolContextInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (UnityToolContextInfo tool in Tools)
            {
                if (tool != null && !string.IsNullOrWhiteSpace(tool.Name))
                    _byName[tool.Name] = tool;
            }
        }

        // ==============================
        // FIND TOOL
        // ==============================

        /// <summary>
        /// Ada göre tool bulur. Eski hâlde '==' ile büyük/küçük harf duyarlý karþýlaþtýrma
        /// yapýlýyordu; "execute_code" ile "Execute_Code" farklý sayýlýyor ve model
        /// adý biraz farklý yazdýðýnda tool bulunamýyordu.
        /// </summary>
        public UnityToolContextInfo FindTool(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            EnsureIndex();
            return _byName.TryGetValue(name, out UnityToolContextInfo tool) ? tool : null;
        }

        public bool HasTool(string name)
        {
            return FindTool(name) != null;
        }

        // ==============================
        // SEARCH
        // ==============================

        /// <summary>
        /// Ad, açýklama veya yetenek metinlerinde arar.
        ///
        /// ToLower() yerine kültürden baðýmsýz karþýlaþtýrma: Türkçe kültürde
        /// "UI".ToLower() -> "uý" olduðu için arama sessizce boþ dönüyordu.
        /// Ayrýca Description null olan tool'larda NullReferenceException atýyordu.
        /// </summary>
        public List<UnityToolContextInfo> Search(string keyword, int max = 25)
        {
            var result = new List<UnityToolContextInfo>();

            if (string.IsNullOrWhiteSpace(keyword) || max <= 0)
                return result;

            foreach (UnityToolContextInfo tool in Tools)
            {
                if (tool == null)
                    continue;

                bool match =
                    ContextFormat.ContainsIgnoreCase(tool.Name, keyword) ||
                    ContextFormat.ContainsIgnoreCase(tool.Description, keyword) ||
                    ContextFormat.ContainsIgnoreCase(tool.Category, keyword);

                if (!match && tool.Capabilities != null)
                {
                    foreach (string capability in tool.Capabilities)
                    {
                        if (ContextFormat.ContainsIgnoreCase(capability, keyword))
                        {
                            match = true;
                            break;
                        }
                    }
                }

                if (!match)
                    continue;

                result.Add(tool);

                if (result.Count >= max)
                    break;
            }

            return result;
        }

        public List<UnityToolContextInfo> ByCategory(string category, int max = 25)
        {
            var result = new List<UnityToolContextInfo>();

            if (string.IsNullOrWhiteSpace(category) || max <= 0)
                return result;

            foreach (UnityToolContextInfo tool in Tools)
            {
                if (tool == null)
                    continue;

                if (string.Equals(tool.Category, category, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(tool);

                    if (result.Count >= max)
                        break;
                }
            }

            return result;
        }

        // ==============================
        // CLEAR
        // ==============================

        public void Clear()
        {
            Tools.Clear();
            EnsureIndex();
            _byName.Clear();
            ToolCount = 0;
        }

        // ==============================
        // RENDER
        // ==============================

        /// <summary>
        /// Bilerek ucuz: yalnýzca sayý. Resmî tool þemasý MCPToolDiscovery'den gelir,
        /// bu satýr onunla yarýþmaz. Ayrýntý için Render() çaðýrýn.
        /// </summary>
        public string GetSummary()
        {
            return "Available tools: " + ToolCount;
        }

        /// <summary>
        /// Kompakt tool listesi. YALNIZCA MCPToolDiscovery þemasý prompt'a girmiyorsa
        /// kullanýn - aksi hâlde ayný bilgi iki kez gider.
        /// </summary>
        public string Render(int maxTools = 20, int charBudget = 0, bool includeDescriptions = true)
        {
            if (ToolCount == 0)
                return "Available tools: yok (tool kesfi calismadi mi?).";

            var sb = new StringBuilder();
            sb.Append("Available tools (").Append(ToolCount).AppendLine("):");

            int shown = 0;

            foreach (UnityToolContextInfo tool in Tools)
            {
                if (tool == null)
                    continue;

                if (shown >= maxTools)
                    break;

                sb.Append("  ").Append(tool.Name);

                if (tool.Parameters != null && tool.Parameters.Count > 0)
                    sb.Append('(').Append(ContextFormat.JoinLimited(tool.Parameters, 6)).Append(')');

                if (includeDescriptions && !string.IsNullOrWhiteSpace(tool.Description))
                    sb.Append(" - ").Append(ContextFormat.Truncate(tool.Description.Trim(), 100));

                sb.AppendLine();
                shown++;
            }

            if (ToolCount > shown)
                sb.Append("  ... (+").Append(ToolCount - shown).AppendLine(" tool daha)");

            return ContextFormat.Truncate(sb.ToString().TrimEnd(), charBudget);
        }

        public override string ToString()
        {
            return GetSummary();
        }
    }

    // =====================================
    // TOOL MODEL
    // =====================================

    [Serializable]
    public class UnityToolContextInfo
    {
        public string Name = string.Empty;
        public string Description = string.Empty;
        public string Category = string.Empty;

        public List<string> Parameters = new List<string>();
        public List<string> Capabilities = new List<string>();

        public override string ToString()
        {
            return Parameters != null && Parameters.Count > 0
                ? Name + "(" + string.Join(", ", Parameters) + ")"
                : Name;
        }
    }
}