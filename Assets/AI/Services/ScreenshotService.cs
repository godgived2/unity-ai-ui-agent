#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using AI.Core;

namespace AI.Services
{
    /// <summary>
    /// Unity Editor görüntü alma servisi.
    ///
    /// AI Vision sistemi için temel servis.
    ///
    /// Kullaným:
    /// - Scene görüntüsü
    /// - Editor preview
    /// - AI analiz inputu
    /// </summary>
    public class ScreenshotService
    {

        private bool initialized;


        private string screenshotFolder =
            "AI_Screenshots";



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
                "ScreenshotService Initialized"
            );

        }



        // =====================================
        // CAPTURE SCENE VIEW
        // =====================================

        public string CaptureSceneView()
        {

            string path =
                GetScreenshotPath(
                    "SceneView"
                );


            ScreenCapture.CaptureScreenshot(
                path
            );


            AssetDatabase.Refresh();


            return path;

        }



        // =====================================
        // CAPTURE GAME VIEW
        // =====================================

        public string CaptureGameView()
        {

            string path =
                GetScreenshotPath(
                    "GameView"
                );


            ScreenCapture.CaptureScreenshot(
                path
            );


            AssetDatabase.Refresh();


            return path;

        }



        // =====================================
        // CREATE PATH
        // =====================================

        private string GetScreenshotPath(
            string name)
        {

            string folder =
                Path.Combine(
                    Application.dataPath,
                    screenshotFolder
                );


            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(
                    folder
                );
            }



            string file =
                $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.png";



            return Path.Combine(
                folder,
                file
            );

        }



        // =====================================
        // DELETE SCREENSHOTS
        // =====================================

        public void ClearScreenshots()
        {

            string folder =
                Path.Combine(
                    Application.dataPath,
                    screenshotFolder
                );


            if (Directory.Exists(folder))
            {

                Directory.Delete(
                    folder,
                    true
                );


                AssetDatabase.Refresh();

            }

        }

    }

}

#endif