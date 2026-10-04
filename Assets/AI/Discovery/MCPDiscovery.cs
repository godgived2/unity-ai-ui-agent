using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MCPForUnity.Editor.Services;

namespace AI.Discovery
{
    /// <summary>
    /// MCP Tool keþif sistemi.
    ///
    /// Unity MCP server içerisindeki
    /// mevcut tool'larý bulur.
    /// </summary>
    public class MCPDiscovery
    {

        private readonly List<ToolMetadata> tools =
            new List<ToolMetadata>();


        private bool discovered;



        // =====================================
        // DISCOVER
        // =====================================

        public List<ToolMetadata> Discover(
            bool refresh = false)
        {

            if (discovered && !refresh)
                return tools;



            tools.Clear();



            try
            {

                var result =
                    MCPServiceLocator
                    .ToolDiscovery
                    .DiscoverAllTools();



                if (result == null)
                {

                    Debug.LogWarning(
                        "MCP Tool discovery returned empty"
                    );


                    return tools;

                }



                foreach (var tool in result)
                {

                    if (tool == null)
                        continue;



                    ToolMetadata metadata =
                        new ToolMetadata();


                    metadata.Name =
                        tool.Name;


                    metadata.Description =
                        string.IsNullOrEmpty(tool.Description)
                        ?
                        "Unity MCP Tool"
                        :
                        tool.Description;



                    metadata.Category =
                        DetectCategory(
                            tool.Name
                        );



                    if (tool.Parameters != null)
                    {

                        foreach (var parameter in tool.Parameters)
                        {

                            metadata.Parameters
                            .Add(
                                parameter.Name
                            );

                        }

                    }



                    tools.Add(
                        metadata
                    );

                }


                discovered = true;


                Debug.Log(
                    "MCP Tools Found : "
                    + tools.Count
                );

            }
            catch (Exception e)
            {

                Debug.LogError(
                    "MCP Discovery Error : "
                    + e.Message
                );

            }



            return tools;

        }



        // =====================================
        // CATEGORY
        // =====================================

        private string DetectCategory(
            string name)
        {

            string n =
                name.ToLower();



            if (n.Contains("ui"))
                return "UI";


            if (n.Contains("gameobject"))
                return "Scene";


            if (n.Contains("script"))
                return "Code";


            if (n.Contains("asset"))
                return "Asset";


            return "General";

        }



        // =====================================
        // CLEAR
        // =====================================

        public void Clear()
        {

            tools.Clear();

            discovered = false;

        }

    }

}