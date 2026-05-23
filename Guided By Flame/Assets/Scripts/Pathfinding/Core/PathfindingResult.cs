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
        /// Liczba odwiedzonych węzłów (metryka Explored Nodes / Nodes Expanded).
        /// Suma węzłów wciągniętych do listy zamkniętej.
        /// Kluczowa metryka algorytmiczna — niezależna od mocy procesora.
        /// </summary>
        public int ExploredNodes { get; set; }
        
        /// <summary>
        /// Czas wykonania wyrażony w milisekundach wykorzystując Stopwatch.
        /// </summary>
        public double ExecutionTimeMs { get; set; }
        
        /// <summary>
        /// Długość wyznaczonej ścieżki (licząc odległości według geometrii siatki 
        /// np. 1.0 dla ortogonalnych, 1.414 dla przekątnych).
        /// </summary>
        public float PathLength { get; set; }

        /// <summary>
        /// Ilość cykli procesora pobrana poprzez Stopwatch.GetTimestamp() albo ElapsedTicks.
        /// Precyzyjniejsza niż milisekundy dla bardzo szybkich algorytmów.
        /// </summary>
        public long ExecutionTicks { get; set; }

        // ────────────────────────────────────────────────
        //  NOWE METRYKI DLA PRACY MAGISTERSKIEJ
        // ────────────────────────────────────────────────

        /// <summary>
        /// Delta alokacji pamięci GC (Garbage Collector) w bajtach.
        /// Mierzona jako GC.GetTotalMemory() po – GC.GetTotalMemory() przed wywołaniem FindPath().
        /// Im mniejsza wartość, tym algorytm jest bardziej przyjazny dla GC Unity.
        /// </summary>
        public long GCAllocBytes { get; set; }

        /// <summary>
        /// Liczba zmian kierunku na znalezionej ścieżce.
        /// direction[i] = Path[i+1] - Path[i]; zmiana kierunku = (dir[i] != dir[i-1]).
        /// Mniejsza wartość = gładsza ścieżka, bardziej naturalna trajektoria NPC.
        /// </summary>
        public int DirectionChanges { get; set; }

        /// <summary>
        /// Metryka gładkości ścieżki: DirectionChanges / PathLength.
        /// Wartość 0 = idealnie prosta linia. Im bliżej 0, tym gładsza ścieżka.
        /// Użyteczna do porównania czytelności trajektorii NPC w grach 2D.
        /// </summary>
        public float PathSmoothness { get; set; }

        /// <summary>
        /// Liczba wymuszonych rekalkulacji trasy w scenariuszach dynamicznych.
        /// Dla scenariuszy statycznych pozostaje 0.
        /// </summary>
        public int PathRecalculations { get; set; }

        /// <summary>
        /// Oblicza metryki gładkości ścieżki na podstawie listy Path.
        /// Wywołaj po zakończeniu FindPath() i ustaleniu Path.
        /// Złożoność: O(P) gdzie P = Path.Count.
        /// </summary>
        public void CalculateSmoothnessMetrics()
        {
            DirectionChanges = 0;
            PathSmoothness = 0f;

            if (Path == null || Path.Count < 3)
                return;

            Vector2Int prevDirection = Path[1] - Path[0];

            for (int i = 2; i < Path.Count; i++)
            {
                Vector2Int currentDirection = Path[i] - Path[i - 1];
                if (currentDirection != prevDirection)
                {
                    DirectionChanges++;
                }
                prevDirection = currentDirection;
            }

            if (PathLength > 0f)
            {
                PathSmoothness = DirectionChanges / PathLength;
            }
        }
    }
}
