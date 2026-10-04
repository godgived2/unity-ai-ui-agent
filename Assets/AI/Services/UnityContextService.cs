using System;
using UnityEngine;
using AI.Core;

namespace AI.Services
{
    /// <summary>
    /// Unity içerisindeki genel çalýþma context bilgisini yönetir.
    /// 
    /// Agent'ýn Unity hakkýnda bilgi alacaðý ana servislerden biridir.
    /// 
    /// Saðladýðý bilgiler:
    /// - Aktif scene
    /// - Seçili obje
    /// - Unity versiyonu
    /// - Platform
    /// - Editor durumu
    /// - Runtime bilgileri
    /// </summary>
    public class UnityContextService
    {


        private bool initialized;



        // ==============================
        // INITIALIZE
        // ==============================

        public void Initialize()
        {

            if (initialized)
                return;


            AIServiceLocator.Register(
                this
            );


            initialized = true;


            AIConstants.Log(
                "UnityContextService Initialized"
            );

        }



        // ==============================
        // UNITY INFO
        // ==============================

        public UnityContextData GetContext()
        {

            UnityContextData data =
                new UnityContextData();



            data.UnityVersion =
                Application.unityVersion;



            data.Platform =
                Application.platform
                .ToString();



            data.ProductName =
                Application.productName;



            data.IsEditor =
#if UNITY_EDITOR
                true;
#else
                false;
#endif



            data.Time =
                DateTime.Now.ToString();



            return data;

        }



        // ==============================
        // ACTIVE SCENE
        // ==============================

        public string GetActiveScene()
        {

#if UNITY_EDITOR

            return UnityEditor.SceneManagement
                .EditorSceneManager
                .GetActiveScene()
                .name;

#else

            return
                UnityEngine.SceneManagement
                .SceneManager
                .GetActiveScene()
                .name;

#endif

        }



        // ==============================
        // DEBUG
        // ==============================

        public void PrintContext()
        {

            UnityContextData data =
                GetContext();


            Debug.Log(
                JsonUtility.ToJson(
                    data,
                    true
                )
            );

        }

    }



    // =================================
    // DATA MODEL
    // =================================

    [Serializable]
    public class UnityContextData
    {

        public string UnityVersion;

        public string ProductName;

        public string Platform;

        public bool IsEditor;

        public string Time;


        public string ActiveScene;

    }

}