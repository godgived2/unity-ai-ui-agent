using System;
using System.Collections.Generic;

namespace AI.Models
{
    [Serializable]
    public class UnitySceneModel
    {
        public string Name;

        public string Path;

        public bool IsLoaded;

        public bool IsActive;

        public int RootObjectCount;

        public int TotalGameObjectCount;

        public int ComponentCount;

        public int CameraCount;

        public int LightCount;

        public int CanvasCount;

        public List<string> RootObjects = new();

        public List<string> Cameras = new();

        public List<string> Lights = new();

        public List<string> Canvases = new();

        public override string ToString()
        {
            return $"{Name} ({RootObjectCount} Roots)";
        }
    }
}