using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Pathfinding.Core;
using System;

namespace Pathfinding.Algorithms
{
    /// <summary>
    /// Własny algorytm zachłanny, oparty na ważonym A* (Weighted A*).
    /// Zastosowana waga na heurystykę (np. 50.0) powoduje, że algorytm zachowuje się 
    /// bardzo zachłannie (dąży silnie do celu kompensując zły wybór przez gigantyczną przewagą w węźle bliskim celowi).
    /// Zapewnia jednak odnalezienie drogi dzięki obecności GCost (w przeciwieństwie do czystego GBFS który może wpaść w lokalne minimum).
    /// </summary>
    public class CustomGreedyAlgorithm : IPathfindingAlgorithm
    {
        public string AlgorithmName => "CustomGreedy";

        private class Node : IHeapItem<Node>
        {
            public int X { get; }
            public int Y { get; }
            public int GCost { get; set; }
            public float HCost { get; set; } // Float przez wagę
            public Node Parent { get; set; }
            
            private int _heapIndex;

            public float FCost => GCost + HCost;

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
                return -compare;
            }
        }

        public Pathfinding.Core.PathfindingResult FindPath(GridMap grid, Vector2Int startPos, Vector2Int targetPos)
        {
            var result = new Pathfinding.Core.PathfindingResult();
            Stopwatch sw = Stopwatch.StartNew();

            Node startNode = new Node(startPos.x, startPos.y);
            Node targetNode = new Node(targetPos.x, targetPos.y);

            MinHeap<Node> openSet = new MinHeap<Node>(grid.Width * grid.Height);
            HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();
            Dictionary<Vector2Int, Node> allNodes = new Dictionary<Vector2Int, Node>();
            
            allNodes[startPos] = startNode;
            openSet.Add(startNode);

            float greedyWeight = 50.0f; // Mocny nacisk na heurystykę
            
            while (openSet.Count > 0)
            {
                Node currentNode = openSet.RemoveFirst();
                Vector2Int currentPos = new Vector2Int(currentNode.X, currentNode.Y);
                closedSet.Add(currentPos);
                result.ExploredNodes++;
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

                    // DS3: Uwzględniamy wagę terenu (GetMovementCost).
                    float terrainCost = grid.GetMovementCost(neighbor.X, neighbor.Y);
                    int moveCostToNeighbor = currentNode.GCost + (int)(GetDistance(currentNode, neighbor) * terrainCost);
                    if (currentNode.Parent != null)
                    {
                        Vector2Int currentDir = new Vector2Int(currentNode.X - currentNode.Parent.X, currentNode.Y - currentNode.Parent.Y);
                        Vector2Int newDir = new Vector2Int(neighbor.X - currentNode.X, neighbor.Y - currentNode.Y);
                        if (currentDir != newDir)
                        {
                            moveCostToNeighbor += 2; // Minimalna kara za skręt
                        }
                    }

                    bool inOpenSet = openSet.Contains(neighbor);
                    
                    if (moveCostToNeighbor < neighbor.GCost || !inOpenSet)
                    {
                        neighbor.GCost = moveCostToNeighbor;
                        neighbor.HCost = GetDistance(neighbor, targetNode) * greedyWeight;
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

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;

                    int checkX = node.X + x;
                    int checkY = node.Y + y;

                    if (grid.IsValidCoordinate(checkX, checkY))
                    {
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

        private int GetDistance(Node nodeA, Node nodeB)
        {
            int dstX = Math.Abs(nodeA.X - nodeB.X);
            int dstY = Math.Abs(nodeA.Y - nodeB.Y);

            if (dstX > dstY)
                return 14 * dstY + 10 * (dstX - dstY);
            return 14 * dstX + 10 * (dstY - dstX);
        }
    }
}
