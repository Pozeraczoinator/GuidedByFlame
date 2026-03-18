using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Pathfinding.Core;
using Pathfinding.Algorithms;

namespace Pathfinding.Benchmark
{
    public class BenchmarkManager : MonoBehaviour
    {
        [Header("Konfiguracja Testów")]
        public string testCasesFileName = "TestCases.csv";
        public string mapFileName = "Map.txt";
        public int testIterations = 10;

        private List<IPathfindingAlgorithm> _algorithms;
        private GridMap _gridMap;
        
        private struct TestCase
        {
            public int startX, startY;
            public int targetX, targetY;
        }

        private void Start()
        {
            // Inicjalizacja algorytmów
            _algorithms = new List<IPathfindingAlgorithm>
            {
                new AStarAlgorithm(),
                new DijkstraAlgorithm(),
                new GreedyBestFirstAlgorithm(),
                new CustomGreedyAlgorithm(),
                new JumpPointSearchAlgorithm()
            };

            // Wczytanie mapy z TXT
            if (LoadGridMap())
            {
                StartCoroutine(RunBenchmarkCoroutine());
            }
        }

        private bool LoadGridMap()
        {
            // Poszukujemy pliku Map.txt (absolutna, root Unity, pulpit)
            string path = mapFileName;
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "..", mapFileName);
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "../..", mapFileName);

            if (!File.Exists(path))
            {
                UnityEngine.Debug.LogError($"Nie znaleziono pliku mapy: {path}");
                return false;
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) return false;

            int height = lines.Length;
            int width = lines[0].Length;

            bool[,] collisionData = new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                string line = lines[height - 1 - y]; // Odczyt od dołu do góry by pasował do układu osi Y w grach
                for (int x = 0; x < width; x++)
                {
                    if (x < line.Length)
                    {
                        // 0 to brak kolizji (walkable=true), 1 to kolizja (walkable=false)
                        collisionData[x, y] = (line[x] == '0');
                    }
                    else
                    {
                        collisionData[x, y] = false;
                    }
                }
            }

            _gridMap = new GridMap(collisionData);
            UnityEngine.Debug.Log($"Wczytano GridMap z {path}. Wymiary: {width}x{height}");
            return true;
        }

        private IEnumerator RunBenchmarkCoroutine()
        {
            string testCasesPath = testCasesFileName;
            if (!File.Exists(testCasesPath)) testCasesPath = Path.Combine(Application.dataPath, "..", testCasesFileName);
            if (!File.Exists(testCasesPath)) testCasesPath = Path.Combine(Application.dataPath, "../..", testCasesFileName);
            
            if (!File.Exists(testCasesPath))
            {
                UnityEngine.Debug.LogError($"Nie znaleziono pliku CSV do testów! {testCasesPath}");
                yield break;
            }

            List<TestCase> testCases = LoadTestCases(testCasesPath);
            UnityEngine.Debug.Log($"Wczytano {testCases.Count} przypadków testowych z CSV.");

            // Rozgrzewka JIT
            if (testCases.Count > 0)
            {
                var warmupCase = testCases[0];
                foreach (var algo in _algorithms)
                {
                    algo.FindPath(_gridMap, new Vector2Int(warmupCase.startX, warmupCase.startY), new Vector2Int(warmupCase.targetX, warmupCase.targetY));
                }
            }

            foreach (var algorithm in _algorithms)
            {
                UnityEngine.Debug.Log($"Rozpoczynanie testów dla algorytmu: {algorithm.AlgorithmName}");
                
                string resultsPath = Path.Combine(Application.dataPath, "..", $"benchmark_{algorithm.AlgorithmName}_results.csv");
                using (StreamWriter writer = new StreamWriter(resultsPath, false))
                {
                    writer.AutoFlush = true; // GWARANTUJE ZAPIS LINIJKOWY OD RAZU
                    writer.WriteLine("TestID;StartX;StartY;TargetX;TargetY;PathFound;AvgExecutionTimeMs;ExploredNodes;PathLength;SimulatedFPS");

                    for (int i = 0; i < testCases.Count; i++)
                    {
                        TestCase tc = testCases[i];
                        Vector2Int startPos = new Vector2Int(tc.startX, tc.startY);
                        Vector2Int targetPos = new Vector2Int(tc.targetX, tc.targetY);

                        Pathfinding.Core.PathfindingResult finalResult = null;
                        double totalMs = 0;

                        for (int j = 0; j < testIterations; j++)
                        {
                            var result = algorithm.FindPath(_gridMap, startPos, targetPos);
                            totalMs += result.ExecutionTimeMs;
                            if (j == 0) finalResult = result; 
                        }

                        double avgMs = totalMs / testIterations;
                        double simulatedFrameTime = 10.0 + avgMs; 
                        double simulatedFPS = 1000.0 / simulatedFrameTime;

                        writer.WriteLine($"{i};{tc.startX};{tc.startY};{tc.targetX};{tc.targetY};" +
                                         $"{finalResult.PathFound};{avgMs.ToString("F4")};{finalResult.ExploredNodes};" +
                                         $"{finalResult.PathLength.ToString("F2")};{simulatedFPS.ToString("F2")}");
                        
                        if (i % 10 == 0 || i == testCases.Count - 1)
                        {
                            UnityEngine.Debug.Log($"[BenchmarkManager] {algorithm.AlgorithmName}: Postęp {i + 1}/{testCases.Count} testów...");
                        }

                        yield return null; 
                    }
                }
                UnityEngine.Debug.Log($"[BenchmarkManager] Pomyślnie zapisano plik z wynikami: {resultsPath}");
            }
            UnityEngine.Debug.Log("Wszystkie testy benchmarków zostały ukończone pomyślnie.");
        }

        private List<TestCase> LoadTestCases(string path)
        {
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
                        startX = int.Parse(columns[0]),
                        startY = int.Parse(columns[1]),
                        targetX = int.Parse(columns[2]),
                        targetY = int.Parse(columns[3])
                    });
                }
            }
            return list;
        }
    }
}
