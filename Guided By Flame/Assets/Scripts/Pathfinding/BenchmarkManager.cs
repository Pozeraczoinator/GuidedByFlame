using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Pathfinding.Core;
using Pathfinding.Algorithms;
using Pathfinding.MapGenerators;

namespace Pathfinding.Benchmark
{
    /// <summary>
    /// Główny manager benchmarkowy do pracy magisterskiej.
    /// 
    /// Kluczowe cechy naukowe:
    /// 1. Cold Start rejestrowany osobno (iteracja 0 → ColdStartTimeMs)
    /// 2. Randomizacja kolejności algorytmów w każdej iteracji (Fisher-Yates shuffle)
    ///    → eliminacja thermal throttling bias
    /// 3. Pomiar GC Alloc (GC.GetTotalMemory delta)
    /// 4. Pomiar PathSmoothness (liczba zmian kierunku / długość ścieżki)
    /// 5. Tryb statyczny i DS1 z ruchomymi przeszkodami
    /// 6. Jeden wspólny plik CSV — łatwy import do R/Python/Excel
    /// 7. Statystyki: Avg, Min, Max, StdDev czasu wykonania
    ///
    /// Format CSV (separator: średnik):
    /// TestID;Algorithm;StartX;StartY;TargetX;TargetY;Scenario;ObstacleDensity;
    /// PathFound;ColdStartTimeMs;ColdStartTicks;ColdStartGCAllocBytes;
    /// AvgExecutionTimeMs;MinExecutionTimeMs;MaxExecutionTimeMs;StdDevExecutionTimeMs;
    /// AvgExecutionTicks;AvgGCAllocBytes;
    /// ExploredNodes;PathLength;DirectionChanges;PathSmoothness
    /// </summary>
    public class BenchmarkManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────
        //  KONFIGURACJA Z INSPEKTORA UNITY
        // ─────────────────────────────────────────────────────────

        [Header("═══ Pliki Wejściowe ═══")]
        [Tooltip("Nazwa pliku z przypadkami testowymi (CSV). Szukany w katalogu projektu.")]
        public string testCasesFileName = "TestCases.csv";

        [Tooltip("Nazwa pliku mapy (TXT). Format: 0=walkable, 1=obstacle. Szukany w katalogu projektu.")]
        public string mapFileName = "Map.txt";

        [Header("═══ Parametry Benchmarku ═══")]
        [Tooltip("Liczba iteracji na algorytm na test case. Minimum 2 (1 cold + 1 warm). Zalecane: 30.")]
        [Range(2, 200)]
        public int testIterations = 30;

        [Tooltip("Nazwa pliku wynikowego CSV.")]
        public string outputFileName = "benchmark_results.csv";

        [Header("═══ Scenariusz ═══")]
        [Tooltip("Tryb testu: Static albo DS1_MovingObstacles.")]
        public DynamicScenario scenario = DynamicScenario.Static;

        [Tooltip("Seed RNG do generowania map proceduralnych. Ten sam seed = te same wyniki.")]
        public int randomSeed = 42;

        [Header("═══ Generacja Map ═══")]
        [Tooltip("Czy generować mapy proceduralne z różnym zagęszczeniem zamiast czytać Map.txt.")]
        public bool useProceduralMaps = false;

        [Tooltip("Wymiar mapy proceduralnej — szerokość.")]
        [Range(10, 500)]
        public int proceduralMapWidth = 32;

        [Tooltip("Wymiar mapy proceduralnej — wysokość.")]
        [Range(10, 500)]
        public int proceduralMapHeight = 20;

        [Tooltip("Poziomy zagęszczenia przeszkód do przetestowania (np. 0.1, 0.2, 0.3, 0.4).")]
        public float[] obstacleDensityLevels = { 0.10f, 0.20f, 0.30f, 0.40f };

        [Header("═══ DS1: Moving Obstacles ═══")]
        [Tooltip("Liczba ruchomych przeszkód na mapie.")]
        [Range(1, 20)]
        public int movingObstacleCount = 3;

