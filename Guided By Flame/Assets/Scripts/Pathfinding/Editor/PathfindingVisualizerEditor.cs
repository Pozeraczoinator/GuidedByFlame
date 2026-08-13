using UnityEditor;
using UnityEngine;

namespace Pathfinding.Visualization
{
    [CustomEditor(typeof(PathfindingVisualizer))]
    public class PathfindingVisualizerEditor : Editor
    {
        private SerializedProperty _benchmarkMode;
        private SerializedProperty _selectedAlgorithm;
        private SerializedProperty _autoStartBenchmark;
        private SerializedProperty _runWithoutVisualization;
        private SerializedProperty _runFullBenchmarkSuite;
        private SerializedProperty _scenario;
        private SerializedProperty _randomSeed;
        private SerializedProperty _mapFileName;
        private SerializedProperty _testCasesFileName;
        private SerializedProperty _benchmarkIterations;
        private SerializedProperty _outputFileName;
        private SerializedProperty _monitorCPUTemperature;
        private SerializedProperty _headlessIterationsPerYield;
        private SerializedProperty _headlessRowsPerFlush;
        private SerializedProperty _forceGcBeforeColdStart;
        private SerializedProperty _stopBenchmarkKey;
        private SerializedProperty _mapSource;
        private SerializedProperty _proceduralMapWidth;
        private SerializedProperty _proceduralMapHeight;
        private SerializedProperty _useSuiteMapSizes;
        private SerializedProperty _suiteMapSizes;
        private SerializedProperty _proceduralDensity;
        private SerializedProperty _suiteDensities;
        private SerializedProperty _suiteSeeds;
        private SerializedProperty _includeFileMapInFullSuite;
        private SerializedProperty _useDistanceBucketing;
        private SerializedProperty _pairsPerBucket;
        private SerializedProperty _movingObstacleCount;
        private SerializedProperty _patrolLength;
        private SerializedProperty _maxDS1Replans;
        private SerializedProperty _maxDS1ConsecutiveFailedReplans;
        private SerializedProperty _pathObstructionChanges;
        private SerializedProperty _pathObstructionSpacing;
        private SerializedProperty _maxTargetEscapes;
        private SerializedProperty _runBatchGeneration;
        private SerializedProperty _batchOutputDirectory;
        private SerializedProperty _basemapPrefab;
        private SerializedProperty _obstacleSprite;
        private SerializedProperty _changeMarkerSprite;
        private SerializedProperty _movingObstacleSprite;
        private SerializedProperty _agentPrefab;
        private SerializedProperty _visualizationStepDelay;
        private SerializedProperty _agentMoveSpeed;
        private SerializedProperty _pauseBetweenTests;
        private SerializedProperty _replanPauseDuration;
        private SerializedProperty _colorWalkable;
        private SerializedProperty _colorExplored;
        private SerializedProperty _colorPath;
        private SerializedProperty _colorStart;
        private SerializedProperty _colorTarget;
        private SerializedProperty _colorReplanPause;
        private SerializedProperty _colorCurrentAgentCell;

