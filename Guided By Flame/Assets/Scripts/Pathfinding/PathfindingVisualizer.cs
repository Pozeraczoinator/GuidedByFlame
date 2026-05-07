using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
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
    ///   ✓ 3 scenariusze dynamiczne + 1 statyczny
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
        /// 4 scenariusze testowe:
        /// - Static: stała mapa, brak zmian
        /// - DynamicAddWalls: dodawanie losowych ścian między testami (wymuszanie rekalkulacji)
        /// - DynamicRemoveWalls: usuwanie istniejących ścian (otwieranie nowych dróg)
        /// - DynamicToggle: losowe przełączanie ścian (najbardziej chaotyczny scenariusz)
        /// </summary>
        public enum ScenarioType
        {
            Static,
            DynamicAddWalls,
            DynamicRemoveWalls,
            DynamicToggle,
            DS2_MovingObstacles,
            DS3_WeightedTerrain
        }

        public enum MapTopology { FromFile, OpenField, Maze, RoomCorridor, ScatteredBlock }

        // ─────────────────────────────────────────────────────────
        //  KONFIGURACJA Z INSPEKTORA
        // ─────────────────────────────────────────────────────────

        [Header("═══ Tryb Benchmarku ═══")]
        [Tooltip("SingleAlgorithm = testuj tylko wybrany algorytm.\nAllAlgorithms = testuj wszystkie 5 po kolei (z animacją i losową kolejnością).")]
        public BenchmarkMode benchmarkMode = BenchmarkMode.AllAlgorithms;

        [Header("═══ Scenariusz Testowy ═══")]
        [Tooltip("Static / DynamicAddWalls / DynamicRemoveWalls / DynamicToggle / DS2_MovingObstacles / DS3_WeightedTerrain")]
        public ScenarioType scenario = ScenarioType.Static;

        [Tooltip("Liczba zmian przeszkód między kolejnymi algorytmami w trybie Dynamic/DS1.")]
        [Range(1, 50)]
        public int dynamicChangesCount = 5;

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

        [Tooltip("Nazwa pliku wynikowego CSV (23 kolumny, separator: średnik).")]
        public string outputFileName = "benchmark_results.csv";

        [Header("═══ Monitoring Sprzętowy ═══")]
        [Tooltip("Czy mierzyć temperaturę CPU przy każdym teście (Windows only). Spowalnia ~100ms per pomiar.")]
        public bool monitorCPUTemperature = false;

        [Header("═══ Generacja Map Proceduralnych ═══")]
        [Tooltip("Źródło mapy: FromFile = wczytaj z pliku TXT, inne = generuj proceduralnie.")]
        public MapTopology mapSource = MapTopology.FromFile;

        [Tooltip("Zagęszczenie przeszkód dla map proceduralnych (0.0–0.5).")]
        [Range(0f, 0.5f)]
        public float proceduralDensity = 0.2f;

        [Header("═══ Distance Bucketing (Naukowy Dobór Punktów) ═══")]
        [Tooltip("Czy generować test cases automatycznie z distance bucketing zamiast czytać z pliku CSV.")]
        public bool useDistanceBucketing = false;

        [Tooltip("Ile par testowych na wiązkę dystansową (SHORT/MEDIUM/LONG).")]
        [Range(5, 100)]
        public int pairsPerBucket = 30;

        [Tooltip("Ile par z nieosiągalnym celem (UNREACHABLE).")]
        [Range(0, 20)]
        public int unreachablePairs = 5;

        [Header("═══ DS2: Ruchome Przeszkody ═══")]
        [Tooltip("Liczba ruchomych przeszkód na mapie (patrol guards).")]
        [Range(1, 20)]
        public int movingObstacleCount = 3;

        [Tooltip("Długość trasy patrol każdej przeszkody (w polach).")]
        [Range(3, 20)]
        public int patrolLength = 6;

        [Header("═══ DS3: Dynamiczne Wagi Terenu ═══")]
        [Tooltip("Wzorzec zmiany wag: Random / Radial (ogień) / Linear (fala).")]
        public WeightedTerrainManager.ChangePattern weightChangePattern = WeightedTerrainManager.ChangePattern.Random;

        [Tooltip("Ile pól zmienia wagę co krok.")]
        [Range(1, 50)]
        public int weightChangesPerStep = 10;

        [Tooltip("Początkowe pokrycie pól z niedomyślnym kosztem (0.0–0.5).")]
        [Range(0f, 0.5f)]
        public float initialWeightCoverage = 0.1f;

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
        [Tooltip("Sprite dla zmian dynamicznych")]
        public Sprite dynamicChangeSprite;
        [Tooltip("Prefabrykat poruszającego się agenta (kostki)")]
        public GameObject agentPrefab;
        public float visualizationStepDelay = 0.05f;
        public float agentMoveSpeed = 10.0f;
        [Tooltip("Pauza w sekundach między kolejnymi algorytmami/testami.")]
        public float pauseBetweenTests = 2.0f;

        [Header("═══ Kolory ═══")]
        public Color colorWalkable = Color.white;
        public Color colorExplored = new Color(0.6f, 0.8f, 1f, 0.8f);
        public Color colorPath = new Color(1f, 1f, 0.2f, 0.9f);
        public Color colorStart = Color.red;
        public Color colorTarget = Color.green;

        // ─────────────────────────────────────────────────────────
        //  STAN WEWNĘTRZNY
        // ─────────────────────────────────────────────────────────

        private GridMap _gridMap;
        private GridMap _originalGridMap;
        private List<TestCase> _testCases = new List<TestCase>();
        private int _currentTestCaseIndex = 0;
        private SpriteRenderer[,] _basemapRenderers;
        private GameObject[,] _spawnedObstacles;
        private GameObject[,] _spawnedDynamicChanges;
        private GameObject _agentObject;
        private bool _isVisualizing = false;
        private bool _isAutoRunning = false;
        private System.Random _shuffleRng;

        // DS2/DS3 managery
        private MovingObstacleManager _ds2Manager;
        private WeightedTerrainManager _ds3Manager;

        private struct TestCase
        {
            public int startX, startY;
            public int targetX, targetY;
        }

        // ─────────────────────────────────────────────────────────
        //  LIFECYCLE
        // ─────────────────────────────────────────────────────────

        private void Start()
        {
            _shuffleRng = new System.Random(randomSeed);

            // Tryb batch generation — generuj mapy i wyjdź
            if (runBatchGeneration)
            {
                RunBatchGeneration();
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

            GenerateBasemapVisuals();
            Debug.Log($"[Visualizer] Gotowy. Tryb: {benchmarkMode}, Scenariusz: {scenario}, " +
                      $"Mapa: {mapSource}, Testy: {_testCases.Count}. Wciśnij SPACJĘ aby rozpocząć.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space) && !_isAutoRunning)
            {
                _isAutoRunning = true;
                StartCoroutine(AutoRunAllCases());
            }
        }

        // ─────────────────────────────────────────────────────────
        //  GŁÓWNA PĘTLA BENCHMARK + WIZUALIZACJA
        // ─────────────────────────────────────────────────────────

        private IEnumerator AutoRunAllCases()
        {
            string resultsPath = Path.Combine(Application.dataPath, "..", outputFileName);
            Debug.Log($"[Visualizer] ══════════════════════════════════════════");
            Debug.Log($"[Visualizer] START BENCHMARKU");
            Debug.Log($"[Visualizer] Tryb: {benchmarkMode} | Scenariusz: {scenario}");
            Debug.Log($"[Visualizer] Iteracje: {benchmarkIterations} | Testy: {_testCases.Count}");
            Debug.Log($"[Visualizer] Plik CSV: {resultsPath}");
            Debug.Log($"[Visualizer] ══════════════════════════════════════════");

            // Monitoring temperatury — start
            float tempStart = -1f;
            if (monitorCPUTemperature)
            {
                tempStart = HardwareMonitor.GetCPUTemperature();
                Debug.Log($"[HardwareMonitor] Temp. CPU na starcie: {tempStart:F1}°C");
            }

            float mapDensity = 1f - ((float)_gridMap.CountWalkable() / (_gridMap.Width * _gridMap.Height));

            using (StreamWriter writer = new StreamWriter(resultsPath, false))
            {
                writer.AutoFlush = true;
                writer.WriteLine(BenchmarkMetrics.GetCsvHeader());

                while (_currentTestCaseIndex < _testCases.Count)
                {
                    TestCase tc = _testCases[_currentTestCaseIndex];
                    int testId = _currentTestCaseIndex;
                    _currentTestCaseIndex++;

                    Vector2Int startPos = new Vector2Int(tc.startX, tc.startY);
                    Vector2Int targetPos = new Vector2Int(tc.targetX, tc.targetY);

                    // Walidacja
                    if (!_gridMap.IsWalkable(startPos) || !_gridMap.IsWalkable(targetPos))
                    {
                        Debug.LogWarning($"[Visualizer] Test {testId}: start/cel nie jest walkable. Pomijam.");
                        continue;
                    }

                    // ─── Pobierz i LOSUJ kolejność algorytmów ───
                    List<IPathfindingAlgorithm> algorithmsToRun = GetAlgorithmsToRun();
                    ShuffleList(algorithmsToRun); // Fisher-Yates — eliminacja thermal throttling bias

                    Debug.Log($"[Visualizer] ── Test {testId + 1}/{_testCases.Count} ── " +
                              $"Kolejność: {string.Join(" → ", GetAlgorithmNames(algorithmsToRun))}");

                    // DS2: inicjalizacja ruchomych przeszkód per test case
                    if (scenario == ScenarioType.DS2_MovingObstacles)
                    {
                        _ds2Manager = new MovingObstacleManager(randomSeed + testId);
                        _ds2Manager.GenerateObstacles(_gridMap, movingObstacleCount, startPos, targetPos, patrolLength);
                        RefreshBasemapColors();
                    }

                    // DS3: inicjalizacja wag terenu per test case
                    if (scenario == ScenarioType.DS3_WeightedTerrain)
                    {
                        _ds3Manager = new WeightedTerrainManager(randomSeed + testId);
                        _ds3Manager.InitializeWeights(_gridMap, weightChangePattern, initialWeightCoverage);
                    }

                    foreach (var algorithm in algorithmsToRun)
                    {
                        // ─── Scenariusz dynamiczny: zmodyfikuj mapę PRZED pomiarem ───
                        List<Vector2Int> dynamicChanges = null;
                        if (scenario == ScenarioType.DynamicAddWalls ||
                            scenario == ScenarioType.DynamicRemoveWalls ||
                            scenario == ScenarioType.DynamicToggle)
                        {
                            dynamicChanges = ApplyDynamicScenario(startPos, targetPos);
                            RefreshBasemapColors();
                        }
                        else if (scenario == ScenarioType.DS2_MovingObstacles && _ds2Manager != null)
                        {
                            dynamicChanges = _ds2Manager.StepAll(_gridMap);
                            // Weryfikacja: upewnij się że WSZYSTKIE przeszkody blokują swoje pola
                            _ds2Manager.VerifyObstaclePositions(_gridMap);
                            RefreshBasemapColors();
                        }
                        else if (scenario == ScenarioType.DS3_WeightedTerrain && _ds3Manager != null)
                        {
                            dynamicChanges = _ds3Manager.ApplyDynamicWeightChanges(
                                _gridMap, weightChangePattern, weightChangesPerStep, startPos, targetPos);
                        }

                        float currentDensity = (scenario == ScenarioType.Static)
                            ? mapDensity
                            : 1f - ((float)_gridMap.CountWalkable() / (_gridMap.Width * _gridMap.Height));

                        // ─── KROK 1: Pomiar z N iteracjami ───
                        BenchmarkMetrics metrics;
                        Pathfinding.Core.PathfindingResult visualResult;
                        MeasureAlgorithm(algorithm, _gridMap, startPos, targetPos,
                            testId, currentDensity, out metrics, out visualResult);

                        if (monitorCPUTemperature)
                        {
                            metrics.CPUTemperature = HardwareMonitor.GetCPUTemperature();
                        }

                        Debug.Log($"[Visualizer] {algorithm.AlgorithmName}: " +
                                  $"start={startPos} → cel={targetPos} | " +
                                  $"Znaleziono: {metrics.PathFound} | " +
                                  $"Czas: {metrics.AvgExecutionTimeMs:F4}ms | " +
                                  $"Węzły: {metrics.ExploredNodes}");

                        // ─── KROK 2: Animacja wizualizacji ───
                        StartCoroutine(VisualizeRoutine(visualResult, startPos, targetPos, 
                            algorithm.AlgorithmName, dynamicChanges));

                        while (_isVisualizing)
                        {
                            yield return null;
                        }

                        // ─── KROK 3: Zapis PO animacji ───
                        writer.WriteLine(metrics.ToCsvRow());
                        Debug.Log($"[Visualizer] ✓ Zapisano: {algorithm.AlgorithmName} | " +
                                  $"Ścieżka: {metrics.PathLength:F2} | " +
                                  $"Smoothness: {metrics.PathSmoothness:F4}");

                        // ─── KROK 4: Cofnij zmiany dynamiczne (resetuj mapę) ───
                        if (scenario == ScenarioType.DynamicAddWalls ||
                            scenario == ScenarioType.DynamicRemoveWalls ||
                            scenario == ScenarioType.DynamicToggle)
                        {
                            if (dynamicChanges != null && dynamicChanges.Count > 0)
                            {
                                RevertDynamicChanges(dynamicChanges);
                                RefreshBasemapColors();
                            }
                        }

                        yield return new WaitForSeconds(pauseBetweenTests);
                    }

                    // Po zakończeniu test case'a: resetuj mapę dla DS2/DS3
                    if (scenario == ScenarioType.DS2_MovingObstacles ||
                        scenario == ScenarioType.DS3_WeightedTerrain)
                    {
                        _gridMap = _originalGridMap.Clone();
                        RefreshBasemapColors();
                    }
                }
            }

            // Monitoring temperatury — koniec
            if (monitorCPUTemperature)
            {
                float tempEnd = HardwareMonitor.GetCPUTemperature();
                Debug.Log($"[HardwareMonitor] Temp. CPU na końcu: {tempEnd:F1}°C " +
                          $"(delta: {tempEnd - tempStart:F1}°C)");
            }

            Debug.Log($"[Visualizer] ══════════════════════════════════════════");
            Debug.Log($"[Visualizer] ✓ BENCHMARK ZAKOŃCZONY");
            Debug.Log($"[Visualizer] Wyniki: {Path.GetFullPath(resultsPath)}");
            Debug.Log($"[Visualizer] ══════════════════════════════════════════");
            _isAutoRunning = false;
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

            for (int iter = 0; iter < benchmarkIterations; iter++)
            {
                // GC.Collect() TYLKO przed cold start — nie blokuj silnika w warm iterations
                long gcBefore;
                if (iter == 0)
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

            visualResult = allResults[0];

            string scenarioLabel = scenario switch
            {
                ScenarioType.Static => "Static",
                ScenarioType.DynamicAddWalls => "DynamicAddWalls",
                ScenarioType.DynamicRemoveWalls => "DynamicRemoveWalls",
                ScenarioType.DynamicToggle => "DynamicToggle",
                ScenarioType.DS2_MovingObstacles => "DS2_MovingObstacles",
                ScenarioType.DS3_WeightedTerrain => "DS3_WeightedTerrain",
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
                ObstacleDensity = density
            };
            metrics.AggregateFrom(allResults);
        }

        // ─────────────────────────────────────────────────────────
        //  SCENARIUSZE DYNAMICZNE
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Aplikuje wybrany scenariusz dynamiczny.
        /// Zwraca listę zmienionych pozycji do późniejszego cofnięcia.
        /// 
        /// Scenariusze:
        ///   DynamicAddWalls — dodaje losowe ściany (walkable → obstacle)
        ///   DynamicRemoveWalls — usuwa losowe ściany (obstacle → walkable)
        ///   DynamicToggle — przełącza losowe pola
        /// </summary>
        private List<Vector2Int> ApplyDynamicScenario(Vector2Int start, Vector2Int target)
        {
            var changes = new List<Vector2Int>();
            int attempts = 0;
            int maxAttempts = dynamicChangesCount * 20;

            while (changes.Count < dynamicChangesCount && attempts < maxAttempts)
            {
                attempts++;
                int x = _shuffleRng.Next(0, _gridMap.Width);
                int y = _shuffleRng.Next(0, _gridMap.Height);
                Vector2Int pos = new Vector2Int(x, y);

                // Chronimy start i cel (i ich sąsiadów)
                if (IsNearPoint(pos, start, 1) || IsNearPoint(pos, target, 1))
                    continue;

                bool currentWalkable = _gridMap.IsWalkable(x, y);

                switch (scenario)
                {
                    case ScenarioType.DynamicAddWalls:
                        if (currentWalkable) // Dodaj ścianę tylko tam gdzie jest wolne
                        {
                            _gridMap.SetWalkable(x, y, false);
                            changes.Add(pos);
                        }
                        break;

                    case ScenarioType.DynamicRemoveWalls:
                        if (!currentWalkable) // Usuń ścianę tylko tam gdzie jest blokada
                        {
                            _gridMap.SetWalkable(x, y, true);
                            changes.Add(pos);
                        }
                        break;

                    case ScenarioType.DynamicToggle:
                        _gridMap.SetWalkable(x, y, !currentWalkable);
                        changes.Add(pos);
                        break;
                }
            }

            return changes;
        }

        /// <summary>
        /// Cofa zmiany dynamiczne — przywraca stan sprzed modyfikacji.
        /// </summary>
        private void RevertDynamicChanges(List<Vector2Int> changes)
        {
            foreach (var pos in changes)
            {
                bool currentState = _gridMap.IsWalkable(pos);
                _gridMap.SetWalkable(pos, !currentState);
            }
        }

        private bool IsNearPoint(Vector2Int a, Vector2Int b, int radius)
        {
            return Math.Abs(a.x - b.x) <= radius && Math.Abs(a.y - b.y) <= radius;
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

                // JPS pomijany w DS3 — nie wspiera weighted gridów
                if (scenario != ScenarioType.DS3_WeightedTerrain)
                {
                    list.Add(new JumpPointSearchAlgorithm());
                }

                return list;
            }
            else
            {
                // W trybie SingleAlgorithm: ostrzegaj jeśli JPS + DS3
                if (selectedAlgorithm == AlgorithmChoice.JumpPointSearch &&
                    scenario == ScenarioType.DS3_WeightedTerrain)
                {
                    Debug.LogWarning("[Visualizer] JPS nie wspiera weighted gridów (DS3). Przełączam na A*.");
                    return new List<IPathfindingAlgorithm> { new AStarAlgorithm() };
                }
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
                    _testCases.Add(new TestCase
                    {
                        startX = int.Parse(columns[0].Trim()),
                        startY = int.Parse(columns[1].Trim()),
                        targetX = int.Parse(columns[2].Trim()),
                        targetY = int.Parse(columns[3].Trim())
                    });
                }
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
            _spawnedDynamicChanges = new GameObject[width, height];

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
                    if (!_gridMap.IsWalkable(x, y) && obstacleSprite != null)
                    {
                        GameObject obs = new GameObject($"Obstacle_{x}_{y}");
                        obs.transform.position = new Vector3(x, y, -0.1f);
                        obs.transform.parent = this.transform;
                        obs.transform.localScale = new Vector3(0.95f, 0.95f, 1f);

                        SpriteRenderer obsSr = obs.AddComponent<SpriteRenderer>();
                        obsSr.sprite = obstacleSprite;
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
        }

        /// <summary>
        /// Odświeża kolory basemapy zgodnie z aktualnym stanem GridMap.
        /// Wywoływane po zmianach dynamicznych.
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
                        if (_spawnedObstacles[x, y] == null && obstacleSprite != null)
                        {
                            GameObject obs = new GameObject($"Obstacle_{x}_{y}");
                            obs.transform.position = new Vector3(x, y, -0.1f);
                            obs.transform.parent = this.transform;
                            obs.transform.localScale = new Vector3(0.95f, 0.95f, 1f);

                            SpriteRenderer obsSr = obs.AddComponent<SpriteRenderer>();
                            obsSr.sprite = obstacleSprite;
                            obsSr.sortingOrder = 1; // Wyżej niż basemap

                            _spawnedObstacles[x, y] = obs;
                        }
                        if (_spawnedObstacles[x, y] != null) _spawnedObstacles[x, y].SetActive(true);
                    }
                    else
                    {
                        if (_spawnedObstacles[x, y] != null) _spawnedObstacles[x, y].SetActive(false);
                    }

                    // Reset dynamic changes objects
                    if (_spawnedDynamicChanges[x, y] != null)
                    {
                        _spawnedDynamicChanges[x, y].SetActive(false);
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
            List<Vector2Int> dynamicChanges)
        {
            _isVisualizing = true;

            // Zresetuj podświetlenia (zachowaj aktualny stan ścian)
            RefreshBasemapColors();

            // Pokaż zmiany dynamiczne (prefaby na wierzchu)
            if (dynamicChanges != null)
            {
                foreach (var pos in dynamicChanges)
                {
                    if (pos.x >= 0 && pos.x < _gridMap.Width && pos.y >= 0 && pos.y < _gridMap.Height)
                    {
                        if (_spawnedDynamicChanges[pos.x, pos.y] == null && dynamicChangeSprite != null)
                        {
                            GameObject dyn = new GameObject($"DynamicChange_{pos.x}_{pos.y}");
                            dyn.transform.position = new Vector3(pos.x, pos.y, -0.2f); // Bliżej kamery
                            dyn.transform.parent = this.transform;
                            dyn.transform.localScale = new Vector3(0.95f, 0.95f, 1f);

                            SpriteRenderer dynSr = dyn.AddComponent<SpriteRenderer>();
                            dynSr.sprite = dynamicChangeSprite;
                            dynSr.sortingOrder = 2; // Najwyżej w hierarchii 2D (poza agentem)

                            _spawnedDynamicChanges[pos.x, pos.y] = dyn;
                        }
                        if (_spawnedDynamicChanges[pos.x, pos.y] != null)
                        {
                            _spawnedDynamicChanges[pos.x, pos.y].SetActive(true);
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

        private bool GenerateProceduralMap()
        {
            IMapGenerator generator = mapSource switch
            {
                MapTopology.OpenField => new OpenFieldGenerator(),
                MapTopology.Maze => new MazeGenerator(),
                MapTopology.RoomCorridor => new RoomCorridorGenerator(),
                MapTopology.ScatteredBlock => new ScatteredBlockGenerator(),
                _ => null
            };

            if (generator == null)
            {
                Debug.LogError("[Visualizer] Nieznana topologia mapy.");
                return false;
            }

            // Użyj rozmiarów z istniejącej mapy (32x20) lub z pola
            int w = 32, h = 20;
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
            var enhanced = selector.GenerateTestCases(_gridMap, pairsPerBucket, unreachablePairs);

            _testCases.Clear();
            foreach (var etc in enhanced)
            {
                _testCases.Add(new TestCase
                {
                    startX = etc.StartX, startY = etc.StartY,
                    targetX = etc.TargetX, targetY = etc.TargetY
                });
            }

            // Eksportuj CSV z metadanymi
            string csvPath = Path.Combine(Application.dataPath, "..", "EnhancedTestCases.csv");
            TestPointSelector.ExportToCsv(enhanced, csvPath);
            Debug.Log($"[Visualizer] Distance bucketing: {_testCases.Count} par " +
                      $"(SHORT: {pairsPerBucket}, MEDIUM: {pairsPerBucket}, " +
                      $"LONG: {pairsPerBucket}, UNREACHABLE: {unreachablePairs})");
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
                        var testCases = selector.GenerateTestCases(map, pairsPerBucket, unreachablePairs);
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
