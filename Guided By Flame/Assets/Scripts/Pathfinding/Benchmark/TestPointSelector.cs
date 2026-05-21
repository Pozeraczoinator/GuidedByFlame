using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Pathfinding.Core;

namespace Pathfinding.Benchmark
{
    /// <summary>
    /// Naukowy system doboru punktów startowych i końcowych dla benchmarków.
    ///
    /// Implementuje distance bucketing na podstawie realnej długości najkrótszej ścieżki,
    /// a nie samej odległości w linii prostej. Dzięki temu punkty w labiryntach,
    /// pokojach i korytarzach są klasyfikowane według faktycznej trudności trasy.
    ///
    /// Kluczowe cechy:
    /// 1. Shortest-path reachability check — gwarancja istnienia ścieżki
    /// 2. Deterministyczny seed — powtarzalność eksperymentów
    /// 3. Deduplication — brak powtarzających się par start-cel
    /// 4. Eksport do rozszerzonego formatu CSV
    ///
    /// Złożoność generacji: O(attempts * W * H * log(W * H)).
    /// Koszt dotyczy tylko generowania test cases, nie właściwego benchmarku algorytmów.
    /// </summary>
    public class TestPointSelector
    {
        /// <summary>Kategoria realnej długości ścieżki.</summary>
        public enum DistanceBucket
        {
            Short,
            Medium,
            Long
        }