        private void OnEnable()
        {
            _benchmarkMode = serializedObject.FindProperty("benchmarkMode");
            _selectedAlgorithm = serializedObject.FindProperty("selectedAlgorithm");
            _autoStartBenchmark = serializedObject.FindProperty("autoStartBenchmark");
            _runWithoutVisualization = serializedObject.FindProperty("runWithoutVisualization");
            _runFullBenchmarkSuite = serializedObject.FindProperty("runFullBenchmarkSuite");
            _scenario = serializedObject.FindProperty("scenario");
            _randomSeed = serializedObject.FindProperty("randomSeed");
            _mapFileName = serializedObject.FindProperty("mapFileName");
            _testCasesFileName = serializedObject.FindProperty("testCasesFileName");
            _benchmarkIterations = serializedObject.FindProperty("benchmarkIterations");
            _outputFileName = serializedObject.FindProperty("outputFileName");
            _monitorCPUTemperature = serializedObject.FindProperty("monitorCPUTemperature");
            _headlessIterationsPerYield = serializedObject.FindProperty("headlessIterationsPerYield");
            _headlessRowsPerFlush = serializedObject.FindProperty("headlessRowsPerFlush");
            _forceGcBeforeColdStart = serializedObject.FindProperty("forceGcBeforeColdStart");
            _stopBenchmarkKey = serializedObject.FindProperty("stopBenchmarkKey");
            _mapSource = serializedObject.FindProperty("mapSource");
            _proceduralMapWidth = serializedObject.FindProperty("proceduralMapWidth");
            _proceduralMapHeight = serializedObject.FindProperty("proceduralMapHeight");
            _useSuiteMapSizes = serializedObject.FindProperty("useSuiteMapSizes");
            _suiteMapSizes = serializedObject.FindProperty("suiteMapSizes");
            _proceduralDensity = serializedObject.FindProperty("proceduralDensity");
            _suiteDensities = serializedObject.FindProperty("suiteDensities");
            _suiteSeeds = serializedObject.FindProperty("suiteSeeds");
            _includeFileMapInFullSuite = serializedObject.FindProperty("includeFileMapInFullSuite");
            _useDistanceBucketing = serializedObject.FindProperty("useDistanceBucketing");
            _pairsPerBucket = serializedObject.FindProperty("pairsPerBucket");
            _movingObstacleCount = serializedObject.FindProperty("movingObstacleCount");
            _patrolLength = serializedObject.FindProperty("patrolLength");
            _maxDS1Replans = serializedObject.FindProperty("maxDS1Replans");
            _maxDS1ConsecutiveFailedReplans = serializedObject.FindProperty("maxDS1ConsecutiveFailedReplans");
            _pathObstructionChanges = serializedObject.FindProperty("pathObstructionChanges");
            _pathObstructionSpacing = serializedObject.FindProperty("pathObstructionSpacing");
            _maxTargetEscapes = serializedObject.FindProperty("maxTargetEscapes");
            _runBatchGeneration = serializedObject.FindProperty("runBatchGeneration");
            _batchOutputDirectory = serializedObject.FindProperty("batchOutputDirectory");
            _basemapPrefab = serializedObject.FindProperty("basemapPrefab");
            _obstacleSprite = serializedObject.FindProperty("obstacleSprite");
            _changeMarkerSprite = serializedObject.FindProperty("changeMarkerSprite");
            _movingObstacleSprite = serializedObject.FindProperty("movingObstacleSprite");
            _agentPrefab = serializedObject.FindProperty("agentPrefab");
            _visualizationStepDelay = serializedObject.FindProperty("visualizationStepDelay");
            _agentMoveSpeed = serializedObject.FindProperty("agentMoveSpeed");
            _pauseBetweenTests = serializedObject.FindProperty("pauseBetweenTests");
            _replanPauseDuration = serializedObject.FindProperty("replanPauseDuration");
            _colorWalkable = serializedObject.FindProperty("colorWalkable");
            _colorExplored = serializedObject.FindProperty("colorExplored");
            _colorPath = serializedObject.FindProperty("colorPath");
            _colorStart = serializedObject.FindProperty("colorStart");
            _colorTarget = serializedObject.FindProperty("colorTarget");
            _colorReplanPause = serializedObject.FindProperty("colorReplanPause");
            _colorCurrentAgentCell = serializedObject.FindProperty("colorCurrentAgentCell");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptReference();
            DrawBenchmarkMode();

            if (_runBatchGeneration.boolValue)
            {
                DrawBatchGeneration();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            if (_runFullBenchmarkSuite.boolValue)
                DrawFullSuite();
            else
                DrawSingleConfiguration();

            DrawSharedBenchmarkSettings();
            DrawScenarioSettings();

            if (!_runFullBenchmarkSuite.boolValue && !_runWithoutVisualization.boolValue)
                DrawVisualizationSettings();

            DrawBatchGeneration();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptReference()
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((PathfindingVisualizer)target), typeof(PathfindingVisualizer), false);
        }

        private void DrawBenchmarkMode()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Benchmark", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_benchmarkMode);

            var mode = (PathfindingVisualizer.BenchmarkMode)_benchmarkMode.enumValueIndex;
            if (mode == PathfindingVisualizer.BenchmarkMode.SingleAlgorithm)
                EditorGUILayout.PropertyField(_selectedAlgorithm);

            EditorGUILayout.PropertyField(_autoStartBenchmark);
            EditorGUILayout.PropertyField(_runWithoutVisualization);
            EditorGUILayout.PropertyField(_runFullBenchmarkSuite);

