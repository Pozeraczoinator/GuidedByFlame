using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding.Core;

namespace Pathfinding.Benchmark
{
    /// <summary>
    /// Manager scenariusza DS3 — dynamiczne wagi terenu (Weighted Terrain).
    /// 
    /// Symuluje zmienne koszty poruszania się po terenie:
    /// - Normalny teren: koszt 1.0
    /// - Błoto/piasek: koszt 2.0–3.0
    /// - Ogień/zagrożenie: koszt 5.0–10.0
    /// 
    /// Wagi zmieniają się dynamicznie co N kroków — symuluje:
    /// - Rozprzestrzeniający się ogień (radialny wzorzec)
    /// - Zmienne warunki pogodowe (losowy wzorzec)
    /// - Zalanie terenu (liniowy wzorzec — fala)
    /// 
    /// UWAGA: JPS pomijany w tym scenariuszu (nie wspiera weighted gridów).
    /// Testujemy: A*, Dijkstra, CustomGreedy, GBFS.
    /// 
    /// Złożoność per update: O(changesPerUpdate)
    /// </summary>
    public class WeightedTerrainManager
    {
        /// <summary>Wzorzec zmiany wag terenu.</summary>
        public enum ChangePattern
        {
            /// <summary>Losowa zmiana rozproszonych pól.</summary>
            Random,
            /// <summary>Ogień rozprzestrzeniający się radialnie z epicentrum.</summary>
            Radial,
            /// <summary>Fala przesuwająca się liniowo przez mapę.</summary>
            Linear
        }

        /// <summary>Predefiniowane poziomy kosztów terenu.</summary>
        public static readonly float[] CostLevels = { 1.0f, 2.0f, 5.0f, 10.0f };

        private readonly System.Random _rng;
        private int _stepCounter;

        // Stan dla wzorca radialnego
        private Vector2Int _radialCenter;
        private int _radialRadius;

        // Stan dla wzorca liniowego
        private int _linearWaveFront;
        private bool _linearHorizontal;

        public WeightedTerrainManager(int seed = 42)
        {
            _rng = new System.Random(seed);
            _stepCounter = 0;
            _radialRadius = 0;
            _linearWaveFront = 0;
        }

        /// <summary>
        /// Inicjalizuje mapę z początkową dystrybucją wag terenu.
        /// Rozmieszcza strefy o różnych kosztach na podstawie wzorca.
        /// </summary>
        /// <param name="grid">Mapa do modyfikacji</param>
        /// <param name="pattern">Wzorzec początkowy</param>
        /// <param name="initialCoverage">Jaki % pól ma niedomyślny koszt (0.0–0.5)</param>
        public void InitializeWeights(GridMap grid, ChangePattern pattern, float initialCoverage = 0.1f)
        {
            initialCoverage = Mathf.Clamp(initialCoverage, 0f, 0.5f);
            int totalCells = grid.Width * grid.Height;
            int cellsToChange = (int)(totalCells * initialCoverage);

            switch (pattern)
            {
                case ChangePattern.Random:
                    ApplyRandomWeights(grid, cellsToChange);
                    break;
                case ChangePattern.Radial:
                    _radialCenter = new Vector2Int(grid.Width / 2, grid.Height / 2);
                    _radialRadius = 2;
                    ApplyRadialWeights(grid);
                    break;
                case ChangePattern.Linear:
                    _linearHorizontal = _rng.NextDouble() > 0.5;
                    _linearWaveFront = 0;
                    ApplyLinearWeights(grid);
                    break;
            }
        }

        /// <summary>
        /// Aplikuje dynamiczną zmianę wag terenu — jeden krok symulacji.
        /// Wywołuj co N kroków NPC aby symulować zmieniające się warunki.
        /// </summary>
        /// <param name="grid">Mapa do modyfikacji</param>
        /// <param name="pattern">Wzorzec zmiany</param>
        /// <param name="changesPerUpdate">Ile pól zmienić na krok</param>
        /// <param name="start">Punkt startowy (chroniony — koszt zawsze 1.0)</param>
        /// <param name="target">Punkt docelowy (chroniony — koszt zawsze 1.0)</param>
        /// <returns>Lista zmienionych pozycji</returns>
        public List<Vector2Int> ApplyDynamicWeightChanges(GridMap grid, ChangePattern pattern,
            int changesPerUpdate, Vector2Int start, Vector2Int target)
        {
            _stepCounter++;
            var changed = new List<Vector2Int>();

            switch (pattern)
            {
                case ChangePattern.Random:
                    changed = ApplyRandomChanges(grid, changesPerUpdate, start, target);
                    break;
                case ChangePattern.Radial:
                    _radialRadius++;
                    changed = ApplyRadialExpansion(grid, start, target);
                    break;
                case ChangePattern.Linear:
                    _linearWaveFront++;
                    changed = ApplyLinearAdvance(grid, start, target);
                    break;
            }

            return changed;
        }

        /// <summary>
        /// Resetuje wszystkie wagi terenu do 1.0 (normalny teren).
        /// </summary>
        public void ResetAllWeights(GridMap grid)
        {
            for (int x = 0; x < grid.Width; x++)
                for (int y = 0; y < grid.Height; y++)
                    grid.SetMovementCost(x, y, 1.0f);
            _stepCounter = 0;
            _radialRadius = 0;
            _linearWaveFront = 0;
        }

        // ─── Wzorzec: Random ───

