using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding.Core;

namespace Pathfinding.MapGenerators
{
    /// <summary>
    /// Generator map z rozproszonymi blokami przeszkód o regularnym rozmiarze.
    /// 
    /// Rozmieszcza bloki NxN (np. 3×3, 5×5) w losowych pozycjach na mapie.
    /// Tworzy topologię z regularnymi, przewidywalnymi przeszkodami — 
    /// testuje symmetry breaking w JPS i pruning effectiveness.
    /// 
    /// Kluczowe cechy dla benchmarku:
    /// - Regularne bloki tworzą symetryczne sytuacje — stress test dla JPS pruning
    /// - Przestrzeń między blokami jest otwarta — częściowa przewaga JPS
    /// - Łatwe do wizualnej walidacji (regularne wzory)
    /// - Parametryczny rozmiar bloków pozwala testować granulację przeszkód
    /// </summary>
    public class ScatteredBlockGenerator : IMapGenerator
    {
        public string TopologyName => "ScatteredBlock";

        /// <summary>Rozmiar boku bloku przeszkody (kwadrat blockSize × blockSize).</summary>
        private readonly int _blockSize;

        public ScatteredBlockGenerator(int blockSize = 3)
        {
            _blockSize = Mathf.Max(2, blockSize);
        }

        public GridMap Generate(int width, int height, float obstacleDensity, int seed)
        {
            var rng = new System.Random(seed);
            obstacleDensity = Mathf.Clamp01(obstacleDensity);
            bool[,] walkable = new bool[width, height];

            // Inicjuj jako pustą mapę
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    walkable[x, y] = true;

            // Oblicz ile bloków potrzebujemy na docelowe zagęszczenie
            int totalCells = width * height;
            int targetObstacles = (int)(totalCells * obstacleDensity);
            int blockArea = _blockSize * _blockSize;
            int numBlocks = Mathf.Max(1, targetObstacles / blockArea);

            // Rozmieść bloki z collision detection
            var placedBlocks = new List<(int x, int y)>();
            int attempts = 0;
            int maxAttempts = numBlocks * 20;

            while (placedBlocks.Count < numBlocks && attempts < maxAttempts)
            {
                attempts++;
                int bx = rng.Next(0, width - _blockSize);
                int by = rng.Next(0, height - _blockSize);

                // Sprawdź kolizję z istniejącymi blokami (z marginesem 1 tile)
                bool collision = false;
                foreach (var placed in placedBlocks)
                {
                    if (Math.Abs(bx - placed.x) < _blockSize + 1 &&
                        Math.Abs(by - placed.y) < _blockSize + 1)
                    {
                        collision = true;
                        break;
                    }
                }

                if (!collision)
                {
                    // Umieść blok
                    for (int dx = 0; dx < _blockSize; dx++)
                        for (int dy = 0; dy < _blockSize; dy++)
                            walkable[bx + dx, by + dy] = false;

                    placedBlocks.Add((bx, by));
                }
            }

            return new GridMap(walkable);
        }
    }
}
