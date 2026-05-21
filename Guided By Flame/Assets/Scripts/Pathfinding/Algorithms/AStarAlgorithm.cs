using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Pathfinding.Core;
using System;

namespace Pathfinding.Algorithms
{
    public class AStarAlgorithm : IPathfindingAlgorithm
    {
        public string AlgorithmName => "AStar";

        private class Node : IHeapItem<Node>
        {
            public int X { get; }
            public int Y { get; }
            public int GCost { get; set; }
            public int HCost { get; set; }
            public Node Parent { get; set; }
            
            private int _heapIndex;
            
            public int FCost => GCost + HCost;

            public Node(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int HeapIndex
            {
                get => _heapIndex;
                set => _heapIndex = value;
            }

            public int CompareTo(Node other)
            {
                int compare = FCost.CompareTo(other.FCost);
                if (compare == 0)
                {
                    compare = HCost.CompareTo(other.HCost);
                }
                // Deterministyczny tiebreak: przy równych kosztach rozstrzygaj pozycją.
                // Gwarantuje identyczne wyniki niezależnie od kolejności wstawiania do kopca.
                if (compare == 0)
                {
                    int posA = X * 10000 + Y;
                    int posB = other.X * 10000 + other.Y;
                    compare = posA.CompareTo(posB);
                }
                return -compare; // MinHeap requires highest priority to return 1 (zwracamy -1 dla mniejszego kosztu)
            }
        }

        public Pathfinding.Core.PathfindingResult FindPath(GridMap grid, Vector2Int startPos, Vector2Int targetPos)
        {
            var result = new Pathfinding.Core.PathfindingResult();
            
            Stopwatch sw = Stopwatch.StartNew();

            Node startNode = new Node(startPos.x, startPos.y);
            Node targetNode = new Node(targetPos.x, targetPos.y);

            // Maksymalny rozmiar kopca: szerokość * wysokość siatki
            MinHeap<Node> openSet = new MinHeap<Node>(grid.Width * grid.Height);
            HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();
            
            // Do szybkiego sprawdzania zawartości openSet, ewentualnie przechowywania instancji węzłów
            Dictionary<Vector2Int, Node> allNodes = new Dictionary<Vector2Int, Node>();
            
            allNodes[startPos] = startNode;
            openSet.Add(startNode);

            while (openSet.Count > 0)
            {
                Node currentNode = openSet.RemoveFirst();
                Vector2Int currentPos = new Vector2Int(currentNode.X, currentNode.Y);
                closedSet.Add(currentPos);
                result.ExploredNodes++;
                if (PathfindingRuntimeOptions.RecordExploredNodesHistory)
                    result.ExploredNodesHistory.Add(currentPos);

                if (currentPos == targetPos)
                {
                    result.PathFound = true;
                    RetracePath(startNode, currentNode, result);
                    sw.Stop();
                    result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
                    result.ExecutionTicks = sw.ElapsedTicks;
                    return result;
                }

                foreach (Node neighbor in GetNeighbors(grid, currentNode, allNodes))
                {
                    Vector2Int neighborPos = new Vector2Int(neighbor.X, neighbor.Y);
                    
                    if (!grid.IsWalkable(neighborPos) || closedSet.Contains(neighborPos))
                        continue;

                    // Ortogonalnie koszt 10, przekątna 14. Zakładamy grid 8-kierunkowy.
                    // DS3: Uwzględniamy wagę terenu (GetMovementCost) — koszt wejścia na pole sąsiada.
                    // Na mapach bez wag (DS2/Static) GetMovementCost() zwraca 1.0f → brak zmiany.
                    float terrainCost = grid.GetMovementCost(neighbor.X, neighbor.Y);
                    int moveCostToNeighbor = currentNode.GCost + (int)(GetOctagonalDistance(currentNode, neighbor) * terrainCost);
                    
                    bool inOpenSet = openSet.Contains(neighbor); // W MinHeap zaimplementowaliśmy IHeapItem
                    
                    if (moveCostToNeighbor < neighbor.GCost || !inOpenSet)
                    {
                        neighbor.GCost = moveCostToNeighbor;
                        neighbor.HCost = GetOctagonalDistance(neighbor, targetNode);
                        neighbor.Parent = currentNode;

                        if (!inOpenSet)
                            openSet.Add(neighbor);
                        else
                            openSet.UpdateItem(neighbor);
                    }
                }
            }

            sw.Stop();
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
            result.ExecutionTicks = sw.ElapsedTicks;
            return result;
        }

        private void RetracePath(Node startNode, Node endNode, Pathfinding.Core.PathfindingResult result)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            Node currentNode = endNode;
            float length = 0;

            while (currentNode != startNode)
            {
                path.Add(new Vector2Int(currentNode.X, currentNode.Y));
                
                // Obliczamy długość w jednostkach Unity (1 dla prostych, 1.414 dla skosów)
                if (currentNode.X != currentNode.Parent.X && currentNode.Y != currentNode.Parent.Y)
                    length += 1.414f;
                else
                    length += 1.0f;
                    
                currentNode = currentNode.Parent;
            }
            
            path.Reverse();
            result.Path = path;
            result.PathLength = length;
        }

        private List<Node> GetNeighbors(GridMap grid, Node node, Dictionary<Vector2Int, Node> allNodes)
        {
            List<Node> neighbors = new List<Node>();

            // Zakładamy ruchy 8-kierunkowe, jak to w grach 2D bywa
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;

                    int checkX = node.X + x;
                    int checkY = node.Y + y;

                    if (grid.IsValidCoordinate(checkX, checkY))
                    {
                        // Zapobiegaj ścinaniu zakrętów
                        if (x != 0 && y != 0)
                        {
                            if (!grid.IsWalkable(node.X + x, node.Y) || !grid.IsWalkable(node.X, node.Y + y))
                            {
                                continue;
                            }
                        }

                        Vector2Int pos = new Vector2Int(checkX, checkY);
                        if (!allNodes.TryGetValue(pos, out Node neighborNode))
                        {
                            neighborNode = new Node(checkX, checkY) { GCost = int.MaxValue };
                            allNodes[pos] = neighborNode;
                        }
                        neighbors.Add(neighborNode);
                    }
                }
            }
            return neighbors;
        }

        private int GetOctagonalDistance(Node nodeA, Node nodeB)
        {
            int dstX = Math.Abs(nodeA.X - nodeB.X);
            int dstY = Math.Abs(nodeA.Y - nodeB.Y);

            // Odległość oktagonalna (Czebyszewa zmodyfikowana) - 14 diagonalia, 10 ortogonalnie
            if (dstX > dstY)
                return 14 * dstY + 10 * (dstX - dstY);
            return 14 * dstX + 10 * (dstY - dstX);
        }
    }
}
