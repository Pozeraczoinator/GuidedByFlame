using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;
using Pathfinding.Core;
using Pathfinding.Algorithms;
using Pathfinding.Benchmark;
using Pathfinding.MapGenerators;

namespace Pathfinding.Visualization
{
    /// <summary>
    /// Wizualizator pathfindingu z PEŁNYM systemem benchmarkingu.
    /// 
    /// Realizuje WSZYSTKIE wymagania pracy magisterskiej:
    ///   ✓ Losowa kolejność algorytmów (Fisher-Yates) — eliminacja thermal throttling
    ///   ✓ Cold Start jako osobna metryka (iteracja 0 → ColdStartTimeMs)
    ///   ✓ Pomiar GC Alloc, CPU Ticks, PathSmoothness
    ///   ✓ Monitoring temperatury CPU (opcjonalny, Windows)
    ///   ✓ Static + dynamic obstacle scenarios
    ///   ✓ Zapis do CSV PO ZAKOŃCZENIU animacji (1 wynik = 1 animacja)
    ///
    /// FLOW per test case:
    ///   1. Uruchom algorytm (N iteracji → zbierz metryki)
    ///   2. Pokaż animację (explored nodes → ruch agenta)
    ///   3. PO animacji → zapisz wynik do CSV
    ///   4. Pauza → następny algorytm/test
    /// </summary>
    public class PathfindingVisualizer : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────
        //  ENUMY
        // ─────────────────────────────────────────────────────────

        public enum AlgorithmChoice { AStar, Dijkstra, GreedyBestFirst, CustomGreedy, JumpPointSearch }
        public enum BenchmarkMode { SingleAlgorithm, AllAlgorithms }

        /// <summary>
        /// Scenariusze testowe do pracy magisterskiej:
        /// - Static: stała mapa, brak zmian
        /// - DS1_MovingObstacles: ruchome przeszkody, wspólny deterministyczny snapshot dla algorytmów
        /// - DS2_PathObstruction: deterministyczne dodawanie przeszkód na trasie referencyjnej
        /// - DS3_EscapingTarget: punkt końcowy ucieka co 2 kroki agenta o 1 pole
        /// </summary>
        public enum ScenarioType
        {
            Static = 0,
            DS1_MovingObstacles = 1,
            DS2_PathObstruction = 2,
            DS3_EscapingTarget = 3
        }

        public enum MapTopology { FromFile, OpenField, Maze, RoomCorridor, ScatteredBlock }

        // ─────────────────────────────────────────────────────────
        //  KONFIGURACJA Z INSPEKTORA
        // ─────────────────────────────────────────────────────────

        [Header("═══ Tryb Benchmarku ═══")]
        [Tooltip("SingleAlgorithm = testuj tylko wybrany algorytm.\nAllAlgorithms = testuj wszystkie 5 po kolei (z animacją i losową kolejnością).")]
        public BenchmarkMode benchmarkMode = BenchmarkMode.AllAlgorithms;

        [Tooltip("Startuje benchmark automatycznie po uruchomieniu sceny. Gdy wyłączone, start pod spacją.")]
        public bool autoStartBenchmark = false;

        [Tooltip("Uruchamia pomiary bez tworzenia sprite'ów i animacji. Najlepszy tryb do zbierania danych.")]
        public bool runWithoutVisualization = false;

        [Tooltip("Uruchamia pełny zestaw: wybrane algorytmy x wszystkie scenariusze x wszystkie mapy proceduralne.")]
        public bool runFullBenchmarkSuite = false;

        [Header("═══ Scenariusz Testowy ═══")]
        [Tooltip("Static / DS1_MovingObstacles / DS2_PathObstruction / DS3_EscapingTarget")]
        public ScenarioType scenario = ScenarioType.Static;

        [Tooltip("Seed RNG — ten sam seed = te same wyniki. Kluczowe dla powtarzalności.")]
        public int randomSeed = 42;

        [Header("═══ Konfiguracja Algorytmu ═══")]
        [Tooltip("Algorytm do wizualizacji (używany tylko w trybie SingleAlgorithm).")]
        public AlgorithmChoice selectedAlgorithm = AlgorithmChoice.AStar;
        public string mapFileName = "Map.txt";
        public string testCasesFileName = "TestCases.csv";

        [Header("═══ Benchmark ═══")]
        [Tooltip("Liczba iteracji pomiarowych per algorytm/test. Iteracja 0 = cold start.")]
        [Range(1, 200)]
        public int benchmarkIterations = 30;

        [Tooltip("Nazwa pliku wynikowego CSV (35 kolumn, separator: średnik).")]
        public string outputFileName = "benchmark_results.csv";

        [Header("═══ Monitoring Sprzętowy ═══")]
        [Tooltip("Czy mierzyć temperaturę CPU przy każdym teście (Windows only). Spowalnia ~100ms per pomiar.")]
        public bool monitorCPUTemperature = false;

        [Header("═══ Headless Benchmark ═══")]
        [Tooltip("Ile iteracji jednego algorytmu wykonać przed oddaniem klatki Unity w trybie bez wizualizacji.")]
        [Range(1, 30)]
        public int headlessIterationsPerYield = 15;

        [Tooltip("Co ile wierszy CSV wymusić zapis na dysk w trybie bez wizualizacji.")]
        [Range(1, 10000)]
        public int headlessRowsPerFlush = 250;

        [Tooltip("Opcjonalnie wymusza pełne GC przed cold startem. Nie jest wymagane przez licznik zaalokowanych bajtów i znacząco spowalnia full suite.")]
        public bool forceGcBeforeColdStart = false;

        [Tooltip("Klawisz proszący benchmark o zatrzymanie po najbliższej bezpiecznej porcji pracy.")]
        public KeyCode stopBenchmarkKey = KeyCode.Escape;

        [Header("═══ Generacja Map Proceduralnych ═══")]
        [Tooltip("Źródło mapy: FromFile = wczytaj z pliku TXT, inne = generuj proceduralnie.")]
        public MapTopology mapSource = MapTopology.FromFile;

        [Tooltip("Szerokość map proceduralnych w pełnym benchmarku.")]
        [Range(10, 500)]
        public int proceduralMapWidth = 32;

        [Tooltip("Wysokość map proceduralnych w pełnym benchmarku.")]
        [Range(10, 500)]
        public int proceduralMapHeight = 20;

        [Tooltip("Gdy włączone, pełny benchmark uruchamia te same testy dla wielu kwadratowych rozmiarów map.")]
        public bool useSuiteMapSizes = false;

        [Tooltip("Kwadratowe rozmiary map używane w pełnym benchmarku, np. 32 oznacza mapę 32x32.")]
        public int[] suiteMapSizes = { 32, 64, 128 };

        [Tooltip("Zagęszczenie przeszkód dla map proceduralnych (0.0–0.5).")]
        [Range(0f, 0.5f)]
        public float proceduralDensity = 0.2f;

        [Tooltip("Poziomy zagęszczenia przeszkód używane przez pełny benchmark.")]
        public float[] suiteDensities = { 0.10f, 0.20f, 0.30f, 0.40f };

        [Tooltip("Seedy map używane przez pełny benchmark.")]
        public int[] suiteSeeds = { 42, 123, 256, 789 };

        [Tooltip("Dodaje Map.txt jako dodatkową mapę w pełnym benchmarku.")]
        public bool includeFileMapInFullSuite = false;

        [Header("═══ Distance Bucketing (Naukowy Dobór Punktów) ═══")]
        [Tooltip("Czy generować test cases automatycznie z bucketingiem po realnej długości najkrótszej ścieżki zamiast czytać z pliku CSV.")]
        public bool useDistanceBucketing = false;

        [Tooltip("Ile osiągalnych par testowych na wiązkę dystansową (SHORT/MEDIUM/LONG).")]
        [Range(5, 100)]
        public int pairsPerBucket = 30;

        [Header("═══ DS1: Ruchome Przeszkody ═══")]
        [Tooltip("Minimalna liczba ruchomych przeszkód. Dla długich tras DS1 skaluje ją automatycznie: około 1 przeszkoda na 12 kroków trasy referencyjnej.")]
        [Range(1, 64)]
        public int movingObstacleCount = 3;

        [Tooltip("Długość trasy patrol każdej przeszkody (w polach).")]
        [Range(3, 20)]
        public int patrolLength = 6;

        [Tooltip("Deterministyczny limit ponownych wyznaczeń ścieżki w jednym przebiegu DS1.")]
        [Range(1, 1000)]
        public int maxDS1Replans = 120;

        [Tooltip("DS1 kończy się niepowodzeniem po tylu kolejnych nieudanych replanach bez ruchu agenta.")]
        [Range(1, 200)]
        public int maxDS1ConsecutiveFailedReplans = 20;

        [Header("═══ DS2: Blokady na Trasie ═══")]
        [Tooltip("Górny limit trwałych blokad DS2. Faktyczna liczba skaluje się z długością trasy.")]
        [Range(1, 60)]
        public int pathObstructionChanges = 40;

        [Tooltip("Co ile kroków agenta uruchamiać kolejne zdarzenie DS2.")]
        [Range(2, 12)]
        public int pathObstructionSpacing = 8;

        [Header("═══ DS3: Escaping Target ═══")]
        [Tooltip("Maksymalna liczba ucieczek punktu końcowego w jednym teście DS3.")]
        [Range(5, 200)]
        public int maxTargetEscapes = 50;

        [Header("═══ Batch Generator ═══")]
        [Tooltip("Czy wygenerować wszystkie 64 kombinacje map przy starcie (zamiast benchmarku).")]
        public bool runBatchGeneration = false;

        [Tooltip("Katalog wyjściowy dla batch-generowanych map.")]
        public string batchOutputDirectory = "GeneratedMaps";

        [Header("═══ Wizualizacja ═══")]
        [Tooltip("Prefabrykat kwadratu bazowego mapy (zwykły Sprite)")]
        public GameObject basemapPrefab;
        [Tooltip("Sprite dla ściany/przeszkody")]
        public Sprite obstacleSprite;
        [FormerlySerializedAs("dynamicChangeSprite")]
        [Tooltip("Sprite dla markerów zmian scenariusza i rekalkulacji.")]
        public Sprite changeMarkerSprite;
        [Tooltip("Sprite tylko dla ruchomych przeszkód DS1. Gdy puste, używany jest zwykły Obstacle Sprite.")]
        public Sprite movingObstacleSprite;
        [Tooltip("Prefabrykat poruszającego się agenta (kostki)")]
        public GameObject agentPrefab;
        public float visualizationStepDelay = 0.05f;
        public float agentMoveSpeed = 10.0f;
        [Tooltip("Pauza w sekundach między kolejnymi algorytmami/testami.")]
        public float pauseBetweenTests = 2.0f;
        [Tooltip("Pauza wizualna w DS1 w momencie wymuszonej rekalkulacji.")]
        public float replanPauseDuration = 0.35f;

        [Header("═══ Kolory ═══")]
        public Color colorWalkable = Color.white;
        public Color colorExplored = new Color(0.6f, 0.8f, 1f, 0.8f);
        public Color colorPath = new Color(1f, 1f, 0.2f, 0.9f);
        public Color colorStart = Color.red;
        public Color colorTarget = Color.green;
        public Color colorReplanPause = new Color(1f, 0.35f, 0.1f, 1f);
        public Color colorCurrentAgentCell = new Color(1f, 0.55f, 0.05f, 1f);

        // ─────────────────────────────────────────────────────────
        //  STAN WEWNĘTRZNY
        // ─────────────────────────────────────────────────────────

        private GridMap _gridMap;
        private GridMap _originalGridMap;
        private List<TestCase> _testCases = new List<TestCase>();
        private int _currentTestCaseIndex = 0;
        private SpriteRenderer[,] _basemapRenderers;
        private GameObject[,] _spawnedObstacles;
        private GameObject[,] _spawnedChangeMarkers;
        private GameObject _agentObject;
        private bool _isVisualizing = false;
        private bool _isAutoRunning = false;
        private System.Random _shuffleRng;
        private string _activeMapTopology = "FromFile";
        private int _activeMapSeed = 0;
        private float _activeMapDensity = 0f;
        private int _activeMapWidth = 0;
        private int _activeMapHeight = 0;
        private int _suiteTestId = 0;
        private bool _stopBenchmarkRequested = false;
        private int _rowsSinceFlush = 0;
        private double _nextFullSuiteHeartbeatRealtime = 0d;
        private int _cachedDS1ReferenceTestId = -1;
        private Vector2Int _cachedDS1ReferenceStart;
        private Vector2Int _cachedDS1ReferenceTarget;
        private int _cachedDS1ReferenceMapSeed;
        private int _cachedDS1ReferenceMapWidth;
        private int _cachedDS1ReferenceMapHeight;
        private List<Vector2Int> _cachedDS1ReferencePath;
        private MovingObstacleManager _cachedDS1InitialManager;
        private int _cachedTickLimitTestId = -1;
        private ScenarioType _cachedTickLimitScenario;
        private int _cachedDynamicTickLimit;

        // Dynamic scenario managers
        private MovingObstacleManager _ds1Manager;

        private struct TestCase
        {
            public int startX, startY;
            public int targetX, targetY;
            public string distanceBucket;
            public float euclideanDistance;
            public float octagonalDistance;
            public float referenceShortestPathLength;
        }

        private bool ShouldVisualize => !runWithoutVisualization && !runFullBenchmarkSuite;
        private bool ShouldLogDetailedBenchmark => ShouldVisualize;
        private const int HeadlessProgressInterval = 50;
        private const int FullSuiteCheckpointInterval = 50;
        private const double FullSuiteHeartbeatIntervalSeconds = 30d;
        private const long FullSuiteMaxWorkSliceMs = 5000;
        private const string AllocationMeasurementVersion = "ThreadAllocatedBytesV1";

        private class MeasurementBatch
        {
            public BenchmarkMetrics Metrics;
            public Pathfinding.Core.PathfindingResult VisualResult;
            public bool Cancelled;
        }

        private class DS2DynamicState
        {
            public readonly List<DS2ObstructionEvent> Schedule;
            // Zachowane wyłącznie dla nieużywanego, starszego wariantu generatora DS2.
            public readonly System.Random Rng = new System.Random(0);
            public readonly HashSet<Vector2Int> BlockedCells = new HashSet<Vector2Int>();
            public int NextEventIndex;

            public DS2DynamicState(List<DS2ObstructionEvent> schedule)
            {
                Schedule = schedule;
            }
        }

        private class DS2ObstructionEvent
        {
            public readonly int TriggerStep;
            public readonly List<Vector2Int> Cells;

            public DS2ObstructionEvent(int triggerStep, List<Vector2Int> cells)
            {
                TriggerStep = triggerStep;
                Cells = cells;
            }
        }

        private class DS3EscapingTargetState
        {
            public Vector2Int CurrentTarget;
            public readonly Vector2Int OriginalTarget;
            public readonly Vector2Int EscapeAnchor;
            public readonly System.Random Rng;
            public int StepsSinceLastEscape;
            public int TotalEscapes;

            public DS3EscapingTargetState(
                Vector2Int initialTarget, Vector2Int escapeAnchor, int seed)
            {
                CurrentTarget = initialTarget;
                OriginalTarget = initialTarget;
                EscapeAnchor = escapeAnchor;
                Rng = new System.Random(seed);
                StepsSinceLastEscape = 0;
                TotalEscapes = 0;
            }
        }

        private bool IsMovingObstacleCell(int x, int y)
        {
            if (_ds1Manager == null)
                return false;

            Vector2Int pos = new Vector2Int(x, y);
            foreach (var obstacle in _ds1Manager.Obstacles)
            {
                if (obstacle.CurrentPosition == pos)
                    return true;
            }

            return false;
        }

        private static string FormatProgress(int completed, int total, string unitLabel)
        {
            if (total <= 0)
                return $"0.0% (0/0 {unitLabel})";

            int clampedCompleted = Mathf.Clamp(completed, 0, total);
            float percent = clampedCompleted * 100f / total;
            return string.Format(CultureInfo.InvariantCulture,
                "{0:F1}% ({1}/{2} {3})", percent, clampedCompleted, total, unitLabel);
        }

        private static int CountItems<T>(IEnumerable<T> items)
        {
            int count = 0;
            foreach (T item in items)
                count++;
            return count;
        }

