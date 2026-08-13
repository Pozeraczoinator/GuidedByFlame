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
                // Deterministyczny tiebreak: przy równych kosztach rozstrzygaj pozycją.
                // Gwarantuje identyczne wyniki niezależnie od kolejności wstawiania do kopca.
                if (compare == 0)
                {
                    compare = Pos.x.CompareTo(other.Pos.x);
                    if (compare == 0)
                        compare = Pos.y.CompareTo(other.Pos.y);
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
                if (PathfindingRuntimeOptions.RecordExploredNodesHistory)
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

                IdentifySuccessors(currentNode, startPos, targetPos, grid, allNodes, openSet, closedSet, result);
            }

            sw.Stop();
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
            result.ExecutionTicks = sw.ElapsedTicks;
            return result;
        }

        private void IdentifySuccessors(Node currentNode, Vector2Int startPos, Vector2Int targetPos, GridMap grid,
            Dictionary<Vector2Int, Node> allNodes, MinHeap<Node> openSet, HashSet<Vector2Int> closedSet,
            Pathfinding.Core.PathfindingResult result)
        {
            List<Vector2Int> neighbors = FindNeighbors(currentNode, grid);

            foreach (var neighborPos in neighbors)
            {
                Vector2Int dir = new Vector2Int(
                    Math.Sign(neighborPos.x - currentNode.Pos.x),
                    Math.Sign(neighborPos.y - currentNode.Pos.y)
                );

                Vector2Int? jumpPoint = Jump(currentNode.Pos, dir, targetPos, grid, result);

                if (jumpPoint.HasValue)
                {
                    Vector2Int jp = jumpPoint.Value;
                    if (closedSet.Contains(jp)) continue;

                    int moveCost = currentNode.GCost + GetOctagonalDistance(currentNode.Pos, jp);

                    if (!allNodes.TryGetValue(jp, out Node jpNode))
                    {
                        jpNode = new Node(jp) { GCost = int.MaxValue };
                        allNodes[jp] = jpNode;
                    }

                    bool inOpenSet = openSet.Contains(jpNode);

                    if (moveCost < jpNode.GCost || !inOpenSet)
                    {
                        jpNode.GCost = moveCost;
                        jpNode.HCost = GetOctagonalDistance(jp, targetPos);
                        jpNode.Parent = currentNode;

                        if (!inOpenSet)
                            openSet.Add(jpNode);
                        else
                            openSet.UpdateItem(jpNode);
                    }
                }
            }
        }

        private Vector2Int? Jump(Vector2Int currentPos, Vector2Int dir, Vector2Int targetPos, GridMap grid,
            Pathfinding.Core.PathfindingResult result)
        {
            Vector2Int nextPos = currentPos + dir;
            result.JumpScannedCells++;

            if (!CanMove(grid, currentPos, dir))
                return null;

            int x = nextPos.x;
            int y = nextPos.y;
            int dx = dir.x;
            int dy = dir.y;

            // Diagonal move is legal only when both adjacent orthogonal cells are free.
            // This prevents squeezing through a blocked 2x2 corner like: 01 / 10.
            if (dx != 0 && dy != 0)
            {
                if (!grid.IsWalkable(x - dx, y) || !grid.IsWalkable(x, y - dy))
                    return null;
            }

            if (nextPos == targetPos)
                return nextPos;

            // Sprawdzanie dla ruchu diagonalnego
            if (dx != 0 && dy != 0)
            {
                if ((grid.IsWalkable(x - dx, y + dy) && !grid.IsWalkable(x - dx, y)) ||
                    (grid.IsWalkable(x + dx, y - dy) && !grid.IsWalkable(x, y - dy)))
                {
                    return nextPos;
                }

                if (Jump(nextPos, new Vector2Int(dx, 0), targetPos, grid, result).HasValue ||
                    Jump(nextPos, new Vector2Int(0, dy), targetPos, grid, result).HasValue)
                {
                    return nextPos;
                }
            }
            else // Ruch ortogonalny
            {
                if (dx != 0) // Poziomo
                {
                    if (HasNewSideOpening(grid, currentPos, nextPos, new Vector2Int(0, 1)) ||
                        HasNewSideOpening(grid, currentPos, nextPos, new Vector2Int(0, -1)))
                    {
                        return nextPos;
                    }
                }
                else // Pionowo
                {
                    if (HasNewSideOpening(grid, currentPos, nextPos, new Vector2Int(1, 0)) ||
                        HasNewSideOpening(grid, currentPos, nextPos, new Vector2Int(-1, 0)))
                    {
                        return nextPos;
                    }
                }
            }

            return Jump(nextPos, dir, targetPos, grid, result);
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
                    if (grid.IsWalkable(x, y + dy) && grid.IsWalkable(x + dx, y))
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
                        AddStraightNeighbors(neighbors, grid, node.Pos, new Vector2Int(0, dy));
                    }
                    else
                    {
                        AddStraightNeighbors(neighbors, grid, node.Pos, new Vector2Int(dx, 0));
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

        private bool CanMove(GridMap grid, Vector2Int from, Vector2Int dir)
        {
            Vector2Int to = from + dir;

            if (!grid.IsWalkable(to))
                return false;

            if (dir.x != 0 && dir.y != 0)
            {
                return grid.IsWalkable(from.x + dir.x, from.y) &&
                       grid.IsWalkable(from.x, from.y + dir.y);
            }

            return true;
        }

        private bool HasNewSideOpening(GridMap grid, Vector2Int previous, Vector2Int current, Vector2Int sideDir)
        {
            return grid.IsWalkable(current + sideDir) && !grid.IsWalkable(previous + sideDir);
        }

        private void AddStraightNeighbors(List<Vector2Int> neighbors, GridMap grid, Vector2Int pos, Vector2Int forwardDir)
        {
            AddIfCanMove(neighbors, grid, pos, forwardDir);

            if (forwardDir.x != 0)
            {
                AddSideOpeningNeighbors(neighbors, grid, pos, forwardDir, new Vector2Int(0, 1));
                AddSideOpeningNeighbors(neighbors, grid, pos, forwardDir, new Vector2Int(0, -1));
            }
            else
            {
                AddSideOpeningNeighbors(neighbors, grid, pos, forwardDir, new Vector2Int(1, 0));
                AddSideOpeningNeighbors(neighbors, grid, pos, forwardDir, new Vector2Int(-1, 0));
            }
        }

        private void AddSideOpeningNeighbors(List<Vector2Int> neighbors, GridMap grid, Vector2Int pos, Vector2Int forwardDir, Vector2Int sideDir)
        {
            if (!HasNewSideOpening(grid, pos - forwardDir, pos, sideDir))
                return;

            AddIfCanMove(neighbors, grid, pos, sideDir);
            AddIfCanMove(neighbors, grid, pos, forwardDir + sideDir);
        }

        private void AddIfCanMove(List<Vector2Int> neighbors, GridMap grid, Vector2Int pos, Vector2Int dir)
        {
            if (CanMove(grid, pos, dir))
            {
                Vector2Int next = pos + dir;
                if (!neighbors.Contains(next))
                    neighbors.Add(next);
            }
        }

        private void RetracePath(Node startNode, Node endNode, Pathfinding.Core.PathfindingResult result)
        {
            List<Vector2Int> jumpPoints = new List<Vector2Int>();
            Node currentNode = endNode;

            while (currentNode != startNode)
            {
                jumpPoints.Add(currentNode.Pos);
                currentNode = currentNode.Parent;
            }
            
            jumpPoints.Reverse();
            
            // JPS zwraca jump points — rekonstruujemy pełną ścieżkę krok po kroku.
            // Między dwoma jump points poruszamy się: najpierw diagonalnie (min(dx,dy) kroków),
            // potem ortogonalnie (|dx-dy| kroków). To odpowiada formule GetOctagonalDistance (14*diag + 10*orth).
            List<Vector2Int> fullPath = new List<Vector2Int>();
            Vector2Int current = startNode.Pos;
            
            float length = 0;

            foreach (var point in jumpPoints)
            {
                // Interpoluj pełną ścieżkę od current do point
                while (current != point)
                {
                    int dx = point.x - current.x;
                    int dy = point.y - current.y;
                    int stepX = Math.Sign(dx);
                    int stepY = Math.Sign(dy);

                    // Jeśli możemy iść diagonalnie (oba kierunki niezerowe) — idź diagonalnie
                    // Jeśli tylko jeden kierunek — idź ortogonalnie
                    if (stepX != 0 && stepY != 0)
                    {
                        current = new Vector2Int(current.x + stepX, current.y + stepY);
                        length += 1.414f;
                    }
                    else
                    {
                        current = new Vector2Int(current.x + stepX, current.y + stepY);
                        length += 1.0f;
                    }
                    
                    fullPath.Add(current);
                }
            }

            result.Path = fullPath;
            result.PathLength = length;
        }

        private int GetOctagonalDistance(Vector2Int posA, Vector2Int posB)
        {
            int dstX = Math.Abs(posA.x - posB.x);
            int dstY = Math.Abs(posA.y - posB.y);

            if (dstX > dstY)
                return 14 * dstY + 10 * (dstX - dstY);
            return 14 * dstX + 10 * (dstY - dstX);
        }
    }
}
