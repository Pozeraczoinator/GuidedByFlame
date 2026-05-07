using System;
using UnityEngine;

namespace Pathfinding.Core
{
    /// <summary>
    /// Reprezentuje dwuwymiarową siatkę (grid) wykorzystywaną przez algorytmy pathfindingu.
    /// Zawiera informacje o przeszkodach (zablokowanych polach). Oddzielona od MonoBehaviour.
    /// Wspiera dynamiczną modyfikację przeszkód w runtime (SetWalkable).
    /// </summary>
    public class GridMap
    {
        private bool[,] _isWalkable;
        private float[,] _movementCost;
        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>
        /// Inicjuje siatkę o zadanych wymiarach z tablicy 2D, gdzie true oznacza pole po którym można chodzić.
        /// </summary>
        public GridMap(bool[,] walkableMap)
        {
            if (walkableMap == null)
                throw new ArgumentNullException(nameof(walkableMap));
                
            Width = walkableMap.GetLength(0);
            Height = walkableMap.GetLength(1);
            _isWalkable = walkableMap;
            InitMovementCosts();
        }

        /// <summary>
        /// Tworzy pustą siatkę o zadanych wymiarach (domyślnie wszystko walkable).
        /// Użyteczne do generowania map proceduralnych w benchmarkach.
        /// </summary>
        public GridMap(int width, int height, bool defaultWalkable = true)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Wymiary siatki muszą być dodatnie.");

            Width = width;
            Height = height;
            _isWalkable = new bool[width, height];

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    _isWalkable[x, y] = defaultWalkable;

            InitMovementCosts();
        }

        /// <summary>
        /// Inicjalizuje tablicę kosztów ruchu domyślną wartością 1.0f.
        /// </summary>
        private void InitMovementCosts()
        {
            _movementCost = new float[Width, Height];
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    _movementCost[x, y] = 1.0f;
        }

        // ─── Wagi terenu (DS3: Weighted Terrain) ───

        /// <summary>
        /// Zwraca koszt wejścia na pole (x, y). Domyślnie 1.0f.
        /// Wartości > 1.0 oznaczają trudniejszy teren (np. błoto, ogień).
        /// Używane przez algorytmy A*, Dijkstra, CustomGreedy, GBFS w scenariuszu DS3.
        /// JPS pomijany w DS3 (nie wspiera weighted gridów).
        /// </summary>
        public float GetMovementCost(int x, int y)
        {
            if (!IsValidCoordinate(x, y)) return float.MaxValue;
            return _movementCost[x, y];
        }

        public float GetMovementCost(Vector2Int pos)
        {
            return GetMovementCost(pos.x, pos.y);
        }

        /// <summary>
        /// Ustawia koszt wejścia na pole. Złożoność: O(1).
        /// </summary>
        /// <param name="cost">Koszt ruchu (1.0 = normalny, >1.0 = utrudniony)</param>
        public void SetMovementCost(int x, int y, float cost)
        {
            if (!IsValidCoordinate(x, y)) return;
            _movementCost[x, y] = cost;
        }

        public void SetMovementCost(Vector2Int pos, float cost)
        {
            SetMovementCost(pos.x, pos.y, cost);
        }

        /// <summary>
        /// Sprawdza, czy koordynaty mieszczą się w obrębie siatki.
        /// </summary>
        public bool IsValidCoordinate(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        /// <summary>
        /// Sprawdza, czy podane koordynaty są na mapie oraz czy nie ma na nich przeszkody.
        /// </summary>
        public bool IsWalkable(int x, int y)
        {
            if (!IsValidCoordinate(x, y))
                return false;
            
            return _isWalkable[x, y];
        }

        public bool IsWalkable(Vector2Int position)
        {
            return IsWalkable(position.x, position.y);
        }

        /// <summary>
        /// Ustawia stan przeszkody na danym polu. Złożoność: O(1).
        /// Używane w scenariuszach dynamicznych do dodawania/usuwania ścian w runtime.
        /// </summary>
        /// <param name="x">Współrzędna X</param>
        /// <param name="y">Współrzędna Y</param>
        /// <param name="walkable">true = pole wolne, false = przeszkoda</param>
        public void SetWalkable(int x, int y, bool walkable)
        {
            if (!IsValidCoordinate(x, y))
                return;
            
            _isWalkable[x, y] = walkable;
        }

        /// <summary>
        /// Ustawia stan przeszkody na danym polu. Overload dla Vector2Int.
        /// </summary>
        public void SetWalkable(Vector2Int position, bool walkable)
        {
            SetWalkable(position.x, position.y, walkable);
        }

        /// <summary>
        /// Tworzy głęboką kopię siatki. Użyteczne do resetowania stanu po modyfikacjach dynamicznych.
        /// Złożoność: O(W×H).
        /// </summary>
        public GridMap Clone()
        {
            bool[,] copy = new bool[Width, Height];
            System.Array.Copy(_isWalkable, copy, _isWalkable.Length);
            var clone = new GridMap(copy);
            System.Array.Copy(_movementCost, clone._movementCost, _movementCost.Length);
            return clone;
        }

        /// <summary>
        /// Zlicza pola walkable. Użyteczne do obliczania zagęszczenia przeszkód.
        /// Złożoność: O(W×H).
        /// </summary>
        public int CountWalkable()
        {
            int count = 0;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (_isWalkable[x, y]) count++;
            return count;
        }
    }
}
