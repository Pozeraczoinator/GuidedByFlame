using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Pathfinding.Core;

namespace Pathfinding.Benchmark
{
    /// <summary>
    /// Naukowy system doboru punktów startowych i końcowych dla benchmarków.
    /// 
    /// Implementuje Distance Bucketing — podział par (start, goal) na wiązki:
    /// - SHORT:  0–33% maksymalnego dystansu (overhead algorytmu vs krótka ścieżka)
    /// - MEDIUM: 33–66% maksymalnego dystansu (typowy scenariusz gry)
    /// - LONG:   66–100% maksymalnego dystansu (pełne obciążenie, dominacja heurystyki)
    /// 
    /// Kluczowe cechy:
    /// 1. BFS reachability check — gwarancja istnienia ścieżki
    /// 2. Deterministyczny seed — powtarzalność eksperymentów
    /// 3. Generacja par UNREACHABLE — testowanie worst-case
    /// 4. Eksport do rozszerzonego formatu CSV
    /// 
    /// Złożoność generacji: O(pairsPerBucket × W×H) — BFS per para
    /// </summary>
    public class TestPointSelector
    {
        /// <summary>Kategoria dystansu pary testowej.</summary>
        public enum DistanceBucket
        {
            Short,
            Medium,
            Long,
            Unreachable
        }

        /// <summary>Rozszerzona struktura test case z metadanymi naukowymi.</summary>
        public struct EnhancedTestCase
        {
            public int StartX, StartY;
            public int TargetX, TargetY;
            public DistanceBucket Bucket;
            public float EuclideanDistance;
            public bool IsReachable;

            public override string ToString()
            {
                return $"{StartX},{StartY},{TargetX},{TargetY},{Bucket},{EuclideanDistance:F2},{IsReachable}";
            }
        }

        private readonly System.Random _rng;

        public TestPointSelector(int seed = 42)
        {
            _rng = new System.Random(seed);
        }

        /// <summary>
        /// Generuje kompletny zestaw par testowych z distance bucketing.
        /// </summary>
        /// <param name="grid">Mapa na której operujemy</param>
        /// <param name="pairsPerBucket">Ile par na wiązkę (SHORT/MEDIUM/LONG)</param>
        /// <param name="unreachablePairs">Ile par nieosiągalnych</param>
        /// <returns>Lista par z metadanymi</returns>
        public List<EnhancedTestCase> GenerateTestCases(GridMap grid, int pairsPerBucket = 30, 
            int unreachablePairs = 5)
        {
            var result = new List<EnhancedTestCase>();
            
            // Zbierz wszystkie walkable pola
            var walkablePool = GetWalkablePositions(grid);
            if (walkablePool.Count < 2)
            {
                Debug.LogError("[TestPointSelector] Za mało walkable pól do generacji testów!");
                return result;
            }

            // Oblicz maksymalny dystans (przekątna mapy)
            float maxDist = Mathf.Sqrt(grid.Width * grid.Width + grid.Height * grid.Height);
            float shortMax = maxDist / 3f;
            float mediumMax = maxDist * 2f / 3f;

            // Generuj pary dla każdej wiązki
            result.AddRange(GenerateBucketPairs(grid, walkablePool, DistanceBucket.Short, 
                0f, shortMax, pairsPerBucket));
            result.AddRange(GenerateBucketPairs(grid, walkablePool, DistanceBucket.Medium, 
                shortMax, mediumMax, pairsPerBucket));
            result.AddRange(GenerateBucketPairs(grid, walkablePool, DistanceBucket.Long, 
                mediumMax, maxDist + 1f, pairsPerBucket));

            // Generuj pary nieosiągalne
            result.AddRange(GenerateUnreachablePairs(grid, walkablePool, unreachablePairs));

            Debug.Log($"[TestPointSelector] Wygenerowano {result.Count} par testowych " +
                      $"(SHORT: {pairsPerBucket}, MEDIUM: {pairsPerBucket}, " +
                      $"LONG: {pairsPerBucket}, UNREACHABLE: {unreachablePairs})");

            return result;
        }

        /// <summary>
        /// Generuje pary dla jednej wiązki dystansowej.
        /// Powtarza losowanie aż zbierze wymaganą liczbę par z ważną osiągalnością.
        /// </summary>
        private List<EnhancedTestCase> GenerateBucketPairs(GridMap grid, List<Vector2Int> pool,
            DistanceBucket bucket, float minDist, float maxDist, int count)
        {
            var pairs = new List<EnhancedTestCase>();
            int attempts = 0;
            int maxAttempts = count * 100;

            while (pairs.Count < count && attempts < maxAttempts)
            {
                attempts++;
                Vector2Int start = pool[_rng.Next(pool.Count)];
                Vector2Int goal = pool[_rng.Next(pool.Count)];
                if (start == goal) continue;

                float dist = Vector2Int.Distance(start, goal);
                if (dist < minDist || dist >= maxDist) continue;

                // BFS reachability check
                bool reachable = BFSReachabilityCheck(grid, start, goal);
                if (!reachable) continue; // Dla bucketów SHORT/MEDIUM/LONG chcemy osiągalne

                pairs.Add(new EnhancedTestCase
                {
                    StartX = start.x, StartY = start.y,
                    TargetX = goal.x, TargetY = goal.y,
                    Bucket = bucket,
                    EuclideanDistance = dist,
                    IsReachable = true
                });
            }

            if (pairs.Count < count)
            {
                Debug.LogWarning($"[TestPointSelector] Wiązka {bucket}: udało się wygenerować " +
                                 $"tylko {pairs.Count}/{count} par (za mało walkable pól w zakresie dystansu).");
            }

            return pairs;
        }

