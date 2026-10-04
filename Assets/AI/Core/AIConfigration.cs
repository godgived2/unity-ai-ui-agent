using UnityEngine;

namespace AI.Core
{
    /// <summary>
    /// Local AI sisteminin bütün çalýþma ayarlarýný tutar.
    /// Model, Ollama, MCP ve Agent ayarlarý buradan yönetilir.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AIConfiguration",
        menuName = "AI/AI Configuration"
    )]
    public class AIConfiguration : ScriptableObject
    {

        // ==============================
        // MODEL SETTINGS
        // ==============================

        [Header("Local Model")]

        public string modelName =
            AIConstants.DEFAULT_MODEL;


        public float temperature = 0.1f;


        public int maxTokens = 4096;



        // ==============================
        // OLLAMA SETTINGS
        // ==============================

        [Header("Ollama")]

        public string ollamaUrl =
            AIConstants.OLLAMA_URL;



        public bool useStreaming = false;



        // ==============================
        // MCP SETTINGS
        // ==============================

        [Header("MCP")]

        public bool enableMCP = true;


        public string mcpServerName =
            AIConstants.MCP_SERVER_NAME;



        // ==============================
        // AGENT SETTINGS
        // ==============================

        [Header("Agent")]

        public bool enablePlanning = true;


        public bool enableVerification = true;


        public bool enableVision = true;



        // ==============================
        // CONTEXT SETTINGS
        // ==============================

        [Header("Context")]

        public bool includeHierarchy = true;


        public bool includeSceneData = true;


        public bool includeProjectData = true;


        public bool includeConsoleLogs = true;



        // ==============================
        // DEBUG
        // ==============================

        [Header("Debug")]

        public bool debugMode = true;



        /// <summary>
        /// Runtime ayar kontrolü
        /// </summary>
        public void Validate()
        {

            if (string.IsNullOrEmpty(modelName))
            {
                modelName =
                    AIConstants.DEFAULT_MODEL;
            }


            if (string.IsNullOrEmpty(ollamaUrl))
            {
                ollamaUrl =
                    AIConstants.OLLAMA_URL;
            }


            if (maxTokens <= 0)
            {
                maxTokens = 4096;
            }

        }



        public string GetModelInfo()
        {
            return
                $"Model: {modelName}\n" +
                $"Temperature: {temperature}\n" +
                $"Max Tokens: {maxTokens}";
        }

    }
}