        private int EstimateFullSuiteTestCaseTotal()
        {
            int generatedMapConfigs = CountItems(GetSuiteMapSizes()) *
                                      GetSuiteTopologies().Count *
                                      CountItems(GetSuiteDensities()) *
                                      CountItems(GetSuiteSeeds());
            int fileMapConfigs = includeFileMapInFullSuite ? 1 : 0;
            int scenarioCount = Enum.GetValues(typeof(ScenarioType)).Length;
            int expectedPairsPerMap = pairsPerBucket * 3;

            return (generatedMapConfigs + fileMapConfigs) * scenarioCount * expectedPairsPerMap;
        }

        private static long BeginAllocationMeasurement(bool forceFullCollection)
        {
            if (forceFullCollection)
                HardwareMonitor.ForceGCAndGetMemory();

            return GC.GetAllocatedBytesForCurrentThread();
        }

        private static void CalculateReportedPathMetrics(
            Pathfinding.Core.PathfindingResult result,
            Vector2Int startPos,
            bool isColdStart)
        {
            if (!isColdStart)
                return;

            result.CalculatePathCost(startPos);
            if (result.PathFound)
                result.CalculateSmoothnessMetrics(startPos);
        }

        private string BuildSuiteFingerprint()
        {
            string raw = string.Join("|", new[]
            {
                BenchmarkMetrics.GetCsvHeader(),
                AllocationMeasurementVersion,
                benchmarkMode.ToString(),
                selectedAlgorithm.ToString(),
                benchmarkIterations.ToString(CultureInfo.InvariantCulture),
                includeFileMapInFullSuite.ToString(),
                useDistanceBucketing.ToString(),
                pairsPerBucket.ToString(CultureInfo.InvariantCulture),
                movingObstacleCount.ToString(CultureInfo.InvariantCulture),
                patrolLength.ToString(CultureInfo.InvariantCulture),
                maxDS1Replans.ToString(CultureInfo.InvariantCulture),
                maxDS1ConsecutiveFailedReplans.ToString(CultureInfo.InvariantCulture),
                pathObstructionChanges.ToString(CultureInfo.InvariantCulture),
                pathObstructionSpacing.ToString(CultureInfo.InvariantCulture),
                maxTargetEscapes.ToString(CultureInfo.InvariantCulture),
                string.Join(",", GetSuiteMapSizes()),
                string.Join(",", GetSuiteTopologies()),
                string.Join(",", GetSuiteDensities().Select(value =>
                    value.ToString("R", CultureInfo.InvariantCulture))),
                string.Join(",", GetSuiteSeeds())
            });

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }

        private int PrepareFullSuiteResume(
            string resultsPath,
            string checkpointPath,
            string suiteFingerprint,
            int estimatedTotalTestCases)
        {
            int resumeTestId = ReadFullSuiteCheckpoint(checkpointPath, suiteFingerprint);

            // Obsługa plików utworzonych przed dodaniem checkpointów: jeżeli CSV ma
            // aktualny schemat, odnajdź pierwszy niekompletny TestID i uratuj dane.
            if (resumeTestId < 0 && File.Exists(resultsPath))
                resumeTestId = FindFirstIncompleteTestId(resultsPath);

            if (resumeTestId <= 0 || resumeTestId >= estimatedTotalTestCases)
            {
                if (File.Exists(checkpointPath))
                    File.Delete(checkpointPath);
                return 0;
            }

            TruncateCsvAtTestId(resultsPath, resumeTestId);
            WriteFullSuiteCheckpoint(checkpointPath, suiteFingerprint, resumeTestId);
            return resumeTestId;
        }

        private int ReadFullSuiteCheckpoint(string checkpointPath, string suiteFingerprint)
        {
            if (!File.Exists(checkpointPath))
                return -1;

            try
            {
                string[] lines = File.ReadAllLines(checkpointPath);
                if (lines.Length != 2 || lines[0] != suiteFingerprint ||
                    !int.TryParse(lines[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nextTestId))
                {
                    Debug.LogWarning("[Visualizer] Checkpoint nie pasuje do bieżącej konfiguracji. " +
                                     "Benchmark rozpocznie nowy plik.");
                    return 0;
                }

                return Mathf.Max(0, nextTestId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Visualizer] Nie można odczytać checkpointu: {ex.Message}. " +
                                 "Benchmark rozpocznie nowy plik.");
                return 0;
            }
        }

