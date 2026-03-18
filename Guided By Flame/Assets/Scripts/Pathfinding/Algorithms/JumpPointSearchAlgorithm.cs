using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Pathfinding.Core;
using System;

namespace Pathfinding.Algorithms
{
    public class JumpPointSearchAlgorithm : IPathfindingAlgorithm
    {
        public string AlgorithmName => "JumpPointSearch";

        private class Node : IHeapItem<Node>
        {
            public Vector2Int Pos { get; }
            public int GCost { get; set; }
            public int HCost { get; set; }
            public Node Parent { get; set; }
            
            private int _heapIndex;

            public int FCost => GCost + HCost;

            public Node(Vector2Int pos)
            {
                Pos = pos;
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

            Node startNode = new Node(startPos);
            Node targetNode = new Node(targetPos);

            MinHeap<Node> openSet = new MinHeap<Node>(grid.Width * grid.Height);
            HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();
            Dictionary<Vector2Int, Node> allNodes = new Dictionary<Vector2Int, Node>();
            
            allNodes[startPos] = startNode;
            openSet.Add(startNode);

            while (openSet.Count > 0)
            {
                Node currentNode = openSet.RemoveFirst();
                closedSet.Add(currentNode.Pos);
                result.ExploredNodes++;
                result.ExploredNodesHistory.Add(currentNode.Pos);

                if (currentNode.Pos == targetPos)
                {
                    result.PathFound = true;
                    RetracePath(startNode, currentNode, result);
                    sw.Stop();
                    result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
                    result.ExecutionTicks = sw.ElapsedTicks;
                    return result;
                }

                IdentifySuccessors(currentNode, startPos, targetPos, grid, allNodes, openSet, closedSet);
            }

            sw.Stop();
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
            result.ExecutionTicks = sw.ElapsedTicks;
            return result;
        }

        private void IdentifySuccessors(Node currentNode, Vector2Int startPos, Vector2Int targetPos, GridMap grid,
            Dictionary<Vector2Int, Node> allNodes, MinHeap<Node> openSet, HashSet<Vector2Int> closedSet)
        {
            List<Vector2Int> neighbors = FindNeighbors(currentNode, grid);

            foreach (var neighborPos in neighbors)
            {
                Vector2Int dir = new Vector2Int(
                    Math.Sign(neighborPos.x - currentNode.Pos.x),
                    Math.Sign(neighborPos.y - currentNode.Pos.y)
                );

                Vector2Int? jumpPoint = Jump(currentNode.Pos, dir, targetPos, grid);

                if (jumpPoint.HasValue)
                {
                    Vector2Int jp = jumpPoint.Value;
                    if (closedSet.Contains(jp)) continue;

                    int moveCost = currentNode.GCost + GetDistance(currentNode.Pos, jp);

                    if (!allNodes.TryGetValue(jp, out Node jpNode))
                    {
                        jpNode = new Node(jp) { GCost = int.MaxValue };
                        allNodes[jp] = jpNode;
                    }

                    bool inOpenSet = openSet.Contains(jpNode);

                    if (moveCost < jpNode.GCost || !inOpenSet)
                    {
                        jpNode.GCost = moveCost;
                        jpNode.HCost = GetDistance(jp, targetPos);
                        jpNode.Parent = currentNode;

                        if (!inOpenSet)
                            openSet.Add(jpNode);
                        else
                            openSet.UpdateItem(jpNode);
                    }
                }
            }
        }

        private Vector2Int? Jump(Vector2Int currentPos, Vector2Int dir, Vector2Int targetPos, GridMap grid)
        {
            Vector2Int nextPos = currentPos + dir;

            if (!grid.IsWalkable(nextPos))
                return null;

            if (nextPos == targetPos)
                return nextPos;

            int x = nextPos.x;
            int y = nextPos.y;
            int dx = dir.x;
            int dy = dir.y;

            // Sprawdzanie dla ruchu diagonalnego
            if (dx != 0 && dy != 0)
            {
                if ((grid.IsWalkable(x - dx, y + dy) && !grid.IsWalkable(x - dx, y)) ||
                    (grid.IsWalkable(x + dx, y - dy) && !grid.IsWalkable(x, y - dy)))
                {
                    return nextPos;
                }

                if (Jump(nextPos, new Vector2Int(dx, 0), targetPos, grid).HasValue ||
                    Jump(nextPos, new Vector2Int(0, dy), targetPos, grid).HasValue)
                {
                    return nextPos;
                }
            }
            else // Ruch ortogonalny
            {
                if (dx != 0) // Poziomo
                {
                    if ((grid.IsWalkable(x + dx, y + 1) && !grid.IsWalkable(x, y + 1)) ||
                        (grid.IsWalkable(x + dx, y - 1) && !grid.IsWalkable(x, y - 1)))
                    {
                        return nextPos;
                    }
                }
                else // Pionowo
                {
                    if ((grid.IsWalkable(x + 1, y + dy) && !grid.IsWalkable(x + 1, y)) ||
                        (grid.IsWalkable(x - 1, y + dy) && !grid.IsWalkable(x - 1, y)))
                    {
                        return nextPos;
                    }
                }
            }

            // Unikaj ścinania rogów przy diagonali
            if (dx != 0 && dy != 0)
            {
                if (!grid.IsWalkable(x - dx, y) || !grid.IsWalkable(x, y - dy))
                    return null;
            }

            return Jump(nextPos, dir, targetPos, grid);
        }

