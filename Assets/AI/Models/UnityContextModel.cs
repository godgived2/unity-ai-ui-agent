using System;
using System.Collections.Generic;

namespace AI.Models
{
    [Serializable]
    public class UnityContextModel
    {
        // Project
        public string ProjectName;
        public string UnityVersion;
        public string ActiveScene;
        public string Platform;

        // Assets
        public List<UnityAssetModel> Assets = new();

        // Scenes
        public List<UnitySceneModel> Scenes = new();

        // Selection
        public List<string> SelectedObjects = new();

        // Hierarchy
        public List<string> RootGameObjects = new();

        // Console
        public List<string> Errors = new();
        public List<string> Warnings = new();
        public List<string> Logs = new();

        // Packages
        public List<string> Packages = new();

        // Tool Discovery
        public List<string> Tools = new();

        // Statistics
        public int SceneCount;
        public int AssetCount;
        public int ScriptCount;
        public int PrefabCount;
        public int MaterialCount;
        public int TextureCount;
        public int AudioCount;
        public int AnimationCount;
        public int UxmlCount;
        public int UssCount;

        public DateTime ScanTime = DateTime.Now;
    }
}