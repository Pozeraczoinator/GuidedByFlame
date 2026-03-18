using System;
using UnityEngine;

namespace Pathfinding.Core
{
    /// <summary>
    /// Reprezentuje dwuwymiarową siatkę (grid) wykorzystywaną przez algorytmy pathfindingu.
    /// Zawiera informacje o przeszkodach (zablokowanych polach). Oddzielona od MonoBehaviour.
    /// </summary>
    public class GridMap
    {
        private readonly bool[,] _isWalkable;
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
    }
}
