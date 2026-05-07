using System.Collections.Generic;
using UnityEngine;
using Pathfinding.Core;

namespace Pathfinding.MapGenerators
{
    /// <summary>
    /// Generator map typu "Rooms & Corridors" algorytmem BSP (Binary Space Partitioning).
    /// 
    /// Produkuje pokoje o losowych rozmiarach połączone wąskimi korytarzami (1-2 tile).
    /// Korytarze tworzą naturalne wąskie gardła (chokepoints) — kluczowe dla testowania
    /// zachowania algorytmów w scenariuszach z bottleneckami.
    /// 
    /// Kluczowe cechy dla benchmarku:
    /// - Chokepoints wymuszają specyficzne ścieżki — mniejsza rola heurystyki
    /// - JPS: umiarkowana wydajność (skoki w pokojach, ale nie przez korytarze)
    /// - Dijkstra: eksploruje całe pokoje zanim znajdzie korytarz wyjściowy
    /// - GBFS: podatny na pułapki (heurystyka wskazuje przez ścianę)
    /// 
    /// Algorytm BSP:
    /// 1. Rekurencyjnie dziel prostokąt na mniejsze partycje (pion/poziom)
    /// 2. W każdym liściu BSP umieść pokój o losowym rozmiarze
    /// 3. Połącz sibling-pokoje L-kształtnym korytarzem
    /// </summary>
    public class RoomCorridorGenerator : IMapGenerator
    {
        public string TopologyName => "RoomCorridor";

        /// <summary>Minimalny rozmiar partycji BSP (poniżej nie dzielimy).</summary>
        private readonly int _minPartitionSize;
        /// <summary>Szerokość korytarzy łączących pokoje.</summary>
        private readonly int _corridorWidth;

        public RoomCorridorGenerator(int minPartitionSize = 5, int corridorWidth = 1)
        {
            _minPartitionSize = minPartitionSize;
            _corridorWidth = corridorWidth;
        }

        private class BSPNode
        {
            public int X, Y, W, H;
            public BSPNode Left, Right;
            public (int x, int y, int w, int h)? Room;
        }

        public GridMap Generate(int width, int height, float obstacleDensity, int seed)
        {
            var rng = new System.Random(seed);
            bool[,] walkable = new bool[width, height];

            // Wszystko jako ściana
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    walkable[x, y] = false;

            // BSP
            var root = new BSPNode { X = 0, Y = 0, W = width, H = height };

            // Głębokość podziału zależy od zagęszczenia — więcej podziałów = mniejsze pokoje = więcej ścian
            int maxDepth = Mathf.Clamp((int)(3 + obstacleDensity * 4), 2, 6);
            SplitBSP(root, rng, 0, maxDepth);

            // Wygeneruj pokoje w liściach
            GenerateRooms(root, rng, obstacleDensity);

            // Wyrzeźb pokoje na mapie
            CarveRooms(root, walkable);

            // Połącz pokoje korytarzami
            ConnectRooms(root, walkable, rng);

            return new GridMap(walkable);
        }

        private void SplitBSP(BSPNode node, System.Random rng, int depth, int maxDepth)
        {
            if (depth >= maxDepth) return;
            if (node.W < _minPartitionSize * 2 && node.H < _minPartitionSize * 2) return;

            bool splitHorizontal;
            if (node.W < _minPartitionSize * 2) splitHorizontal = true;
            else if (node.H < _minPartitionSize * 2) splitHorizontal = false;
            else splitHorizontal = rng.NextDouble() > 0.5;

            if (splitHorizontal && node.H >= _minPartitionSize * 2)
            {
                int split = rng.Next(_minPartitionSize, node.H - _minPartitionSize + 1);
                node.Left = new BSPNode { X = node.X, Y = node.Y, W = node.W, H = split };
                node.Right = new BSPNode { X = node.X, Y = node.Y + split, W = node.W, H = node.H - split };
            }
            else if (!splitHorizontal && node.W >= _minPartitionSize * 2)
            {
                int split = rng.Next(_minPartitionSize, node.W - _minPartitionSize + 1);
                node.Left = new BSPNode { X = node.X, Y = node.Y, W = split, H = node.H };
                node.Right = new BSPNode { X = node.X + split, Y = node.Y, W = node.W - split, H = node.H };
            }
            else return;

            SplitBSP(node.Left, rng, depth + 1, maxDepth);
            SplitBSP(node.Right, rng, depth + 1, maxDepth);
        }

