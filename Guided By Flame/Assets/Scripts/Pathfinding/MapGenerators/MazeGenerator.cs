using System.Collections.Generic;
using Pathfinding.Core;

namespace Pathfinding.MapGenerators
{
    /// <summary>
    /// Generator labiryntów typu "Perfect Maze" algorytmem Recursive Backtracker (DFS).
    /// 
    /// Produkuje labirynty z dokładnie jedną ścieżką między dowolnymi dwoma punktami 
    /// (spanning tree na grafie komórek). Opcjonalnie usuwa losowe ściany 
    /// (wall_removal_ratio) aby stworzyć cykle i alternatywne trasy.
    /// 
    /// Kluczowe cechy dla benchmarku:
    /// - JPS traci przewagę — wąskie korytarze (1-tile) generują mało jump pointów
    /// - Dijkstra i A* powinny mieć zbliżoną liczbę odwiedzonych węzłów
    /// - GBFS może znajdować suboptymalne ścieżki (pułapki heurystyki w labiryntach)
    /// - Zagęszczenie przeszkód kontrolowane przez wall_removal_ratio (nie density)
    /// 
    /// Algorytm:
    /// 1. Grid inicjowany jako pełna ściana
    /// 2. Komórki na pozycjach nieparzystych (2k+1, 2l+1) — potencjalne korytarze
    /// 3. DFS z losowym wyborem sąsiada — wyrzeźb ściany między komórkami
    /// 4. Post-processing: usuń losowe ściany proporcjonalnie do (1 - obstacleDensity)
    /// </summary>
    public class MazeGenerator : IMapGenerator
    {
        public string TopologyName => "Maze";

        public GridMap Generate(int width, int height, float obstacleDensity, int seed)
        {
            var rng = new System.Random(seed);
            bool[,] walkable = new bool[width, height];

            // Krok 1: Wszystko jako ściana
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    walkable[x, y] = false;

            // Krok 2: Wymiary siatki komórek (co 2 pola)
            int cellsX = (width - 1) / 2;
            int cellsY = (height - 1) / 2;
            if (cellsX < 1 || cellsY < 1)
            {
                // Mapa za mała na labirynt — zwróć pustą
                for (int x = 0; x < width; x++)
                    for (int y = 0; y < height; y++)
                        walkable[x, y] = true;
                return new GridMap(walkable);
            }

            bool[,] visited = new bool[cellsX, cellsY];
            var stack = new Stack<(int cx, int cy)>();

            // Start DFS od losowej komórki
            int startCX = rng.Next(0, cellsX);
            int startCY = rng.Next(0, cellsY);
            visited[startCX, startCY] = true;
            walkable[startCX * 2 + 1, startCY * 2 + 1] = true;
            stack.Push((startCX, startCY));

            // Kierunki: góra, dół, lewo, prawo (w przestrzeni komórek)
            int[] dx = { 0, 0, -1, 1 };
            int[] dy = { 1, -1, 0, 0 };

            // Krok 3: DFS Recursive Backtracker
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Peek();

                // Znajdź nieodwiedzonych sąsiadów
                var unvisited = new List<int>();
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dx[d];
                    int ny = cy + dy[d];
                    if (nx >= 0 && nx < cellsX && ny >= 0 && ny < cellsY && !visited[nx, ny])
                        unvisited.Add(d);
                }

                if (unvisited.Count > 0)
                {
                    // Losowy nieodwiedzony sąsiad
                    int dir = unvisited[rng.Next(unvisited.Count)];
                    int nx = cx + dx[dir];
                    int ny = cy + dy[dir];

                    // Wyrzeźb ścianę między komórkami
                    int wallX = cx * 2 + 1 + dx[dir];
                    int wallY = cy * 2 + 1 + dy[dir];
                    walkable[wallX, wallY] = true;

                    // Oznacz nową komórkę jako korytarz
                    walkable[nx * 2 + 1, ny * 2 + 1] = true;
                    visited[nx, ny] = true;
                    stack.Push((nx, ny));
                }
                else
                {
                    stack.Pop(); // Backtrack
                }
            }

            // Krok 4: Usuń dodatkowe ściany aby kontrolować zagęszczenie
            // obstacleDensity = 0.5 → labirynt typowy, 0.1 → dużo otwartej przestrzeni
            float wallRemovalRatio = 1.0f - obstacleDensity * 2.0f;
            if (wallRemovalRatio > 0)
            {
                var walls = new List<(int x, int y)>();
                for (int x = 1; x < width - 1; x++)
                    for (int y = 1; y < height - 1; y++)
                        if (!walkable[x, y])
                            walls.Add((x, y));

                // Fisher-Yates shuffle
                for (int i = walls.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(0, i + 1);
                    var tmp = walls[i];
                    walls[i] = walls[j];
                    walls[j] = tmp;
                }

                int toRemove = (int)(walls.Count * wallRemovalRatio);
                for (int i = 0; i < toRemove && i < walls.Count; i++)
                {
                    walkable[walls[i].x, walls[i].y] = true;
                }
            }

            return new GridMap(walkable);
        }
    }
}