        [Tooltip("Długość trasy patrol każdej przeszkody (w polach).")]
        [Range(3, 20)]
        public int patrolLength = 6;

        [Header("═══ Monitoring Sprzętowy ═══")]
        [Tooltip("Czy zapisywać temperaturę CPU na początku i końcu benchmarku (Windows only).")]
        public bool monitorCPUTemperature = false;

        // ─────────────────────────────────────────────────────────
        //  TYPY
        // ─────────────────────────────────────────────────────────

        public enum DynamicScenario
        {
            Static = 0,
            DS1_MovingObstacles = 1
        }

        private struct TestCase
        {
            public int startX, startY;
            public int targetX, targetY;
        }

        // ─────────────────────────────────────────────────────────
        //  STAN WEWNĘTRZNY
        // ─────────────────────────────────────────────────────────

        private List<IPathfindingAlgorithm> _algorithms;
        private GridMap _gridMap;
        private System.Random _shuffleRng;

        // ─────────────────────────────────────────────────────────
        //  LIFECYCLE
        // ─────────────────────────────────────────────────────────

        private void Start()
        {
            _algorithms = new List<IPathfindingAlgorithm>
            {
                new AStarAlgorithm(),
                new DijkstraAlgorithm(),
                new GreedyBestFirstAlgorithm(),
                new CustomGreedyAlgorithm(),
                new JumpPointSearchAlgorithm()
            };

            _shuffleRng = new System.Random(randomSeed);

            if (!useProceduralMaps)
            {
                if (LoadGridMap())
                {
                    StartCoroutine(RunBenchmarkCoroutine());
                }
            }
            else
            {
                StartCoroutine(RunProceduralBenchmarkCoroutine());
            }
        }

        // ─────────────────────────────────────────────────────────
        //  ŁADOWANIE DANYCH
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Wczytuje mapę z pliku TXT. Format: '0' = walkable, '1' = obstacle.
        /// Odczyt od dołu do góry aby pasował do układu osi Y w Unity.
        /// </summary>
        private bool LoadGridMap()
        {
            string path = FindFile(mapFileName);
            if (path == null)
            {
                UnityEngine.Debug.LogError($"[BenchmarkManager] Nie znaleziono pliku mapy: {mapFileName}");
                return false;
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) return false;

            int height = lines.Length;
            int width = lines[0].Length;
            bool[,] collisionData = new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                string line = lines[height - 1 - y]; // Odczyt od dołu do góry
                for (int x = 0; x < width; x++)
                {
                    if (x < line.Length)
                        collisionData[x, y] = (line[x] == '0');
                    else
                        collisionData[x, y] = false;
                }
            }