        private void GenerateRooms(BSPNode node, System.Random rng, float density)
        {
            if (node.Left == null && node.Right == null)
            {
                // Liść — generuj pokój (mniejszy niż partycja, z marginesem)
                int margin = 1;
                int roomW = rng.Next(Mathf.Max(2, node.W / 2), Mathf.Max(3, node.W - margin));
                int roomH = rng.Next(Mathf.Max(2, node.H / 2), Mathf.Max(3, node.H - margin));
                int roomX = node.X + rng.Next(0, Mathf.Max(1, node.W - roomW));
                int roomY = node.Y + rng.Next(0, Mathf.Max(1, node.H - roomH));
                node.Room = (roomX, roomY, roomW, roomH);
                return;
            }
            if (node.Left != null) GenerateRooms(node.Left, rng, density);
            if (node.Right != null) GenerateRooms(node.Right, rng, density);
        }

        private void CarveRooms(BSPNode node, bool[,] walkable)
        {
            if (node.Room.HasValue)
            {
                var r = node.Room.Value;
                for (int x = r.x; x < r.x + r.w && x < walkable.GetLength(0); x++)
                    for (int y = r.y; y < r.y + r.h && y < walkable.GetLength(1); y++)
                        walkable[x, y] = true;
                return;
            }
            if (node.Left != null) CarveRooms(node.Left, walkable);
            if (node.Right != null) CarveRooms(node.Right, walkable);
        }

        private void ConnectRooms(BSPNode node, bool[,] walkable, System.Random rng)
        {
            if (node.Left == null || node.Right == null) return;

            ConnectRooms(node.Left, walkable, rng);
            ConnectRooms(node.Right, walkable, rng);

            // Znajdź centra pokojów obu poddrzew
            var centerA = GetRoomCenter(node.Left);
            var centerB = GetRoomCenter(node.Right);
            if (!centerA.HasValue || !centerB.HasValue) return;

            // Korytarz L-kształtny
            CarveCorridor(walkable, centerA.Value, centerB.Value, rng);
        }

        private Vector2Int? GetRoomCenter(BSPNode node)
        {
            if (node.Room.HasValue)
            {
                var r = node.Room.Value;
                return new Vector2Int(r.x + r.w / 2, r.y + r.h / 2);
            }
            // Szukaj w poddrzewach
            var left = node.Left != null ? GetRoomCenter(node.Left) : null;
            if (left.HasValue) return left;
            return node.Right != null ? GetRoomCenter(node.Right) : null;
        }

        private void CarveCorridor(bool[,] walkable, Vector2Int a, Vector2Int b, System.Random rng)
        {
            int w = walkable.GetLength(0);
            int h = walkable.GetLength(1);

            // Losowo: najpierw poziomo potem pionowo, lub odwrotnie
            if (rng.NextDouble() > 0.5)
            {
                CarveHorizontal(walkable, a.x, b.x, a.y, w, h);
                CarveVertical(walkable, a.y, b.y, b.x, w, h);
            }
            else
            {
                CarveVertical(walkable, a.y, b.y, a.x, w, h);
                CarveHorizontal(walkable, a.x, b.x, b.y, w, h);
            }
        }

        private void CarveHorizontal(bool[,] walkable, int x1, int x2, int y, int w, int h)
        {
            int start = Mathf.Min(x1, x2);
            int end = Mathf.Max(x1, x2);
            for (int x = start; x <= end; x++)
            {
                for (int dy = 0; dy < _corridorWidth; dy++)
                {
                    int cy = y + dy;
                    if (x >= 0 && x < w && cy >= 0 && cy < h)
                        walkable[x, cy] = true;
                }
            }
        }

        private void CarveVertical(bool[,] walkable, int y1, int y2, int x, int w, int h)
        {
            int start = Mathf.Min(y1, y2);
            int end = Mathf.Max(y1, y2);
            for (int y = start; y <= end; y++)
            {
                for (int dx = 0; dx < _corridorWidth; dx++)
                {
                    int cx = x + dx;
                    if (cx >= 0 && cx < w && y >= 0 && y < h)
                        walkable[cx, y] = true;
                }
            }
        }
    }
}
