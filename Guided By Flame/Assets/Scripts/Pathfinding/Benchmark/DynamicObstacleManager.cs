using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding.Core;

namespace Pathfinding.Benchmark
{
    /// <summary>
    /// Manager scenariuszy dynamicznych — generuje mapy proceduralne z zadanym zagęszczeniem
    /// przeszkód i modyfikuje je w locie (dodawanie/usuwanie ścian).
    /// Klucz: deterministyczny seed RNG gwarantuje powtarzalność eksperymentów.
    ///
    /// Użycie:
    ///   var dm = new DynamicObstacleManager(seed: 42);
    ///   GridMap map = dm.GenerateMap(50, 50, obstacleDensity: 0.2f, start, target);
    ///   dm.ApplyDynamicChanges(map, changesToApply: 10, start, target);
    /// </summary>
    public class DynamicObstacleManager
    {
        private readonly System.Random _rng;
        private readonly int _seed;

        /// <summary>
        /// Inicjalizacja z deterministycznym seedem.
        /// Gwarantuje powtarzalność eksperymentów — kluczowe dla pracy naukowej.
        /// </summary>
        /// <param name="seed">Ziarno generatora. Ten sam seed → te same mapy i modyfikacje.</param>
        public DynamicObstacleManager(int seed = 42)
        {
            _seed = seed;
            _rng = new System.Random(seed);
        }

        /// <summary>
        /// Generuje mapę proceduralną z zadanym zagęszczeniem przeszkód.
        /// Start i cel zawsze pozostają walkable. Otoczenie start/cel (1-tile margin) też walkable.
        /// 
        /// Złożoność: O(W×H) — jednokrotny przebieg po wszystkich polach.
        /// </summary>
        /// <param name="width">Szerokość mapy</param>
        /// <param name="height">Wysokość mapy</param>
        /// <param name="obstacleDensity">Procent przeszkód (0.0 do 1.0). Np. 0.3 = 30% ścian.</param>
        /// <param name="start">Punkt startowy (gwarantowane walkable)</param>
        /// <param name="target">Punkt docelowy (gwarantowane walkable)</param>
        /// <returns>Nowa GridMap z rozmieszczonymi przeszkodami</returns>
        public GridMap GenerateMap(int width, int height, float obstacleDensity,
            Vector2Int start, Vector2Int target)
        {
            obstacleDensity = Mathf.Clamp01(obstacleDensity);
            bool[,] walkable = new bool[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    // Losowo blokuj pola zgodnie z zagęszczeniem
                    walkable[x, y] = (_rng.NextDouble() >= obstacleDensity);
                }
            }

            // Gwarantuj walkable: start, cel, i ich bezpośredni sąsiedzi (margin 1 tile)
            EnsureWalkableArea(walkable, width, height, start);
            EnsureWalkableArea(walkable, width, height, target);

            return new GridMap(walkable);
        }

        /// <summary>
        /// Aplikuje dynamiczne zmiany na mapie — dodaje losowe ściany i/lub usuwa istniejące.
        /// Nigdy nie blokuje start ani target. Wymusza rekalkulację ścieżki.
        ///
        /// Złożoność: O(changesToApply) — stała liczba operacji niezależna od rozmiaru mapy.
        /// </summary>
        /// <param name="grid">Mapa do modyfikacji (in-place)</param>
        /// <param name="changesToApply">Liczba zmian (pól do dodania/usunięcia jako przeszkody)</param>
        /// <param name="start">Punkt startowy (chroniony przed blokowaniem)</param>
        /// <param name="target">Punkt docelowy (chroniony przed blokowaniem)</param>
        /// <returns>Lista pozycji, które zostały zmienione</returns>
        public List<Vector2Int> ApplyDynamicChanges(GridMap grid, int changesToApply,
            Vector2Int start, Vector2Int target)
        {
            var changedPositions = new List<Vector2Int>(changesToApply);
            int attempts = 0;
            int maxAttempts = changesToApply * 10; // Zapobiega nieskończonej pętli na małych mapach

            while (changedPositions.Count < changesToApply && attempts < maxAttempts)
            {
                attempts++;
                int x = _rng.Next(0, grid.Width);
                int y = _rng.Next(0, grid.Height);
                Vector2Int pos = new Vector2Int(x, y);

                // Nigdy nie blokuj startu ani celu
                if (pos == start || pos == target)
                    continue;

                // Nie blokuj bezpośrednich sąsiadów startu/celu
                if (IsAdjacentTo(pos, start) || IsAdjacentTo(pos, target))
                    continue;

                // Przełącz stan walkable ↔ obstacle (toggle)
                bool currentState = grid.IsWalkable(x, y);
                grid.SetWalkable(x, y, !currentState);
                changedPositions.Add(pos);
            }

            return changedPositions;
        }

        /// <summary>
        /// Resetuje mapę do stanu przed dynamicznymi zmianami, przywracając walkable
        /// na zmienionych pozycjach. Złożoność: O(changedPositions.Count).
        /// </summary>
        public void RevertChanges(GridMap grid, List<Vector2Int> changedPositions)
        {
            foreach (var pos in changedPositions)
            {
                bool currentState = grid.IsWalkable(pos);
                grid.SetWalkable(pos, !currentState);
            }
        }

        /// <summary>
        /// Generuje wiele predefiniowanych poziomów zagęszczenia do systematycznych testów.
        /// Domyślne wartości: 10%, 20%, 30%, 40%.
        /// </summary>
        public static float[] GetStandardDensityLevels()
        {
            return new float[] { 0.10f, 0.20f, 0.30f, 0.40f };
        }

        // ─── Metody prywatne ───

        /// <summary>
        /// Zapewnia, że punkt i jego 8 sąsiadów są walkable (margin 1 tile).
        /// </summary>
        private void EnsureWalkableArea(bool[,] walkable, int width, int height, Vector2Int center)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = center.x + dx;
                    int y = center.y + dy;
                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        walkable[x, y] = true;
                    }
                }
            }
        }

        /// <summary>
        /// Sprawdza, czy dwa punkty sąsiadują (odległość Czebyszewa ≤ 1).
        /// </summary>
        private bool IsAdjacentTo(Vector2Int a, Vector2Int b)
        {
            return Math.Abs(a.x - b.x) <= 1 && Math.Abs(a.y - b.y) <= 1;
        }
    }
}
