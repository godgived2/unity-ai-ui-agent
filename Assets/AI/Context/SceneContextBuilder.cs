using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace AI.Context
{
    /// <summary>
    /// Sahne yakalama sýnýrlarý.
    /// </summary>
    [Serializable]
    public class SceneCaptureOptions
    {
        /// <summary>
        /// SceneContext.RootObjects listesine alýnacak kök obje sayýsý tavaný.
        ///
        /// Bilerek düþük: bu bilgi HierarchyContext ile çakýþýyor. SceneContext ucuz bir
        /// baþlýk, aðacýn kendisi Hierarchy'nin iþi.
        /// </summary>
        public int MaxRootObjects = 15;

        /// <summary>Canvas / EventSystem varlýðý taransýn mý.</summary>
        public bool DetectUiRig = true;

        /// <summary>Additive yüklü diðer sahneler raporlansýn mý.</summary>
        public bool IncludeAdditionalScenes = true;

        public static SceneCaptureOptions Default => new SceneCaptureOptions();
    }

    /// <summary>
    /// Aktif sahnenin durumunu güvenli biçimde okur.
    ///
    /// Not: önceki turdaki SceneSnapshot ara sýnýfý KALDIRILDI. Alanlarý artýk
    /// SceneContext'in kendisi taþýyor, dolayýsýyla ContextBuilder doðrudan
    /// context.Scene = SceneContextBuilder.Build() diyebilir.
    /// </summary>
    public static class SceneContextBuilder
    {
        /// <summary>Geriye dönük uyumlu giriþ noktasý.</summary>
        public static SceneContext Build()
        {
            return Build(SceneCaptureOptions.Default);
        }

        public static SceneContext Build(SceneCaptureOptions options)
        {
            options = options ?? SceneCaptureOptions.Default;

            var context = new SceneContext();
            Scene scene;

            try
            {
                scene = SceneManager.GetActiveScene();
            }
            catch (Exception ex)
            {
                context.InvalidReason = ex.Message;
                return context;
            }

            if (!scene.IsValid())
            {
                context.InvalidReason = "aktif sahne gecersiz";
                return context;
            }

            context.SceneName = scene.name;
            context.ScenePath = scene.path;
            context.BuildIndex = scene.buildIndex;
            context.IsLoaded = scene.isLoaded;
            context.IsDirty = scene.isDirty;
            context.IsValid = true;

            DetectPrefabStage(context);

            // KRITIK: GetRootGameObjects() yuklenmemis sahnede ArgumentException atar.
            // Eski kod isLoaded'i context'e yaziyor ama kosula HIC bakmadan devam
            // ediyordu; domain reload sirasinda ve sahne gecisinde cokme sebebi buydu.
            if (!scene.isLoaded)
            {
                context.InvalidReason = "sahne henuz yuklenmedi";
                return context;
            }

            GameObject[] roots;

            try
            {
                roots = scene.GetRootGameObjects();
            }
            catch (Exception ex)
            {
                context.InvalidReason = "kok objeler okunamadi: " + ex.Message;
                return context;
            }

            context.RootObjectCount = roots.Length;

            int limit = Mathf.Max(0, options.MaxRootObjects);

            foreach (GameObject root in roots)
            {
                if (root == null)
                    continue;

                if (context.RootObjects.Count >= limit)
                    continue;

                context.RootObjects.Add(root.name);
            }

            context.HiddenRootObjectCount =
                Mathf.Max(0, context.RootObjectCount - context.RootObjects.Count);

            if (options.DetectUiRig)
                DetectUiRig(context, roots);

            if (options.IncludeAdditionalScenes)
                CollectAdditionalScenes(context, scene);

            return context;
        }

        /// <summary>
        /// Yüklü tüm sahneleri ayrý ayrý döndürür (multi-scene kurulumlarý için).
        /// </summary>
        public static List<SceneContext> BuildAllLoadedScenes(SceneCaptureOptions options = null)
        {
            options = options ?? SceneCaptureOptions.Default;

            var result = new List<SceneContext>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene;

                try
                {
                    scene = SceneManager.GetSceneAt(i);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!scene.IsValid())
                    continue;

                var context = new SceneContext
                {
                    SceneName = scene.name,
                    ScenePath = scene.path,
                    BuildIndex = scene.buildIndex,
                    IsLoaded = scene.isLoaded,
                    IsDirty = scene.isDirty,
                    IsValid = true
                };

                if (scene.isLoaded)
                {
                    try
                    {
                        GameObject[] roots = scene.GetRootGameObjects();
                        context.RootObjectCount = roots.Length;

                        int limit = Mathf.Max(0, options.MaxRootObjects);

                        foreach (GameObject root in roots)
                        {
                            if (root == null || context.RootObjects.Count >= limit)
                                continue;

                            context.RootObjects.Add(root.name);
                        }

                        context.HiddenRootObjectCount =
                            Mathf.Max(0, context.RootObjectCount - context.RootObjects.Count);
                    }
                    catch (Exception ex)
                    {
                        context.InvalidReason = "kok objeler okunamadi: " + ex.Message;
                    }
                }

                result.Add(context);
            }

            return result;
        }

        // ==============================
        // DETECTION HELPERS
        // ==============================

        private static void DetectPrefabStage(SceneContext context)
        {
#if UNITY_EDITOR
            try
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();

                if (stage == null)
                    return;

                context.InPrefabStage = true;
                context.PrefabAssetPath = stage.assetPath ?? string.Empty;
                context.PrefabRootName = stage.prefabContentsRoot != null
                    ? stage.prefabContentsRoot.name
                    : string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Prefab stage okunamadi: " + ex.Message);
            }
#endif
        }

        /// <summary>
        /// Canvas / EventSystem varlýðýný kök aðaçlarýnda arar.
        ///
        /// FindObjectsOfType kullanýlmýyor: sahnedeki her objeyi tarar ve 150 adýmlýk bir
        /// döngüde bu maliyet birikir. Canvas pratikte kökte veya köke yakýn durur.
        /// </summary>
        private static void DetectUiRig(SceneContext context, GameObject[] roots)
        {
            try
            {
                foreach (GameObject root in roots)
                {
                    if (root == null)
                        continue;

                    if (!context.HasCanvas)
                    {
                        Canvas canvas = root.GetComponentInChildren<Canvas>(true);

                        if (canvas != null)
                        {
                            context.HasCanvas = true;
                            context.PrimaryCanvasName = canvas.gameObject.name;
                        }
                    }

                    if (!context.HasEventSystem)
                    {
                        EventSystem eventSystem = root.GetComponentInChildren<EventSystem>(true);

                        if (eventSystem != null)
                            context.HasEventSystem = true;
                    }

                    if (context.HasCanvas && context.HasEventSystem)
                        return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] UI rig taranamadi: " + ex.Message);
            }
        }

        private static void CollectAdditionalScenes(SceneContext context, Scene activeScene)
        {
            try
            {
                if (SceneManager.sceneCount <= 1)
                    return;

                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);

                    if (!scene.IsValid() || scene == activeScene)
                        continue;

                    string name = string.IsNullOrEmpty(scene.name) ? "(unnamed)" : scene.name;

                    if (!scene.isLoaded)
                        name += " (yuklenmemis)";

                    context.AdditionalScenes.Add(name);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Ek sahneler okunamadi: " + ex.Message);
            }
        }
    }
}