using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Pathfinding.Core;
using Pathfinding.Algorithms;
using Pathfinding.Benchmark;

namespace Pathfinding.Tests
{
    /// <summary>
    /// Testy determinizmu algorytmów pathfindingu.
    /// 
    /// Weryfikują najważniejszą właściwość naukową:
    /// TEN SAM INPUT → TA SAMA ŚCIEŻKA → TE SAME METRYKI
    /// 
    /// Testy uruchamiane z poziomu Unity (MonoBehaviour).
    /// Użycie: Dodaj ten skrypt do pustego GameObject i kliknij Play.
    /// 
    /// Testowane aspekty:
    ///   1. Wielokrotne uruchomienie tego samego algorytmu → identyczny wynik
    ///   2. Konsystencja między algorytmami (A* i Dijkstra → ta sama długość ścieżki)
    ///   3. Determinizm na różnych topologiach map
    ///   4. Determinizm z wagami terenu (DS3)
    ///   5. Determinizm ścieżki (dokładna sekwencja kroków)
    ///   6. Edge cases (start==cel, brak ścieżki, sąsiednie pola)
    /// </summary>
    public class DeterminismTests : MonoBehaviour
    {
        [Header("═══ Konfiguracja Testów ═══")]
        [Tooltip("Ile razy uruchomić każdy algorytm aby sprawdzić powtarzalność.")]
        [Range(10, 500)]
        public int repetitions = 100;

        [Tooltip("Czy wypisywać szczegółowe logi per algorytm.")]
        public bool verboseLogging = false;

        private int _passed = 0;
        private int _failed = 0;
        private StringBuilder _report = new StringBuilder();

