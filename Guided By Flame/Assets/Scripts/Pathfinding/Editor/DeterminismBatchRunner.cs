#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pathfinding.Tests.Editor
{
    /// <summary>
    /// Entry point for deterministic pathfinding tests in CI or batch mode.
    /// The DeterminismTests component exits Unity with a matching status code.
    /// </summary>
    public static class DeterminismBatchRunner
    {
        public static void Run()
        {
            // Batch-mode otherwise leaves Unity's last-session state pointing at an
            // empty Untitled scene, which is confusing when the project is opened again.
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            GameObject testObject = new GameObject("DeterminismBatchRunner");
            DeterminismTests tests = testObject.AddComponent<DeterminismTests>();
            tests.RunAllTests();
        }
    }
}
#endif
