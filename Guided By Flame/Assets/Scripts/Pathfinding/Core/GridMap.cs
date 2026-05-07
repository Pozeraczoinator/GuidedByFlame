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
            return new GridMap(copy);
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
