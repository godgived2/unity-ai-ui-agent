using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using AI.Core;

namespace AI.Services
{
    /// <summary>
    /// Unity projesini tarayan ana servis.
    ///
    /// AI'ýn proje hakkýnda bilgi sahibi olmasýný saðlar.
    ///
    /// Toplanan bilgiler:
    /// - Assets
    /// - Scripts
    /// - Scenes
    /// - Prefabs
    /// - Packages
    /// - Folders
    /// </summary>
    public class UnityProjectScanner
    {

        private bool initialized;


        private readonly List<string> scripts =
            new List<string>();


        private readonly List<string> scenes =
            new List<string>();


        private readonly List<string> prefabs =
            new List<string>();


        private readonly List<string> assets =
            new List<string>();


        private readonly List<string> folders =
            new List<string>();



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
                "UnityProjectScanner Initialized"
            );

        }



        // =====================================
        // FULL SCAN
        // =====================================

        public ProjectScanResult ScanProject()
        {

            Clear();


            ScanFolders();

            ScanAssets();

            ScanScripts();

            ScanScenes();

            ScanPrefabs();



            return new ProjectScanResult
            {

                Scripts = new List<string>(scripts),

                Scenes = new List<string>(scenes),

                Prefabs = new List<string>(prefabs),

                Assets = new List<string>(assets),

                Folders = new List<string>(folders)

            };

        }



        // =====================================
        // FOLDERS
        // =====================================

        private void ScanFolders()
        {

            string[] paths =
                AssetDatabase
                .GetAllAssetPaths();


            foreach (string path in paths)
            {

                if (AssetDatabase.IsValidFolder(path))
                {
                    folders.Add(path);
                }

            }

        }



        // =====================================
        // ASSETS
        // =====================================

        private void ScanAssets()
        {

            string[] paths =
                AssetDatabase
                .GetAllAssetPaths();


            foreach (string path in paths)
            {

                if (path.StartsWith("Assets"))
                {
                    assets.Add(path);
                }

            }

        }



        // =====================================
        // SCRIPTS
        // =====================================

        private void ScanScripts()
        {

            string[] guids =
                AssetDatabase
                .FindAssets("t:MonoScript");


            foreach (string guid in guids)
            {

                string path =
                    AssetDatabase
                    .GUIDToAssetPath(guid);


                scripts.Add(path);

            }

        }



        // =====================================
        // SCENES
        // =====================================

        private void ScanScenes()
        {

            string[] guids =
                AssetDatabase
                .FindAssets("t:Scene");


            foreach (string guid in guids)
            {

                string path =
                    AssetDatabase
                    .GUIDToAssetPath(guid);


                scenes.Add(path);

            }

        }



        // =====================================
        // PREFABS
        // =====================================

        private void ScanPrefabs()
        {

            string[] guids =
                AssetDatabase
                .FindAssets("t:Prefab");


            foreach (string guid in guids)
            {

                string path =
                    AssetDatabase
                    .GUIDToAssetPath(guid);


                prefabs.Add(path);

            }

        }



        // =====================================
        // CLEAR
        // =====================================

        public void Clear()
        {

            scripts.Clear();

            scenes.Clear();

            prefabs.Clear();

            assets.Clear();

            folders.Clear();

        }



        // =====================================
        // QUICK INFO
        // =====================================

        public string GetSummary()
        {

            return
                $"Assets : {assets.Count}\n" +
                $"Scripts : {scripts.Count}\n" +
                $"Scenes : {scenes.Count}\n" +
                $"Prefabs : {prefabs.Count}\n" +
                $"Folders : {folders.Count}";

        }

    }



    // =====================================
    // MODEL
    // =====================================

    [Serializable]
    public class ProjectScanResult
    {

        public List<string> Assets;

        public List<string> Scripts;

        public List<string> Scenes;

        public List<string> Prefabs;

        public List<string> Folders;

    }

}