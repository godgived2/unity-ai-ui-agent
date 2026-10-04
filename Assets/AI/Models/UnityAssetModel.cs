using System;
using UnityEngine;

namespace AI.Models
{
    [Serializable]
    public class UnityAssetModel
    {
        public string Guid;

        public string Name;

        public string Path;

        public string Extension;

        public string AssetType;

        public long FileSize;

        public DateTime LastWriteTime;

        public bool IsUnderResources;

        public bool IsUnderStreamingAssets;

        public bool IsEditorOnly;

        public bool IsAddressable;

        public bool Exists;

        public override string ToString()
        {
            return $"{AssetType} : {Name}";
        }
    }
}