            _gridMap = new GridMap(collisionData);
            UnityEngine.Debug.Log($"[BenchmarkManager] Wczytano GridMap z {path}. Wymiary: {width}x{height}");
            return true;
        }

        /// <summary>
        /// Wczytuje przypadki testowe z CSV. Format: startX,startY,targetX,targetY (nagłówek pomijany).
        /// </summary>
        private List<TestCase> LoadTestCases()
        {
            string path = FindFile(testCasesFileName);
            if (path == null)
            {
                UnityEngine.Debug.LogError($"[BenchmarkManager] Nie znaleziono pliku testów: {testCasesFileName}");
                return new List<TestCase>();
            }

            List<TestCase> list = new List<TestCase>();
            var lines = File.ReadAllLines(path);

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var columns = lines[i].Split(',');
                if (columns.Length >= 4)
                {
                    list.Add(new TestCase
                    {
                        startX = int.Parse(columns[0].Trim()),
                        startY = int.Parse(columns[1].Trim()),
                        targetX = int.Parse(columns[2].Trim()),
                        targetY = int.Parse(columns[3].Trim())
                    });
                }
            }

            UnityEngine.Debug.Log($"[BenchmarkManager] Wczytano {list.Count} przypadków testowych.");
            return list;
        }

        /// <summary>
        /// Szuka pliku w kilku standardowych lokalizacjach Unity.
        /// </summary>
        private string FindFile(string fileName)
        {
            string[] searchPaths = {
                fileName,
                Path.Combine(Application.dataPath, "..", fileName),
                Path.Combine(Application.dataPath, "../..", fileName),
                Path.Combine(Application.dataPath, fileName)
            };

            foreach (var p in searchPaths)
            {
                if (File.Exists(p))
                    return Path.GetFullPath(p);
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────
        //  WYBÓR ALGORYTMÓW NA PODSTAWIE SCENARIUSZA
        // ─────────────────────────────────────────────────────────

        private List<IPathfindingAlgorithm> GetAlgorithmsForScenario()
        {
            return _algorithms;
        }

        private string GetScenarioLabel()
        {
            switch (scenario)
            {
                case DynamicScenario.Static: return "Static";
                case DynamicScenario.DS1_MovingObstacles: return "DS1_MovingObstacles";
                default: return "Unknown";
            }
        }

        // ─────────────────────────────────────────────────────────
        //  BENCHMARK — TRYB Z PLIKU MAPY
        // ─────────────────────────────────────────────────────────

        private IEnumerator RunBenchmarkCoroutine()
        {
            List<TestCase> testCases = LoadTestCases();
            if (testCases.Count == 0) yield break;

            string resultsPath = Path.Combine(Application.dataPath, "..", outputFileName);
            string scenarioLabel = GetScenarioLabel();
            var activeAlgorithms = GetAlgorithmsForScenario();

            UnityEngine.Debug.Log($"[BenchmarkManager] Start benchmarku. Iteracje: {testIterations}, " +
                                  $"Algorytmy: {activeAlgorithms.Count}, Testy: {testCases.Count}, " +
                                  $"Tryb: {scenarioLabel}");

            // Monitoring temperatury — początek
            float tempStart = -1f;
            if (monitorCPUTemperature)
            {
                tempStart = HardwareMonitor.GetCPUTemperature();
                UnityEngine.Debug.Log($"[HardwareMonitor] Temperatura CPU na starcie: {tempStart:F1}°C");
            }

            using (StreamWriter writer = new StreamWriter(resultsPath, false))
            {
                writer.AutoFlush = true;
                writer.WriteLine(BenchmarkMetrics.GetCsvHeader());

                float mapDensity = 1f - ((float)_gridMap.CountWalkable() / (_gridMap.Width * _gridMap.Height));

                for (int testIdx = 0; testIdx < testCases.Count; testIdx++)
                {
                    TestCase tc = testCases[testIdx];
                    Vector2Int startPos = new Vector2Int(tc.startX, tc.startY);
                    Vector2Int targetPos = new Vector2Int(tc.targetX, tc.targetY);

                    // Walidacja — upewnij się że start i cel są walkable
                    if (!_gridMap.IsWalkable(startPos) || !_gridMap.IsWalkable(targetPos))
                    {
                        UnityEngine.Debug.LogWarning($"[BenchmarkManager] Test {testIdx}: Start lub cel nie jest walkable! Pomijam.");
                        continue;
                    }

                    // Randomizacja kolejności algorytmów — eliminacja thermal throttling bias
                    ShuffleList(activeAlgorithms);

                    foreach (var algorithm in activeAlgorithms)
                    {
                        GridMap testGrid = (scenario != DynamicScenario.Static)
                            ? _gridMap.Clone()
                            : _gridMap;

                        BenchmarkMetrics metrics = RunAlgorithmBenchmark(
                            algorithm, testGrid, startPos, targetPos, testIdx,
                            scenarioLabel, mapDensity
                        );

                        writer.WriteLine(metrics.ToCsvRow());
                        yield return null;
                    }

                    if (testIdx % 10 == 0 || testIdx == testCases.Count - 1)
                    {
                        UnityEngine.Debug.Log($"[BenchmarkManager] Postęp: {testIdx + 1}/{testCases.Count} testów ukończonych.");
                    }
                    yield return null;
                }
            }

            // Monitoring temperatury — koniec
            if (monitorCPUTemperature)
            {
                float tempEnd = HardwareMonitor.GetCPUTemperature();
                UnityEngine.Debug.Log($"[HardwareMonitor] Temperatura CPU na końcu: {tempEnd:F1}°C (delta: {tempEnd - tempStart:F1}°C)");
            }

            UnityEngine.Debug.Log($"[BenchmarkManager] ✓ BENCHMARK ZAKOŃCZONY. " +
                                  $"Wyniki zapisane do: {Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputFileName))}");
        }

        // ─────────────────────────────────────────────────────────
        //  BENCHMARK — TRYB PROCEDURALNY (generowane mapy)
        // ─────────────────────────────────────────────────────────

        private IEnumerator RunProceduralBenchmarkCoroutine()
        {
            List<TestCase> testCases = LoadTestCases();
            if (testCases.Count == 0) yield break;

            string resultsPath = Path.Combine(Application.dataPath, "..", outputFileName);
            var dynamicManager = new DynamicObstacleManager(randomSeed);
            string scenarioLabel = GetScenarioLabel();
            var activeAlgorithms = GetAlgorithmsForScenario();

            UnityEngine.Debug.Log($"[BenchmarkManager] Start benchmarku proceduralnego. " +
                                  $"Rozmiar mapy: {proceduralMapWidth}x{proceduralMapHeight}, " +
                                  $"Gęstości: [{string.Join(", ", obstacleDensityLevels.Select(d => $"{d:P0}"))}]");

            float tempStart = -1f;
            if (monitorCPUTemperature)
            {
                tempStart = HardwareMonitor.GetCPUTemperature();
                UnityEngine.Debug.Log($"[HardwareMonitor] Temperatura CPU na starcie: {tempStart:F1}°C");
            }

            using (StreamWriter writer = new StreamWriter(resultsPath, false))
            {
                writer.AutoFlush = true;
                writer.WriteLine(BenchmarkMetrics.GetCsvHeader());

                foreach (float density in obstacleDensityLevels)
                {
                    UnityEngine.Debug.Log($"[BenchmarkManager] ── Zagęszczenie: {density:P0} ──");

                    for (int testIdx = 0; testIdx < testCases.Count; testIdx++)
                    {
                        TestCase tc = testCases[testIdx];
                        Vector2Int startPos = new Vector2Int(tc.startX, tc.startY);
                        Vector2Int targetPos = new Vector2Int(tc.targetX, tc.targetY);

                        startPos = ClampToMap(startPos, proceduralMapWidth, proceduralMapHeight);
                        targetPos = ClampToMap(targetPos, proceduralMapWidth, proceduralMapHeight);

                        GridMap proceduralGrid = dynamicManager.GenerateMap(
                            proceduralMapWidth, proceduralMapHeight, density, startPos, targetPos
                        );

                        ShuffleList(activeAlgorithms);

                        foreach (var algorithm in activeAlgorithms)
                        {
                            GridMap testGrid = (scenario != DynamicScenario.Static)
                                ? proceduralGrid.Clone()
                                : proceduralGrid;

                            BenchmarkMetrics metrics = RunAlgorithmBenchmark(
                                algorithm, testGrid, startPos, targetPos, testIdx,
                                scenarioLabel, density
                            );

                            writer.WriteLine(metrics.ToCsvRow());
                            yield return null;
                        }

                        if (testIdx % 10 == 0 || testIdx == testCases.Count - 1)
                        {
                            UnityEngine.Debug.Log($"[BenchmarkManager] Gęstość {density:P0}: " +
                                                  $"{testIdx + 1}/{testCases.Count} testów.");
                        }
                        yield return null;
                    }
                }
            }

            if (monitorCPUTemperature)
            {
                float tempEnd = HardwareMonitor.GetCPUTemperature();
                UnityEngine.Debug.Log($"[HardwareMonitor] Temperatura CPU na końcu: {tempEnd:F1}°C (delta: {tempEnd - tempStart:F1}°C)");
            }

            UnityEngine.Debug.Log($"[BenchmarkManager] ✓ BENCHMARK PROCEDURALNY ZAKOŃCZONY. " +
                                  $"Wyniki: {Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputFileName))}");
        }

        // ─────────────────────────────────────────────────────────
        //  CORE: Uruchomienie benchmarku jednego algorytmu
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Uruchamia N iteracji jednego algorytmu na jednym test case.
        /// 
        /// PROTOKÓŁ NAUKOWY:
        /// 1. Iteracja 0 = Cold Start (JIT warm-up) — rejestrowana osobno
        /// 2. Iteracje 1..N-1 = Warm iterations — podstawa statystyk
        /// 3. Kolejność algorytmów randomizowana (Fisher-Yates) poziom wyżej
        /// 4. GC.Collect() wywoływany TYLKO RAZ przed cold start (iteracja 0)
        /// 5. W trybie DS1: ruchome przeszkody krok po kroku
        /// 
        /// Złożoność: O(testIterations × złożoność algorytmu)
        /// </summary>
        private BenchmarkMetrics RunAlgorithmBenchmark(
            IPathfindingAlgorithm algorithm, GridMap grid,
            Vector2Int startPos, Vector2Int targetPos,
            int testId, string scenarioLabel, float density)
        {
            var allResults = new List<Pathfinding.Core.PathfindingResult>(testIterations);

            // Inicjalizacja managerów dynamicznych per algorytm-test
            MovingObstacleManager ds2Mgr = null;

            switch (scenario)
            {
                case DynamicScenario.DS1_MovingObstacles:
                    ds2Mgr = new MovingObstacleManager(randomSeed + testId);
                    ds2Mgr.GenerateObstacles(grid, movingObstacleCount, startPos, targetPos, patrolLength);
                    break;
            }

            for (int iter = 0; iter < testIterations; iter++)
            {
                // GC.Collect() TYLKO przed cold start (iteracja 0)
                long gcBefore;
                if (iter == 0)
                {
                    gcBefore = HardwareMonitor.ForceGCAndGetMemory();
                }
                else
                {
                    gcBefore = GC.GetTotalMemory(false);
                }

                // Uruchom algorytm z pomiarem
                Pathfinding.Core.PathfindingResult result = algorithm.FindPath(grid, startPos, targetPos);

                // Pomiar GC po zakończeniu
                long gcAfter = GC.GetTotalMemory(false);
                result.GCAllocBytes = Math.Max(0, gcAfter - gcBefore);

                // Oblicz metryki gładkości
                if (result.PathFound)
                {
                    result.CalculateSmoothnessMetrics();
                }

                allResults.Add(result);

                // Modyfikacje dynamiczne między iteracjami
                if (iter < testIterations - 1)
                {
                    switch (scenario)
                    {
                        case DynamicScenario.DS1_MovingObstacles:
                            ds2Mgr.StepAll(grid);
                            ds2Mgr.VerifyObstaclePositions(grid);
                            break;
                    }
                }
            }

            // Agreguj wyniki
            var metrics = new BenchmarkMetrics
            {
                AlgorithmName = algorithm.AlgorithmName,
                TestID = testId,
                StartX = startPos.x,
                StartY = startPos.y,
                TargetX = targetPos.x,
                TargetY = targetPos.y,
                Scenario = scenarioLabel,
                ObstacleDensity = density,
                MapTopology = useProceduralMaps ? "Procedural" : "FromFile",
                MapSeed = randomSeed,
                MapDensity = density,
                MapWidth = grid.Width,
                MapHeight = grid.Height
            };

            metrics.AggregateFrom(allResults);
            return metrics;
        }

        // ─────────────────────────────────────────────────────────
        //  NARZĘDZIA
        // ─────────────────────────────────────────────────────────

        private Vector2Int ClampToMap(Vector2Int pos, int mapWidth, int mapHeight)
        {
            return new Vector2Int(
                Mathf.Clamp(pos.x, 1, mapWidth - 2),
                Mathf.Clamp(pos.y, 1, mapHeight - 2)
            );
        }

        /// <summary>
        /// Fisher-Yates shuffle — losowa permutacja listy in-place.
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
    }
}
