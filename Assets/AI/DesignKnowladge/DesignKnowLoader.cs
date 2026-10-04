using System.IO;
using UnityEngine;

namespace AI.DesignKnowledge
{
    public static class DesignKnowledgeLoader
    {
        private static string path =
            "Assets/AI/DesignKnowledge/DesignKnowledge.json";


        public static string Load()
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning(
                    "DesignKnowledge.json bulunamadý"
                );

                return "";
            }


            return File.ReadAllText(path);
        }
    }
}