            if (_runFullBenchmarkSuite.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Full Benchmark Suite runs headless. SingleAlgorithm means one selected algorithm across every map and scenario. AllAlgorithms means every supported algorithm.",
                    MessageType.Info);
            }
        }

        private void DrawSingleConfiguration()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Current Test Setup", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_scenario);
            EditorGUILayout.PropertyField(_randomSeed);
            EditorGUILayout.PropertyField(_mapSource);

            var mapSource = (PathfindingVisualizer.MapTopology)_mapSource.enumValueIndex;
            if (mapSource == PathfindingVisualizer.MapTopology.FromFile)
                EditorGUILayout.PropertyField(_mapFileName);
            else
                EditorGUILayout.PropertyField(_proceduralDensity);

            EditorGUILayout.PropertyField(_proceduralMapWidth);
            EditorGUILayout.PropertyField(_proceduralMapHeight);
            DrawTestCaseSettings(allowFileInput: true);
        }

        private void DrawFullSuite()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Full Suite Maps", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_useSuiteMapSizes);
            if (_useSuiteMapSizes.boolValue)
            {
                EditorGUILayout.PropertyField(_suiteMapSizes, true);
                EditorGUILayout.HelpBox("Each value is used as a square map size, e.g. 64 means 64x64.", MessageType.None);
            }
            else
            {
                EditorGUILayout.PropertyField(_proceduralMapWidth);
                EditorGUILayout.PropertyField(_proceduralMapHeight);
            }
            EditorGUILayout.PropertyField(_suiteDensities, true);
            EditorGUILayout.PropertyField(_suiteSeeds, true);
            EditorGUILayout.PropertyField(_includeFileMapInFullSuite);
            if (_includeFileMapInFullSuite.boolValue)
                EditorGUILayout.PropertyField(_mapFileName);

            DrawTestCaseSettings(allowFileInput: false);
        }

        private void DrawTestCaseSettings(bool allowFileInput)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Test Points", EditorStyles.boldLabel);

            if (allowFileInput)
                EditorGUILayout.PropertyField(_useDistanceBucketing);
            else
                EditorGUILayout.HelpBox("Full suite uses distance bucketing for every generated map.", MessageType.None);

            if (!allowFileInput || _useDistanceBucketing.boolValue)
            {
                EditorGUILayout.PropertyField(_pairsPerBucket);
            }
            else
            {
                EditorGUILayout.PropertyField(_testCasesFileName);
            }
        }

        private void DrawSharedBenchmarkSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_benchmarkIterations);
            EditorGUILayout.PropertyField(_outputFileName);
            EditorGUILayout.PropertyField(_monitorCPUTemperature);

            if (_runWithoutVisualization.boolValue || _runFullBenchmarkSuite.boolValue)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Headless Responsiveness", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_headlessIterationsPerYield);
                EditorGUILayout.PropertyField(_headlessRowsPerFlush);
                EditorGUILayout.PropertyField(_forceGcBeforeColdStart);
                EditorGUILayout.PropertyField(_stopBenchmarkKey);
            }
        }

        private void DrawScenarioSettings()
        {
            bool fullSuite = _runFullBenchmarkSuite.boolValue;
            var scenario = (PathfindingVisualizer.ScenarioType)_scenario.intValue;

            bool showDs1 = fullSuite || scenario == PathfindingVisualizer.ScenarioType.DS1_MovingObstacles;
            bool showDs2 = fullSuite || scenario == PathfindingVisualizer.ScenarioType.DS2_PathObstruction;
            bool showDs3 = fullSuite || scenario == PathfindingVisualizer.ScenarioType.DS3_EscapingTarget;

            if (!showDs1 && !showDs2 && !showDs3)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scenario Parameters", EditorStyles.boldLabel);

            if (showDs1)
            {
                EditorGUILayout.PropertyField(_movingObstacleCount);
                EditorGUILayout.PropertyField(_patrolLength);
                EditorGUILayout.PropertyField(_maxDS1Replans);
                EditorGUILayout.PropertyField(_maxDS1ConsecutiveFailedReplans);
            }

            if (showDs2)
            {
                EditorGUILayout.PropertyField(_pathObstructionChanges);
                EditorGUILayout.PropertyField(_pathObstructionSpacing);
            }

            if (showDs3)
            {
                EditorGUILayout.PropertyField(_maxTargetEscapes);
            }
        }

        private void DrawVisualizationSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Visualization", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_basemapPrefab);
            EditorGUILayout.PropertyField(_obstacleSprite);
            EditorGUILayout.PropertyField(_changeMarkerSprite);
            EditorGUILayout.PropertyField(_movingObstacleSprite);
            EditorGUILayout.PropertyField(_agentPrefab);
            EditorGUILayout.PropertyField(_visualizationStepDelay);
            EditorGUILayout.PropertyField(_agentMoveSpeed);
            EditorGUILayout.PropertyField(_pauseBetweenTests);
            EditorGUILayout.PropertyField(_replanPauseDuration);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Colors", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_colorWalkable);
            EditorGUILayout.PropertyField(_colorExplored);
            EditorGUILayout.PropertyField(_colorPath);
            EditorGUILayout.PropertyField(_colorStart);
            EditorGUILayout.PropertyField(_colorTarget);
            EditorGUILayout.PropertyField(_colorReplanPause);
            EditorGUILayout.PropertyField(_colorCurrentAgentCell);
        }

        private void DrawBatchGeneration()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Map Generation Tool", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_runBatchGeneration);
            if (_runBatchGeneration.boolValue)
            {
                EditorGUILayout.PropertyField(_batchOutputDirectory);
                EditorGUILayout.HelpBox("This tool only exports maps and test cases on Start; it does not run benchmarks.", MessageType.Warning);
            }
        }
    }
}
