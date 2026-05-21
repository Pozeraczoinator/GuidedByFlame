using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        /// - DS2_PathObstruction: dodawanie/usuwanie przeszkód na bazowej trasie NPC
        /// - DS3_DoorGateToggle: deterministyczne bramy otwierane/zamykane na korytarzach trasy
        /// </summary>
        public enum ScenarioType
        {
            Static = 0,
            DS1_MovingObstacles = 1,
            DS2_PathObstruction = 2,
            DS3_DoorGateToggle = 3
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
        [Tooltip("Static / DS1_MovingObstacles / DS2_PathObstruction / DS3_DoorGateToggle")]
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

        [Tooltip("Nazwa pliku wynikowego CSV (32 kolumny, separator: średnik).")]
        public string outputFileName = "benchmark_results.csv";

        [Header("═══ Monitoring Sprzętowy ═══")]
        [Tooltip("Czy mierzyć temperaturę CPU przy każdym teście (Windows only). Spowalnia ~100ms per pomiar.")]
        public bool monitorCPUTemperature = false;

        [Header("═══ Headless Benchmark ═══")]
        [Tooltip("Ile iteracji jednego algorytmu wykonać przed oddaniem klatki Unity w trybie bez wizualizacji.")]
        [Range(1, 30)]
        public int headlessIterationsPerYield = 3;

        [Tooltip("Co ile wierszy CSV wymusić zapis na dysk w trybie bez wizualizacji.")]
        [Range(1, 10000)]
        public int headlessRowsPerFlush = 250;

        [Tooltip("Wymusza pelne GC przed cold startem kazdego algorytmu. Dokladniejsze GCAlloc, ale bardzo wolne w duzych full suite.")]
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
        [Tooltip("Liczba ruchomych przeszkód na mapie (patrol guards).")]
        [Range(1, 20)]
        public int movingObstacleCount = 3;

        [Tooltip("Długość trasy patrol każdej przeszkody (w polach).")]
        [Range(3, 20)]
        public int patrolLength = 6;

        [Header("═══ DS2: Blokady na Trasie ═══")]
        [Tooltip("Ile pól na bazowej trasie zmodyfikować przez dodanie/usunięcie przeszkód.")]
        [Range(1, 30)]
        public int pathObstructionChanges = 6;

        [Tooltip("Co który krok bazowej trasy rozważać jako kandydat do blokady.")]
        [Range(2, 12)]
        public int pathObstructionSpacing = 4;

        [Header("═══ DS3: Bramy / Drzwi ═══")]
        [Tooltip("Liczba deterministycznych bram ustawianych na bazowej trasie.")]
        [Range(1, 12)]
        public int gateToggleCount = 4;

        [Tooltip("Szerokość ściany bramy w polach. Wartości nieparzyste wyglądają najlepiej.")]
        [Range(1, 7)]
        public int gateWidth = 3;

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

        private class MeasurementBatch
        {
            public BenchmarkMetrics Metrics;
            public Pathfinding.Core.PathfindingResult VisualResult;
            public bool Cancelled;
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
                                      $"Węzły: {metrics.ExploredNodes}");
                        }

                        if (ShouldVisualize)
                        {
                            // ─── KROK 2: Animacja wizualizacji ───
                            if (scenario == ScenarioType.DS1_MovingObstacles)
                            {
                                StartCoroutine(VisualizeDS1ReplanningRoutine(
                                    algorithm, visualResult, startPos, targetPos, testId));
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
                        scenario == ScenarioType.DS3_DoorGateToggle)
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
            ScenarioType originalScenario = scenario;
            MapTopology originalMapSource = mapSource;
            int originalRandomSeed = randomSeed;
            List<TestCase> originalTestCases = _testCases;
            int estimatedTotalTestCases = EstimateFullSuiteTestCaseTotal();

            _suiteTestId = 0;
            _stopBenchmarkRequested = false;
            _rowsSinceFlush = 0;

            Debug.Log("[Visualizer] START PEŁNEGO BENCHMARKU HEADLESS");
            Debug.Log($"[Visualizer] Tryb algorytmów: {benchmarkMode} | Iteracje: {benchmarkIterations}");
            Debug.Log($"[Visualizer] Przerwanie: naciśnij {stopBenchmarkKey}, zapis CSV zostanie domknięty.");
            Debug.Log($"[Visualizer] Planowany postęp: {FormatProgress(0, estimatedTotalTestCases, "testów")}");
            Debug.Log($"[Visualizer] Wyniki CSV: {resultsPath}");

            float tempStart = -1f;
            if (monitorCPUTemperature)
            {
                                HardwareMonitor.StartTemperatureMonitoring();
                tempStart = HardwareMonitor.GetCPUTemperature();
                Debug.Log($"[HardwareMonitor] Temp. CPU na starcie: {tempStart:F1}°C");
            }

            using (StreamWriter writer = new StreamWriter(resultsPath, false))
            {
                writer.AutoFlush = false;
                writer.WriteLine(BenchmarkMetrics.GetCsvHeader());

                if (includeFileMapInFullSuite && LoadGridMap())
                {
                    _activeMapTopology = "FromFile";
                    _activeMapSeed = 0;
                    _activeMapDensity = 1f - ((float)_gridMap.CountWalkable() / (_gridMap.Width * _gridMap.Height));
                    _activeMapWidth = _gridMap.Width;
                    _activeMapHeight = _gridMap.Height;
                    _testCases = GenerateTestCasesForMap(_gridMap, randomSeed);
                    yield return StartCoroutine(RunAllScenariosForCurrentMap(writer, estimatedTotalTestCases));
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
                                yield return StartCoroutine(RunAllScenariosForCurrentMap(writer, estimatedTotalTestCases));
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

            if (_stopBenchmarkRequested)
            {
                Debug.LogWarning($"[Visualizer] Pełny benchmark został zatrzymany. Częściowe wyniki zapisane w: {Path.GetFullPath(resultsPath)}");
            }
            else
            {
                Debug.Log($"[Visualizer] PEŁNY BENCHMARK ZAKOŃCZONY. Wyniki: {Path.GetFullPath(resultsPath)}");
            }

            Debug.Log($"[Visualizer] Postęp końcowy: {FormatProgress(_suiteTestId, estimatedTotalTestCases, "testów")}");
            _isAutoRunning = false;
        }

        private IEnumerator RunAllScenariosForCurrentMap(StreamWriter writer, int estimatedTotalTestCases)
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
                        Pathfinding.Core.PathfindingResult ignoredVisualResult;
                        MeasureAlgorithm(algorithm, algorithmGrid, startPos, targetPos,
                            _suiteTestId, currentDensity, out BenchmarkMetrics metrics, out ignoredVisualResult);
                        ApplyTestCaseMetadata(metrics, tc);

                        if (monitorCPUTemperature)
                            metrics.CPUTemperature = HardwareMonitor.GetCPUTemperature();

                        writer.WriteLine(metrics.ToCsvRow());
                        _rowsSinceFlush++;

                        if (_rowsSinceFlush >= headlessRowsPerFlush)
                        {
                            writer.Flush();
                            _rowsSinceFlush = 0;
                            yield return null;
                        }
                    }

                    _suiteTestId++;
                    if (_suiteTestId % HeadlessProgressInterval == 0)
                    {
                        writer.Flush();
                        Debug.Log($"[Visualizer] Postęp pełnego benchmarku: {FormatProgress(_suiteTestId, estimatedTotalTestCases, "testów")}");
                        yield return null;
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
                _ds1Manager.StepAll(_gridMap);
                _ds1Manager.VerifyObstaclePositions(_gridMap);
                return;
            }

            if (scenario == ScenarioType.DS2_PathObstruction)
            {
                scenarioChanges = ApplyDS2PathObstruction(startPos, targetPos, testId);
                return;
            }

            if (scenario == ScenarioType.DS3_DoorGateToggle)
            {
                scenarioChanges = ApplyDS3DoorGateToggle(startPos, targetPos, testId);
            }
        }

        private MovingObstacleManager CreateDS1ManagerForCurrentTest(
            GridMap grid, Vector2Int startPos, Vector2Int targetPos, int testId)
        {
            int seed = (runFullBenchmarkSuite ? _activeMapSeed : randomSeed) + testId;
            var manager = new MovingObstacleManager(seed);
            manager.GenerateObstacles(grid, movingObstacleCount, startPos, targetPos, patrolLength);
            return manager;
        }

        private List<Vector2Int> ApplyDS2PathObstruction(Vector2Int startPos, Vector2Int targetPos, int testId)
        {
            var changes = new List<Vector2Int>();
            var baseline = new AStarAlgorithm().FindPath(_gridMap, startPos, targetPos);
            if (!baseline.PathFound || baseline.Path == null || baseline.Path.Count == 0)
                return changes;

            int seed = (runFullBenchmarkSuite ? _activeMapSeed : randomSeed) + testId + 4000;
            var rng = new System.Random(seed);
            int spacing = Mathf.Max(2, pathObstructionSpacing);

            for (int i = spacing; i < baseline.Path.Count - 1 && changes.Count < pathObstructionChanges; i += spacing)
            {
                Vector2Int pos = baseline.Path[i];
                if (IsProtectedPoint(pos, startPos, targetPos))
                    continue;

                if (_gridMap.IsWalkable(pos))
                {
                    _gridMap.SetWalkable(pos, false);
                    changes.Add(pos);
                }

                if (changes.Count >= pathObstructionChanges)
                    break;

                if (TryFindNearbyObstacleToOpen(pos, startPos, targetPos, rng, out Vector2Int opened))
                {
                    _gridMap.SetWalkable(opened, true);
                    changes.Add(opened);
                }
            }

            return changes;
        }

        private List<Vector2Int> ApplyDS3DoorGateToggle(Vector2Int startPos, Vector2Int targetPos, int testId)
        {
            var changes = new List<Vector2Int>();
            var baseline = new AStarAlgorithm().FindPath(_gridMap, startPos, targetPos);
            if (!baseline.PathFound || baseline.Path == null || baseline.Path.Count < 4)
                return changes;

            int gatesPlaced = 0;
            int spacing = Mathf.Max(3, baseline.Path.Count / Mathf.Max(1, gateToggleCount + 1));
            int halfWidth = Mathf.Max(0, gateWidth / 2);

            for (int i = spacing; i < baseline.Path.Count - 1 && gatesPlaced < gateToggleCount; i += spacing)
            {
                Vector2Int gateCenter = baseline.Path[i];
                if (IsProtectedPoint(gateCenter, startPos, targetPos))
                    continue;

                Vector2Int previous = baseline.Path[Mathf.Max(0, i - 1)];
                Vector2Int next = baseline.Path[Mathf.Min(baseline.Path.Count - 1, i + 1)];
                Vector2Int direction = new Vector2Int(Math.Sign(next.x - previous.x), Math.Sign(next.y - previous.y));
                if (direction == Vector2Int.zero)
                    direction = Vector2Int.right;

                Vector2Int perpendicular = new Vector2Int(-direction.y, direction.x);
                if (perpendicular == Vector2Int.zero)
                    perpendicular = Vector2Int.up;

                bool closeDoor = ((testId + gatesPlaced) % 2 == 0);

                for (int offset = -halfWidth; offset <= halfWidth; offset++)
                {
                    Vector2Int wallPos = gateCenter + perpendicular * offset;
                    if (!_gridMap.IsValidCoordinate(wallPos.x, wallPos.y) || IsProtectedPoint(wallPos, startPos, targetPos))
                        continue;

                    bool shouldBeWalkable = wallPos == gateCenter && !closeDoor;
                    if (_gridMap.IsWalkable(wallPos) != shouldBeWalkable)
                    {
                        _gridMap.SetWalkable(wallPos, shouldBeWalkable);
                        changes.Add(wallPos);
                    }
                }

                gatesPlaced++;
            }

            return changes;
        }

        private bool TryFindNearbyObstacleToOpen(
            Vector2Int center, Vector2Int startPos, Vector2Int targetPos,
            System.Random rng, out Vector2Int obstacle)
        {
            var candidates = new List<Vector2Int>();
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    Vector2Int pos = new Vector2Int(center.x + dx, center.y + dy);
                    if (!_gridMap.IsValidCoordinate(pos.x, pos.y) || IsProtectedPoint(pos, startPos, targetPos))
                        continue;

                    if (!_gridMap.IsWalkable(pos))
                        candidates.Add(pos);
                }
            }

            if (candidates.Count == 0)
            {
                obstacle = default;
                return false;
            }

            obstacle = candidates[rng.Next(candidates.Count)];
            return true;
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
            visualManager.StepAll(visualGrid);
            visualManager.VerifyObstaclePositions(visualGrid);

            _gridMap = visualGrid;
            RefreshBasemapColors();

            if (_agentObject != null)
            {
                _agentObject.SetActive(true);
                _agentObject.transform.position = new Vector3(startPos.x, startPos.y, -2f);
            }

            Vector2Int currentPos = startPos;
            Pathfinding.Core.PathfindingResult currentResult = initialResult;
            int replanCount = 0;
            int safetyLimit = visualGrid.Width * visualGrid.Height * 2;

            while (currentPos != targetPos && safetyLimit-- > 0)
            {
                yield return StartCoroutine(PaintPathfindingOverlay(currentResult, startPos, targetPos, true));

                if (currentResult == null || !currentResult.PathFound || currentResult.Path == null || currentResult.Path.Count < 2)
                {
                    Debug.LogWarning($"[Visualizer][DS1] {algorithm.AlgorithmName}: brak dalszej drogi po {replanCount} rekalkulacjach.");
                    break;
                }

                bool replanned = false;
                for (int i = 1; i < currentResult.Path.Count; i++)
                {
                    Vector2Int nextPos = currentResult.Path[i];

                    List<(Vector2Int oldPos, Vector2Int newPos)> moves = visualManager.StepAll(visualGrid);
                    visualManager.VerifyObstaclePositions(visualGrid);
                    yield return StartCoroutine(AnimateDS1Obstacles(moves));

                    _gridMap = visualGrid;
                    RefreshBasemapColors();
                    yield return StartCoroutine(PaintPathfindingOverlay(currentResult, startPos, targetPos, false));

                    if (!visualGrid.IsWalkable(nextPos))
                    {
                        replanCount++;
                        if (_basemapRenderers != null)
                        {
                            _basemapRenderers[currentPos.x, currentPos.y].color = colorCurrentAgentCell;
                            _basemapRenderers[nextPos.x, nextPos.y].color = colorReplanPause;
                            _basemapRenderers[startPos.x, startPos.y].color = colorStart;
                            _basemapRenderers[targetPos.x, targetPos.y].color = colorTarget;
                        }
                        Debug.Log($"[Visualizer][DS1] {algorithm.AlgorithmName}: rekalkulacja #{replanCount}, zablokowany krok {nextPos}.");
                        currentResult = algorithm.FindPath(visualGrid, currentPos, targetPos);
                        replanned = true;
                        yield return new WaitForSeconds(replanPauseDuration);
                        break;
                    }

                    yield return StartCoroutine(MoveAgentTo(nextPos));
                    currentPos = nextPos;

                    if (_basemapRenderers != null && currentPos != targetPos)
                        _basemapRenderers[currentPos.x, currentPos.y].color = colorPath;

                    if (currentPos == targetPos)
                        break;
                }

                if (!replanned && currentPos != targetPos)
                    currentResult = algorithm.FindPath(visualGrid, currentPos, targetPos);
            }

            Debug.Log($"[Visualizer][DS1] {algorithm.AlgorithmName}: wizualizacja zakończona, rekalkulacje={replanCount}.");
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
            var allResults = new List<Pathfinding.Core.PathfindingResult>(benchmarkIterations);
            bool previousHistoryRecording = PathfindingRuntimeOptions.RecordExploredNodesHistory;

            try
            {
                for (int iter = 0; iter < benchmarkIterations; iter++)
                {
                    PathfindingRuntimeOptions.RecordExploredNodesHistory = ShouldVisualize && iter == 0;

                    // GC.Collect() TYLKO przed cold start — nie blokuj silnika w warm iterations
                    long gcBefore;
                    if (iter == 0 && forceGcBeforeColdStart)
                        gcBefore = HardwareMonitor.ForceGCAndGetMemory();
                    else
                        gcBefore = GC.GetTotalMemory(false);

                    Pathfinding.Core.PathfindingResult result = algorithm.FindPath(grid, startPos, targetPos);

                    long gcAfter = GC.GetTotalMemory(false);
                    result.GCAllocBytes = Math.Max(0, gcAfter - gcBefore);

                    if (result.PathFound)
                        result.CalculateSmoothnessMetrics();

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
                ScenarioType.DS3_DoorGateToggle => "DS3_DoorGateToggle",
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

                    long gcBefore;
                    if (iter == 0 && forceGcBeforeColdStart)
                        gcBefore = HardwareMonitor.ForceGCAndGetMemory();
                    else
                        gcBefore = GC.GetTotalMemory(false);

                    Pathfinding.Core.PathfindingResult result = algorithm.FindPath(grid, startPos, targetPos);

                    long gcAfter = GC.GetTotalMemory(false);
                    result.GCAllocBytes = Math.Max(0, gcAfter - gcBefore);

                    if (result.PathFound)
                        result.CalculateSmoothnessMetrics();

                    allResults.Add(result);

                    if (sw.ElapsedMilliseconds > 16 && iter + 1 < benchmarkIterations)
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

            string scenarioLabel = scenario switch
            {
                ScenarioType.Static => "Static",
                ScenarioType.DS1_MovingObstacles => "DS1_MovingObstacles",
                ScenarioType.DS2_PathObstruction => "DS2_PathObstruction",
                ScenarioType.DS3_DoorGateToggle => "DS3_DoorGateToggle",
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

        private static void ApplyTestCaseMetadata(BenchmarkMetrics metrics, TestCase testCase)
        {
            metrics.DistanceBucket = string.IsNullOrWhiteSpace(testCase.distanceBucket)
                ? "Unknown"
                : testCase.distanceBucket;
            metrics.EuclideanDistance = testCase.euclideanDistance;
            metrics.OctagonalDistance = testCase.octagonalDistance;
            metrics.ReferenceShortestPathLength = testCase.referenceShortestPathLength;
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
