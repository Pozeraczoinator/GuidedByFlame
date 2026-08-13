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
        /// Liczba pól sprawdzonych wewnątrz procedury Jump() w JPS.
        /// Dla pozostałych algorytmów pozostaje 0. Ta metryka pokazuje pracę ukrytą
        /// za skokami, której nie widać w ExploredNodes.
        /// </summary>
        public int JumpScannedCells { get; set; }
        
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
        /// Dyskretny koszt faktycznie przebytej ścieżki: 10 za ruch ortogonalny
        /// i 14 za ruch diagonalny. Nie uwzględnia wag terenu.
        /// </summary>
        public int PathCost { get; set; }

        /// <summary>
        /// Ilość cykli procesora pobrana poprzez Stopwatch.GetTimestamp() albo ElapsedTicks.
        /// Precyzyjniejsza niż milisekundy dla bardzo szybkich algorytmów.
        /// </summary>
        public long ExecutionTicks { get; set; }

        // ────────────────────────────────────────────────
        //  NOWE METRYKI DLA PRACY MAGISTERSKIEJ
        // ────────────────────────────────────────────────

        /// <summary>
        /// Liczba bajtów zaalokowanych na bieżącym wątku podczas pomiaru.
        /// Mierzona jako różnica GC.GetAllocatedBytesForCurrentThread() przed i po operacji.
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
        /// Oblicza dyskretny koszt 10/14 dla całej zapisanej trasy,
        /// z uwzględnieniem pierwszego odcinka start -> Path[0].
        /// </summary>
        public void CalculatePathCost(Vector2Int startPosition)
        {
            PathCost = 0;
            if (Path == null || Path.Count == 0)
                return;

            Vector2Int previousPosition = startPosition;
            foreach (Vector2Int position in Path)
            {
                int dx = Mathf.Abs(position.x - previousPosition.x);
                int dy = Mathf.Abs(position.y - previousPosition.y);
                int diagonalSteps = Mathf.Min(dx, dy);
                int straightSteps = Mathf.Max(dx, dy) - diagonalSteps;
                PathCost += diagonalSteps * 14 + straightSteps * 10;
                previousPosition = position;
            }
        }

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

        /// <summary>
        /// Oblicza gładkość z uwzględnieniem pierwszego odcinka start -> Path[0].
        /// Lista Path celowo nie zawiera pola startowego, dlatego benchmark powinien
        /// używać tego przeciążenia zamiast wariantu bez argumentu.
        /// </summary>
        public void CalculateSmoothnessMetrics(Vector2Int startPosition)
        {
            DirectionChanges = 0;
            PathSmoothness = 0f;

            if (Path == null || Path.Count == 0)
                return;

            Vector2Int previousPosition = startPosition;
            Vector2Int previousDirection = Path[0] - previousPosition;
            previousPosition = Path[0];

            for (int i = 1; i < Path.Count; i++)
            {
                Vector2Int currentDirection = Path[i] - previousPosition;
                if (currentDirection != previousDirection)
                    DirectionChanges++;

                previousDirection = currentDirection;
                previousPosition = Path[i];
            }

            if (PathLength > 0f)
                PathSmoothness = DirectionChanges / PathLength;
        }
    }
}
