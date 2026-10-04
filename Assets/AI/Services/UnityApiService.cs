#if UNITY_EDITOR

using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AI.Services
{
    public static class UnityApiService
    {
        public static string GetUnityVersion()
        {
            return Application.unityVersion;
        }

        public static string GetProjectName()
        {
            return Application.productName;
        }

        public static string GetProjectPath()
        {
            return Application.dataPath.Replace("/Assets", "");
        }

        public static string GetAssetsPath()
        {
            return Application.dataPath;
        }

        public static string GetActiveScene()
        {
            return EditorSceneManager.GetActiveScene().name;
        }

        public static string GetActiveScenePath()
        {
            return EditorSceneManager.GetActiveScene().path;
        }

        public static bool IsPlaying()
        {
            return EditorApplication.isPlaying;
        }

        public static bool IsCompiling()
        {
            return EditorApplication.isCompiling;
        }

        public static bool IsUpdating()
        {
            return EditorApplication.isUpdating;
        }

        public static bool IsPaused()
        {
            return EditorApplication.isPaused;
        }

        public static string GetBuildTarget()
        {
            return EditorUserBuildSettings.activeBuildTarget.ToString();
        }

        public static string GetBuildTargetGroup()
        {
            return BuildPipeline.GetBuildTargetGroup(
                EditorUserBuildSettings.activeBuildTarget).ToString();
        }

        public static string GetColorSpace()
        {
            return PlayerSettings.colorSpace.ToString();
        }

        public static string GetScriptingBackend()
        {
            return PlayerSettings
                .GetScriptingBackend(
                    BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget))
                .ToString();
        }

        public static string GetApiCompatibilityLevel()
        {
            return PlayerSettings
                .GetApiCompatibilityLevel(
                    BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget))
                .ToString();
        }

        public static GameObject GetSelectedObject()
        {
            return Selection.activeGameObject;
        }

        public static string GetSelectedObjectName()
        {
            return Selection.activeGameObject != null
                ? Selection.activeGameObject.name
                : "";
        }

        public static int GetSelectionCount()
        {
            return Selection.objects.Length;
        }

        public static bool HasSelection()
        {
            return Selection.activeObject != null;
        }

        public static bool IsPrefabMode()
        {
#if UNITY_2021_1_OR_NEWER
            return UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null;
#else
            return false;
#endif
        }

        public static DateTime GetEditorTime()
        {
            return DateTime.Now;
        }
    }
}

#endif