using System.Collections.Generic;
using UnityEngine;
using System.Linq;

// Klasa przechowująca kompletne wyniki testu dla danego zapytania
public class PathfindingResult
{
    public List<Vector2Int> Path { get; set; }
    public int ExploredNodesCount { get; set; }
    public bool Success { get; set; }
}

public class AStarPathfinder
{
    // Wewnętrzna klasa reprezentująca pojedynczą kratkę (węzeł) na mapie
    private class Node
    {
        public Vector2Int Position { get; set; }
        public int G { get; set; } // Koszt od punktu startowego
        public int H { get; set; } // Koszt heurystyczny (szacowany) do celu
        public int F => G + H;     // Całkowity koszt węzła
        public Node Parent { get; set; } // Referencja do rodzica (aby odtworzyć ścieżkę)

        public Node(Vector2Int pos) { Position = pos; }
    }

    /// <summary>
    /// Główna funkcja wyznaczająca ścieżkę.
    /// obstaclesGrid: tablica 2D, gdzie 'true' oznacza przeszkodę (ścianę), a 'false' wolną drogę.
    /// </summary>
    public PathfindingResult FindPath(Vector2Int start, Vector2Int target, bool[,] obstaclesGrid)
    {
        int width = obstaclesGrid.GetLength(0);
        int height = obstaclesGrid.GetLength(1);
        int exploredCount = 0; // Licznik do badań magisterskich

        List<Node> openList = new List<Node>();
        HashSet<Vector2Int> closedList = new HashSet<Vector2Int>();

        Node startNode = new Node(start);
        openList.Add(startNode);

        while (openList.Count > 0)
        {
            // Znajdź węzeł z najniższym kosztem F.
            // Uwaga do pracy magisterskiej: W bardzo dużych mapach optymalizuje się to strukturą "Min-Heap" (Kopiec).
            Node currentNode = openList.OrderBy(n => n.F).ThenBy(n => n.H).First();
            
            openList.Remove(currentNode);
            closedList.Add(currentNode.Position);
            exploredCount++;

            // Warunek końcowy - dotarliśmy do celu
            if (currentNode.Position == target)
            {
                return new PathfindingResult 
                { 
                    Path = RetracePath(startNode, currentNode), 
                    ExploredNodesCount = exploredCount,
                    Success = true 
                };
            }

            // Sprawdzanie sąsiadów (Góra, Prawo, Dół, Lewo)
            Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

            foreach (Vector2Int dir in directions)
            {
                Vector2Int neighborPos = currentNode.Position + dir;

                // Sprawdzenie czy sąsiad mieści się w granicach mapy
                if (neighborPos.x < 0 || neighborPos.x >= width || neighborPos.y < 0 || neighborPos.y >= height)
                    continue;

                // Sprawdzenie czy to ściana lub czy węzeł był już całkowicie sprawdzony
                if (obstaclesGrid[neighborPos.x, neighborPos.y] || closedList.Contains(neighborPos))
                    continue;

                // Koszt przejścia o jedno pole wynosi 10 (często stosowana konwencja zamiast 1)
                int newCostToNeighbor = currentNode.G + 10; 

                Node neighborNode = openList.FirstOrDefault(n => n.Position == neighborPos);

                // Jeśli sąsiada nie ma na liście otwartej LUB znaleźliśmy do niego krótszą ścieżkę
                if (neighborNode == null || newCostToNeighbor < neighborNode.G)
                {
                    if (neighborNode == null)
                    {
                        neighborNode = new Node(neighborPos);
                        openList.Add(neighborNode);
                    }

                    neighborNode.G = newCostToNeighbor;
                    neighborNode.H = GetManhattanDistance(neighborPos, target);
                    neighborNode.Parent = currentNode;
                }
            }
        }

        // Zwrócenie pustego wyniku, jeśli ścieżka nie istnieje (np. cel jest zamurowany)
        return new PathfindingResult { Path = new List<Vector2Int>(), ExploredNodesCount = exploredCount, Success = false };
    }

    // Funkcja odtwarzająca ścieżkę od tyłu na podstawie zapisanych "rodziców"
    private List<Vector2Int> RetracePath(Node startNode, Node endNode)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Node currentNode = endNode;

        while (currentNode != startNode)
        {
            path.Add(currentNode.Position);
            currentNode = currentNode.Parent;
        }
        path.Reverse(); // Odwracamy, by była od startu do mety
        return path;
    }

    // Heurystyka: Odległość Manhattan (suma różnic na osi X i Y)
    private int GetManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y)) * 10;
    }
}