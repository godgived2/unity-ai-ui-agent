using System;
using System.Collections.Generic;
using System.Linq;

namespace AI.Memory
{
    /// <summary>
    /// MCP Tool kullaným geçmiþi.
    ///
    /// Agent tarafýndan çalýþtýrýlan
    /// tool çaðrýlarýný kayýt eder.
    /// </summary>
    public class ToolHistory
    {


        private readonly List<ToolHistoryEntry> _entries =
            new List<ToolHistoryEntry>();





        // =====================================
        // ADD ENTRY
        // =====================================

        public void Add(
            string toolName,
            string parameters,
            string result,
            bool success)
        {

            if (string.IsNullOrWhiteSpace(toolName))
                return;



            _entries.Add(
                new ToolHistoryEntry
                {

                    ToolName = toolName,

                    Parameters = parameters,

                    Result = result,

                    Success = success,

                    Time = DateTime.Now

                }
            );

        }





        // =====================================
        // GET ALL
        // =====================================

        public List<ToolHistoryEntry> GetAll()
        {

            return
                new List<ToolHistoryEntry>(
                    _entries
                );

        }





        // =====================================
        // LAST CALLS
        // =====================================

        public List<ToolHistoryEntry> GetLast(
            int count)
        {

            if (count <= 0)
                return new List<ToolHistoryEntry>();



            return _entries
                .TakeLast(count)
                .ToList();

        }





        // =====================================
        // SEARCH TOOL
        // =====================================

        public List<ToolHistoryEntry> Search(
            string toolName)
        {

            if (string.IsNullOrWhiteSpace(toolName))
                return new List<ToolHistoryEntry>();



            return _entries
                .Where(x =>
                    x.ToolName.Equals(
                        toolName,
                        StringComparison.OrdinalIgnoreCase
                    ))
                .ToList();

        }





        // =====================================
        // SUCCESS RATE
        // =====================================

        public float GetSuccessRate(
            string toolName)
        {

            var list =
                Search(toolName);



            if (list.Count == 0)
                return 0;



            int success =
                list.Count(
                    x => x.Success
                );



            return
                (float)success /
                list.Count;

        }





        // =====================================
        // CLEAR
        // =====================================

        public void Clear()
        {

            _entries.Clear();

        }




        public int Count =>
            _entries.Count;


    }





    // =====================================
    // TOOL HISTORY MODEL
    // =====================================

    [Serializable]
    public class ToolHistoryEntry
    {

        public string ToolName;


        public string Parameters;


        public string Result;


        public bool Success;


        public DateTime Time;

    }

}