        private void ApplyRandomWeights(GridMap grid, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int x = _rng.Next(0, grid.Width);
                int y = _rng.Next(0, grid.Height);
                if (grid.IsWalkable(x, y))
                {
                    float cost = CostLevels[_rng.Next(1, CostLevels.Length)]; // Pomijamy 1.0
                    grid.SetMovementCost(x, y, cost);
                }
            }
        }

        private List<Vector2Int> ApplyRandomChanges(GridMap grid, int count, 
            Vector2Int start, Vector2Int target)
        {
            var changed = new List<Vector2Int>();
            int attempts = 0;

            while (changed.Count < count && attempts < count * 10)
            {
                attempts++;
                int x = _rng.Next(0, grid.Width);
                int y = _rng.Next(0, grid.Height);
                var pos = new Vector2Int(x, y);

                if (!grid.IsWalkable(x, y) || pos == start || pos == target) continue;

                float newCost = CostLevels[_rng.Next(0, CostLevels.Length)];
                grid.SetMovementCost(x, y, newCost);
                changed.Add(pos);
            }
            return changed;
        }

        // ─── Wzorzec: Radial (rozprzestrzeniający się ogień) ───

        private void ApplyRadialWeights(GridMap grid)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    float dist = Vector2Int.Distance(new Vector2Int(x, y), _radialCenter);
                    if (dist <= _radialRadius && grid.IsWalkable(x, y))
                    {
                        // Im bliżej centrum, tym wyższy koszt
                        float intensity = 1.0f - (dist / Mathf.Max(1, _radialRadius));
                        int costIndex = Mathf.Clamp((int)(intensity * (CostLevels.Length - 1)), 0, CostLevels.Length - 1);
                        grid.SetMovementCost(x, y, CostLevels[costIndex]);
                    }
                }
            }
        }

        private List<Vector2Int> ApplyRadialExpansion(GridMap grid, Vector2Int start, Vector2Int target)
        {
            var changed = new List<Vector2Int>();
            int maxRadius = Mathf.Max(grid.Width, grid.Height);
            if (_radialRadius > maxRadius) return changed;

            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    var pos = new Vector2Int(x, y);
                    float dist = Vector2Int.Distance(pos, _radialCenter);

                    if (dist <= _radialRadius && dist > _radialRadius - 2 && grid.IsWalkable(x, y))
                    {
                        if (pos == start || pos == target) continue;
                        float intensity = 1.0f - (dist / Mathf.Max(1, _radialRadius));
                        int costIndex = Mathf.Clamp((int)(intensity * (CostLevels.Length - 1)), 0, CostLevels.Length - 1);
                        grid.SetMovementCost(x, y, CostLevels[costIndex]);
                        changed.Add(pos);
                    }
                }
            }
            return changed;
        }

        // ─── Wzorzec: Linear (fala) ───

        private void ApplyLinearWeights(GridMap grid)
        {
            int front = _linearWaveFront;
            int waveWidth = 3;

            for (int i = 0; i < waveWidth; i++)
            {
                int line = front + i;
                if (_linearHorizontal)
                {
                    if (line >= grid.Width) break;
                    for (int y = 0; y < grid.Height; y++)
                        if (grid.IsWalkable(line, y))
                            grid.SetMovementCost(line, y, CostLevels[Mathf.Min(i + 1, CostLevels.Length - 1)]);
                }
                else
                {
                    if (line >= grid.Height) break;
                    for (int x = 0; x < grid.Width; x++)
                        if (grid.IsWalkable(x, line))
                            grid.SetMovementCost(x, line, CostLevels[Mathf.Min(i + 1, CostLevels.Length - 1)]);
                }
            }
        }

        private List<Vector2Int> ApplyLinearAdvance(GridMap grid, Vector2Int start, Vector2Int target)
        {
            var changed = new List<Vector2Int>();
            int limit = _linearHorizontal ? grid.Width : grid.Height;
            if (_linearWaveFront >= limit) return changed;

            int waveWidth = 3;

            // Wyczyść stary front (przywróć koszt 1.0)
            int oldFront = _linearWaveFront - 1;
            if (oldFront >= 0)
            {
                if (_linearHorizontal)
                {
                    for (int y = 0; y < grid.Height; y++)
                    {
                        var pos = new Vector2Int(oldFront, y);
                        if (grid.IsWalkable(pos) && pos != start && pos != target)
                        {
                            grid.SetMovementCost(pos, 1.0f);
                            changed.Add(pos);
                        }
                    }
                }
                else
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var pos = new Vector2Int(x, oldFront);
                        if (grid.IsWalkable(pos) && pos != start && pos != target)
                        {
                            grid.SetMovementCost(pos, 1.0f);
                            changed.Add(pos);
                        }
                    }
                }
            }

            // Nowy front
            for (int i = 0; i < waveWidth; i++)
            {
                int line = _linearWaveFront + i;
                if (line >= limit) break;

                float cost = CostLevels[Mathf.Min(i + 1, CostLevels.Length - 1)];
                if (_linearHorizontal)
                {
                    for (int y = 0; y < grid.Height; y++)
                    {
                        var pos = new Vector2Int(line, y);
                        if (grid.IsWalkable(pos) && pos != start && pos != target)
                        {
                            grid.SetMovementCost(pos, cost);
                            changed.Add(pos);
                        }
                    }
                }
                else
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var pos = new Vector2Int(x, line);
                        if (grid.IsWalkable(pos) && pos != start && pos != target)
                        {
                            grid.SetMovementCost(pos, cost);
                            changed.Add(pos);
                        }
                    }
                }
            }

            return changed;
        }
    }
}