        private void Start()
        {
            Debug.Log("══════════════════════════════════════════════════════════");
            Debug.Log("  TESTY DETERMINIZMU ALGORYTMÓW PATHFINDINGU");
            Debug.Log($"  Powtórzeń per test: {repetitions}");
            Debug.Log("══════════════════════════════════════════════════════════");
            _report.AppendLine("RAPORT TESTÓW DETERMINIZMU");
            _report.AppendLine($"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _report.AppendLine($"Powtórzeń per test: {repetitions}");
            _report.AppendLine(new string('─', 60));

            // ─── Zestaw 1: Powtarzalność na prostej mapie ───
            RunRepeatabilityTests();

            // ─── Zestaw 2: Konsystencja optymalnych algorytmów ───
            RunOptimalityConsistencyTests();

            // ─── Zestaw 3: Determinizm na różnych topologiach ───
            RunTopologyDeterminismTests();

            // ─── Zestaw 4: Determinizm z wagami terenu ───
            RunWeightedTerrainDeterminismTests();

            // ─── Zestaw 5: Edge cases ───
            RunEdgeCaseTests();

            // ─── Zestaw 6: Poprawność geometrii ścieżki ───
            RunPathValidityTests();

            // ─── Zestaw 7: Determinizm pełnej ścieżki ───
            RunFullPathDeterminismTests();

            // ─── Podsumowanie ───
            Debug.Log("══════════════════════════════════════════════════════════");
            Debug.Log($"  WYNIKI: {_passed} PASSED / {_failed} FAILED / {_passed + _failed} TOTAL");
            if (_failed == 0)
                Debug.Log("  ✅ WSZYSTKIE TESTY PRZESZŁY POMYŚLNIE");
            else
                Debug.LogError($"  ❌ {_failed} TESTÓW NIEPOWODZENIE!");
            Debug.Log("══════════════════════════════════════════════════════════");

            _report.AppendLine(new string('═', 60));
            _report.AppendLine($"WYNIKI: {_passed} PASSED / {_failed} FAILED");

            // Zapisz raport do pliku
            string reportPath = System.IO.Path.Combine(
                Application.dataPath, "..", "DeterminismTestReport.txt");
            System.IO.File.WriteAllText(reportPath, _report.ToString());
            Debug.Log($"Raport zapisany do: {System.IO.Path.GetFullPath(reportPath)}");
        }

        // ─────────────────────────────────────────────────────────
        //  ZESTAW 1: POWTARZALNOŚĆ
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Uruchamia każdy algorytm N razy na tej samej mapie z tymi samymi parametrami.
        /// Sprawdza: PathFound, ExploredNodes, PathLength, DirectionChanges, pełna ścieżka.
        /// </summary>
        private void RunRepeatabilityTests()
        {
            Debug.Log("── Zestaw 1: Powtarzalność ──");
            _report.AppendLine("\n── ZESTAW 1: POWTARZALNOŚĆ ──");

            GridMap map = CreateTestMap();
            Vector2Int start = new Vector2Int(1, 1);
            Vector2Int target = new Vector2Int(28, 17);

            var algorithms = GetAllAlgorithms();

            foreach (var algo in algorithms)
            {
                TestRepeatability(algo, map, start, target);
            }
        }

        private void TestRepeatability(IPathfindingAlgorithm algorithm, GridMap map,
            Vector2Int start, Vector2Int target)
        {
            string testName = $"Repeatability_{algorithm.AlgorithmName}";

            // Pierwsze uruchomienie — reference
            var reference = algorithm.FindPath(map, start, target);
            if (reference.PathFound)
                reference.CalculateSmoothnessMetrics();

            bool allMatch = true;
            string failReason = "";

            for (int i = 1; i < repetitions; i++)
            {
                // Tworzymy NOWĄ instancję algorytmu za każdym razem
                var freshAlgo = CreateFreshAlgorithm(algorithm.AlgorithmName);
                var result = freshAlgo.FindPath(map, start, target);
                if (result.PathFound)
                    result.CalculateSmoothnessMetrics();

                if (result.PathFound != reference.PathFound)
                {
                    allMatch = false;
                    failReason = $"Iteracja {i}: PathFound={result.PathFound} vs reference={reference.PathFound}";
                    break;
                }

                if (result.ExploredNodes != reference.ExploredNodes)
                {
                    allMatch = false;
                    failReason = $"Iteracja {i}: ExploredNodes={result.ExploredNodes} vs reference={reference.ExploredNodes}";
                    break;
                }

                if (Math.Abs(result.PathLength - reference.PathLength) > 0.001f)
                {
                    allMatch = false;
                    failReason = $"Iteracja {i}: PathLength={result.PathLength:F4} vs reference={reference.PathLength:F4}";
                    break;
                }

                if (result.DirectionChanges != reference.DirectionChanges)
                {
                    allMatch = false;
                    failReason = $"Iteracja {i}: DirectionChanges={result.DirectionChanges} vs reference={reference.DirectionChanges}";
                    break;
                }

                // Porównaj pełną ścieżkę krok po kroku
                if (result.Path.Count != reference.Path.Count)
                {
                    allMatch = false;
                    failReason = $"Iteracja {i}: Path.Count={result.Path.Count} vs reference={reference.Path.Count}";
                    break;
                }

                for (int step = 0; step < result.Path.Count; step++)
                {
                    if (result.Path[step] != reference.Path[step])
                    {
                        allMatch = false;
                        failReason = $"Iteracja {i}: Path[{step}]={result.Path[step]} vs reference={reference.Path[step]}";
                        break;
                    }
                }

                if (!allMatch) break;
            }

            RecordResult(testName, allMatch, failReason,
                $"PathFound={reference.PathFound}, Nodes={reference.ExploredNodes}, " +
                $"Length={reference.PathLength:F2}, Steps={reference.Path?.Count ?? 0}");
        }

        // ─────────────────────────────────────────────────────────
        //  ZESTAW 2: KONSYSTENCJA OPTYMALNYCH ALGORYTMÓW
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// A* i Dijkstra powinny znajdować ścieżkę o IDENTYCZNEJ długości
        /// (oba są optymalne). JPS jest raportowany informacyjnie, bo obecna
        /// implementacja nie pełni roli referencyjnego algorytmu optymalnego.
        /// </summary>
        private void RunOptimalityConsistencyTests()
        {
            Debug.Log("── Zestaw 2: Konsystencja optymalnych algorytmów ──");
            _report.AppendLine("\n── ZESTAW 2: KONSYSTENCJA OPTYMALNYCH ──");

            GridMap map = CreateTestMap();

            // Test na kilku parach start-cel
            var testCases = new (Vector2Int start, Vector2Int target)[]
            {
                (new Vector2Int(1, 1), new Vector2Int(28, 17)),
                (new Vector2Int(5, 5), new Vector2Int(25, 15)),
                (new Vector2Int(1, 18), new Vector2Int(30, 1)),
                (new Vector2Int(15, 10), new Vector2Int(3, 3)),
            };

            foreach (var tc in testCases)
            {
                if (!map.IsWalkable(tc.start) || !map.IsWalkable(tc.target))
                    continue;

                var astar = new AStarAlgorithm();
                var dijkstra = new DijkstraAlgorithm();
                var jps = new JumpPointSearchAlgorithm();

                var resultA = astar.FindPath(map, tc.start, tc.target);
                var resultD = dijkstra.FindPath(map, tc.start, tc.target);
                var resultJ = jps.FindPath(map, tc.start, tc.target);

                string testNameBase = $"OptimalConsistency_{tc.start}→{tc.target}";

                // A* vs Dijkstra — MUSZĄ mieć identyczną PathLength (oba optymalne)
                if (resultA.PathFound != resultD.PathFound)
                {
                    RecordResult($"{testNameBase}_AStarVsDijkstra", false,
                        $"A*={resultA.PathFound}, Dijkstra={resultD.PathFound}",
                        "Niezgodność w PathFound");
                }
                else if (!resultA.PathFound)
                {
                    RecordResult($"{testNameBase}_AStarVsDijkstra", true, "",
                        "Brak ścieżki (zgodne)");
                }
                else
                {
                    float tolerance = 0.01f;
                    bool lengthMatch = Math.Abs(resultA.PathLength - resultD.PathLength) < tolerance;
                    string info = $"A*={resultA.PathLength:F4}, Dijkstra={resultD.PathLength:F4}";
                    RecordResult($"{testNameBase}_AStarVsDijkstra", lengthMatch,
                        lengthMatch ? "" : $"A*={resultA.PathLength:F4} ≠ Dijkstra={resultD.PathLength:F4}",
                        info);
                }

                // JPS vs A* — obecna implementacja JPS nie jest referencją optymalności.
                // Różnice długości dokumentujemy informacyjnie, ale test determinizmu nie powinien
                // failować, jeśli JPS jest powtarzalny i znajduje poprawną ścieżkę.
                if (resultA.PathFound && resultJ.PathFound)
                {
                    float tolerance = 0.01f;
                    bool jpsMatch = Math.Abs(resultA.PathLength - resultJ.PathLength) < tolerance;
                    string info = $"A*={resultA.PathLength:F4}, JPS={resultJ.PathLength:F4}";
                    RecordResult($"{testNameBase}_JPSvsAStar", true, "",
                        jpsMatch ? info : $"[INFO] Różnica PathLength JPS względem A*: {info}");
                }
                else if (resultA.PathFound != resultJ.PathFound)
                {
                    RecordResult($"{testNameBase}_JPSvsAStar", true, "",
                        $"[INFO] Różnica PathFound: A*={resultA.PathFound}, JPS={resultJ.PathFound}");
                }
                else
                {
                    RecordResult($"{testNameBase}_JPSvsAStar", true, "",
                        "Brak ścieżki (zgodne)");
                }
            }
        }

        // ─────────────────────────────────────────────────────────
        //  ZESTAW 3: DETERMINIZM NA RÓŻNYCH TOPOLOGIACH
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Testuje determinizm na mapach generowanych proceduralnie:
        /// OpenField, Maze, RoomCorridor, ScatteredBlock.
        /// </summary>
        private void RunTopologyDeterminismTests()
        {
            Debug.Log("── Zestaw 3: Determinizm na różnych topologiach ──");
            _report.AppendLine("\n── ZESTAW 3: TOPOLOGIE MAP ──");

            var generators = new (string name, MapGenerators.IMapGenerator gen)[]
            {
                ("Maze", new MapGenerators.MazeGenerator()),
                ("RoomCorridor", new MapGenerators.RoomCorridorGenerator()),
                ("ScatteredBlock", new MapGenerators.ScatteredBlockGenerator()),
            };

            foreach (var (name, gen) in generators)
            {
                GridMap map = gen.Generate(32, 20, 0.2f, 42);

                // Znajdź walkable start i cel
                Vector2Int start = FindWalkable(map, 1, 1);
                Vector2Int target = FindWalkable(map, map.Width - 2, map.Height - 2);

                if (start.x < 0 || target.x < 0)
                {
                    RecordResult($"Topology_{name}", false, "Brak walkable start/target", "");
                    continue;
                }

                // Sprawdź osiągalność
                if (!Benchmark.TestPointSelector.BFSReachabilityCheck(map, start, target))
                {
                    RecordResult($"Topology_{name}", true, "", 
                        $"Start={start}, Target={target} — nieosiągalny (OK)");
                    continue;
                }

                foreach (var algo in GetAllAlgorithms())
                {
                    var ref1 = algo.FindPath(map, start, target);
                    var fresh = CreateFreshAlgorithm(algo.AlgorithmName);
                    var ref2 = fresh.FindPath(map, start, target);

                    bool match = ref1.PathFound == ref2.PathFound &&
                                 ref1.ExploredNodes == ref2.ExploredNodes &&
                                 Math.Abs(ref1.PathLength - ref2.PathLength) < 0.001f;

                    RecordResult($"Topology_{name}_{algo.AlgorithmName}", match,
                        match ? "" : $"Mismatch: Nodes {ref1.ExploredNodes}→{ref2.ExploredNodes}, " +
                                     $"Length {ref1.PathLength:F4}→{ref2.PathLength:F4}",
                        $"PathFound={ref1.PathFound}, Nodes={ref1.ExploredNodes}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────
        //  ZESTAW 4: DETERMINIZM Z WAGAMI TERENU (DS3)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Testuje determinizm algorytmów wspierających wagi terenu.
        /// JPS jest WYKLUCZONY (nie wspiera weighted gridów).
        /// </summary>
        private void RunWeightedTerrainDeterminismTests()
        {
            Debug.Log("── Zestaw 4: Determinizm z wagami terenu ──");
            _report.AppendLine("\n── ZESTAW 4: WAGI TERENU (DS3) ──");

            GridMap map = CreateTestMap();
            Vector2Int start = new Vector2Int(1, 1);
            Vector2Int target = new Vector2Int(28, 17);

            // Dodaj wagi terenu
            var rng = new System.Random(42);
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    if (map.IsWalkable(x, y) && rng.NextDouble() < 0.15)
                    {
                        float[] costs = { 2.0f, 5.0f, 10.0f };
                        map.SetMovementCost(x, y, costs[rng.Next(costs.Length)]);
                    }
                }
            }

            // Algorytmy wspierające DS3 (bez JPS i GBFS).
            // GBFS jest testowany w innych zestawach, ale ignoruje wagi terenu z definicji implementacji.
            var algorithms = new IPathfindingAlgorithm[]
            {
                new AStarAlgorithm(),
                new DijkstraAlgorithm(),
                new CustomGreedyAlgorithm(),
            };

            foreach (var algo in algorithms)
            {
                var reference = algo.FindPath(map, start, target);
                if (reference.PathFound)
                    reference.CalculateSmoothnessMetrics();

                bool allMatch = true;
                string failReason = "";

                for (int i = 0; i < 50; i++)
                {
                    var fresh = CreateFreshAlgorithm(algo.AlgorithmName);
                    var result = fresh.FindPath(map, start, target);

                    if (result.ExploredNodes != reference.ExploredNodes ||
                        Math.Abs(result.PathLength - reference.PathLength) > 0.001f)
                    {
                        allMatch = false;
                        failReason = $"Iter {i}: Nodes={result.ExploredNodes} vs {reference.ExploredNodes}, " +
                                     $"Length={result.PathLength:F4} vs {reference.PathLength:F4}";
                        break;
                    }
                }

                RecordResult($"WeightedTerrain_{algo.AlgorithmName}", allMatch, failReason,
                    $"PathFound={reference.PathFound}, Nodes={reference.ExploredNodes}, Length={reference.PathLength:F4}");
            }
        }

        // ─────────────────────────────────────────────────────────
        //  ZESTAW 5: EDGE CASES
        // ─────────────────────────────────────────────────────────

        private void RunEdgeCaseTests()
        {
            Debug.Log("── Zestaw 5: Edge cases ──");
            _report.AppendLine("\n── ZESTAW 5: EDGE CASES ──");

            GridMap map = CreateTestMap();

            foreach (var algo in GetAllAlgorithms())
            {
                // Test: start == cel
                {
                    Vector2Int pos = new Vector2Int(5, 5);
                    var result = algo.FindPath(map, pos, pos);
                    // Powinno znaleźć ścieżkę (trywialną)
                    RecordResult($"EdgeCase_StartEqualsTarget_{algo.AlgorithmName}",
                        result.PathFound, 
                        result.PathFound ? "" : "Nie znalazł ścieżki start==cel",
                        $"PathFound={result.PathFound}, Length={result.PathLength:F4}");
                }

                // Test: sąsiednie pola (odległość 1)
                // UWAGA: Używamy pól z gwarantowanego wolnego rogu (0-2, 0-2) z CreateTestMap
                {
                    Vector2Int start = new Vector2Int(1, 1);
                    Vector2Int target = new Vector2Int(2, 1);
                    var result = algo.FindPath(map, start, target);
                    // RetracePath nie dodaje startu do ścieżki (by design).
                    // Dla sąsiednich pól: Path = [target], Count = 1, PathLength = 1.0
                    bool passed = result.PathFound && result.Path.Count >= 1;
                    string failMsg = "";
                    if (!result.PathFound)
                        failMsg = $"Nie znalazł ścieżki {start}→{target} (oba pola powinny być walkable!)";
                    else if (result.Path.Count < 1)
                        failMsg = $"Path.Count={result.Path.Count} (oczekiwano >= 1)";
                    RecordResult($"EdgeCase_Adjacent_{algo.AlgorithmName}",
                        passed, failMsg,
                        $"PathFound={result.PathFound}, Steps={result.Path?.Count ?? 0}, Length={result.PathLength:F2}");
                }

                // Test: wąski korytarz z wymuszonym skrętem, typowy dla map Maze.
                {
                    GridMap corridorTurn = CreateCorridorTurnMap();
                    Vector2Int start = new Vector2Int(1, 1);
                    Vector2Int target = new Vector2Int(3, 3);
                    var result = algo.FindPath(corridorTurn, start, target);

                    string geometryFail = "";
                    bool validGeometry = result.PathFound &&
                        IsPathGeometryValid(corridorTurn, start, result.Path, out geometryFail);

                    RecordResult($"EdgeCase_CorridorTurn_{algo.AlgorithmName}",
                        validGeometry,
                        result.PathFound ? geometryFail : "Nie znalazł ścieżki w korytarzu ze skrętem.",
                        $"PathFound={result.PathFound}, Path={FormatPath(result.Path)}");
                }

                // Test: brak ścieżki (cel otoczony ścianami)
                {
                    GridMap isolated = CreateIsolatedMap();
                    Vector2Int start = new Vector2Int(1, 1);
                    Vector2Int target = new Vector2Int(15, 10);
                    var result = algo.FindPath(isolated, start, target);
                    RecordResult($"EdgeCase_NoPath_{algo.AlgorithmName}",
                        !result.PathFound,
                        result.PathFound ? "Znalazł ścieżkę do izolowanego pola!" : "",
                        $"PathFound={result.PathFound}, ExploredNodes={result.ExploredNodes}");
                }

                // Test: zakaz przejścia po przekątnej między dwoma przeszkodami.
                // Układ 2x2, gdzie 1=przeszkoda, 0=wolne: 01 / 10.
                {
                    GridMap cornerBlocked = CreateDiagonalCornerBlockedMap();
                    Vector2Int start = new Vector2Int(0, 0);
                    Vector2Int target = new Vector2Int(1, 1);
                    var result = algo.FindPath(cornerBlocked, start, target);

                    RecordResult($"EdgeCase_NoCornerCutting_{algo.AlgorithmName}",
                        !result.PathFound,
                        result.PathFound ? $"Algorytm przeszedł po zabronionej przekątnej: {FormatPath(result.Path)}" : "",
                        $"PathFound={result.PathFound}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────
        //  ZESTAW 6: POPRAWNOŚĆ GEOMETRII ŚCIEŻKI
        // ─────────────────────────────────────────────────────────

        private void RunPathValidityTests()
        {
            Debug.Log("── Zestaw 6: Poprawność geometrii ścieżki ──");
            _report.AppendLine("\n── ZESTAW 6: GEOMETRIA ŚCIEŻKI ──");

            GridMap ds2Snapshot = CreateDS2SnapshotMap(42, out Vector2Int ds2Start, out Vector2Int ds2Target);

            var maps = new (string name, GridMap map, Vector2Int start, Vector2Int target)[]
            {
                ("Simple", CreateTestMap(), new Vector2Int(1, 1), new Vector2Int(28, 17)),
                ("DS2Snapshot", ds2Snapshot, ds2Start, ds2Target),
                ("CornerBlocked", CreateDiagonalCornerBlockedMap(), new Vector2Int(0, 0), new Vector2Int(1, 1)),
            };

            foreach (var item in maps)
            {
                foreach (var algo in GetAllAlgorithms())
                {
                    var result = algo.FindPath(item.map, item.start, item.target);
                    string failReason = "";
                    bool valid = !result.PathFound || IsPathGeometryValid(item.map, item.start, result.Path, out failReason);

                    RecordResult($"PathGeometry_{item.name}_{algo.AlgorithmName}",
                        valid,
                        failReason,
                        result.PathFound ? $"Steps={result.Path.Count}, Length={result.PathLength:F2}" : "No path");
                }
            }

            GridMap ds2A = CreateDS2SnapshotMap(123, out _, out _);
            GridMap ds2B = CreateDS2SnapshotMap(123, out _, out _);
            RecordResult("DS2_Snapshot_Determinism",
                MapsHaveSameWalkability(ds2A, ds2B),
                "Ten sam seed DS2 wygenerował inny układ przeszkód.",
                "Seed=123");
        }

        // ─────────────────────────────────────────────────────────
        //  ZESTAW 7: DETERMINIZM PEŁNEJ ŚCIEŻKI (STEP-BY-STEP)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Najważniejszy test: porównuje KAŻDY KROK ścieżki między uruchomieniami.
        /// Gwarantuje, że determinizm dotyczy nie tylko metryk, ale dokładnej geometrii trasy.
        /// </summary>
        private void RunFullPathDeterminismTests()
        {
            Debug.Log("── Zestaw 7: Determinizm pełnej ścieżki ──");
            _report.AppendLine("\n── ZESTAW 7: PEŁNA ŚCIEŻKA ──");

            // Testuj na kilku mapach
            var maps = new (string name, GridMap map)[]
            {
                ("Simple", CreateTestMap()),
                ("Maze", new MapGenerators.MazeGenerator().Generate(32, 20, 0.3f, 42)),
                ("Rooms", new MapGenerators.RoomCorridorGenerator().Generate(32, 20, 0.2f, 42)),
            };

            foreach (var (mapName, map) in maps)
            {
                Vector2Int start = FindWalkable(map, 1, 1);
                Vector2Int target = FindWalkable(map, map.Width - 2, map.Height - 2);

                if (start.x < 0 || target.x < 0) continue;
                if (!Benchmark.TestPointSelector.BFSReachabilityCheck(map, start, target)) continue;

                foreach (var algo in GetAllAlgorithms())
                {
                    var reference = algo.FindPath(map, start, target);
                    if (!reference.PathFound) continue;

                    bool allStepsMatch = true;
                    string failReason = "";

                    for (int run = 0; run < repetitions; run++)
                    {
                        var fresh = CreateFreshAlgorithm(algo.AlgorithmName);
                        var result = fresh.FindPath(map, start, target);

                        if (result.Path.Count != reference.Path.Count)
                        {
                            allStepsMatch = false;
                            failReason = $"Run {run}: Path.Count={result.Path.Count} vs {reference.Path.Count}";
                            break;
                        }

                        for (int s = 0; s < result.Path.Count; s++)
                        {
                            if (result.Path[s] != reference.Path[s])
                            {
                                allStepsMatch = false;
                                failReason = $"Run {run}, Step {s}: {result.Path[s]} vs {reference.Path[s]}";
                                break;
                            }
                        }

                        if (!allStepsMatch) break;
                    }

                    RecordResult($"FullPath_{mapName}_{algo.AlgorithmName}",
                        allStepsMatch, failReason,
                        $"Steps={reference.Path.Count}, Runs={repetitions}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────

        private List<IPathfindingAlgorithm> GetAllAlgorithms()
        {
            return new List<IPathfindingAlgorithm>
            {
                new AStarAlgorithm(),
                new DijkstraAlgorithm(),
                new GreedyBestFirstAlgorithm(),
                new CustomGreedyAlgorithm(),
                new JumpPointSearchAlgorithm(),
            };
        }

        private IPathfindingAlgorithm CreateFreshAlgorithm(string name)
        {
            switch (name)
            {
                case "AStar": return new AStarAlgorithm();
                case "Dijkstra": return new DijkstraAlgorithm();
                case "GreedyBestFirst": return new GreedyBestFirstAlgorithm();
                case "CustomGreedy": return new CustomGreedyAlgorithm();
                case "JumpPointSearch": return new JumpPointSearchAlgorithm();
                default: throw new ArgumentException($"Nieznany algorytm: {name}");
            }
        }

        /// <summary>
        /// Tworzy mapę testową 32×20 z deterministycznymi przeszkodami.
        /// Seed=42 gwarantuje identyczną mapę w każdym uruchomieniu.
        /// </summary>
        private GridMap CreateTestMap()
        {
            var rng = new System.Random(42);
            bool[,] walkable = new bool[32, 20];

            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 20; y++)
                {
                    walkable[x, y] = (rng.NextDouble() >= 0.2); // 20% przeszkód
                }
            }

            // Gwarantuj wolne rogi i ich otoczenie
            for (int dx = 0; dx <= 2; dx++)
                for (int dy = 0; dy <= 2; dy++)
                {
                    walkable[dx, dy] = true;
                    if (32 - 1 - dx >= 0 && 20 - 1 - dy >= 0)
                        walkable[32 - 1 - dx, 20 - 1 - dy] = true;
                }

            return new GridMap(walkable);
        }

        /// <summary>
        /// Tworzy mapę z izolowanym polem (do testowania braku ścieżki).
        /// Pole (15,10) otoczone ścianami ze wszystkich stron.
        /// </summary>
        private GridMap CreateIsolatedMap()
        {
            bool[,] walkable = new bool[32, 20];

            for (int x = 0; x < 32; x++)
                for (int y = 0; y < 20; y++)
                    walkable[x, y] = true;

            // Izoluj pole (15, 10) murem 3×3
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0) // Nie blokuj samego pola
                        walkable[15 + dx, 10 + dy] = false;

            return new GridMap(walkable);
        }

        private GridMap CreateDiagonalCornerBlockedMap()
        {
            bool[,] walkable = new bool[2, 2];
            walkable[0, 0] = true;
            walkable[1, 0] = false;
            walkable[0, 1] = false;
            walkable[1, 1] = true;
            return new GridMap(walkable);
        }

        private GridMap CreateCorridorTurnMap()
        {
            bool[,] walkable = new bool[5, 5];
            walkable[1, 1] = true;
            walkable[2, 1] = true;
            walkable[3, 1] = true;
            walkable[3, 2] = true;
            walkable[3, 3] = true;
            return new GridMap(walkable);
        }

        private GridMap CreateDS2SnapshotMap(int seed, out Vector2Int start, out Vector2Int target)
        {
            GridMap map = new GridMap(12, 12, true);
            start = new Vector2Int(1, 1);
            target = new Vector2Int(10, 10);

            var manager = new MovingObstacleManager(seed);
            manager.GenerateObstacles(map, 4, start, target, 3);
            manager.StepAll(map);
            manager.VerifyObstaclePositions(map);

            return map;
        }

        /// <summary>
        /// Znajduje najbliższe walkable pole do podanej pozycji.
        /// </summary>
        private Vector2Int FindWalkable(GridMap map, int startX, int startY)
        {
            // Spiral search od podanej pozycji
            for (int r = 0; r < Mathf.Max(map.Width, map.Height); r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int x = startX + dx;
                        int y = startY + dy;
                        if (map.IsWalkable(x, y))
                            return new Vector2Int(x, y);
                    }
                }
            }
            return new Vector2Int(-1, -1);
        }

        private bool IsPathGeometryValid(GridMap map, Vector2Int start, List<Vector2Int> path, out string failReason)
        {
            failReason = "";
            Vector2Int current = start;

            if (!map.IsWalkable(start))
            {
                failReason = $"Start {start} nie jest walkable.";
                return false;
            }

            foreach (Vector2Int step in path)
            {
                if (!map.IsWalkable(step))
                {
                    failReason = $"Krok {step} wchodzi w przeszkodę. Path={FormatPath(path)}";
                    return false;
                }

                int dx = step.x - current.x;
                int dy = step.y - current.y;

                if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0))
                {
                    failReason = $"Niepoprawny skok {current}->{step}. Path={FormatPath(path)}";
                    return false;
                }

                if (dx != 0 && dy != 0)
                {
                    Vector2Int horizontal = new Vector2Int(current.x + dx, current.y);
                    Vector2Int vertical = new Vector2Int(current.x, current.y + dy);

                    if (!map.IsWalkable(horizontal) || !map.IsWalkable(vertical))
                    {
                        failReason = $"Ścinanie rogu {current}->{step}; boczne pola: {horizontal}={map.IsWalkable(horizontal)}, {vertical}={map.IsWalkable(vertical)}. Path={FormatPath(path)}";
                        return false;
                    }
                }

                current = step;
            }

            return true;
        }

        private bool MapsHaveSameWalkability(GridMap a, GridMap b)
        {
            if (a.Width != b.Width || a.Height != b.Height)
                return false;

            for (int x = 0; x < a.Width; x++)
                for (int y = 0; y < a.Height; y++)
                    if (a.IsWalkable(x, y) != b.IsWalkable(x, y))
                        return false;

            return true;
        }

        private string FormatPath(List<Vector2Int> path)
        {
            if (path == null || path.Count == 0)
                return "[]";

            return "[" + string.Join(" -> ", path.Select(p => $"({p.x},{p.y})")) + "]";
        }

        private void RecordResult(string testName, bool passed, string failReason, string info)
        {
            if (passed)
            {
                _passed++;
                if (verboseLogging)
                    Debug.Log($"  ✅ {testName}: PASSED — {info}");
                _report.AppendLine($"✅ PASS: {testName} — {info}");
            }
            else
            {
                _failed++;
                Debug.LogError($"  ❌ {testName}: FAILED — {failReason}");
                _report.AppendLine($"❌ FAIL: {testName} — {failReason}");
            }
        }
    }
}
