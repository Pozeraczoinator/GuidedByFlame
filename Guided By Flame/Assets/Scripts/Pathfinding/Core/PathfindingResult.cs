using System.Collections.Generic;
using UnityEngine;

namespace Pathfinding.Core
{
    /// <summary>
    /// Klasa przechowująca wyniki działania algorytmu oraz metryki potrzebne do benchmarku.
    /// Zwracana przez implementacje IPathfindingAlgorithm.
    /// </summary>
    public class PathfindingResult
    {
        public bool PathFound { get; set; }
        
        /// <summary>
        /// Odnaleziona ścieżka. Pusta w przypadku braku odnalezienia.
        /// </summary>
        public List<Vector2Int> Path { get; set; } = new List<Vector2Int>();
        
        /// <summary>
        /// Kolejność odwiedzania węzłów przez algorytm (do wizualizacji krok po kroku).
        /// </summary>
        public List<Vector2Int> ExploredNodesHistory { get; set; } = new List<Vector2Int>();

        /// <summary>
        /// Liczba odwiedzonych węzłów (metryka Explored Nodes). Suma węzłów wciągniętych do listy zamkniętej.
        /// </summary>
        public int ExploredNodes { get; set; }
        
        /// <summary>
        /// Czas wykonania wyrażony w milisekundach wykorzystując Stopwatch.
        /// </summary>
        public double ExecutionTimeMs { get; set; }
        
        /// <summary>
        /// Długość wyznaczonej ścieżki (licząc odległości według geometrii siatki np. 1D dla ortogonalnych, 1.414 dla przekątnych, o ile wspierane).
        /// W grid-based dla Manhattan zwykle równa liczbie węzłów w Path.
        /// </summary>
        public float PathLength { get; set; }

        /// <summary>
        /// Ilość cykli procesora pobrana poprzez Stopwatch.GetTimestamp() albo ElapsedTicks. (Pomocniczo dla precyzji).
        /// </summary>
        public long ExecutionTicks { get; set; }
    }
}
