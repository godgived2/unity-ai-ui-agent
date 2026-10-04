using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AI.Core;
using MCPForUnity.Editor.Services;

namespace AI.Services
{
    /// <summary>
    /// Unity MCP tool keþif servisi.
    ///
    /// MCP tarafýndan kayýtlý tüm tool'larý
    /// AI sistemine aktarýr.
    /// </summary>
    public class UnityToolDiscovery
    {

        private bool initialized;


        private readonly List<UnityToolInfo> tools =
            new List<UnityToolInfo>();



        // =====================================
        // INITIALIZE
        // =====================================

        public void Initialize()
        {

            if (initialized)
                return;


            AIServiceLocator.Register(this);


            initialized = true;


            AIConstants.Log(
                "UnityToolDiscovery Initialized"
            );

        }



        // =====================================
        // DISCOVER TOOLS
        // =====================================

        public List<UnityToolInfo> DiscoverTools(
            bool refresh = false)
        {

            if (tools.Count > 0 && !refresh)
                return tools;


            tools.Clear();



            try
            {

                var discovered =
                    MCPServiceLocator
                    .ToolDiscovery
                    .DiscoverAllTools();



                if (discovered == null)
                {
                    Debug.LogWarning(
                        "MCP returned no tools"
                    );

                    return tools;
                }



                foreach (var tool in discovered)
                {

                    if (tool == null)
                        continue;



                    UnityToolInfo info =
                        new UnityToolInfo();


                    info.Name =
                        tool.Name;


                    info.Description =
                        string.IsNullOrEmpty(tool.Description)
                        ?
                        "Unity MCP Tool"
                        :
                        tool.Description;



                    info.Parameters =
                        tool.Parameters?
                        .Select(x => x.Name)
                        .ToList()
                        ??
                        new List<string>();



                    tools.Add(info);

                }


            }
            catch (Exception e)
            {

                Debug.LogError(
                    "UnityToolDiscovery Error : "
                    + e.Message
                );

            }



            return tools;

        }



        // =====================================
        // FIND TOOL
        // =====================================

        public UnityToolInfo GetTool(
            string name)
        {

            return tools
                .FirstOrDefault(
                    x =>
                    x.Name == name
                );

        }



        // =====================================
        // SEARCH
        // =====================================

        public List<UnityToolInfo> Search(
            string keyword)
        {

            if (string.IsNullOrEmpty(keyword))
                return new List<UnityToolInfo>();


            keyword =
                keyword.ToLower();



            return tools
                .Where(x =>
                    x.Name
                    .ToLower()
                    .Contains(keyword)

                    ||

                    x.Description
                    .ToLower()
                    .Contains(keyword)
                )
                .ToList();

        }



        // =====================================
        // CLEAR CACHE
        // =====================================

        public void Clear()
        {

            tools.Clear();

        }



        public int ToolCount =>
            tools.Count;

    }



    // =====================================
    // MODEL
    // =====================================

    [Serializable]
    public class UnityToolInfo
    {

        public string Name;

        public string Description;


        public List<string> Parameters =
            new List<string>();

    }

}