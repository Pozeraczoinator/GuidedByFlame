using System.Collections.Generic;
using UnityEngine;

namespace Pathfinding.Core
{
    /// <summary>
    /// Interfejs definiujący standardowy sposób wywoływania algorytmu wyszukiwania ścieżki.
    /// Wzorzec 'Strategy', pozwala na łatwą wymianę algorytmów podczas testów (benchmarking).
    /// </summary>
    public interface IPathfindingAlgorithm
    {
        /// <summary>
        /// Metoda uruchamiająca wyszukiwanie ścieżki na podstawie dostarczonej mapy i współrzędnych.
        /// Zwraca obiekt PathfindingResult, który zawiera metryki wydajnościowe i znalezioną trasę.
        /// </summary>
        /// <param name="grid">Siatka (mapa) na której operujemy</param>
        /// <param name="startPos">Siatkowe współrzędne początkowe</param>
        /// <param name="targetPos">Siatkowe współrzędne docelowe</param>
        /// <returns>Wynik operacji wraz z wszystkimi metrykami do benchmarku</returns>
        PathfindingResult FindPath(GridMap grid, Vector2Int startPos, Vector2Int targetPos);
        
        /// <summary>
        /// Nazwa algorytmu używana do generowania odrębnych plików CSV w benchmarku.
        /// </summary>
        string AlgorithmName { get; }
    }
}