        private List<Vector2Int> FindNeighbors(Node node, GridMap grid)
        {
            List<Vector2Int> neighbors = new List<Vector2Int>();
            Node parent = node.Parent;

            if (parent != null)
            {
                int x = node.Pos.x;
                int y = node.Pos.y;
                int dx = Math.Sign(x - parent.Pos.x);
                int dy = Math.Sign(y - parent.Pos.y);

                if (dx != 0 && dy != 0)
                {
                    // Zapobiegaj ścinaniu rogu
                    if (grid.IsWalkable(x, y + dy)) neighbors.Add(new Vector2Int(x, y + dy));
                    if (grid.IsWalkable(x + dx, y)) neighbors.Add(new Vector2Int(x + dx, y));
                    if (grid.IsWalkable(x, y + dy) || grid.IsWalkable(x + dx, y))
                    {
                        if (grid.IsWalkable(x + dx, y + dy))
                            neighbors.Add(new Vector2Int(x + dx, y + dy));
                    }
                    if (!grid.IsWalkable(x - dx, y) && grid.IsWalkable(x, y + dy))
                        neighbors.Add(new Vector2Int(x - dx, y + dy));
                    if (!grid.IsWalkable(x, y - dy) && grid.IsWalkable(x + dx, y))
                        neighbors.Add(new Vector2Int(x + dx, y - dy));
                }
                else
                {
                    if (dx == 0)
                    {
                        if (grid.IsWalkable(x, y + dy))
                        {
                            neighbors.Add(new Vector2Int(x, y + dy));
                            if (!grid.IsWalkable(x + 1, y)) neighbors.Add(new Vector2Int(x + 1, y + dy));
                            if (!grid.IsWalkable(x - 1, y)) neighbors.Add(new Vector2Int(x - 1, y + dy));
                        }
                    }
                    else
                    {
                        if (grid.IsWalkable(x + dx, y))
                        {
                            neighbors.Add(new Vector2Int(x + dx, y));
                            if (!grid.IsWalkable(x, y + 1)) neighbors.Add(new Vector2Int(x + dx, y + 1));
                            if (!grid.IsWalkable(x, y - 1)) neighbors.Add(new Vector2Int(x + dx, y - 1));
                        }
                    }
                }
            }
            else
            {
                // Root node, dodaj wszystkich poprawnych sąsiadów
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        if (x == 0 && y == 0) continue;
                        if (grid.IsWalkable(node.Pos.x + x, node.Pos.y + y))
                        {
                            // Sprawdzanie ścinania rogów
                            if (x != 0 && y != 0)
                            {
                                if (!grid.IsWalkable(node.Pos.x + x, node.Pos.y) || 
                                    !grid.IsWalkable(node.Pos.x, node.Pos.y + y))
                                    continue;
                            }
                            neighbors.Add(new Vector2Int(node.Pos.x + x, node.Pos.y + y));
                        }
                    }
                }
            }
            return neighbors;
        }

        private void RetracePath(Node startNode, Node endNode, Pathfinding.Core.PathfindingResult result)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            Node currentNode = endNode;

            while (currentNode != startNode)
            {
                path.Add(currentNode.Pos);
                currentNode = currentNode.Parent;
            }
            
            path.Reverse();
            
            // JPS zwraca tzw. jump points. Musimy zrekonstruować pełną trasę co kafel.
            List<Vector2Int> fullPath = new List<Vector2Int>();
            Vector2Int current = startNode.Pos;
            
            float length = 0;

            foreach (var point in path)
            {
                Vector2Int dir = new Vector2Int(
                    Math.Sign(point.x - current.x),
                    Math.Sign(point.y - current.y)
                );

                while (current != point)
                {
                    current += dir;
                    fullPath.Add(current);
                    
                    if (dir.x != 0 && dir.y != 0)
                        length += 1.414f;
                    else
                        length += 1.0f;
                }
            }

            result.Path = fullPath;
            result.PathLength = length;
        }

        private int GetDistance(Vector2Int posA, Vector2Int posB)
        {
            int dstX = Math.Abs(posA.x - posB.x);
            int dstY = Math.Abs(posA.y - posB.y);

            if (dstX > dstY)
                return 14 * dstY + 10 * (dstX - dstY);
            return 14 * dstX + 10 * (dstY - dstX);
        }
    }
}