        /// <summary>Rozszerzona struktura test case z metadanymi naukowymi.</summary>
        public struct EnhancedTestCase
        {
            public int StartX, StartY;
            public int TargetX, TargetY;
            public DistanceBucket Bucket;
            public float EuclideanDistance;
            public float OctagonalDistance;
            public float ShortestPathLength;

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5:F2},{6:F3},{7:F3}",
                    StartX, StartY, TargetX, TargetY, Bucket, EuclideanDistance,
                    OctagonalDistance, ShortestPathLength);
            }
        }

        private class DistanceNode : IHeapItem<DistanceNode>
        {
            public int X { get; }
            public int Y { get; }
            public float Cost { get; set; }

            private int _heapIndex;

            public DistanceNode(int x, int y)
            {
                X = x;
                Y = y;
                Cost = float.MaxValue;
            }

            public int HeapIndex
            {
                get => _heapIndex;
                set => _heapIndex = value;
            }

            public int CompareTo(DistanceNode other)
            {
                int compare = Cost.CompareTo(other.Cost);
                if (compare == 0)
                {
                    int posA = X * 10000 + Y;
                    int posB = other.X * 10000 + other.Y;
                    compare = posA.CompareTo(posB);
                }
                return -compare;
            }
        }

        private struct ReachableDistance
        {
            public Vector2Int Position;
            public float PathLength;
        }

        private struct CandidatePair
        {
            public Vector2Int Start;
            public Vector2Int Target;
            public float EuclideanDistance;
            public float OctagonalDistance;
            public float ShortestPathLength;
        }

        private readonly System.Random _rng;

        public TestPointSelector(int seed = 42)
        {
            _rng = new System.Random(seed);
        }

        /// <summary>
        /// Generuje kompletny zestaw osiągalnych par testowych z distance bucketing.
        /// </summary>
        /// <param name="grid">Mapa, na której operujemy.</param>
        /// <param name="pairsPerBucket">Ile par na wiązkę SHORT/MEDIUM/LONG.</param>
        /// <returns>Lista par z metadanymi.</returns>
        public List<EnhancedTestCase> GenerateTestCases(GridMap grid, int pairsPerBucket = 30)
        {
            var result = new List<EnhancedTestCase>();

            var walkablePool = GetWalkablePositions(grid);
            if (walkablePool.Count < 2)
            {
                Debug.LogError("[TestPointSelector] Za mało walkable pól do generacji testów!");
                return result;
            }

            List<CandidatePair> candidates = CollectReachableCandidatePairs(grid, walkablePool, pairsPerBucket);
            if (candidates.Count == 0)
            {
                Debug.LogWarning("[TestPointSelector] Nie znaleziono żadnej osiągalnej pary start-cel.");
                return result;
            }

            float maxDist = GetMaxShortestPathLength(candidates);
            float shortMax = maxDist / 3f;
            float mediumMax = maxDist * 2f / 3f;

            FillBucketsFromCandidates(candidates, result, pairsPerBucket, shortMax, mediumMax);

            Debug.Log($"[TestPointSelector] Wygenerowano {result.Count} osiągalnych par testowych " +
                      $"(SHORT: {CountBucket(result, DistanceBucket.Short)}/{pairsPerBucket}, " +
                      $"MEDIUM: {CountBucket(result, DistanceBucket.Medium)}/{pairsPerBucket}, " +
                      $"LONG: {CountBucket(result, DistanceBucket.Long)}/{pairsPerBucket}). " +
                      $"Bucketing bazuje na realnej długości najkrótszej ścieżki, max={maxDist:F3}.");

            return result;
        }

        /// <summary>
        /// Losuje punkty startowe i dla każdego liczy odległości do wszystkich osiągalnych celów jedną Dijkstrą.
        /// Zebrana pula kandydatów służy potem do wyznaczenia realnego maxDist dla bucketów.
        /// </summary>
        private List<CandidatePair> CollectReachableCandidatePairs(GridMap grid, List<Vector2Int> pool,
            int pairsPerBucket)
        {
            var candidates = new List<CandidatePair>();
            var usedPairs = new HashSet<string>();
            int sourceAttempts = 0;
            int maxSourceAttempts = Math.Max(40, pairsPerBucket * 8);
            int maxPairsPerStart = Math.Max(8, pairsPerBucket / 2);
            int targetCandidateCount = Math.Max(90, pairsPerBucket * 3 * 8);

            while (candidates.Count < targetCandidateCount && sourceAttempts < maxSourceAttempts)
            {
                sourceAttempts++;
                Vector2Int start = pool[_rng.Next(pool.Count)];
                List<ReachableDistance> reachable = GetReachableDistances(grid, start);
                if (reachable.Count <= 1)
                    continue;

                AddFarthestReachableCandidate(candidates, usedPairs, start, reachable);
                Shuffle(reachable);

                int addedForStart = 0;
                foreach (var candidate in reachable)
                {
                    if (candidate.Position == start)
                        continue;

                    if (AddCandidatePair(candidates, usedPairs, start, candidate))
                        addedForStart++;

                    if (addedForStart >= maxPairsPerStart || candidates.Count >= targetCandidateCount)
                        break;
                }
            }

            return candidates;
        }

        private void FillBucketsFromCandidates(List<CandidatePair> candidates,
            List<EnhancedTestCase> result, int pairsPerBucket, float shortMax, float mediumMax)
        {
            Shuffle(candidates);

            foreach (var candidate in candidates)
            {
                DistanceBucket bucket = GetBucket(candidate.ShortestPathLength, shortMax, mediumMax);
                if (CountBucket(result, bucket) >= pairsPerBucket)
                    continue;

                result.Add(new EnhancedTestCase
                {
                    StartX = candidate.Start.x,
                    StartY = candidate.Start.y,
                    TargetX = candidate.Target.x,
                    TargetY = candidate.Target.y,
                    Bucket = bucket,
                    EuclideanDistance = candidate.EuclideanDistance,
                    OctagonalDistance = candidate.OctagonalDistance,
                    ShortestPathLength = candidate.ShortestPathLength
                });

                if (AllBucketsFilled(result, pairsPerBucket))
                    break;
            }

            WarnIfBucketIncomplete(result, DistanceBucket.Short, pairsPerBucket);
            WarnIfBucketIncomplete(result, DistanceBucket.Medium, pairsPerBucket);
            WarnIfBucketIncomplete(result, DistanceBucket.Long, pairsPerBucket);
        }

        private void AddFarthestReachableCandidate(List<CandidatePair> candidates,
            HashSet<string> usedPairs, Vector2Int start, List<ReachableDistance> reachable)
        {
            ReachableDistance farthest = default;
            bool found = false;

            foreach (var candidate in reachable)
            {
                if (candidate.Position == start)
                    continue;

                if (!found || candidate.PathLength > farthest.PathLength)
                {
                    farthest = candidate;
                    found = true;
                }
            }

            if (found)
                AddCandidatePair(candidates, usedPairs, start, farthest);
        }

        private static bool AddCandidatePair(List<CandidatePair> candidates,
            HashSet<string> usedPairs, Vector2Int start, ReachableDistance candidate)
        {
            string pairKey = GetUndirectedPairKey(start, candidate.Position);
            if (usedPairs.Contains(pairKey))
                return false;

            usedPairs.Add(pairKey);
            candidates.Add(new CandidatePair
            {
                Start = start,
                Target = candidate.Position,
                EuclideanDistance = Vector2Int.Distance(start, candidate.Position),
                OctagonalDistance = CalculateOctagonalDistance(start, candidate.Position),
                ShortestPathLength = candidate.PathLength
            });

            return true;
        }

        private List<ReachableDistance> GetReachableDistances(GridMap grid, Vector2Int start)
        {
            var reachable = new List<ReachableDistance>();

            if (!grid.IsWalkable(start))
                return reachable;

            var openSet = new MinHeap<DistanceNode>(grid.Width * grid.Height);
            var closedSet = new HashSet<Vector2Int>();
            var allNodes = new Dictionary<Vector2Int, DistanceNode>();

            var startNode = new DistanceNode(start.x, start.y) { Cost = 0f };
            allNodes[start] = startNode;
            openSet.Add(startNode);

            while (openSet.Count > 0)
            {
                DistanceNode currentNode = openSet.RemoveFirst();
                Vector2Int current = new Vector2Int(currentNode.X, currentNode.Y);

                if (closedSet.Contains(current))
                    continue;

                closedSet.Add(current);
                reachable.Add(new ReachableDistance { Position = current, PathLength = currentNode.Cost });

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        int nx = current.x + dx;
                        int ny = current.y + dy;
                        var next = new Vector2Int(nx, ny);

                        if (!grid.IsWalkable(nx, ny) || closedSet.Contains(next))
                            continue;

                        if (dx != 0 && dy != 0)
                        {
                            if (!grid.IsWalkable(current.x + dx, current.y) ||
                                !grid.IsWalkable(current.x, current.y + dy))
                                continue;
                        }

                        if (!allNodes.TryGetValue(next, out DistanceNode nextNode))
                        {
                            nextNode = new DistanceNode(nx, ny);
                            allNodes[next] = nextNode;
                        }

                        float stepCost = (dx != 0 && dy != 0) ? 1.414f : 1.0f;
                        float newCost = currentNode.Cost + stepCost;
                        bool inOpenSet = openSet.Contains(nextNode);

                        if (newCost < nextNode.Cost || !inOpenSet)
                        {
                            nextNode.Cost = newCost;

                            if (!inOpenSet)
                                openSet.Add(nextNode);
                            else
                                openSet.UpdateItem(nextNode);
                        }
                    }
                }
            }

            return reachable;
        }

        /// <summary>
        /// Publiczny reachability check zachowany dla testów i walidacji.
        /// Korzysta z tego samego modelu ruchu co shortest path: 8 kierunków bez ścinania rogów.
        /// </summary>
        public static bool BFSReachabilityCheck(GridMap grid, Vector2Int start, Vector2Int goal)
        {
            return TryGetShortestPathLength(grid, start, goal, out _);
        }

        /// <summary>
        /// Dijkstra na siatce 8-kierunkowej. Zwraca realną geometryczną długość najkrótszej ścieżki.
        /// Ruch prosty = 1.0, diagonalny = 1.414.
        /// </summary>
        public static bool TryGetShortestPathLength(GridMap grid, Vector2Int start, Vector2Int goal,
            out float pathLength)
        {
            pathLength = 0f;

            if (!grid.IsWalkable(start) || !grid.IsWalkable(goal))
                return false;

            if (start == goal)
                return true;

            var openSet = new MinHeap<DistanceNode>(grid.Width * grid.Height);
            var closedSet = new HashSet<Vector2Int>();
            var allNodes = new Dictionary<Vector2Int, DistanceNode>();

            var startNode = new DistanceNode(start.x, start.y) { Cost = 0f };
            allNodes[start] = startNode;
            openSet.Add(startNode);

            while (openSet.Count > 0)
            {
                DistanceNode currentNode = openSet.RemoveFirst();
                Vector2Int current = new Vector2Int(currentNode.X, currentNode.Y);

                if (closedSet.Contains(current))
                    continue;

                if (current == goal)
                {
                    pathLength = currentNode.Cost;
                    return true;
                }

                closedSet.Add(current);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        int nx = current.x + dx;
                        int ny = current.y + dy;
                        var next = new Vector2Int(nx, ny);

                        if (!grid.IsWalkable(nx, ny) || closedSet.Contains(next))
                            continue;

                        if (dx != 0 && dy != 0)
                        {
                            if (!grid.IsWalkable(current.x + dx, current.y) ||
                                !grid.IsWalkable(current.x, current.y + dy))
                                continue;
                        }

                        if (!allNodes.TryGetValue(next, out DistanceNode nextNode))
                        {
                            nextNode = new DistanceNode(nx, ny);
                            allNodes[next] = nextNode;
                        }

                        float stepCost = (dx != 0 && dy != 0) ? 1.414f : 1.0f;
                        float newCost = currentNode.Cost + stepCost;
                        bool inOpenSet = openSet.Contains(nextNode);

                        if (newCost < nextNode.Cost || !inOpenSet)
                        {
                            nextNode.Cost = newCost;

                            if (!inOpenSet)
                                openSet.Add(nextNode);
                            else
                                openSet.UpdateItem(nextNode);
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

        private static float GetMaxShortestPathLength(List<CandidatePair> candidates)
        {
            float max = 0f;
            foreach (var candidate in candidates)
            {
                if (candidate.ShortestPathLength > max)
                    max = candidate.ShortestPathLength;
            }
            return max;
        }

        private static int CountBucket(List<EnhancedTestCase> testCases, DistanceBucket bucket)
        {
            int count = 0;
            foreach (var testCase in testCases)
            {
                if (testCase.Bucket == bucket)
                    count++;
            }
            return count;
        }

        private static bool AllBucketsFilled(List<EnhancedTestCase> testCases, int pairsPerBucket)
        {
            return CountBucket(testCases, DistanceBucket.Short) >= pairsPerBucket &&
                   CountBucket(testCases, DistanceBucket.Medium) >= pairsPerBucket &&
                   CountBucket(testCases, DistanceBucket.Long) >= pairsPerBucket;
        }

        private static DistanceBucket GetBucket(float pathLength, float shortMax, float mediumMax)
        {
            if (pathLength < shortMax)
                return DistanceBucket.Short;

            if (pathLength < mediumMax)
                return DistanceBucket.Medium;

            return DistanceBucket.Long;
        }

        public static float CalculateOctagonalDistance(Vector2Int a, Vector2Int b)
        {
            int dx = Math.Abs(a.x - b.x);
            int dy = Math.Abs(a.y - b.y);
            int diagonalSteps = Math.Min(dx, dy);
            int straightSteps = Math.Abs(dx - dy);

            return diagonalSteps * 1.414f + straightSteps;
        }

        private static void WarnIfBucketIncomplete(List<EnhancedTestCase> testCases, DistanceBucket bucket,
            int expectedCount)
        {
            int actualCount = CountBucket(testCases, bucket);
            if (actualCount >= expectedCount)
                return;

            Debug.LogWarning($"[TestPointSelector] Wiązka {bucket}: udało się wygenerować " +
                             $"tylko {actualCount}/{expectedCount} unikalnych par. " +
                             "Na tej mapie może brakować osiągalnych par w danym zakresie realnej długości.");
        }

        private static string GetUndirectedPairKey(Vector2Int a, Vector2Int b)
        {
            int keyA = a.x * 10000 + a.y;
            int keyB = b.x * 10000 + b.y;

            if (keyA <= keyB)
                return $"{a.x},{a.y}|{b.x},{b.y}";

            return $"{b.x},{b.y}|{a.x},{a.y}";
        }

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(0, i + 1);
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        /// <summary>
        /// Eksportuje rozszerzony zestaw testów do CSV.
        /// Format: StartX,StartY,TargetX,TargetY,DistanceBucket,EuclideanDist,OctagonalDistance,ShortestPathLength
        /// </summary>
        public static void ExportToCsv(List<EnhancedTestCase> testCases, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("StartX,StartY,TargetX,TargetY,DistanceBucket,EuclideanDist,OctagonalDistance,ShortestPathLength");

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
