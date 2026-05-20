using System.IO;
using System.Text;
using Pathfinding.Core;
using UnityEngine;

namespace Pathfinding.MapGenerators
{
    /// <summary>
    /// Eksportuje GridMap do pliku .txt w formacie kompatybilnym z PathfindingVisualizer.
    /// Format: '0' = walkable, '1' = przeszkoda. Każdy wiersz = jeden rząd Y mapy.
    /// Odczyt od dołu do góry (zgodny z LoadGridMap w PathfindingVisualizer).
    /// 
    /// Użycie:
    ///   MapExporter.ExportToFile(gridMap, "GeneratedMaps/maze_32x20_d20_s42.txt");
    ///   string text = MapExporter.ExportToString(gridMap);
    /// </summary>
    public static class MapExporter
    {
        /// <summary>
        /// Eksportuje mapę do pliku .txt. Tworzy katalog jeśli nie istnieje.
        /// Format zgodny z PathfindingVisualizer.LoadGridMap() — odczyt od dołu do góry.
        /// </summary>
        /// <param name="grid">Mapa do eksportu</param>
        /// <param name="filePath">Ścieżka pliku wyjściowego (względna lub bezwzględna)</param>
        public static void ExportToFile(GridMap grid, string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string content = ExportToString(grid);
            File.WriteAllText(filePath, content);
            Debug.Log($"[MapExporter] Wyeksportowano mapę {grid.Width}x{grid.Height} do: {filePath}");
        }

        /// <summary>
        /// Konwertuje mapę do stringa w formacie .txt.
        /// Y odwrócony — wiersz 0 w pliku = górna krawędź = najwyższy Y.
        /// </summary>
        public static string ExportToString(GridMap grid)
        {
            var sb = new StringBuilder();

            // Zapisz od góry do dołu (Y malejąco) — zgodnie z konwencją LoadGridMap
            for (int y = grid.Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    sb.Append(grid.IsWalkable(x, y) ? '0' : '1');
                }
                if (y > 0) sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generuje standaryzowaną nazwę pliku na podstawie parametrów mapy.
        /// Format: {topology}_{W}x{H}_d{density}_s{seed}.txt
        /// Przykład: "Maze_32x20_d20_s42.txt"
        /// </summary>
        public static string GenerateFileName(string topology, int width, int height, 
            float density, int seed)
        {
            int densityPercent = (int)(density * 100);
            return $"{topology}_{width}x{height}_d{densityPercent}_s{seed}.txt";
        }
    }
}
