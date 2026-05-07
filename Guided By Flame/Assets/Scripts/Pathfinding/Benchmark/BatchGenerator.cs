using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Pathfinding.Core;
using Pathfinding.MapGenerators;

namespace Pathfinding.Benchmark
{
    /// <summary>
    /// Batch generator — generuje wszystkie kombinacje map do systematycznych testów.
    /// 
    /// Kombinacje: 4 topologie × 4 zagęszczenia × 4 seedy = 64 mapy
    /// Każda mapa eksportowana do pliku .txt kompatybilnego z BenchmarkManager.
    /// 
    /// Dodatkowo generuje rozszerzone TestCases.csv z distance bucketing
    /// i BFS reachability validation dla każdej mapy.
    /// 
    /// Użycie w Unity:
    /// 1. Dodaj ten skrypt do pustego GameObject
    /// 2. Kliknij Play → mapy wygenerowane w GeneratedMaps/
    /// 3. Ustaw mapFileName w BenchmarkManager na wygenerowaną mapę
    /// 
    /// Alternatywnie: wywołaj BatchGenerator.GenerateAll() z kodu.
    /// </summary>
    public class BatchGenerator : MonoBehaviour
    {
        [Header("═══ Parametry Generacji ═══")]
        [Tooltip("Szerokość generowanych map.")]
        public int mapWidth = 32;

        [Tooltip("Wysokość generowanych map.")]
        public int mapHeight = 20;

        [Tooltip("Zagęszczenia przeszkód do wygenerowania.")]
        public float[] densities = { 0.10f, 0.20f, 0.30f, 0.40f };

        [Tooltip("Seedy RNG — każdy seed generuje inną instancję mapy.")]
        public int[] seeds = { 42, 123, 256, 789 };

        [Tooltip("Katalog wyjściowy (względny do katalogu projektu).")]
        public string outputDirectory = "GeneratedMaps";

        [Header("═══ Generacja Test Cases ═══")]
        [Tooltip("Ile par testowych na wiązkę dystansową (SHORT/MEDIUM/LONG).")]
        [Range(5, 100)]
        public int pairsPerBucket = 30;

        [Tooltip("Ile par z nieosiągalnym celem.")]
        [Range(0, 20)]
        public int unreachablePairs = 5;

        [Tooltip("Czy generować test cases dla każdej mapy.")]
        public bool generateTestCases = true;

        [Header("═══ Kontrola ═══")]
        [Tooltip("Automatycznie generuj przy starcie sceny.")]
        public bool generateOnStart = true;

        private void Start()
        {
            if (generateOnStart)
            {
                GenerateAll();
            }
        }

        /// <summary>
        /// Generuje wszystkie kombinacje map i opcjonalnie test cases.
        /// </summary>
        public void GenerateAll()
        {
            string basePath = Path.Combine(Application.dataPath, "..", outputDirectory);
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);

            var generators = new List<IMapGenerator>
            {
                new OpenFieldGenerator(),
                new MazeGenerator(),
                new RoomCorridorGenerator(),
                new ScatteredBlockGenerator()
            };

            int totalMaps = generators.Count * densities.Length * seeds.Length;
            int generated = 0;

            Debug.Log($"[BatchGenerator] ═══ START ═══ Generuję {totalMaps} map " +
                      $"({generators.Count} topologii × {densities.Length} zagęszczeń × {seeds.Length} seedów)");

            foreach (var gen in generators)
            {
                // Katalog per topologia
                string topoDir = Path.Combine(basePath, gen.TopologyName);
                if (!Directory.Exists(topoDir))
                    Directory.CreateDirectory(topoDir);

                foreach (float density in densities)
                {
                    foreach (int seed in seeds)
                    {
                        // Generuj mapę
                        GridMap map = gen.Generate(mapWidth, mapHeight, density, seed);

                        // Eksportuj do .txt
                        string fileName = MapExporter.GenerateFileName(
                            gen.TopologyName, mapWidth, mapHeight, density, seed);
                        string filePath = Path.Combine(topoDir, fileName);
                        MapExporter.ExportToFile(map, filePath);

                        // Opcjonalnie: generuj test cases
                        if (generateTestCases)
                        {
                            var selector = new TestPointSelector(seed);
                            var testCases = selector.GenerateTestCases(map, pairsPerBucket, unreachablePairs);

                            string csvName = Path.GetFileNameWithoutExtension(fileName) + "_TestCases.csv";
                            string csvPath = Path.Combine(topoDir, csvName);
                            TestPointSelector.ExportToCsv(testCases, csvPath);
                        }

                        generated++;
                        if (generated % 16 == 0 || generated == totalMaps)
                        {
                            Debug.Log($"[BatchGenerator] Postęp: {generated}/{totalMaps} map wygenerowanych.");
                        }
                    }
                }
            }

            // Generuj raport podsumowujący
            GenerateSummaryReport(basePath, generators);

            Debug.Log($"[BatchGenerator] ═══ ZAKOŃCZONO ═══ Wygenerowano {generated} map w: {Path.GetFullPath(basePath)}");
        }

        /// <summary>
        /// Generuje plik CSV z podsumowaniem wszystkich wygenerowanych map.
        /// </summary>
        private void GenerateSummaryReport(string basePath, List<IMapGenerator> generators)
        {
            string reportPath = Path.Combine(basePath, "map_summary.csv");
            using (var writer = new StreamWriter(reportPath, false))
            {
                writer.WriteLine("Topology,Width,Height,Density,Seed,FileName,WalkableCells,TotalCells,ActualDensity");

                foreach (var gen in generators)
                {
                    foreach (float density in densities)
                    {
                        foreach (int seed in seeds)
                        {
                            GridMap map = gen.Generate(mapWidth, mapHeight, density, seed);
                            int walkable = map.CountWalkable();
                            int total = mapWidth * mapHeight;
                            float actualDensity = 1f - (float)walkable / total;
                            string fileName = MapExporter.GenerateFileName(
                                gen.TopologyName, mapWidth, mapHeight, density, seed);

                            writer.WriteLine($"{gen.TopologyName},{mapWidth},{mapHeight}," +
                                             $"{density:F2},{seed},{fileName}," +
                                             $"{walkable},{total},{actualDensity:F4}");
                        }
                    }
                }
            }

            Debug.Log($"[BatchGenerator] Raport podsumowujący: {reportPath}");
        }
    }
}
