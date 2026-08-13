using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pathfinding.Core
{
    /// <summary>
    /// Agregator wyników wielu iteracji jednego algorytmu na jednym test case.
    /// Oblicza średnią, min, max, odchylenie standardowe czasu wykonania,
    /// oraz przechowuje metrykę cold start (pierwsza iteracja).
    ///
    /// Złożoność agregacji: O(n) gdzie n = liczba iteracji.
    /// </summary>
    public class BenchmarkMetrics
    {
        // ─── Identyfikatory ───
        public string AlgorithmName { get; set; }
        public int TestID { get; set; }
        public int StartX { get; set; }
        public int StartY { get; set; }
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public string MapTopology { get; set; } = "Unknown";
        public int MapSeed { get; set; }
        public float MapDensity { get; set; }
        public int MapWidth { get; set; }
        public int MapHeight { get; set; }
        public string Scenario { get; set; }        // "Static" lub "Dynamic"
        public float ObstacleDensity { get; set; }   // Procent przeszkód (0.0 - 1.0)
        public string DistanceBucket { get; set; } = "Unknown";
        public float EuclideanDistance { get; set; } = -1f;
        public float OctagonalDistance { get; set; } = -1f;
        public float ReferenceShortestPathLength { get; set; } = -1f;

        // ─── Wynik ścieżki (z pierwszej udanej iteracji) ───
        public bool PathFound { get; set; }
        public int ExploredNodes { get; set; }
        public int JumpScannedCells { get; set; }
        public float PathLength { get; set; }
        public int PathCost { get; set; }
        public int DirectionChanges { get; set; }
        public float PathSmoothness { get; set; }
        public int PathRecalculations { get; set; }

        // ─── Cold Start (pierwsza iteracja — JIT warm-up) ───
        public double ColdStartTimeMs { get; set; }
        public long ColdStartTicks { get; set; }
        public long ColdStartGCAllocBytes { get; set; }

        // ─── Statystyki czasu wykonania (iteracje 2..N) ───
        public double AvgExecutionTimeMs { get; set; }
        public double MinExecutionTimeMs { get; set; }
        public double MaxExecutionTimeMs { get; set; }
        public double StdDevExecutionTimeMs { get; set; }

        // ─── Statystyki CPU ticks (iteracje 2..N) ───
        public double AvgExecutionTicks { get; set; }

        // ─── Statystyki GC (iteracje 2..N) ───
        public double AvgGCAllocBytes { get; set; }

        // ─── Monitoring sprzętowy ───
        /// <summary>
        /// Temperatura CPU w °C zmierzona w momencie testu. -1 jeśli niedostępna.
        /// </summary>
        public float CPUTemperature { get; set; } = -1f;

        /// <summary>
        /// Oblicza zagregowane metryki z listy wyników poszczególnych iteracji.
        /// Iteracja [0] traktowana jako cold start — NIE jest usuwana, lecz raportowana osobno.
        /// Iteracje [1..N-1] stanowią podstawę do obliczenia Avg, Min, Max, StdDev.
        /// 
        /// Jeśli jest tylko 1 iteracja, cold start = jedyny wynik, a metryki "gorące" = 0.
        /// </summary>
        /// <param name="results">Lista wyników z kolejnych iteracji</param>
        public void AggregateFrom(List<PathfindingResult> results)
        {
            if (results == null || results.Count == 0)
                return;

            // --- Cold Start (iteracja 0) ---
            var coldResult = results[0];
            ColdStartTimeMs = coldResult.ExecutionTimeMs;
            ColdStartTicks = coldResult.ExecutionTicks;
            ColdStartGCAllocBytes = coldResult.GCAllocBytes;

            // Metryki ścieżki bierzemy z cold start (ścieżka jest taka sama niezależnie od iteracji)
            PathFound = coldResult.PathFound;
            ExploredNodes = coldResult.ExploredNodes;
            JumpScannedCells = coldResult.JumpScannedCells;
            PathLength = coldResult.PathLength;
            PathCost = coldResult.PathCost;
            DirectionChanges = coldResult.DirectionChanges;
            PathSmoothness = coldResult.PathSmoothness;
            PathRecalculations = coldResult.PathRecalculations;

            // --- Jeśli tylko 1 iteracja, cold start = jedyny wynik ---
            if (results.Count == 1)
            {
                AvgExecutionTimeMs = ColdStartTimeMs;
                MinExecutionTimeMs = ColdStartTimeMs;
                MaxExecutionTimeMs = ColdStartTimeMs;
                StdDevExecutionTimeMs = 0;
                AvgExecutionTicks = ColdStartTicks;
                AvgGCAllocBytes = ColdStartGCAllocBytes;
                return;
            }

            // --- Warm iterations (1..N-1) ---
            int warmCount = results.Count - 1;
            double sumMs = 0;
            double minMs = double.MaxValue;
            double maxMs = double.MinValue;
            long sumTicks = 0;
            long sumGC = 0;

            for (int i = 1; i < results.Count; i++)
            {
                double ms = results[i].ExecutionTimeMs;
                sumMs += ms;
                if (ms < minMs) minMs = ms;
                if (ms > maxMs) maxMs = ms;
                sumTicks += results[i].ExecutionTicks;
                sumGC += results[i].GCAllocBytes;
            }

            AvgExecutionTimeMs = sumMs / warmCount;
            MinExecutionTimeMs = minMs;
            MaxExecutionTimeMs = maxMs;
            AvgExecutionTicks = (double)sumTicks / warmCount;
            AvgGCAllocBytes = (double)sumGC / warmCount;

            // --- StdDev ---
            double sumSquaredDiff = 0;
            for (int i = 1; i < results.Count; i++)
            {
                double diff = results[i].ExecutionTimeMs - AvgExecutionTimeMs;
                sumSquaredDiff += diff * diff;
            }
            StdDevExecutionTimeMs = Math.Sqrt(sumSquaredDiff / warmCount);
        }

        /// <summary>
        /// Generuje nagłówek CSV (separator: średnik).
        /// Zgodny z formatem łatwym do importu w R/Python/Excel.
        /// </summary>
        public static string GetCsvHeader()
        {
            return "TestID;Algorithm;StartX;StartY;TargetX;TargetY;Scenario;ObstacleDensity;" +
                   "MapTopology;MapSeed;MapDensity;MapWidth;MapHeight;" +
                   "DistanceBucket;EuclideanDistance;OctagonalDistance;ReferenceShortestPathLength;" +
                   "PathFound;ColdStartTimeMs;ColdStartTicks;ColdStartGCAllocBytes;" +
                   "AvgExecutionTimeMs;MinExecutionTimeMs;MaxExecutionTimeMs;StdDevExecutionTimeMs;" +
                   "AvgExecutionTicks;AvgGCAllocBytes;" +
                   "ExploredNodes;JumpScannedCells;PathLength;PathCost10_14;DirectionChanges;PathSmoothness;PathRecalculations;CPUTemperature";
        }

        /// <summary>
        /// Generuje wiersz CSV z danymi tej metryki.
        /// Wszystkie wartości zmiennoprzecinkowe formatowane z 6 miejscami po przecinku
        /// dla precyzji naukowej.
        /// </summary>
        public string ToCsvRow()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0};{1};{2};{3};{4};{5};{6};{7:F2};" +
                "{8};{9};{10:F2};{11};{12};{13};{14:F3};{15:F3};{16:F3};" +
                "{17};{18:F6};{19};{20};{21:F6};{22:F6};{23:F6};{24:F6};" +
                "{25:F2};{26:F2};{27};{28};{29:F4};{30};{31};{32:F6};{33};{34:F1}",
                TestID, AlgorithmName, StartX, StartY, TargetX, TargetY, Scenario, ObstacleDensity,
                MapTopology, MapSeed, MapDensity, MapWidth, MapHeight, DistanceBucket,
                EuclideanDistance, OctagonalDistance, ReferenceShortestPathLength, PathFound, ColdStartTimeMs,
                ColdStartTicks, ColdStartGCAllocBytes, AvgExecutionTimeMs, MinExecutionTimeMs,
                MaxExecutionTimeMs, StdDevExecutionTimeMs, AvgExecutionTicks, AvgGCAllocBytes,
                ExploredNodes, JumpScannedCells, PathLength, PathCost, DirectionChanges,
                PathSmoothness, PathRecalculations, CPUTemperature);
        }
    }
}