        private int FindFirstIncompleteTestId(string resultsPath)
        {
            try
            {
                using (var reader = new StreamReader(resultsPath))
                {
                    if (reader.ReadLine() != BenchmarkMetrics.GetCsvHeader())
                        return 0;

                    var expectedAlgorithms = new HashSet<string>(
                        GetAlgorithmsToRun().Select(algorithm => algorithm.AlgorithmName));
                    var algorithmsForCurrentTest = new HashSet<string>();
                    int currentTestId = 0;
                    string line;

                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] columns = line.Split(';');
                        if (columns.Length != 35 ||
                            !int.TryParse(columns[0], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int rowTestId))
                            return currentTestId;

                        if (rowTestId != currentTestId)
                        {
                            if (rowTestId != currentTestId + 1 ||
                                !algorithmsForCurrentTest.SetEquals(expectedAlgorithms))
                                return currentTestId;

                            currentTestId = rowTestId;
                            algorithmsForCurrentTest.Clear();
                        }

                        if (!expectedAlgorithms.Contains(columns[1]) ||
                            !algorithmsForCurrentTest.Add(columns[1]))
                            return currentTestId;
                    }

                    return algorithmsForCurrentTest.SetEquals(expectedAlgorithms)
                        ? currentTestId + 1
                        : currentTestId;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Visualizer] Nie można zweryfikować istniejącego CSV: {ex.Message}. " +
                                 "Benchmark rozpocznie nowy plik.");
                return 0;
            }
        }

        private void TruncateCsvAtTestId(string resultsPath, int firstTestIdToRepeat)
        {
            string temporaryPath = resultsPath + ".resume.tmp";
            using (var reader = new StreamReader(resultsPath))
            using (var writer = new StreamWriter(temporaryPath, false))
            {
                string header = reader.ReadLine();
                writer.WriteLine(header);

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int separator = line.IndexOf(';');
                    if (separator <= 0 ||
                        !int.TryParse(line.Substring(0, separator), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int testId) ||
                        testId >= firstTestIdToRepeat)
                        break;

                    writer.WriteLine(line);
                }
            }

            File.Copy(temporaryPath, resultsPath, true);
            File.Delete(temporaryPath);
        }

        private void WriteFullSuiteCheckpoint(
            string checkpointPath, string suiteFingerprint, int nextTestId)
        {
            string temporaryPath = checkpointPath + ".tmp";
            File.WriteAllLines(temporaryPath, new[]
            {
                suiteFingerprint,
                nextTestId.ToString(CultureInfo.InvariantCulture)
            });

            if (File.Exists(checkpointPath))
                File.Replace(temporaryPath, checkpointPath, null);
            else
                File.Move(temporaryPath, checkpointPath);
        }

        private Sprite GetObstacleSpriteForCell(int x, int y)
        {
            if (movingObstacleSprite != null && scenario == ScenarioType.DS1_MovingObstacles && IsMovingObstacleCell(x, y))
                return movingObstacleSprite;

            return obstacleSprite;
        }

        // ─────────────────────────────────────────────────────────
        //  LIFECYCLE
        // ─────────────────────────────────────────────────────────

        private void Start()
        {
            _shuffleRng = new System.Random(randomSeed);
            ConfigureHeadlessRuntime();

            // Tryb batch generation — generuj mapy i wyjdź
            if (runBatchGeneration)
            {
                RunBatchGeneration();
                return;
            }

            if (runFullBenchmarkSuite)
            {
                Debug.Log("[Visualizer] Pełny benchmark gotowy. Start: spacja albo Auto Start Benchmark.");
                if (autoStartBenchmark)
                {
                    _isAutoRunning = true;
                    StartCoroutine(RunFullBenchmarkSuite());
                }
                return;
            }

            // Wczytaj lub wygeneruj mapę
            bool mapLoaded = false;
            if (mapSource == MapTopology.FromFile)
            {
                mapLoaded = LoadGridMap();
            }
            else
            {
                mapLoaded = GenerateProceduralMap();
            }

            if (!mapLoaded) return;

            _activeMapTopology = mapSource.ToString();
            _activeMapSeed = mapSource == MapTopology.FromFile ? 0 : randomSeed;
            _activeMapDensity = mapSource == MapTopology.FromFile
                ? 1f - ((float)_gridMap.CountWalkable() / (_gridMap.Width * _gridMap.Height))
                : proceduralDensity;
            _activeMapWidth = _gridMap.Width;
            _activeMapHeight = _gridMap.Height;

            _originalGridMap = _gridMap.Clone();

            // Wczytaj lub wygeneruj test cases
            if (useDistanceBucketing)
            {
                GenerateDistanceBucketedTestCases();
            }
            else
            {
                LoadTestCases();
            }

            if (ShouldVisualize)
            {
                GenerateBasemapVisuals();
            }

            Debug.Log($"[Visualizer] Gotowy. Tryb: {benchmarkMode}, Scenariusz: {scenario}, " +
                      $"Mapa: {mapSource}, Testy: {_testCases.Count}. " +
                      (autoStartBenchmark ? "Start automatyczny." : "Wciśnij SPACJĘ aby rozpocząć."));

            if (autoStartBenchmark)
            {
                _isAutoRunning = true;
                StartCoroutine(AutoRunAllCases());
            }
        }

        private void ConfigureHeadlessRuntime()
        {
            if (!runWithoutVisualization && !runFullBenchmarkSuite)
                return;

            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        private void Update()
        {
            if (_isAutoRunning && Input.GetKeyDown(stopBenchmarkKey))
            {
                _stopBenchmarkRequested = true;
                Debug.LogWarning($"[Visualizer] Zatrzymanie benchmarku zaplanowane po najbliższej bezpiecznej porcji pracy ({stopBenchmarkKey}).");
            }

            if (Input.GetKeyDown(KeyCode.Space) && !_isAutoRunning)
            {
                _isAutoRunning = true;
                StartCoroutine(runFullBenchmarkSuite ? RunFullBenchmarkSuite() : AutoRunAllCases());
            }
        }

        // ─────────────────────────────────────────────────────────
        //  GŁÓWNA PĘTLA BENCHMARK + WIZUALIZACJA
        // ─────────────────────────────────────────────────────────

        private IEnumerator AutoRunAllCases()
        {
            string resultsPath = Path.Combine(Application.dataPath, "..", outputFileName);
            _currentTestCaseIndex = 0;
            _stopBenchmarkRequested = false;
            _rowsSinceFlush = 0;
            int totalTestCases = _testCases.Count;
            Debug.Log($"[Visualizer] ══════════════════════════════════════════");
            Debug.Log($"[Visualizer] START BENCHMARKU");
            Debug.Log($"[Visualizer] Tryb: {benchmarkMode} | Scenariusz: {scenario}");
            Debug.Log($"[Visualizer] Iteracje: {benchmarkIterations} | Testy: {totalTestCases}");
            Debug.Log($"[Visualizer] Postęp: {FormatProgress(0, totalTestCases, "testów")}");
            Debug.Log($"[Visualizer] Plik CSV: {resultsPath}");
            Debug.Log($"[Visualizer] ══════════════════════════════════════════");

            // Monitoring temperatury — start
            float tempStart = -1f;
            if (monitorCPUTemperature)
            {
                                HardwareMonitor.StartTemperatureMonitoring();
                tempStart = HardwareMonitor.GetCPUTemperature();
                Debug.Log($"[HardwareMonitor] Temp. CPU na starcie: {tempStart:F1}°C");
            }

            float mapDensity = 1f - ((float)_gridMap.CountWalkable() / (_gridMap.Width * _gridMap.Height));

            using (StreamWriter writer = new StreamWriter(resultsPath, false))
            {
                writer.AutoFlush = ShouldVisualize;
                writer.WriteLine(BenchmarkMetrics.GetCsvHeader());

                while (_currentTestCaseIndex < _testCases.Count)
                {
                    if (_stopBenchmarkRequested)
                        break;

                    TestCase tc = _testCases[_currentTestCaseIndex];
                    int testId = _currentTestCaseIndex;
                    _currentTestCaseIndex++;

                    // Każdy przypadek rozpoczyna się od identycznej mapy bazowej.
                    // Bez resetu snapshot DS1 z poprzedniego testu pozostawiał
                    // na mapie obce przeszkody ruchome.
                    _gridMap = _originalGridMap.Clone();

                    Vector2Int startPos = new Vector2Int(tc.startX, tc.startY);
                    Vector2Int targetPos = new Vector2Int(tc.targetX, tc.targetY);

                    // Walidacja
                    if (!_gridMap.IsWalkable(startPos) || !_gridMap.IsWalkable(targetPos))
                    {
                        Debug.LogWarning($"[Visualizer] Test {testId + 1}/{totalTestCases}: start/cel nie jest walkable. Pomijam. " +
                                         $"Postęp: {FormatProgress(testId + 1, totalTestCases, "testów")}");
                        continue;
                    }

                    // ─── Pobierz i LOSUJ kolejność algorytmów ───
                    List<IPathfindingAlgorithm> algorithmsToRun = GetAlgorithmsToRun();
                    ShuffleList(algorithmsToRun); // Fisher-Yates — eliminacja thermal throttling bias

                    if (ShouldLogDetailedBenchmark)
                    {
                        Debug.Log($"[Visualizer] ── Test {testId + 1}/{_testCases.Count} ── " +
                                  $"Kolejność: {string.Join(" → ", GetAlgorithmNames(algorithmsToRun))}");
                    }
                    if (ShouldLogDetailedBenchmark)
                    {
                        Debug.Log($"[Visualizer] Postęp: {FormatProgress(testId, totalTestCases, "testów")}");
                    }

                    List<Vector2Int> scenarioChanges = null;
                    PrepareScenarioSnapshot(startPos, targetPos, testId, out scenarioChanges);
                    GridMap scenarioSnapshot = _gridMap.Clone();

                    if (ShouldVisualize)
                        RefreshBasemapColors();

                    float currentDensity = (scenario == ScenarioType.Static)
                        ? mapDensity
                        : 1f - ((float)scenarioSnapshot.CountWalkable() / (scenarioSnapshot.Width * scenarioSnapshot.Height));

                    foreach (var algorithm in algorithmsToRun)
                    {
                        if (_stopBenchmarkRequested)
                            break;

                        _gridMap = ShouldVisualize ? scenarioSnapshot.Clone() : scenarioSnapshot;
                        if (ShouldVisualize) RefreshBasemapColors();
                        // ─── KROK 1: Pomiar z N iteracjami ───
                        BenchmarkMetrics metrics;
                        Pathfinding.Core.PathfindingResult visualResult;

                        if (ShouldVisualize)
                        {
                            MeasureAlgorithm(algorithm, _gridMap, startPos, targetPos,
                                testId, currentDensity, out metrics, out visualResult);
                        }
                        else
                        {
                            MeasureAlgorithm(algorithm, _gridMap, startPos, targetPos,
                                testId, currentDensity, out metrics, out visualResult);
                        }

                        ApplyTestCaseMetadata(metrics, tc);

                        if (monitorCPUTemperature)
                        {
                            metrics.CPUTemperature = HardwareMonitor.GetCPUTemperature();
                        }

                        if (ShouldLogDetailedBenchmark)
                        {
                            Debug.Log($"[Visualizer] {algorithm.AlgorithmName}: " +
                                      $"start={startPos} → cel={targetPos} | " +
                                      $"Znaleziono: {metrics.PathFound} | " +
                                      $"Czas: {metrics.AvgExecutionTimeMs:F4}ms | " +
                                      $"Węzły: {metrics.ExploredNodes} | " +
                                      $"JPS scan: {metrics.JumpScannedCells}");
                        }

                        if (ShouldVisualize)
                        {
                            // ─── KROK 2: Animacja wizualizacji ───
                            if (scenario == ScenarioType.DS1_MovingObstacles)
                            {
                                StartCoroutine(VisualizeDS1ReplanningRoutine(
                                    algorithm, visualResult, startPos, targetPos, testId));
                            }
                            else if (scenario == ScenarioType.DS2_PathObstruction)
                            {
                                StartCoroutine(VisualizeDS2ReplanningRoutine(
                                    algorithm, startPos, targetPos, testId));
                            }
                            else if (scenario == ScenarioType.DS3_EscapingTarget)
                            {
                                StartCoroutine(VisualizeDS3ReplanningRoutine(
                                    algorithm, startPos, targetPos, testId));
                            }
                            else
                            {
                                StartCoroutine(VisualizeRoutine(visualResult, startPos, targetPos,
                                    algorithm.AlgorithmName, scenarioChanges));
                            }

                            while (_isVisualizing)
                            {
                                yield return null;
                            }
                        }

                        // ─── KROK 3: Zapis PO animacji ───
                        writer.WriteLine(metrics.ToCsvRow());
                        _rowsSinceFlush++;
                        if (!ShouldVisualize && _rowsSinceFlush >= headlessRowsPerFlush)
                        {
                            writer.Flush();
                            _rowsSinceFlush = 0;
                            yield return null;
                        }

                        if (ShouldLogDetailedBenchmark)
                        {
                            Debug.Log($"[Visualizer] ✓ Zapisano: {algorithm.AlgorithmName} | " +
                                      $"Ścieżka: {metrics.PathLength:F2} | " +
                                      $"Smoothness: {metrics.PathSmoothness:F4}");
                        }

                        if (ShouldVisualize)
                            yield return new WaitForSeconds(pauseBetweenTests);
                    }

                    if (!ShouldVisualize && (testId + 1) % HeadlessProgressInterval == 0)
                    {
                        writer.Flush();
                        _rowsSinceFlush = 0;
                        Debug.Log($"[Visualizer] Postęp headless: {FormatProgress(testId + 1, totalTestCases, "testów")}");
                        yield return null;
                    }

                    // Reset mapy po zakonczeniu test case z dynamicznym scenariuszem.
                    if (scenario == ScenarioType.DS1_MovingObstacles ||
                        scenario == ScenarioType.DS2_PathObstruction ||
                        scenario == ScenarioType.DS3_EscapingTarget)
                    {
                        _gridMap = _originalGridMap.Clone();
                        if (ShouldVisualize) RefreshBasemapColors();
                    }

                    if (ShouldLogDetailedBenchmark)
                    {
                        Debug.Log($"[Visualizer] Postęp: {FormatProgress(testId + 1, totalTestCases, "testów")}");
                    }
                }

                writer.Flush();
            }

            // Monitoring temperatury — koniec
            if (monitorCPUTemperature)
            {
                                HardwareMonitor.StopTemperatureMonitoring();
                float tempEnd = HardwareMonitor.GetCPUTemperature();
                Debug.Log($"[HardwareMonitor] Temp. CPU na końcu: {tempEnd:F1}°C " +
                          $"(delta: {tempEnd - tempStart:F1}°C)");
            }

            Debug.Log($"[Visualizer] ══════════════════════════════════════════");
            Debug.Log(_stopBenchmarkRequested
                ? "[Visualizer] BENCHMARK ZATRZYMANY PRZEZ UŻYTKOWNIKA"
                : "[Visualizer] ✓ BENCHMARK ZAKOŃCZONY");
            Debug.Log($"[Visualizer] Postęp końcowy: {FormatProgress(_currentTestCaseIndex, totalTestCases, "testów")}");
            Debug.Log($"[Visualizer] Wyniki: {Path.GetFullPath(resultsPath)}");
            Debug.Log($"[Visualizer] ══════════════════════════════════════════");
            _isAutoRunning = false;
        }

        private IEnumerator RunFullBenchmarkSuite()
        {
            string resultsPath = Path.Combine(Application.dataPath, "..", outputFileName);
            string checkpointPath = resultsPath + ".checkpoint";
            ScenarioType originalScenario = scenario;
            MapTopology originalMapSource = mapSource;
            int originalRandomSeed = randomSeed;
            List<TestCase> originalTestCases = _testCases;
            int estimatedTotalTestCases = EstimateFullSuiteTestCaseTotal();
            int originalVSyncCount = QualitySettings.vSyncCount;
            int originalTargetFrameRate = Application.targetFrameRate;
            bool originalRunInBackground = Application.runInBackground;
            StackTraceLogType originalLogStackTrace =
                Application.GetStackTraceLogType(LogType.Log);

            // Full suite nie renderuje animacji. Każde oczekiwanie na VSync lub rozbudowany
            // stack trace zwykłego heartbeat'u jest czystym narzutem poza eksperymentem.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

            string suiteFingerprint = BuildSuiteFingerprint();
            int resumeTestId = PrepareFullSuiteResume(
                resultsPath, checkpointPath, suiteFingerprint, estimatedTotalTestCases);

            _suiteTestId = 0;
            _stopBenchmarkRequested = false;
            _rowsSinceFlush = 0;
            _nextFullSuiteHeartbeatRealtime = Time.realtimeSinceStartupAsDouble;

            Debug.Log("[Visualizer] START PEŁNEGO BENCHMARKU HEADLESS");
            Debug.Log($"[Visualizer] Tryb algorytmów: {benchmarkMode} | Iteracje: {benchmarkIterations}");
            Debug.Log($"[Visualizer] Przerwanie: naciśnij {stopBenchmarkKey}, zapis CSV zostanie domknięty.");
            Debug.Log($"[Visualizer] Planowany postęp: {FormatProgress(0, estimatedTotalTestCases, "testów")}");
            Debug.Log($"[Visualizer] Wyniki CSV: {resultsPath}");
            if (resumeTestId > 0)
                Debug.Log($"[Visualizer] Wznawianie od TestID={resumeTestId}. " +
                          "Wszystkie wcześniejsze kompletne przypadki pozostają w CSV.");

            float tempStart = -1f;
            if (monitorCPUTemperature)
            {
                                HardwareMonitor.StartTemperatureMonitoring();
                tempStart = HardwareMonitor.GetCPUTemperature();
                Debug.Log($"[HardwareMonitor] Temp. CPU na starcie: {tempStart:F1}°C");
            }

            using (StreamWriter writer = new StreamWriter(resultsPath, resumeTestId > 0))
            {
                writer.AutoFlush = false;
                if (resumeTestId == 0)
                {
                    writer.WriteLine(BenchmarkMetrics.GetCsvHeader());
                    writer.Flush();
                    WriteFullSuiteCheckpoint(checkpointPath, suiteFingerprint, 0);
                }

                if (includeFileMapInFullSuite && LoadGridMap())
                {
                    _activeMapTopology = "FromFile";
                    _activeMapSeed = 0;
                    _activeMapDensity = 1f - ((float)_gridMap.CountWalkable() / (_gridMap.Width * _gridMap.Height));
                    _activeMapWidth = _gridMap.Width;
                    _activeMapHeight = _gridMap.Height;
                    _testCases = GenerateTestCasesForMap(_gridMap, randomSeed);
                    yield return StartCoroutine(RunAllScenariosForCurrentMap(
                        writer, estimatedTotalTestCases, resumeTestId, checkpointPath, suiteFingerprint));
                }

                foreach ((int width, int height) size in GetSuiteMapSizes())
                {
                    if (_stopBenchmarkRequested)
                        break;

                    foreach (MapTopology topology in GetSuiteTopologies())
                    {
                        if (_stopBenchmarkRequested)
                            break;

                        IMapGenerator generator = CreateMapGenerator(topology);
                        if (generator == null) continue;

                        foreach (float density in GetSuiteDensities())
                        {
                            if (_stopBenchmarkRequested)
                                break;

                            foreach (int seed in GetSuiteSeeds())
                            {
                                if (_stopBenchmarkRequested)
                                    break;

                                randomSeed = seed;
                                _shuffleRng = new System.Random(seed);
                                _gridMap = generator.Generate(size.width, size.height, density, seed);
                                _activeMapTopology = generator.TopologyName;
                                _activeMapSeed = seed;
                                _activeMapDensity = density;
                                _activeMapWidth = _gridMap.Width;
                                _activeMapHeight = _gridMap.Height;
                                _testCases = GenerateTestCasesForMap(_gridMap, seed);

                                Debug.Log($"[Visualizer] Mapa: {_activeMapTopology}, rozmiar={_activeMapWidth}x{_activeMapHeight}, density={density:P0}, seed={seed}, testy={_testCases.Count}");
                                yield return StartCoroutine(RunAllScenariosForCurrentMap(
                                    writer, estimatedTotalTestCases, resumeTestId, checkpointPath, suiteFingerprint));
                            }
                        }
                    }
                }

                writer.Flush();
            }

            if (monitorCPUTemperature)
            {
                                HardwareMonitor.StopTemperatureMonitoring();
                float tempEnd = HardwareMonitor.GetCPUTemperature();
                Debug.Log($"[HardwareMonitor] Temp. CPU na końcu: {tempEnd:F1}°C (delta: {tempEnd - tempStart:F1}°C)");
            }

            scenario = originalScenario;
            mapSource = originalMapSource;
            randomSeed = originalRandomSeed;
            _testCases = originalTestCases;
            _shuffleRng = new System.Random(randomSeed);
            QualitySettings.vSyncCount = originalVSyncCount;
            Application.targetFrameRate = originalTargetFrameRate;
            Application.runInBackground = originalRunInBackground;
            Application.SetStackTraceLogType(LogType.Log, originalLogStackTrace);

            if (_stopBenchmarkRequested)
            {
                Debug.LogWarning($"[Visualizer] Pełny benchmark został zatrzymany. Częściowe wyniki zapisane w: {Path.GetFullPath(resultsPath)}");
            }
            else
            {
                if (File.Exists(checkpointPath))
                    File.Delete(checkpointPath);
                Debug.Log($"[Visualizer] PEŁNY BENCHMARK ZAKOŃCZONY. Wyniki: {Path.GetFullPath(resultsPath)}");
            }

            Debug.Log($"[Visualizer] Postęp końcowy: {FormatProgress(_suiteTestId, estimatedTotalTestCases, "testów")}");
            _isAutoRunning = false;
        }

        private IEnumerator RunAllScenariosForCurrentMap(
            StreamWriter writer,
            int estimatedTotalTestCases,
            int resumeTestId,
            string checkpointPath,
            string suiteFingerprint)
        {
            GridMap baseMap = _gridMap.Clone();
            List<TestCase> mapTestCases = new List<TestCase>(_testCases);

            foreach (ScenarioType suiteScenario in Enum.GetValues(typeof(ScenarioType)))
            {
                if (_stopBenchmarkRequested)
                    yield break;

                scenario = suiteScenario;
                Debug.Log($"[Visualizer] Scenariusz: {scenario} | Mapa: {_activeMapTopology} | " +
                          $"Globalnie: {FormatProgress(_suiteTestId, estimatedTotalTestCases, "testów")}");

                for (int i = 0; i < mapTestCases.Count; i++)
                {
                    if (_stopBenchmarkRequested)
                        yield break;

                    if (_suiteTestId < resumeTestId)
                    {
                        _suiteTestId++;
                        continue;
                    }

                    _gridMap = baseMap.Clone();
                    _originalGridMap = baseMap.Clone();
                    _shuffleRng = new System.Random(_activeMapSeed + _suiteTestId + (int)scenario * 1000);

                    TestCase tc = mapTestCases[i];
                    Vector2Int startPos = new Vector2Int(tc.startX, tc.startY);
                    Vector2Int targetPos = new Vector2Int(tc.targetX, tc.targetY);

                    if (!_gridMap.IsWalkable(startPos) || !_gridMap.IsWalkable(targetPos))
                    {
                        Debug.LogWarning($"[Visualizer] Pomijam test {_suiteTestId + 1}: start/cel nie jest walkable. " +
                                         $"Globalnie: {FormatProgress(_suiteTestId + 1, estimatedTotalTestCases, "testów")}");
                        _suiteTestId++;
                        continue;
                    }

                    List<Vector2Int> scenarioChanges;
                    PrepareScenarioSnapshot(startPos, targetPos, _suiteTestId, out scenarioChanges);
                    GridMap scenarioSnapshot = _gridMap.Clone();

                    float currentDensity = 1f - ((float)scenarioSnapshot.CountWalkable() / (scenarioSnapshot.Width * scenarioSnapshot.Height));
                    List<IPathfindingAlgorithm> algorithmsToRun = GetAlgorithmsToRun();
                    ShuffleList(algorithmsToRun);

                    foreach (IPathfindingAlgorithm algorithm in algorithmsToRun)
                    {
                        if (_stopBenchmarkRequested)
                            yield break;

                        GridMap algorithmGrid = scenarioSnapshot;
                        var measurement = new MeasurementBatch();
                        if (Time.realtimeSinceStartupAsDouble >= _nextFullSuiteHeartbeatRealtime)
                        {
                            Debug.Log($"[Visualizer] Benchmark pracuje: " +
                                      $"{FormatProgress(_suiteTestId, estimatedTotalTestCases, "testów")} | " +
                                      $"TestID={_suiteTestId} | {scenario} | {algorithm.AlgorithmName}");
                            _nextFullSuiteHeartbeatRealtime =
                                Time.realtimeSinceStartupAsDouble + FullSuiteHeartbeatIntervalSeconds;
                        }
                        yield return StartCoroutine(MeasureAlgorithmBatched(
                            algorithm, algorithmGrid, startPos, targetPos,
                            _suiteTestId, currentDensity, measurement));

                        if (measurement.Cancelled || _stopBenchmarkRequested)
                            yield break;

                        BenchmarkMetrics metrics = measurement.Metrics;
                        ApplyTestCaseMetadata(metrics, tc);

                        if (monitorCPUTemperature)
                            metrics.CPUTemperature = HardwareMonitor.GetCPUTemperature();

                        writer.WriteLine(metrics.ToCsvRow());
                        _rowsSinceFlush++;
                    }

                    _suiteTestId++;
                    // Granica transakcji obejmuje małą paczkę kompletnych TestID. Awaria
                    // może cofnąć najwyżej tę paczkę; wznowienie obetnie ją i policzy ponownie.
                    if (_suiteTestId % FullSuiteCheckpointInterval == 0)
                    {
                        writer.Flush();
                        _rowsSinceFlush = 0;
                        WriteFullSuiteCheckpoint(
                            checkpointPath, suiteFingerprint, _suiteTestId);
                    }

                }
            }
        }

        private void PrepareScenarioSnapshot(Vector2Int startPos, Vector2Int targetPos, int testId,
            out List<Vector2Int> scenarioChanges)
        {
            scenarioChanges = null;

            if (scenario == ScenarioType.DS1_MovingObstacles)
            {
                _ds1Manager = CreateDS1ManagerForCurrentTest(_gridMap, startPos, targetPos, testId);
                _ds1Manager.VerifyObstaclePositions(_gridMap);
                return;
            }

            if (scenario == ScenarioType.DS2_PathObstruction)
            {
                return;
            }

            if (scenario == ScenarioType.DS3_EscapingTarget)
            {
                return;
            }
        }

        private MovingObstacleManager CreateDS1ManagerForCurrentTest(
            GridMap grid, Vector2Int startPos, Vector2Int targetPos, int testId)
        {
            if (_cachedDS1ReferenceTestId == testId &&
                _cachedDS1ReferenceStart == startPos &&
                _cachedDS1ReferenceTarget == targetPos &&
                _cachedDS1ReferenceMapSeed == _activeMapSeed &&
                _cachedDS1ReferenceMapWidth == grid.Width &&
                _cachedDS1ReferenceMapHeight == grid.Height &&
                _cachedDS1InitialManager != null)
                return _cachedDS1InitialManager.CloneInitialForGrid(grid);

            int seed = GetStableScenarioSeed(startPos, targetPos, 1000);
            var manager = new MovingObstacleManager(seed);
            List<Vector2Int> referencePath;
            if (_cachedDS1ReferenceTestId == testId &&
                _cachedDS1ReferenceStart == startPos &&
                _cachedDS1ReferenceTarget == targetPos &&
                _cachedDS1ReferenceMapSeed == _activeMapSeed &&
                _cachedDS1ReferenceMapWidth == grid.Width &&
                _cachedDS1ReferenceMapHeight == grid.Height &&
                _cachedDS1ReferencePath != null)
            {
                referencePath = _cachedDS1ReferencePath;
            }
            else
            {
                TestPointSelector.TryGetShortestPath(grid, startPos, targetPos,
                    out referencePath, out _);
                _cachedDS1ReferenceTestId = testId;
                _cachedDS1ReferenceStart = startPos;
                _cachedDS1ReferenceTarget = targetPos;
                _cachedDS1ReferenceMapSeed = _activeMapSeed;
                _cachedDS1ReferenceMapWidth = grid.Width;
                _cachedDS1ReferenceMapHeight = grid.Height;
                _cachedDS1ReferencePath = referencePath;
            }
            int adaptiveObstacleCount = Mathf.Clamp(
                Mathf.Max(movingObstacleCount, Mathf.CeilToInt(referencePath.Count / 12f)),
                1, 64);
            manager.GenerateObstacles(grid, adaptiveObstacleCount, startPos, targetPos,
                patrolLength, ShouldLogDetailedBenchmark, referencePath);
            // Cache musi pozostać nieruchomym szablonem. Zwracany manager jest
            // modyfikowany przez pierwszą iterację symulacji.
            _cachedDS1InitialManager = manager.CloneInitial();
            return manager;
        }

        private DS2DynamicState CreateDS2DynamicState(
            GridMap baseGrid, Vector2Int startPos, Vector2Int targetPos)
        {
            return new DS2DynamicState(
                BuildDS2ObstructionSchedule(baseGrid, startPos, targetPos));
        }

        private int GetDS2ObstructionLimit()
        {
            return Mathf.Max(1, pathObstructionChanges);
        }

        private List<DS2ObstructionEvent> BuildDS2ObstructionSchedule(
            GridMap baseGrid, Vector2Int startPos, Vector2Int targetPos)
        {
            int obstructionLimit = GetDS2ObstructionLimit();
            int spacing = Mathf.Max(1, pathObstructionSpacing);
            int lookahead = Mathf.Clamp(spacing / 2, 2, 4);
            var schedule = new List<DS2ObstructionEvent>(obstructionLimit);

            if (!TestPointSelector.TryGetShortestPath(baseGrid, startPos, targetPos,
                    out List<Vector2Int> referencePath, out _))
                return schedule;

            // Liczba zdarzeń rośnie wraz z długością trasy. Limit z Inspectora
            // pozostaje wyłącznie bezpiecznikiem dla bardzo dużych map.
            int adaptiveEventCount = Mathf.Max(0,
                Mathf.FloorToInt((referencePath.Count - lookahead - 1) / (float)spacing));
            int targetEventCount = Mathf.Min(obstructionLimit, adaptiveEventCount);
            if (targetEventCount == 0)
                return schedule;

            GridMap planningGrid = baseGrid.Clone();
            Vector2Int oraclePosition = startPos;
            int oracleStep = 0;
            int nextTriggerStep = spacing;
            int virtualStepLimit = Mathf.Max(
                referencePath.Count * 3,
                (targetEventCount + 1) * spacing);

            while (schedule.Count < targetEventCount &&
                   oraclePosition != targetPos && oracleStep < virtualStepLimit)
            {
                if (!TestPointSelector.TryGetShortestPath(
                        planningGrid, oraclePosition, targetPos,
                        out List<Vector2Int> oraclePath, out _))
                    break;

                int stepsToTrigger = nextTriggerStep - oracleStep;
                if (stepsToTrigger <= 0)
                    stepsToTrigger = spacing;

                int advance = Mathf.Min(stepsToTrigger, oraclePath.Count);
                if (advance <= 0)
                    break;

                oraclePosition = oraclePath[advance - 1];
                oracleStep += advance;
                if (advance < stepsToTrigger || oraclePosition == targetPos)
                    break;

                if (!TestPointSelector.TryGetShortestPath(
                        planningGrid, oraclePosition, targetPos,
                        out oraclePath, out _))
                    break;

                if (!TrySelectReachableDS2Block(
                        planningGrid, oraclePosition, targetPos, oraclePath,
                        lookahead, spacing, out Vector2Int blockedCell))
                {
                    nextTriggerStep += spacing;
                    continue;
                }

                planningGrid.SetWalkable(blockedCell, false);
                schedule.Add(new DS2ObstructionEvent(
                    nextTriggerStep, new List<Vector2Int> { blockedCell }));
                nextTriggerStep += spacing;
            }

            return schedule;
        }

        private bool TrySelectReachableDS2Block(
            GridMap planningGrid,
            Vector2Int oraclePosition,
            Vector2Int targetPos,
            List<Vector2Int> oraclePath,
            int lookahead,
            int searchWindow,
            out Vector2Int blockedCell)
        {
            blockedCell = default;
            if (oraclePath == null || oraclePath.Count <= lookahead)
                return false;

            int firstIndex = lookahead - 1;
            int lastIndex = Mathf.Min(
                oraclePath.Count - 2,
                firstIndex + Mathf.Max(1, searchWindow) - 1);

            for (int i = firstIndex; i <= lastIndex; i++)
            {
                Vector2Int candidate = oraclePath[i];
                if (IsProtectedPoint(candidate, oraclePosition, targetPos) ||
                    !planningGrid.IsWalkable(candidate))
                    continue;

                GridMap trialGrid = planningGrid.Clone();
                trialGrid.SetWalkable(candidate, false);
                if (!TestPointSelector.TryGetShortestPathLength(
                        trialGrid, oraclePosition, targetPos, out _))
                    continue;

                blockedCell = candidate;
                return true;
            }

            return false;
        }

        private List<Vector2Int> BuildDS2EuclideanCorridorCandidates(
            GridMap baseGrid, Vector2Int startPos, Vector2Int targetPos)
        {
            var seen = new HashSet<Vector2Int>();
            var candidates = new List<Vector2Int>();
            int samples = Mathf.Max(Math.Abs(targetPos.x - startPos.x), Math.Abs(targetPos.y - startPos.y));
            int radius = 2;

            for (int i = 1; i < samples; i++)
            {
                float t = i / (float)samples;
                int centerX = Mathf.RoundToInt(Mathf.Lerp(startPos.x, targetPos.x, t));
                int centerY = Mathf.RoundToInt(Mathf.Lerp(startPos.y, targetPos.y, t));

                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) > radius + 1)
                            continue;

                        Vector2Int pos = new Vector2Int(centerX + dx, centerY + dy);
                        if (!baseGrid.IsValidCoordinate(pos.x, pos.y) || !baseGrid.IsWalkable(pos))
                            continue;

                        if (IsProtectedPoint(pos, startPos, targetPos))
                            continue;

                        if (seen.Add(pos))
                            candidates.Add(pos);
                    }
                }
            }

            return candidates;
        }

        private bool TryPickDS2Candidate(
            List<Vector2Int> candidates,
            HashSet<Vector2Int> used,
            Vector2Int startPos,
            Vector2Int targetPos,
            float targetProgress,
            out Vector2Int selected)
        {
            selected = default;
            bool found = false;
            float bestScore = float.MaxValue;

            foreach (Vector2Int candidate in candidates)
            {
                if (used.Contains(candidate))
                    continue;

                float progress = GetProjectionProgress(candidate, startPos, targetPos);
                float score = Math.Abs(progress - targetProgress);
                if (score < bestScore)
                {
                    bestScore = score;
                    selected = candidate;
                    found = true;
                }
            }

            return found;
        }

        private float GetProjectionProgress(Vector2Int pos, Vector2Int startPos, Vector2Int targetPos)
        {
            Vector2 start = new Vector2(startPos.x, startPos.y);
            Vector2 target = new Vector2(targetPos.x, targetPos.y);
            Vector2 point = new Vector2(pos.x, pos.y);
            Vector2 line = target - start;
            float denominator = Vector2.Dot(line, line);
            if (denominator <= 0.0001f)
                return 0f;

            return Mathf.Clamp01(Vector2.Dot(point - start, line) / denominator);
        }

        private void AddDS2ScheduledCell(
            Vector2Int pos,
            HashSet<Vector2Int> used,
            List<Vector2Int> eventCells,
            int obstructionLimit)
        {
            if (used.Count >= obstructionLimit || !used.Add(pos))
                return;

            eventCells.Add(pos);
        }

        private List<Vector2Int> ApplyDS2ScheduledObstructions(
            GridMap grid,
            int agentStep,
            Vector2Int currentPos,
            DS2DynamicState state)
        {
            var changes = new List<Vector2Int>();

            while (state.NextEventIndex < state.Schedule.Count &&
                   agentStep >= state.Schedule[state.NextEventIndex].TriggerStep)
            {
                foreach (Vector2Int pos in state.Schedule[state.NextEventIndex].Cells)
                {
                    if (pos == currentPos || !grid.IsWalkable(pos) || state.BlockedCells.Contains(pos))
                        continue;

                    grid.SetWalkable(pos, false);
                    state.BlockedCells.Add(pos);
                    changes.Add(pos);
                }

                state.NextEventIndex++;
            }

            return changes;
        }

        private List<Vector2Int> ApplyDS2DynamicObstructions(
            GridMap grid,
            List<Vector2Int> plannedPath,
            int nextStepIndex,
            Vector2Int startPos,
            Vector2Int currentPos,
            Vector2Int targetPos,
            DS2DynamicState state)
        {
            var changes = new List<Vector2Int>();
            int obstructionLimit = GetDS2ObstructionLimit();
            if (plannedPath == null || plannedPath.Count == 0 || state.BlockedCells.Count >= obstructionLimit)
                return changes;

            int spacing = Mathf.Max(1, pathObstructionSpacing);
            if (nextStepIndex > 0 && nextStepIndex % spacing != 0)
                return changes;

            int anchorIndex = Mathf.Min(plannedPath.Count - 1, nextStepIndex + spacing);
            int clusterBudget = Mathf.Min(Mathf.Max(1, obstructionLimit / 4),
                obstructionLimit - state.BlockedCells.Count);

            var candidates = BuildDS2ObstructionCandidates(plannedPath, anchorIndex);
            ShuffleList(candidates, state.Rng);

            // Pierwsze pole na Ĺ›cieĹĽce zostawiamy jako priorytet, ĹĽeby blokada faktycznie wymuszaĹ‚a rekalkulacjÄ™.
            Vector2Int anchor = plannedPath[anchorIndex];
            TryAddDS2Block(grid, anchor, startPos, currentPos, targetPos, state, changes);

            foreach (Vector2Int candidate in candidates)
            {
                if (changes.Count >= clusterBudget || state.BlockedCells.Count >= obstructionLimit)
                    break;

                TryAddDS2Block(grid, candidate, startPos, currentPos, targetPos, state, changes);
            }

            return changes;
        }

        private List<Vector2Int> BuildDS2ObstructionCandidates(List<Vector2Int> plannedPath, int anchorIndex)
        {
            var candidates = new List<Vector2Int>();
            Vector2Int anchor = plannedPath[anchorIndex];
            Vector2Int previous = anchorIndex > 0 ? plannedPath[anchorIndex - 1] : anchor;
            Vector2Int next = anchorIndex + 1 < plannedPath.Count ? plannedPath[anchorIndex + 1] : anchor;
            Vector2Int direction = new Vector2Int(Math.Sign(next.x - previous.x), Math.Sign(next.y - previous.y));
            if (direction == Vector2Int.zero)
                direction = Vector2Int.right;

            Vector2Int perpendicular = new Vector2Int(-direction.y, direction.x);
            if (perpendicular == Vector2Int.zero)
                perpendicular = Vector2Int.up;

            candidates.Add(anchor + perpendicular);
            candidates.Add(anchor - perpendicular);
            candidates.Add(anchor + direction);
            candidates.Add(anchor - direction);
            candidates.Add(anchor + perpendicular + direction);
            candidates.Add(anchor - perpendicular + direction);

            int lookaheadLimit = Mathf.Min(plannedPath.Count, anchorIndex + pathObstructionSpacing * 2 + 1);
            for (int i = anchorIndex + 1; i < lookaheadLimit; i++)
                candidates.Add(plannedPath[i]);

            return candidates;
        }

        private void TryAddDS2Block(
            GridMap grid,
            Vector2Int pos,
            Vector2Int startPos,
            Vector2Int currentPos,
            Vector2Int targetPos,
            DS2DynamicState state,
            List<Vector2Int> changes)
        {
            if (!grid.IsValidCoordinate(pos.x, pos.y))
                return;

            if (pos == startPos || pos == currentPos || IsProtectedPoint(pos, currentPos, targetPos))
                return;

            if (_originalGridMap != null && !_originalGridMap.IsWalkable(pos))
                return;

            if (!grid.IsWalkable(pos) || state.BlockedCells.Contains(pos))
                return;

            grid.SetWalkable(pos, false);
            state.BlockedCells.Add(pos);
            changes.Add(pos);
        }

        private DS3EscapingTargetState CreateDS3EscapingTargetState(
            Vector2Int startPos, Vector2Int targetPos, int testId)
        {
            int seed = GetStableScenarioSeed(startPos, targetPos, 8000);
            return new DS3EscapingTargetState(targetPos, startPos, seed);
        }

        private int GetStableScenarioSeed(
            Vector2Int startPos, Vector2Int targetPos, int scenarioOffset)
        {
            int baseSeed = runFullBenchmarkSuite ? _activeMapSeed : randomSeed;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + baseSeed;
                hash = hash * 31 + startPos.x;
                hash = hash * 31 + startPos.y;
                hash = hash * 31 + targetPos.x;
                hash = hash * 31 + targetPos.y;
                hash = hash * 31 + scenarioOffset;
                return hash;
            }
        }

        private static readonly Vector2Int[] DS3EscapeDirections =
        {
            new Vector2Int(1, 0),   // prawo
            new Vector2Int(-1, 0),  // lewo
            new Vector2Int(0, 1),   // góra
            new Vector2Int(0, -1),  // dół
            new Vector2Int(1, 1),   // góra-prawo
            new Vector2Int(-1, 1),  // góra-lewo
            new Vector2Int(1, -1),  // dół-prawo
            new Vector2Int(-1, -1)  // dół-lewo
        };

        /// <summary>
        /// Co 2 kroki agenta, punkt końcowy ucieka o 1 pole w losowym kierunku.
        /// Kierunek nie może zbliżać celu do stałego punktu startowego testu.
        /// Dzięki temu trajektoria celu jest identyczna dla wszystkich algorytmów.
        /// Zwraca true jeśli cel się przesunął.
        /// </summary>
        private bool TryEscapeTarget(
            GridMap grid,
            DS3EscapingTargetState state)
        {
            if (state.TotalEscapes >= maxTargetEscapes)
                return false;

            state.StepsSinceLastEscape++;
            if (state.StepsSinceLastEscape < 2)
                return false;

            state.StepsSinceLastEscape = 0;

            // Zbierz dostępne kierunki ucieczki
            var validDirs = new List<Vector2Int>();
            foreach (var dir in DS3EscapeDirections)
            {
                Vector2Int candidate = state.CurrentTarget + dir;
                if (!grid.IsValidCoordinate(candidate.x, candidate.y))
                    continue;
                if (!CanMoveOnCurrentGrid(grid, state.CurrentTarget, candidate))
                    continue;

                float oldDistance = TestPointSelector.CalculateOctagonalDistance(
                    state.EscapeAnchor, state.CurrentTarget);
                float newDistance = TestPointSelector.CalculateOctagonalDistance(
                    state.EscapeAnchor, candidate);

                if (newDistance + 0.0001f < oldDistance)
                    continue;

                validDirs.Add(dir);
            }

            if (validDirs.Count == 0)
                return false;

            // Sortuj deterministycznie przed losowaniem
            validDirs.Sort(CompareVector2Int);

            Vector2Int chosen = validDirs[state.Rng.Next(validDirs.Count)];
            state.CurrentTarget += chosen;
            state.TotalEscapes++;
            return true;
        }

        private int CompareVector2Int(Vector2Int a, Vector2Int b)
        {
            int compare = a.x.CompareTo(b.x);
            if (compare != 0)
                return compare;

            return a.y.CompareTo(b.y);
        }

        private bool IsProtectedPoint(Vector2Int pos, Vector2Int startPos, Vector2Int targetPos)
        {
            return Math.Abs(pos.x - startPos.x) <= 1 && Math.Abs(pos.y - startPos.y) <= 1 ||
                   Math.Abs(pos.x - targetPos.x) <= 1 && Math.Abs(pos.y - targetPos.y) <= 1;
        }

        private IEnumerator AnimateDS1Obstacles(List<(Vector2Int oldPos, Vector2Int newPos)> moves)
        {
            if (moves == null || moves.Count == 0) yield break;

            float duration = 0.4f; // Czas animacji przesunięcia
            float elapsed = 0f;
            
            List<(GameObject obj, Vector3 startWorld, Vector3 endWorld)> animData = new List<(GameObject, Vector3, Vector3)>();

            foreach (var m in moves)
            {
                GameObject obs = _spawnedObstacles[m.oldPos.x, m.oldPos.y];
                if (obs != null)
                {
                    Vector3 startW = obs.transform.position;
                    Vector3 endW = new Vector3(m.newPos.x, m.newPos.y, -0.1f);
                    animData.Add((obs, startW, endW));

                    // Zaktualizuj tablicę od razu, by RefreshBasemapColors nie tworzył duplikatów
                    _spawnedObstacles[m.newPos.x, m.newPos.y] = obs;
                    if (m.oldPos != m.newPos) // Jeśli się rzeczywiście przemieścił
                        _spawnedObstacles[m.oldPos.x, m.oldPos.y] = null;
                }
            }

            // Płynna interpolacja pozycji
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Smooth step dla ładniejszego efektu
                t = t * t * (3f - 2f * t);

                foreach (var anim in animData)
                {
                    anim.obj.transform.position = Vector3.Lerp(anim.startWorld, anim.endWorld, t);
                }
                yield return null;
            }

            // Dociągnij do końca i zaktualizuj nazwy
            foreach (var anim in animData)
            {
                anim.obj.transform.position = anim.endWorld;
                anim.obj.name = $"Obstacle_{(int)anim.endWorld.x}_{(int)anim.endWorld.y}";
            }
        }

        private Pathfinding.Core.PathfindingResult FindPathForVisualization(
            IPathfindingAlgorithm algorithm,
            GridMap grid,
            Vector2Int startPos,
            Vector2Int targetPos)
        {
            bool previousHistoryRecording = PathfindingRuntimeOptions.RecordExploredNodesHistory;
            try
            {
                PathfindingRuntimeOptions.RecordExploredNodesHistory = true;
                return algorithm.FindPath(grid, startPos, targetPos);
            }
            finally
            {
                PathfindingRuntimeOptions.RecordExploredNodesHistory = previousHistoryRecording;
            }
        }

        private IEnumerator VisualizeDS1ReplanningRoutine(
            IPathfindingAlgorithm algorithm,
            Pathfinding.Core.PathfindingResult initialResult,
            Vector2Int startPos,
            Vector2Int targetPos,
            int testId)
        {
            _isVisualizing = true;

            GridMap visualGrid = _originalGridMap.Clone();
            MovingObstacleManager visualManager = CreateDS1ManagerForCurrentTest(
                visualGrid, startPos, targetPos, testId);
            _ds1Manager = visualManager;
            visualManager.VerifyObstaclePositions(visualGrid);

            _gridMap = visualGrid;
            RefreshBasemapColors();

            if (_agentObject != null)
            {
                _agentObject.SetActive(true);
                _agentObject.transform.position = new Vector3(startPos.x, startPos.y, -2f);
            }

            Vector2Int currentPos = startPos;
            Pathfinding.Core.PathfindingResult currentResult = FindPathForVisualization(algorithm, visualGrid, currentPos, targetPos);
            int replanCount = 0;
            int consecutiveFailedReplans = 0;
            int safetyLimit = GetDynamicTickLimit(
                visualGrid, startPos, targetPos, testId);

            while (currentPos != targetPos && safetyLimit > 0)
            {
                yield return StartCoroutine(PaintPathfindingOverlay(currentResult, startPos, targetPos, true));

                if (currentResult == null || !currentResult.PathFound || currentResult.Path == null || currentResult.Path.Count == 0)
                {
                    if (replanCount >= Mathf.Max(1, maxDS1Replans) ||
                        consecutiveFailedReplans >= Mathf.Max(1, maxDS1ConsecutiveFailedReplans))
                        break;

                    List<(Vector2Int oldPos, Vector2Int newPos)> waitMoves =
                        visualManager.StepAll(visualGrid, currentPos);
                    safetyLimit--;
                    yield return StartCoroutine(AnimateDS1Obstacles(waitMoves));
                    replanCount++;
                    currentResult = FindPathForVisualization(
                        algorithm, visualGrid, currentPos, targetPos);
                    if (currentResult == null || !currentResult.PathFound ||
                        currentResult.Path == null || currentResult.Path.Count == 0)
                        consecutiveFailedReplans++;
                    else
                        consecutiveFailedReplans = 0;
                    continue;
                }

                bool replanned = false;
                for (int i = 0; i < currentResult.Path.Count; i++)
                {
                    Vector2Int nextPos = currentResult.Path[i];

                    List<(Vector2Int oldPos, Vector2Int newPos)> moves =
                        visualManager.StepAll(visualGrid, currentPos);
                    safetyLimit--;
                    visualManager.VerifyObstaclePositions(visualGrid);
                    yield return StartCoroutine(AnimateDS1Obstacles(moves));

                    _gridMap = visualGrid;
                    RefreshBasemapColors();
                    yield return StartCoroutine(PaintPathfindingOverlay(currentResult, startPos, targetPos, false));

                    if (!CanMoveOnCurrentGrid(visualGrid, currentPos, nextPos))
                    {
                        if (replanCount >= Mathf.Max(1, maxDS1Replans))
                        {
                            safetyLimit = 0;
                            break;
                        }

                        replanCount++;
                        if (_basemapRenderers != null)
                        {
                            _basemapRenderers[currentPos.x, currentPos.y].color = colorCurrentAgentCell;
                            _basemapRenderers[nextPos.x, nextPos.y].color = colorReplanPause;
                            _basemapRenderers[startPos.x, startPos.y].color = colorStart;
                            _basemapRenderers[targetPos.x, targetPos.y].color = colorTarget;
                        }
                        Debug.Log($"[Visualizer][DS1] {algorithm.AlgorithmName}: rekalkulacja #{replanCount}, zablokowany krok {nextPos}.");
                        currentResult = FindPathForVisualization(algorithm, visualGrid, currentPos, targetPos);
                        if (currentResult == null || !currentResult.PathFound ||
                            currentResult.Path == null || currentResult.Path.Count == 0)
                        {
                            consecutiveFailedReplans++;
                            if (consecutiveFailedReplans >=
                                Mathf.Max(1, maxDS1ConsecutiveFailedReplans))
                                safetyLimit = 0;
                        }
                        else
                        {
                            consecutiveFailedReplans = 0;
                        }
                        replanned = true;

                        // Najpierw pokazujemy zatrzymanie i nową trasę. Dopiero potem
                        // agent wykonuje pierwszy krok replanu, który może prowadzić
                        // z powrotem, jeśli jest to najkrótsze legalne obejście.
                        yield return new WaitForSeconds(replanPauseDuration);

                        // Replan odbywa się po ruchu środowiska, więc agent może
                        // wykonać pierwszy krok nowej trasy jeszcze w tym samym ticku.
                        if (currentResult != null && currentResult.PathFound &&
                            currentResult.Path != null && currentResult.Path.Count > 0)
                        {
                            Vector2Int replannedNext = currentResult.Path[0];
                            if (CanMoveOnCurrentGrid(visualGrid, currentPos, replannedNext))
                            {
                                yield return StartCoroutine(MoveAgentTo(replannedNext));
                                currentPos = replannedNext;
                                consecutiveFailedReplans = 0;
                                currentResult.Path.RemoveAt(0);
                            }
                        }

                        break;
                    }

                    yield return StartCoroutine(MoveAgentTo(nextPos));
                    currentPos = nextPos;
                    consecutiveFailedReplans = 0;

                    if (_basemapRenderers != null && currentPos != targetPos)
                        _basemapRenderers[currentPos.x, currentPos.y].color = colorPath;

                    if (currentPos == targetPos)
                        break;
                }

                if (!replanned && currentPos != targetPos)
                    currentResult = FindPathForVisualization(algorithm, visualGrid, currentPos, targetPos);
            }

            Debug.Log($"[Visualizer][DS1] {algorithm.AlgorithmName}: wizualizacja zakończona, rekalkulacje={replanCount}.");
            _isVisualizing = false;
        }

        private IEnumerator VisualizeDS2ReplanningRoutine(
            IPathfindingAlgorithm algorithm,
            Vector2Int startPos,
            Vector2Int targetPos,
            int testId)
        {
            _isVisualizing = true;

            GridMap visualGrid = _originalGridMap.Clone();
            DS2DynamicState ds2State = CreateDS2DynamicState(visualGrid, startPos, targetPos);
            _gridMap = visualGrid;
            RefreshBasemapColors();

            if (_agentObject != null)
            {
                _agentObject.SetActive(true);
                _agentObject.transform.position = new Vector3(startPos.x, startPos.y, -2f);
            }

            Vector2Int currentPos = startPos;
            Pathfinding.Core.PathfindingResult currentResult = FindPathForVisualization(algorithm, visualGrid, currentPos, targetPos);
            int replanCount = 0;
            int ds2Step = 0;
            int safetyLimit = visualGrid.Width * visualGrid.Height * 2;

            while (currentPos != targetPos && safetyLimit-- > 0)
            {
                yield return StartCoroutine(PaintPathfindingOverlay(currentResult, startPos, targetPos, true));

                if (currentResult == null || !currentResult.PathFound || currentResult.Path == null || currentResult.Path.Count == 0)
                {
                    Debug.LogWarning($"[Visualizer][DS2] {algorithm.AlgorithmName}: brak dalszej drogi po {replanCount} rekalkulacjach.");
                    break;
                }

                bool replanned = false;
                for (int i = 0; i < currentResult.Path.Count; i++)
                {
                    Vector2Int nextPos = currentResult.Path[i];
                    List<Vector2Int> changes = ApplyDS2ScheduledObstructions(visualGrid, ds2Step, currentPos, ds2State);

                    _gridMap = visualGrid;
                    RefreshBasemapColors();
                    ShowChangeMarkers(changes);
                    yield return StartCoroutine(PaintPathfindingOverlay(currentResult, startPos, targetPos, false));
                    ShowChangeMarkers(changes);

                    // DS2 modeluje lokalne wykrycie kolizji: wcześniejsza blokada
                    // dalszego fragmentu trasy nie wyzwala jeszcze rekalkulacji.
                    if (!CanMoveOnCurrentGrid(visualGrid, currentPos, nextPos))
                    {
                        replanCount++;
                        if (_basemapRenderers != null)
                        {
                            _basemapRenderers[currentPos.x, currentPos.y].color = colorCurrentAgentCell;
                            _basemapRenderers[nextPos.x, nextPos.y].color = colorReplanPause;
                            _basemapRenderers[startPos.x, startPos.y].color = colorStart;
                            _basemapRenderers[targetPos.x, targetPos.y].color = colorTarget;
                        }

                        Debug.Log($"[Visualizer][DS2] {algorithm.AlgorithmName}: rekalkulacja #{replanCount}, zablokowany krok {nextPos}.");
                        currentResult = FindPathForVisualization(algorithm, visualGrid, currentPos, targetPos);
                        replanned = true;
                        yield return new WaitForSeconds(replanPauseDuration);
                        break;
                    }

                    yield return StartCoroutine(MoveAgentTo(nextPos));
                    currentPos = nextPos;
                    ds2Step++;

                    if (_basemapRenderers != null && currentPos != targetPos)
                        _basemapRenderers[currentPos.x, currentPos.y].color = colorPath;

                    if (currentPos == targetPos || --safetyLimit <= 0)
                        break;
                }

                if (!replanned && currentPos != targetPos)
                    currentResult = FindPathForVisualization(algorithm, visualGrid, currentPos, targetPos);
            }

            Debug.Log($"[Visualizer][DS2] {algorithm.AlgorithmName}: wizualizacja zakoĹ„czona, rekalkulacje={replanCount}, blokady={ds2State.BlockedCells.Count}.");
            _isVisualizing = false;
        }

        private IEnumerator VisualizeDS3ReplanningRoutine(
            IPathfindingAlgorithm algorithm,
            Vector2Int startPos,
            Vector2Int targetPos,
            int testId)
        {
            _isVisualizing = true;

            GridMap visualGrid = _originalGridMap.Clone();
            DS3EscapingTargetState escapeState = CreateDS3EscapingTargetState(
                startPos, targetPos, testId);
            _gridMap = visualGrid;
            RefreshBasemapColors();

            if (_agentObject != null)
            {
                _agentObject.SetActive(true);
                _agentObject.transform.position = new Vector3(startPos.x, startPos.y, -2f);
            }

            Vector2Int currentPos = startPos;
            Vector2Int currentTarget = escapeState.CurrentTarget;
            Pathfinding.Core.PathfindingResult currentResult = FindPathForVisualization(algorithm, visualGrid, currentPos, currentTarget);
            int replanCount = 0;
            int safetyLimit = visualGrid.Width * visualGrid.Height * 4;

            while (currentPos != escapeState.CurrentTarget && safetyLimit-- > 0)
            {
                currentTarget = escapeState.CurrentTarget;
                yield return StartCoroutine(PaintPathfindingOverlay(currentResult, startPos, currentTarget, true));

                if (currentResult == null || !currentResult.PathFound || currentResult.Path == null || currentResult.Path.Count == 0)
                {
                    Debug.LogWarning($"[Visualizer][DS3] {algorithm.AlgorithmName}: brak dalszej drogi po {replanCount} rekalkulacjach.");
                    break;
                }

                bool replanned = false;
                for (int i = 0; i < currentResult.Path.Count; i++)
                {
                    Vector2Int nextPos = currentResult.Path[i];

                    if (!CanMoveOnCurrentGrid(visualGrid, currentPos, nextPos))
                    {
                        replanCount++;
                        currentResult = FindPathForVisualization(algorithm, visualGrid, currentPos, escapeState.CurrentTarget);
                        replanned = true;
                        yield return new WaitForSeconds(replanPauseDuration);
                        break;
                    }

                    yield return StartCoroutine(MoveAgentTo(nextPos));
                    currentPos = nextPos;

                    if (_basemapRenderers != null && currentPos != escapeState.CurrentTarget)
                        _basemapRenderers[currentPos.x, currentPos.y].color = colorPath;

                    if (currentPos == escapeState.CurrentTarget)
                        break;

                    // Co 2 kroki agenta — cel ucieka
                    bool escaped = TryEscapeTarget(visualGrid, escapeState);
                    if (escaped)
                    {
                        replanCount++;
                        Debug.Log($"[Visualizer][DS3] {algorithm.AlgorithmName}: cel uciekł na {escapeState.CurrentTarget} (ucieczka #{escapeState.TotalEscapes}).");

                        // Odśwież kolory celu na basemap
                        _gridMap = visualGrid;
                        RefreshBasemapColors();
                        if (_basemapRenderers != null)
                        {
                            _basemapRenderers[startPos.x, startPos.y].color = colorStart;
                            _basemapRenderers[escapeState.CurrentTarget.x, escapeState.CurrentTarget.y].color = colorTarget;
                        }

                        currentResult = FindPathForVisualization(algorithm, visualGrid, currentPos, escapeState.CurrentTarget);
                        replanned = true;
                        yield return new WaitForSeconds(replanPauseDuration);
                        break;
                    }

                    if (--safetyLimit <= 0)
                        break;
                }

                if (!replanned && currentPos != escapeState.CurrentTarget)
                    currentResult = FindPathForVisualization(algorithm, visualGrid, currentPos, escapeState.CurrentTarget);
            }

            Debug.Log($"[Visualizer][DS3] {algorithm.AlgorithmName}: wizualizacja zakończona, rekalkulacje={replanCount}, ucieczki={escapeState.TotalEscapes}.");
            _isVisualizing = false;
        }

        private IEnumerator PaintPathfindingOverlay(
            Pathfinding.Core.PathfindingResult result,
            Vector2Int startPos,
            Vector2Int targetPos,
            bool animateExplored)
        {
            RefreshBasemapColors();

            if (_basemapRenderers != null)
            {
                _basemapRenderers[startPos.x, startPos.y].color = colorStart;
                _basemapRenderers[targetPos.x, targetPos.y].color = colorTarget;
            }

            if (result == null || !result.PathFound)
                yield break;

            foreach (Vector2Int pos in result.ExploredNodesHistory)
            {
                if (pos != startPos && pos != targetPos &&
                    pos.x >= 0 && pos.x < _gridMap.Width && pos.y >= 0 && pos.y < _gridMap.Height)
                {
                    _basemapRenderers[pos.x, pos.y].color = colorExplored;
                    if (animateExplored)
                        yield return new WaitForSeconds(Mathf.Min(visualizationStepDelay, 0.02f));
                }
            }

            foreach (Vector2Int pos in result.Path)
            {
                if (pos != startPos && pos != targetPos &&
                    pos.x >= 0 && pos.x < _gridMap.Width && pos.y >= 0 && pos.y < _gridMap.Height)
                {
                    _basemapRenderers[pos.x, pos.y].color = colorPath;
                }
            }
        }

        private IEnumerator MoveAgentTo(Vector2Int gridPos)
        {
            if (_agentObject == null)
                yield break;

            Vector3 nextPosition = new Vector3(gridPos.x, gridPos.y, -2f);
            while (Vector3.Distance(_agentObject.transform.position, nextPosition) > 0.01f)
            {
                _agentObject.transform.position = Vector3.MoveTowards(
                    _agentObject.transform.position, nextPosition, agentMoveSpeed * Time.deltaTime);
                yield return null;
            }

            _agentObject.transform.position = nextPosition;
        }

        // ─────────────────────────────────────────────────────────
        //  POMIAR ALGORYTMU
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Uruchamia algorytm N razy (benchmarkIterations), zbiera metryki.
        /// Iteracja 0 = cold start (rejestrowana osobno w CSV).
        /// Wynik wizualny pochodzi z iteracji 0 (zawiera ExploredNodesHistory).
        /// GC.Collect() wywoływany TYLKO raz przed cold start.
        /// </summary>
        private void MeasureAlgorithm(
            IPathfindingAlgorithm algorithm, GridMap grid,
            Vector2Int startPos, Vector2Int targetPos,
            int testId, float density,
            out BenchmarkMetrics metrics, out Pathfinding.Core.PathfindingResult visualResult)
        {
            if (scenario == ScenarioType.DS1_MovingObstacles)
            {
                MeasureDS1DynamicAlgorithm(algorithm, startPos, targetPos, testId, density,
                    out metrics, out visualResult);
                return;
            }

            if (scenario == ScenarioType.DS2_PathObstruction)
            {
                MeasureDS2DynamicAlgorithm(algorithm, startPos, targetPos, testId, density,
                    out metrics, out visualResult);
                return;
            }

            if (scenario == ScenarioType.DS3_EscapingTarget)
            {
                MeasureDS3DynamicAlgorithm(algorithm, startPos, targetPos, testId, density,
                    out metrics, out visualResult);
                return;
            }

            var allResults = new List<Pathfinding.Core.PathfindingResult>(benchmarkIterations);
            bool previousHistoryRecording = PathfindingRuntimeOptions.RecordExploredNodesHistory;

            try
            {
                for (int iter = 0; iter < benchmarkIterations; iter++)
                {
                    PathfindingRuntimeOptions.RecordExploredNodesHistory = ShouldVisualize && iter == 0;

                    // GC.Collect() TYLKO przed cold start — nie blokuj silnika w warm iterations
                    long gcBefore = BeginAllocationMeasurement(
                        iter == 0 && forceGcBeforeColdStart);

                    Pathfinding.Core.PathfindingResult result =
                        algorithm.FindPath(grid, startPos, targetPos);

                    long gcAfter = GC.GetAllocatedBytesForCurrentThread();
                    result.GCAllocBytes = Math.Max(0, gcAfter - gcBefore);
                    CalculateReportedPathMetrics(result, startPos, iter == 0);

                    allResults.Add(result);
                }
            }
            finally
            {
                PathfindingRuntimeOptions.RecordExploredNodesHistory = previousHistoryRecording;
            }

            visualResult = allResults[0];

            string scenarioLabel = scenario switch
            {
                ScenarioType.Static => "Static",
                ScenarioType.DS1_MovingObstacles => "DS1_MovingObstacles",
                ScenarioType.DS2_PathObstruction => "DS2_PathObstruction",
                ScenarioType.DS3_EscapingTarget => "DS3_EscapingTarget",
                _ => "Static"
            };

            metrics = new BenchmarkMetrics
            {
                AlgorithmName = algorithm.AlgorithmName,
                TestID = testId,
                StartX = startPos.x,
                StartY = startPos.y,
                TargetX = targetPos.x,
                TargetY = targetPos.y,
                Scenario = scenarioLabel,
                ObstacleDensity = density,
                MapTopology = _activeMapTopology,
                MapSeed = _activeMapSeed,
                MapDensity = _activeMapDensity,
                MapWidth = _activeMapWidth,
                MapHeight = _activeMapHeight
            };
            metrics.AggregateFrom(allResults);
        }

        private void MeasureDS1DynamicAlgorithm(
            IPathfindingAlgorithm algorithm,
            Vector2Int startPos, Vector2Int targetPos,
            int testId, float density,
            out BenchmarkMetrics metrics, out Pathfinding.Core.PathfindingResult visualResult)
        {
            var allResults = new List<Pathfinding.Core.PathfindingResult>(benchmarkIterations);
            bool previousHistoryRecording = PathfindingRuntimeOptions.RecordExploredNodesHistory;

            try
            {
                for (int iter = 0; iter < benchmarkIterations; iter++)
                {
                    PathfindingRuntimeOptions.RecordExploredNodesHistory = false;

                    long gcBefore = BeginAllocationMeasurement(
                        iter == 0 && forceGcBeforeColdStart);

                    Pathfinding.Core.PathfindingResult result =
                        RunDS1DynamicSimulation(algorithm, startPos, targetPos, testId);

                    long gcAfter = GC.GetAllocatedBytesForCurrentThread();
                    result.GCAllocBytes = Math.Max(0, gcAfter - gcBefore);
                    CalculateReportedPathMetrics(result, startPos, iter == 0);

                    allResults.Add(result);
                }
            }
            finally
            {
                PathfindingRuntimeOptions.RecordExploredNodesHistory = previousHistoryRecording;
            }

            visualResult = allResults.Count > 0 ? allResults[0] : null;
            EnsureDeterministicLogicalResults(
                allResults, "DS1_MovingObstacles", algorithm.AlgorithmName, testId);
            metrics = new BenchmarkMetrics
            {
                AlgorithmName = algorithm.AlgorithmName,
                TestID = testId,
                StartX = startPos.x,
                StartY = startPos.y,
                TargetX = targetPos.x,
                TargetY = targetPos.y,
                Scenario = "DS1_MovingObstacles",
                ObstacleDensity = density,
                MapTopology = _activeMapTopology,
                MapSeed = _activeMapSeed,
                MapDensity = _activeMapDensity,
                MapWidth = _activeMapWidth,
                MapHeight = _activeMapHeight
            };
            metrics.AggregateFrom(allResults);
        }

        private Pathfinding.Core.PathfindingResult RunDS1DynamicSimulation(
            IPathfindingAlgorithm algorithm,
            Vector2Int startPos, Vector2Int targetPos,
            int testId)
        {
            GridMap simulationGrid = _originalGridMap.Clone();
            MovingObstacleManager simulationManager = CreateDS1ManagerForCurrentTest(
                simulationGrid, startPos, targetPos, testId);

            var combinedResult = new Pathfinding.Core.PathfindingResult
            {
                PathFound = false,
                Path = new List<Vector2Int>()
            };

            Vector2Int currentPos = startPos;
            int tickLimit = GetDynamicTickLimit(
                simulationGrid, startPos, targetPos, testId);
            int ticks = 0;
            Pathfinding.Core.PathfindingResult currentPlan =
                algorithm.FindPath(simulationGrid, currentPos, targetPos);
            AccumulateSearchMetrics(combinedResult, currentPlan);
            int pathIndex = 0;
            int consecutiveFailedReplans = 0;

            while (currentPos != targetPos && ticks++ < tickLimit)
            {
                // Jednoznaczny tick DS1: najpierw ruch środowiska, następnie
                // obserwacja/replan i natychmiastowa akcja agenta.
                simulationManager.StepAllWithoutTracking(simulationGrid, currentPos);

                bool planUsable = currentPlan != null && currentPlan.PathFound &&
                                  currentPlan.Path != null && pathIndex < currentPlan.Path.Count &&
                                  CanMoveOnCurrentGrid(simulationGrid, currentPos, currentPlan.Path[pathIndex]);

                if (!planUsable)
                {
                    if (combinedResult.PathRecalculations >= Mathf.Max(1, maxDS1Replans))
                        break;

                    combinedResult.PathRecalculations++;
                    currentPlan = algorithm.FindPath(simulationGrid, currentPos, targetPos);
                    AccumulateSearchMetrics(combinedResult, currentPlan);
                    pathIndex = 0;

                    // Chwilowy brak drogi w DS1 oznacza wait, nie trwałą porażkę.
                    if (currentPlan == null || !currentPlan.PathFound ||
                        currentPlan.Path == null || currentPlan.Path.Count == 0)
                    {
                        consecutiveFailedReplans++;
                        if (consecutiveFailedReplans >=
                            Mathf.Max(1, maxDS1ConsecutiveFailedReplans))
                            break;
                        continue;
                    }

                    consecutiveFailedReplans = 0;
                }

                Vector2Int nextPos = currentPlan.Path[pathIndex];
                if (!CanMoveOnCurrentGrid(simulationGrid, currentPos, nextPos))
                    continue;

                Vector2Int previousPos = currentPos;
                currentPos = nextPos;
                consecutiveFailedReplans = 0;
                pathIndex++;
                combinedResult.Path.Add(currentPos);
                combinedResult.PathLength += GetStepLength(currentPos, previousPos);
            }

            combinedResult.PathFound = currentPos == targetPos;
            return combinedResult;
        }

        private void MeasureDS2DynamicAlgorithm(
            IPathfindingAlgorithm algorithm,
            Vector2Int startPos, Vector2Int targetPos,
            int testId, float density,
            out BenchmarkMetrics metrics, out Pathfinding.Core.PathfindingResult visualResult)
        {
            var allResults = new List<Pathfinding.Core.PathfindingResult>(benchmarkIterations);
            bool previousHistoryRecording = PathfindingRuntimeOptions.RecordExploredNodesHistory;

            try
            {
                for (int iter = 0; iter < benchmarkIterations; iter++)
                {
                    PathfindingRuntimeOptions.RecordExploredNodesHistory = false;

                    long gcBefore = BeginAllocationMeasurement(
                        iter == 0 && forceGcBeforeColdStart);

                    Pathfinding.Core.PathfindingResult result =
                        RunDS2DynamicSimulation(algorithm, startPos, targetPos, testId);

                    long gcAfter = GC.GetAllocatedBytesForCurrentThread();
                    result.GCAllocBytes = Math.Max(0, gcAfter - gcBefore);
                    CalculateReportedPathMetrics(result, startPos, iter == 0);

                    allResults.Add(result);
                }
            }
            finally
            {
                PathfindingRuntimeOptions.RecordExploredNodesHistory = previousHistoryRecording;
            }

            visualResult = allResults.Count > 0 ? allResults[0] : null;
            EnsureDeterministicLogicalResults(
                allResults, "DS2_PathObstruction", algorithm.AlgorithmName, testId);
            metrics = new BenchmarkMetrics
            {
                AlgorithmName = algorithm.AlgorithmName,
                TestID = testId,
                StartX = startPos.x,
                StartY = startPos.y,
                TargetX = targetPos.x,
                TargetY = targetPos.y,
                Scenario = "DS2_PathObstruction",
                ObstacleDensity = density,
                MapTopology = _activeMapTopology,
                MapSeed = _activeMapSeed,
                MapDensity = _activeMapDensity,
                MapWidth = _activeMapWidth,
                MapHeight = _activeMapHeight
            };
            metrics.AggregateFrom(allResults);
        }

        private Pathfinding.Core.PathfindingResult RunDS2DynamicSimulation(
            IPathfindingAlgorithm algorithm,
            Vector2Int startPos, Vector2Int targetPos,
            int testId)
        {
            GridMap simulationGrid = _originalGridMap.Clone();
            DS2DynamicState ds2State = CreateDS2DynamicState(simulationGrid, startPos, targetPos);

            var combinedResult = new Pathfinding.Core.PathfindingResult
            {
                PathFound = false,
                Path = new List<Vector2Int>()
            };

            Vector2Int currentPos = startPos;
            int ds2Step = 0;
            int tickLimit = GetDynamicTickLimit(
                simulationGrid, startPos, targetPos, testId);
            Pathfinding.Core.PathfindingResult currentPlan =
                algorithm.FindPath(simulationGrid, currentPos, targetPos);
            AccumulateSearchMetrics(combinedResult, currentPlan);
            if (!currentPlan.PathFound || currentPlan.Path == null || currentPlan.Path.Count == 0)
                return combinedResult;

            int pathIndex = 0;
            while (currentPos != targetPos && ds2Step < tickLimit)
            {
                List<Vector2Int> changes = ApplyDS2ScheduledObstructions(
                    simulationGrid, ds2Step, currentPos, ds2State);

                if (pathIndex >= currentPlan.Path.Count)
                {
                    combinedResult.PathRecalculations++;
                    currentPlan = algorithm.FindPath(simulationGrid, currentPos, targetPos);
                    AccumulateSearchMetrics(combinedResult, currentPlan);
                    pathIndex = 0;

                    // DS2 tylko dodaje trwałe blokady, więc brak drogi jest końcowy.
                    if (!currentPlan.PathFound || currentPlan.Path == null || currentPlan.Path.Count == 0)
                        return combinedResult;
                }

                Vector2Int nextPos = currentPlan.Path[pathIndex];
                if (!CanMoveOnCurrentGrid(simulationGrid, currentPos, nextPos))
                {
                    combinedResult.PathRecalculations++;
                    currentPlan = algorithm.FindPath(simulationGrid, currentPos, targetPos);
                    AccumulateSearchMetrics(combinedResult, currentPlan);
                    pathIndex = 0;
                    if (!currentPlan.PathFound || currentPlan.Path == null || currentPlan.Path.Count == 0)
                        return combinedResult;
                    nextPos = currentPlan.Path[0];
                }

                Vector2Int previousPos = currentPos;
                currentPos = nextPos;
                pathIndex++;
                ds2Step++;
                combinedResult.Path.Add(currentPos);
                combinedResult.PathLength += GetStepLength(currentPos, previousPos);
            }

            combinedResult.PathFound = currentPos == targetPos;
            return combinedResult;
        }

        private void MeasureDS3DynamicAlgorithm(
            IPathfindingAlgorithm algorithm,
            Vector2Int startPos, Vector2Int targetPos,
            int testId, float density,
            out BenchmarkMetrics metrics, out Pathfinding.Core.PathfindingResult visualResult)
        {
            var allResults = new List<Pathfinding.Core.PathfindingResult>(benchmarkIterations);
            bool previousHistoryRecording = PathfindingRuntimeOptions.RecordExploredNodesHistory;

            try
            {
                for (int iter = 0; iter < benchmarkIterations; iter++)
                {
                    PathfindingRuntimeOptions.RecordExploredNodesHistory = false;

                    long gcBefore = BeginAllocationMeasurement(
                        iter == 0 && forceGcBeforeColdStart);

                    Pathfinding.Core.PathfindingResult result =
                        RunDS3DynamicSimulation(algorithm, startPos, targetPos, testId);

                    long gcAfter = GC.GetAllocatedBytesForCurrentThread();
                    result.GCAllocBytes = Math.Max(0, gcAfter - gcBefore);
                    CalculateReportedPathMetrics(result, startPos, iter == 0);

                    allResults.Add(result);
                }
            }
            finally
            {
                PathfindingRuntimeOptions.RecordExploredNodesHistory = previousHistoryRecording;
            }

            visualResult = allResults.Count > 0 ? allResults[0] : null;
            EnsureDeterministicLogicalResults(
                allResults, "DS3_EscapingTarget", algorithm.AlgorithmName, testId);
            metrics = new BenchmarkMetrics
            {
                AlgorithmName = algorithm.AlgorithmName,
                TestID = testId,
                StartX = startPos.x,
                StartY = startPos.y,
                TargetX = targetPos.x,
                TargetY = targetPos.y,
                Scenario = "DS3_EscapingTarget",
                ObstacleDensity = density,
                MapTopology = _activeMapTopology,
                MapSeed = _activeMapSeed,
                MapDensity = _activeMapDensity,
                MapWidth = _activeMapWidth,
                MapHeight = _activeMapHeight
            };
            metrics.AggregateFrom(allResults);
        }

        private Pathfinding.Core.PathfindingResult RunDS3DynamicSimulation(
            IPathfindingAlgorithm algorithm,
            Vector2Int startPos, Vector2Int targetPos,
            int testId)
        {
            GridMap simulationGrid = _originalGridMap.Clone();
            DS3EscapingTargetState escapeState = CreateDS3EscapingTargetState(
                startPos, targetPos, testId);

            var combinedResult = new Pathfinding.Core.PathfindingResult
            {
                PathFound = false,
                Path = new List<Vector2Int>()
            };

            Vector2Int currentPos = startPos;
            int safetyLimit = simulationGrid.Width * simulationGrid.Height * 4;

            while (currentPos != escapeState.CurrentTarget && safetyLimit-- > 0)
            {
                Pathfinding.Core.PathfindingResult currentPlan =
                    algorithm.FindPath(simulationGrid, currentPos, escapeState.CurrentTarget);

                AccumulateSearchMetrics(combinedResult, currentPlan);

                if (!currentPlan.PathFound || currentPlan.Path == null || currentPlan.Path.Count == 0)
                    return combinedResult;

                bool needsReplan = false;
                for (int pathIndex = 0; pathIndex < currentPlan.Path.Count && currentPos != escapeState.CurrentTarget; pathIndex++)
                {
                    Vector2Int nextPos = currentPlan.Path[pathIndex];

                    if (!CanMoveOnCurrentGrid(simulationGrid, currentPos, nextPos))
                    {
                        needsReplan = true;
                        combinedResult.PathRecalculations++;
                        break;
                    }

                    Vector2Int previousPos = currentPos;
                    currentPos = nextPos;
                    combinedResult.Path.Add(currentPos);
                    combinedResult.PathLength += GetStepLength(currentPos, previousPos);

                    if (currentPos == escapeState.CurrentTarget)
                    {
                        combinedResult.PathFound = true;
                        return combinedResult;
                    }

                    // Co 2 kroki agenta — cel ucieka
                    bool escaped = TryEscapeTarget(simulationGrid, escapeState);
                    if (escaped)
                    {
                        needsReplan = true;
                        combinedResult.PathRecalculations++;
                        break;
                    }

                    if (--safetyLimit <= 0)
                        return combinedResult;
                }

                if (!needsReplan && currentPos != escapeState.CurrentTarget)
                    continue;
            }

            combinedResult.PathFound = currentPos == escapeState.CurrentTarget;
            return combinedResult;
        }

        private void AccumulateSearchMetrics(
            Pathfinding.Core.PathfindingResult total,
            Pathfinding.Core.PathfindingResult partial)
        {
            if (partial == null)
                return;

            total.ExecutionTimeMs += partial.ExecutionTimeMs;
            total.ExecutionTicks += partial.ExecutionTicks;
            total.ExploredNodes += partial.ExploredNodes;
            total.JumpScannedCells += partial.JumpScannedCells;
        }

        private void EnsureDeterministicLogicalResults(
            List<Pathfinding.Core.PathfindingResult> results,
            string scenarioName,
            string algorithmName,
            int testId)
        {
            if (results == null || results.Count < 2)
                return;

            Pathfinding.Core.PathfindingResult expected = results[0];
            for (int run = 1; run < results.Count; run++)
            {
                Pathfinding.Core.PathfindingResult actual = results[run];
                bool same = expected.PathFound == actual.PathFound &&
                            expected.ExploredNodes == actual.ExploredNodes &&
                            expected.JumpScannedCells == actual.JumpScannedCells &&
                            expected.PathRecalculations == actual.PathRecalculations &&
                            Math.Abs(expected.PathLength - actual.PathLength) <= 0.0001f &&
                            PathsEqual(expected.Path, actual.Path);

                if (!same)
                {
                    throw new InvalidOperationException(
                        $"Niedeterministyczny wynik {scenarioName}/{algorithmName}, " +
                        $"TestID={testId}, iteracja={run}. Benchmark został przerwany, " +
                        "aby nie zapisać niespójnych danych.");
                }
            }
        }

        private static bool PathsEqual(List<Vector2Int> expected, List<Vector2Int> actual)
        {
            if (ReferenceEquals(expected, actual))
                return true;
            if (expected == null || actual == null || expected.Count != actual.Count)
                return false;
            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i] != actual[i])
                    return false;
            }
            return true;
        }

        private bool CanMoveOnCurrentGrid(GridMap grid, Vector2Int from, Vector2Int to)
        {
            int dx = to.x - from.x;
            int dy = to.y - from.y;

            if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0))
                return false;

            if (!grid.IsWalkable(from))
                return false;

            if (!grid.IsWalkable(to))
                return false;

            if (dx != 0 && dy != 0)
                return grid.IsWalkable(from.x + dx, from.y) && grid.IsWalkable(from.x, from.y + dy);

            return true;
        }

        private int GetDynamicTickLimit(
            GridMap grid, Vector2Int startPos, Vector2Int targetPos, int testId)
        {
            if (_cachedTickLimitTestId == testId &&
                _cachedTickLimitScenario == scenario)
                return _cachedDynamicTickLimit;

            int tickLimit;
            if (TestPointSelector.TryGetShortestPathLength(
                    grid, startPos, targetPos, out float referenceLength))
            {
                tickLimit = Mathf.Max(64, Mathf.CeilToInt(referenceLength * 8f) + 32);
            }
            else
            {
                tickLimit = Mathf.Max(64, (grid.Width + grid.Height) * 4);
            }

            _cachedTickLimitTestId = testId;
            _cachedTickLimitScenario = scenario;
            _cachedDynamicTickLimit = tickLimit;
            return tickLimit;
        }

        /// <summary>
        /// Uruchamia dokładnie tę samą symulację dynamiczną co benchmark, ale bez
        /// pomiarów i wizualizacji. Punkt wejścia służy testom powtarzalności.
        /// </summary>
        public Pathfinding.Core.PathfindingResult RunDynamicSimulationForDeterminism(
            ScenarioType scenarioType,
            IPathfindingAlgorithm algorithm,
            GridMap baseGrid,
            Vector2Int startPos,
            Vector2Int targetPos,
            int mapSeed,
            int testId = 0)
        {
            GridMap previousOriginalGrid = _originalGridMap;
            int previousMapSeed = _activeMapSeed;
            bool previousFullSuite = runFullBenchmarkSuite;
            ScenarioType previousScenario = scenario;

            try
            {
                _originalGridMap = baseGrid.Clone();
                _activeMapSeed = mapSeed;
                runFullBenchmarkSuite = true;
                scenario = scenarioType;

                Pathfinding.Core.PathfindingResult result = scenarioType switch
                {
                    ScenarioType.DS1_MovingObstacles =>
                        RunDS1DynamicSimulation(algorithm, startPos, targetPos, testId),
                    ScenarioType.DS2_PathObstruction =>
                        RunDS2DynamicSimulation(algorithm, startPos, targetPos, testId),
                    ScenarioType.DS3_EscapingTarget =>
                        RunDS3DynamicSimulation(algorithm, startPos, targetPos, testId),
                    _ => algorithm.FindPath(baseGrid.Clone(), startPos, targetPos)
                };

                result.CalculatePathCost(startPos);
                if (result.PathFound)
                    result.CalculateSmoothnessMetrics(startPos);
                return result;
            }
            finally
            {
                _originalGridMap = previousOriginalGrid;
                _activeMapSeed = previousMapSeed;
                runFullBenchmarkSuite = previousFullSuite;
                scenario = previousScenario;
            }
        }

        private float GetStepLength(Vector2Int to, Vector2Int from)
        {
            return to.x != from.x && to.y != from.y ? 1.414f : 1.0f;
        }

        private IEnumerator MeasureAlgorithmBatched(
            IPathfindingAlgorithm algorithm, GridMap grid,
            Vector2Int startPos, Vector2Int targetPos,
            int testId, float density,
            MeasurementBatch measurement)
        {
            var allResults = new List<Pathfinding.Core.PathfindingResult>(benchmarkIterations);
            bool previousHistoryRecording = PathfindingRuntimeOptions.RecordExploredNodesHistory;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                for (int iter = 0; iter < benchmarkIterations; iter++)
                {
                    if (_stopBenchmarkRequested)
                    {
                        measurement.Cancelled = true;
                        yield break;
                    }

                    PathfindingRuntimeOptions.RecordExploredNodesHistory = false;

                    long gcBefore = BeginAllocationMeasurement(
                        iter == 0 && forceGcBeforeColdStart);

                    Pathfinding.Core.PathfindingResult result;
                    switch (scenario)
                    {
                        case ScenarioType.DS1_MovingObstacles:
                            result = RunDS1DynamicSimulation(algorithm, startPos, targetPos, testId);
                            break;
                        case ScenarioType.DS2_PathObstruction:
                            result = RunDS2DynamicSimulation(algorithm, startPos, targetPos, testId);
                            break;
                        case ScenarioType.DS3_EscapingTarget:
                            result = RunDS3DynamicSimulation(algorithm, startPos, targetPos, testId);
                            break;
                        default:
                            result = algorithm.FindPath(grid, startPos, targetPos);
                            break;
                    }

                    long gcAfter = GC.GetAllocatedBytesForCurrentThread();
                    result.GCAllocBytes = Math.Max(0, gcAfter - gcBefore);
                    CalculateReportedPathMetrics(result, startPos, iter == 0);

                    allResults.Add(result);

                    int iterationsPerYield = Mathf.Max(1, headlessIterationsPerYield);
                    if (iter + 1 < benchmarkIterations &&
                        ((iter + 1) % iterationsPerYield == 0 ||
                         sw.ElapsedMilliseconds > FullSuiteMaxWorkSliceMs))
                    {
                        yield return null;
                        sw.Restart();
                    }
                }
            }
            finally
            {
                PathfindingRuntimeOptions.RecordExploredNodesHistory = previousHistoryRecording;
            }

            measurement.VisualResult = allResults.Count > 0 ? allResults[0] : null;

            if (scenario != ScenarioType.Static)
            {
                EnsureDeterministicLogicalResults(
                    allResults, scenario.ToString(), algorithm.AlgorithmName, testId);
            }

            string scenarioLabel = scenario switch
            {
                ScenarioType.Static => "Static",
                ScenarioType.DS1_MovingObstacles => "DS1_MovingObstacles",
                ScenarioType.DS2_PathObstruction => "DS2_PathObstruction",
                ScenarioType.DS3_EscapingTarget => "DS3_EscapingTarget",
                _ => "Static"
            };

            measurement.Metrics = new BenchmarkMetrics
            {
                AlgorithmName = algorithm.AlgorithmName,
                TestID = testId,
                StartX = startPos.x,
                StartY = startPos.y,
                TargetX = targetPos.x,
                TargetY = targetPos.y,
                Scenario = scenarioLabel,
                ObstacleDensity = density,
                MapTopology = _activeMapTopology,
                MapSeed = _activeMapSeed,
                MapDensity = _activeMapDensity,
                MapWidth = _activeMapWidth,
                MapHeight = _activeMapHeight
            };
            measurement.Metrics.AggregateFrom(allResults);
        }

        // ─────────────────────────────────────────────────────────
        //  ALGORYTMY I LOSOWANIE
        // ─────────────────────────────────────────────────────────

        private List<IPathfindingAlgorithm> GetAlgorithmsToRun()
        {
            if (benchmarkMode == BenchmarkMode.AllAlgorithms)
            {
                var list = new List<IPathfindingAlgorithm>
                {
                    new AStarAlgorithm(),
                    new DijkstraAlgorithm(),
                    new GreedyBestFirstAlgorithm(),
                    new CustomGreedyAlgorithm()
                };

                list.Add(new JumpPointSearchAlgorithm());

                return list;
            }
            else
            {
                return new List<IPathfindingAlgorithm> { CreateSelectedAlgorithm() };
            }
        }

        private IPathfindingAlgorithm CreateSelectedAlgorithm()
        {
            return selectedAlgorithm switch
            {
                AlgorithmChoice.AStar => new AStarAlgorithm(),
                AlgorithmChoice.Dijkstra => new DijkstraAlgorithm(),
                AlgorithmChoice.GreedyBestFirst => new GreedyBestFirstAlgorithm(),
                AlgorithmChoice.CustomGreedy => new CustomGreedyAlgorithm(),
                AlgorithmChoice.JumpPointSearch => new JumpPointSearchAlgorithm(),
                _ => new AStarAlgorithm()
            };
        }

        /// <summary>
        /// Fisher-Yates shuffle — losowa permutacja listy in-place.
        /// Eliminuje bias thermal throttling: algorytm uruchamiany jako pierwszy
        /// nie jest zawsze ten sam.
        /// Złożoność: O(n).
        /// </summary>
        private void ShuffleList<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _shuffleRng.Next(0, i + 1);
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        private void ShuffleList<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        private List<string> GetAlgorithmNames(List<IPathfindingAlgorithm> algorithms)
        {
            var names = new List<string>();
            foreach (var a in algorithms) names.Add(a.AlgorithmName);
            return names;
        }

        // ─────────────────────────────────────────────────────────
        //  ŁADOWANIE DANYCH
        // ─────────────────────────────────────────────────────────

        private bool LoadGridMap()
        {
            string path = mapFileName;
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "..", mapFileName);
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "../..", mapFileName);

            if (!File.Exists(path))
            {
                Debug.LogError($"Nie znaleziono pliku mapy: {path}");
                return false;
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) return false;

            int height = lines.Length;
            int width = lines[0].Length;
            bool[,] collisionData = new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                string line = lines[height - 1 - y];
                for (int x = 0; x < width; x++)
                {
                    if (x < line.Length)
                        collisionData[x, y] = (line[x] == '0');
                    else
                        collisionData[x, y] = false;
                }
            }

            _gridMap = new GridMap(collisionData);
            Debug.Log($"[Visualizer] Wczytano GridMap: {width}x{height}");
            return true;
        }

        private void LoadTestCases()
        {
            string path = testCasesFileName;
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "..", testCasesFileName);
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "../..", testCasesFileName);

            if (!File.Exists(path))
            {
                Debug.LogError($"Nie znaleziono pliku: {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var columns = lines[i].Split(',');
                if (columns.Length >= 4)
                {
                    int startX = int.Parse(columns[0].Trim());
                    int startY = int.Parse(columns[1].Trim());
                    int targetX = int.Parse(columns[2].Trim());
                    int targetY = int.Parse(columns[3].Trim());
                    Vector2Int start = new Vector2Int(startX, startY);
                    Vector2Int target = new Vector2Int(targetX, targetY);

                    float euclideanDistance;
                    float octagonalDistance;
                    float referenceShortestPathLength;
                    ParseTestCaseDistances(columns, start, target, out euclideanDistance,
                        out octagonalDistance, out referenceShortestPathLength);
                    if (octagonalDistance < 0f)
                        octagonalDistance = TestPointSelector.CalculateOctagonalDistance(start, target);

                    _testCases.Add(new TestCase
                    {
                        startX = startX,
                        startY = startY,
                        targetX = targetX,
                        targetY = targetY,
                        distanceBucket = columns.Length >= 5 ? columns[4].Trim() : "Unknown",
                        euclideanDistance = euclideanDistance,
                        octagonalDistance = octagonalDistance,
                        referenceShortestPathLength = referenceShortestPathLength
                    });
                }
            }
        }

        private static void ParseTestCaseDistances(string[] columns, Vector2Int start, Vector2Int target,
            out float euclideanDistance, out float octagonalDistance,
            out float referenceShortestPathLength)
        {
            euclideanDistance = -1f;
            octagonalDistance = TestPointSelector.CalculateOctagonalDistance(start, target);
            referenceShortestPathLength = -1f;

            if (columns.Length == 11)
            {
                euclideanDistance = ParseCsvFloat(columns[5].Trim() + "." + columns[6].Trim());
                octagonalDistance = ParseCsvFloat(columns[7].Trim() + "." + columns[8].Trim());
                referenceShortestPathLength = ParseCsvFloat(columns[9].Trim() + "." + columns[10].Trim());
                return;
            }

            if (columns.Length == 9)
            {
                euclideanDistance = ParseCsvFloat(columns[5].Trim() + "." + columns[6].Trim());
                referenceShortestPathLength = ParseCsvFloat(columns[7].Trim() + "." + columns[8].Trim());
                return;
            }

            if (columns.Length >= 8)
            {
                euclideanDistance = ParseCsvFloat(columns[5]);
                octagonalDistance = ParseCsvFloat(columns[6]);
                referenceShortestPathLength = ParseCsvFloat(columns[7]);
                return;
            }

            if (columns.Length >= 6)
                euclideanDistance = ParseCsvFloat(columns[5]);

            if (columns.Length >= 7)
                referenceShortestPathLength = ParseCsvFloat(columns[6]);
        }

        private static float ParseCsvFloat(string value)
        {
            if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                return parsed;

            if (float.TryParse(value.Trim(), out parsed))
                return parsed;

            return -1f;
        }

        private void ApplyTestCaseMetadata(BenchmarkMetrics metrics, TestCase testCase)
        {
            metrics.DistanceBucket = string.IsNullOrWhiteSpace(testCase.distanceBucket)
                ? "Unknown"
                : testCase.distanceBucket;
            metrics.EuclideanDistance = testCase.euclideanDistance;
            metrics.OctagonalDistance = testCase.octagonalDistance;
            GridMap referenceGrid = _originalGridMap ?? _gridMap;
            Vector2Int start = new Vector2Int(testCase.startX, testCase.startY);
            Vector2Int target = new Vector2Int(testCase.targetX, testCase.targetY);
            if (referenceGrid != null && TestPointSelector.TryGetShortestPathLength(
                    referenceGrid, start, target, out float verifiedReferenceLength))
            {
                metrics.ReferenceShortestPathLength = verifiedReferenceLength;
            }
            else
            {
                metrics.ReferenceShortestPathLength = testCase.referenceShortestPathLength;
            }
        }

        // ─────────────────────────────────────────────────────────
        //  WIZUALIZACJA (GENEROWANIE GRIDU)
        // ─────────────────────────────────────────────────────────

        private void GenerateBasemapVisuals()
        {
            int width = _gridMap.Width;
            int height = _gridMap.Height;
            _basemapRenderers = new SpriteRenderer[width, height];
            _spawnedObstacles = new GameObject[width, height];
            _spawnedChangeMarkers = new GameObject[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector3 worldPos = new Vector3(x, y, 0);
                    GameObject cell = Instantiate(basemapPrefab, worldPos, Quaternion.identity, this.transform);
                    cell.name = $"Basemap_{x}_{y}";
                    cell.transform.localScale = new Vector3(0.95f, 0.95f, 1f);

                    SpriteRenderer sr = cell.GetComponent<SpriteRenderer>();
                    sr.color = colorWalkable; // Basemap jest zawsze jasny (tło)
                    _basemapRenderers[x, y] = sr;

                    // Obstacles
                    Sprite sprite = GetObstacleSpriteForCell(x, y);
                    if (!_gridMap.IsWalkable(x, y) && sprite != null)
                    {
                        GameObject obs = new GameObject($"Obstacle_{x}_{y}");
                        obs.transform.position = new Vector3(x, y, -0.1f);
                        obs.transform.parent = this.transform;
                        obs.transform.localScale = new Vector3(0.95f, 0.95f, 1f);

                        SpriteRenderer obsSr = obs.AddComponent<SpriteRenderer>();
                        obsSr.sprite = sprite;
                        obsSr.sortingOrder = 1; // Wyżej niż basemap
                        
                        _spawnedObstacles[x, y] = obs;
                    }
                }
            }

            if (agentPrefab != null)
            {
                _agentObject = Instantiate(agentPrefab, Vector3.zero, Quaternion.identity);
                _agentObject.name = "PathfindingAgent";
                _agentObject.SetActive(false);
            }

            if (Camera.main != null)
            {
                CameraController camController = Camera.main.gameObject.GetComponent<CameraController>();
                if (camController == null) camController = Camera.main.gameObject.AddComponent<CameraController>();
                camController.AutoSizeToMap(_gridMap.Width, _gridMap.Height);
            }
        }

        /// <summary>
        /// Odświeża kolory basemapy zgodnie z aktualnym stanem GridMap.
        /// Wywoływane po zmianach scenariusza.
        /// </summary>
        private void RefreshBasemapColors()
        {
            for (int x = 0; x < _gridMap.Width; x++)
            {
                for (int y = 0; y < _gridMap.Height; y++)
                {
                    _basemapRenderers[x, y].color = colorWalkable; // Reset koloru tła

                    // Aktualizacja przeszkód
                    if (!_gridMap.IsWalkable(x, y))
                    {
                        Sprite sprite = GetObstacleSpriteForCell(x, y);
                        if (_spawnedObstacles[x, y] == null && sprite != null)
                        {
                            GameObject obs = new GameObject($"Obstacle_{x}_{y}");
                            obs.transform.position = new Vector3(x, y, -0.1f);
                            obs.transform.parent = this.transform;
                            obs.transform.localScale = new Vector3(0.95f, 0.95f, 1f);

                            SpriteRenderer obsSr = obs.AddComponent<SpriteRenderer>();
                            obsSr.sprite = sprite;
                            obsSr.sortingOrder = 1; // Wyżej niż basemap

                            _spawnedObstacles[x, y] = obs;
                        }
                        if (_spawnedObstacles[x, y] != null)
                        {
                            SpriteRenderer obsSr = _spawnedObstacles[x, y].GetComponent<SpriteRenderer>();
                            if (obsSr != null && sprite != null)
                                obsSr.sprite = sprite;
                            _spawnedObstacles[x, y].SetActive(true);
                        }
                    }
                    else
                    {
                        if (_spawnedObstacles[x, y] != null) _spawnedObstacles[x, y].SetActive(false);
                    }

                    // Reset marker objects
                    if (_spawnedChangeMarkers[x, y] != null)
                    {
                        _spawnedChangeMarkers[x, y].SetActive(false);
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────
        //  WIZUALIZACJA (ANIMACJA ŚCIEŻKI)
        // ─────────────────────────────────────────────────────────

        private void ShowChangeMarkers(IEnumerable<Vector2Int> positions)
        {
            if (positions == null)
                return;

            foreach (var pos in positions)
            {
                if (pos.x < 0 || pos.x >= _gridMap.Width || pos.y < 0 || pos.y >= _gridMap.Height)
                    continue;

                if (_spawnedChangeMarkers[pos.x, pos.y] == null && changeMarkerSprite != null)
                {
                    GameObject marker = new GameObject($"ChangeMarker_{pos.x}_{pos.y}");
                    marker.transform.position = new Vector3(pos.x, pos.y, -0.2f);
                    marker.transform.parent = this.transform;
                    marker.transform.localScale = new Vector3(0.95f, 0.95f, 1f);

                    SpriteRenderer markerRenderer = marker.AddComponent<SpriteRenderer>();
                    markerRenderer.sprite = changeMarkerSprite;
                    markerRenderer.sortingOrder = 2;

                    _spawnedChangeMarkers[pos.x, pos.y] = marker;
                }

                if (_spawnedChangeMarkers[pos.x, pos.y] != null)
                    _spawnedChangeMarkers[pos.x, pos.y].SetActive(true);
            }
        }

        private IEnumerator VisualizeRoutine(
            Pathfinding.Core.PathfindingResult result,
            Vector2Int startPos, Vector2Int targetPos,
            string algorithmName,
            List<Vector2Int> scenarioChanges)
        {
            _isVisualizing = true;

            // Zresetuj podświetlenia (zachowaj aktualny stan ścian)
            RefreshBasemapColors();

            // Pokaż zmienione pola scenariusza (prefaby na wierzchu)
            if (scenarioChanges != null)
            {
                foreach (var pos in scenarioChanges)
                {
                    if (pos.x >= 0 && pos.x < _gridMap.Width && pos.y >= 0 && pos.y < _gridMap.Height)
                    {
                        if (_spawnedChangeMarkers[pos.x, pos.y] == null && changeMarkerSprite != null)
                        {
                            GameObject dyn = new GameObject($"ChangeMarker_{pos.x}_{pos.y}");
                            dyn.transform.position = new Vector3(pos.x, pos.y, -0.2f); // Bliżej kamery
                            dyn.transform.parent = this.transform;
                            dyn.transform.localScale = new Vector3(0.95f, 0.95f, 1f);

                            SpriteRenderer dynSr = dyn.AddComponent<SpriteRenderer>();
                            dynSr.sprite = changeMarkerSprite;
                            dynSr.sortingOrder = 2; // Najwyżej w hierarchii 2D (poza agentem)

                            _spawnedChangeMarkers[pos.x, pos.y] = dyn;
                        }
                        if (_spawnedChangeMarkers[pos.x, pos.y] != null)
                        {
                            _spawnedChangeMarkers[pos.x, pos.y].SetActive(true);
                        }
                    }
                }
            }

            // Oznacz START i CEL
            if (startPos.x >= 0 && startPos.x < _gridMap.Width && startPos.y >= 0 && startPos.y < _gridMap.Height)
                _basemapRenderers[startPos.x, startPos.y].color = colorStart;

            if (targetPos.x >= 0 && targetPos.x < _gridMap.Width && targetPos.y >= 0 && targetPos.y < _gridMap.Height)
                _basemapRenderers[targetPos.x, targetPos.y].color = colorTarget;

            if (_agentObject != null) _agentObject.SetActive(false);

            if (!result.PathFound)
            {
                Debug.LogWarning($"[Visualizer] {algorithmName}: BRAK DROGI!");
                yield return new WaitForSeconds(0.5f);
                _isVisualizing = false;
                yield break;
            }

            // 1. Pokazywanie odwiedzonych węzłów
            foreach (Vector2Int pos in result.ExploredNodesHistory)
            {
                if (pos != startPos && pos != targetPos)
                {
                    if (pos.x >= 0 && pos.x < _gridMap.Width && pos.y >= 0 && pos.y < _gridMap.Height)
                    {
                        _basemapRenderers[pos.x, pos.y].color = colorExplored;
                        yield return new WaitForSeconds(visualizationStepDelay);
                    }
                }
            }

            yield return new WaitForSeconds(0.2f);

            // 2. Podświetl ścieżkę (żółty)
            foreach (Vector2Int pos in result.Path)
            {
                if (pos != startPos && pos != targetPos)
                {
                    if (pos.x >= 0 && pos.x < _gridMap.Width && pos.y >= 0 && pos.y < _gridMap.Height)
                    {
                        _basemapRenderers[pos.x, pos.y].color = colorPath;
                    }
                }
            }

            yield return new WaitForSeconds(0.1f);

            // 3. Animacja agenta po ścieżce
            if (_agentObject != null)
            {
                _agentObject.SetActive(true);
                _agentObject.transform.position = new Vector3(startPos.x, startPos.y, -2f);

                foreach (Vector2Int step in result.Path)
                {
                    Vector3 nextPosition = new Vector3(step.x, step.y, -2f);

                    while (Vector3.Distance(_agentObject.transform.position, nextPosition) > 0.01f)
                    {
                        _agentObject.transform.position = Vector3.MoveTowards(
                            _agentObject.transform.position, nextPosition, agentMoveSpeed * Time.deltaTime);
                        yield return null;
                    }
                    _agentObject.transform.position = nextPosition;
                }
            }

            Debug.Log($"[Visualizer] {algorithmName}: Animacja zakończona.");
            _isVisualizing = false;
        }
        // ─────────────────────────────────────────────────────────
        //  GENERACJA MAP PROCEDURALNYCH
        // ─────────────────────────────────────────────────────────

        private IMapGenerator CreateMapGenerator(MapTopology topology)
        {
            return topology switch
            {
                MapTopology.OpenField => new OpenFieldGenerator(),
                MapTopology.Maze => new MazeGenerator(),
                MapTopology.RoomCorridor => new RoomCorridorGenerator(),
                MapTopology.ScatteredBlock => new ScatteredBlockGenerator(),
                _ => null
            };
        }

        private List<MapTopology> GetSuiteTopologies()
        {
            return new List<MapTopology>
            {
                MapTopology.OpenField,
                MapTopology.Maze,
                MapTopology.RoomCorridor,
                MapTopology.ScatteredBlock
            };
        }

        private IEnumerable<float> GetSuiteDensities()
        {
            if (suiteDensities == null || suiteDensities.Length == 0)
            {
                yield return Mathf.Clamp(proceduralDensity, 0f, 0.5f);
                yield break;
            }

            foreach (float density in suiteDensities)
                yield return Mathf.Clamp(density, 0f, 0.5f);
        }

        private IEnumerable<int> GetSuiteSeeds()
        {
            if (suiteSeeds == null || suiteSeeds.Length == 0)
            {
                yield return randomSeed;
                yield break;
            }

            foreach (int seed in suiteSeeds)
                yield return seed;
        }

        private IEnumerable<(int width, int height)> GetSuiteMapSizes()
        {
            if (!useSuiteMapSizes || suiteMapSizes == null || suiteMapSizes.Length == 0)
            {
                yield return (Mathf.Max(10, proceduralMapWidth), Mathf.Max(10, proceduralMapHeight));
                yield break;
            }

            HashSet<int> uniqueSizes = new HashSet<int>();
            foreach (int rawSize in suiteMapSizes)
            {
                int size = Mathf.Clamp(rawSize, 10, 500);
                if (uniqueSizes.Add(size))
                    yield return (size, size);
            }
        }

        private List<TestCase> GenerateTestCasesForMap(GridMap map, int seed)
        {
            var selector = new TestPointSelector(seed);
            var enhanced = selector.GenerateTestCases(map, pairsPerBucket);
            var generated = new List<TestCase>(enhanced.Count);

            foreach (var etc in enhanced)
            {
                generated.Add(new TestCase
                {
                    startX = etc.StartX,
                    startY = etc.StartY,
                    targetX = etc.TargetX,
                    targetY = etc.TargetY,
                    distanceBucket = etc.Bucket.ToString(),
                    euclideanDistance = etc.EuclideanDistance,
                    octagonalDistance = etc.OctagonalDistance,
                    referenceShortestPathLength = etc.ShortestPathLength
                });
            }

            return generated;
        }

        private bool GenerateProceduralMap()
        {
            IMapGenerator generator = CreateMapGenerator(mapSource);

            if (generator == null)
            {
                Debug.LogError("[Visualizer] Nieznana topologia mapy.");
                return false;
            }

            int w = proceduralMapWidth;
            int h = proceduralMapHeight;
            _gridMap = generator.Generate(w, h, proceduralDensity, randomSeed);
            Debug.Log($"[Visualizer] Wygenerowano mapę proceduralną: {generator.TopologyName} " +
                      $"{w}x{h}, gęstość={proceduralDensity:P0}, seed={randomSeed}");
            return true;
        }

        // ─────────────────────────────────────────────────────────
        //  DISTANCE BUCKETING — NAUKOWY DOBÓR PUNKTÓW
        // ─────────────────────────────────────────────────────────

        private void GenerateDistanceBucketedTestCases()
        {
            var selector = new TestPointSelector(randomSeed);
            var enhanced = selector.GenerateTestCases(_gridMap, pairsPerBucket);

            _testCases.Clear();
            foreach (var etc in enhanced)
            {
                _testCases.Add(new TestCase
                {
                    startX = etc.StartX, startY = etc.StartY,
                    targetX = etc.TargetX, targetY = etc.TargetY,
                    distanceBucket = etc.Bucket.ToString(),
                    euclideanDistance = etc.EuclideanDistance,
                    octagonalDistance = etc.OctagonalDistance,
                    referenceShortestPathLength = etc.ShortestPathLength
                });
            }

            // Eksportuj CSV z metadanymi
            string csvPath = Path.Combine(Application.dataPath, "..", "EnhancedTestCases.csv");
            TestPointSelector.ExportToCsv(enhanced, csvPath);
            Debug.Log($"[Visualizer] Distance bucketing: {_testCases.Count} par " +
                      $"(SHORT: {pairsPerBucket}, MEDIUM: {pairsPerBucket}, " +
                      $"LONG: {pairsPerBucket})");
        }

        // ─────────────────────────────────────────────────────────
        //  BATCH GENERATOR
        // ─────────────────────────────────────────────────────────

        private void RunBatchGeneration()
        {
            string basePath = Path.Combine(Application.dataPath, "..", batchOutputDirectory);
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);

            var generators = new List<IMapGenerator>
            {
                new OpenFieldGenerator(),
                new MazeGenerator(),
                new RoomCorridorGenerator(),
                new ScatteredBlockGenerator()
            };

            float[] densities = { 0.10f, 0.20f, 0.30f, 0.40f };
            int[] seeds = { 42, 123, 256, 789 };
            int total = generators.Count * densities.Length * seeds.Length;
            int generated = 0;

            Debug.Log($"[BatchGenerator] ═══ START ═══ Generuję {total} map");

            foreach (var gen in generators)
            {
                string topoDir = Path.Combine(basePath, gen.TopologyName);
                if (!Directory.Exists(topoDir))
                    Directory.CreateDirectory(topoDir);

                foreach (float density in densities)
                {
                    foreach (int seed in seeds)
                    {
                        GridMap map = gen.Generate(32, 20, density, seed);

                        string fileName = MapExporter.GenerateFileName(gen.TopologyName, 32, 20, density, seed);
                        MapExporter.ExportToFile(map, Path.Combine(topoDir, fileName));

                        // Test cases z distance bucketing
                        var selector = new TestPointSelector(seed);
                        var testCases = selector.GenerateTestCases(map, pairsPerBucket);
                        string csvName = Path.GetFileNameWithoutExtension(fileName) + "_TestCases.csv";
                        TestPointSelector.ExportToCsv(testCases, Path.Combine(topoDir, csvName));

                        generated++;
                    }
                }
            }

            Debug.Log($"[BatchGenerator] ═══ ZAKOŃCZONO ═══ {generated} map w: {Path.GetFullPath(basePath)}");
        }
    }
}