        /// <summary>
        /// Generuje pary testowe z nieosiągalnym celem.
        /// Tworzy izolowany region 5x5 otoczony ścianami i umieszcza cel wewnątrz.
        /// UWAGA: Modyfikuje mapę (dodaje izolowany pokój) — używaj na kopii gridu!
        /// </summary>
        private List<EnhancedTestCase> GenerateUnreachablePairs(GridMap grid, 
            List<Vector2Int> walkablePool, int count)
        {
            var pairs = new List<EnhancedTestCase>();

            for (int i = 0; i < count; i++)
            {
                // Znajdź pozycję startu z walkable pool
                if (walkablePool.Count == 0) break;
                Vector2Int start = walkablePool[_rng.Next(walkablePool.Count)];

                // Cel: pole które jest walkable ale w odizolowanym regionie
                // Szukamy pola które BFS nie osiągnie z żadnego innego pola
                // Prosta metoda: użyj rogu mapy i sprawdź osiągalność
                Vector2Int goal = FindIsolatedGoal(grid, start);
                if (goal.x < 0) continue; // Nie znaleziono

                pairs.Add(new EnhancedTestCase
                {
                    StartX = start.x, StartY = start.y,
                    TargetX = goal.x, TargetY = goal.y,
                    Bucket = DistanceBucket.Unreachable,
                    EuclideanDistance = Vector2Int.Distance(start, goal),
                    IsReachable = false
                });
            }

            return pairs;
        }

        /// <summary>
        /// Szuka walkable pola nieosiągalnego z punktu start (inny connected component).
        /// </summary>
        private Vector2Int FindIsolatedGoal(GridMap grid, Vector2Int start)
        {
            // Wykonaj flood fill z punktu start — oznacz wszystkie osiągalne
            var reachable = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            reachable.Add(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = current.x + dx;
                        int ny = current.y + dy;
                        var next = new Vector2Int(nx, ny);
                        if (grid.IsWalkable(nx, ny) && !reachable.Contains(next))
                        {
                            // Corner cutting check
                            if (dx != 0 && dy != 0)
                            {
                                if (!grid.IsWalkable(current.x + dx, current.y) ||
                                    !grid.IsWalkable(current.x, current.y + dy))
                                    continue;
                            }
                            reachable.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            // Szukaj walkable pola NIE w zbiorze reachable
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    if (grid.IsWalkable(x, y) && !reachable.Contains(new Vector2Int(x, y)))
                        return new Vector2Int(x, y);
                }
            }

            return new Vector2Int(-1, -1); // Brak izolowanych pól
        }

        /// <summary>
        /// BFS reachability check — sprawdza czy cel jest osiągalny ze startu.
        /// Uwzględnia 8-kierunkowy ruch z corner cutting prevention.
        /// Złożoność: O(W×H) worst case.
        /// </summary>
        public static bool BFSReachabilityCheck(GridMap grid, Vector2Int start, Vector2Int goal)
        {
            if (!grid.IsWalkable(start) || !grid.IsWalkable(goal)) return false;
            if (start == goal) return true;

            var visited = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == goal) return true;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = current.x + dx;
                        int ny = current.y + dy;
                        var next = new Vector2Int(nx, ny);

                        if (grid.IsWalkable(nx, ny) && !visited.Contains(next))
                        {
                            // Corner cutting prevention (jak w A*)
                            if (dx != 0 && dy != 0)
                            {
                                if (!grid.IsWalkable(current.x + dx, current.y) ||
                                    !grid.IsWalkable(current.x, current.y + dy))
                                    continue;
                            }
                            visited.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Zbiera listę wszystkich walkable pozycji na mapie.
        /// </summary>
        private List<Vector2Int> GetWalkablePositions(GridMap grid)
        {
            var positions = new List<Vector2Int>();
            for (int x = 0; x < grid.Width; x++)
                for (int y = 0; y < grid.Height; y++)
                    if (grid.IsWalkable(x, y))
                        positions.Add(new Vector2Int(x, y));
            return positions;
        }

        /// <summary>
        /// Eksportuje rozszerzony zestaw testów do CSV.
        /// Format: StartX,StartY,TargetX,TargetY,DistanceBucket,EuclideanDist,IsReachable
        /// </summary>
        public static void ExportToCsv(List<EnhancedTestCase> testCases, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("StartX,StartY,TargetX,TargetY,DistanceBucket,EuclideanDist,IsReachable");

            foreach (var tc in testCases)
            {
                sb.AppendLine(tc.ToString());
            }

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(filePath, sb.ToString());
            Debug.Log($"[TestPointSelector] Wyeksportowano {testCases.Count} par testowych do: {filePath}");
        }
    }
}
