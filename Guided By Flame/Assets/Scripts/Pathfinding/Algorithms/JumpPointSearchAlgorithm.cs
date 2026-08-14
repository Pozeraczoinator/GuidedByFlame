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

        private sealed class Node : IHeapItem<Node>
        {
            public Vector2Int Pos { get; }
            public int GCost;
            public int HCost;
            public Node Parent;
            public bool Closed;
            public int SearchId;
            public int HeapIndex { get; set; } = -1;

            public int FCost => GCost + HCost;

            public Node(Vector2Int pos)
            {
                Pos = pos;
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

        private Node[,] _nodes;
        private MinHeap<Node> _openSet;
        private int _width;
        private int _height;
        private int _searchId;
        private readonly Vector2Int[] _neighborBuffer = new Vector2Int[8];
        private readonly List<Node> _jumpPointBuffer = new List<Node>();

        public Pathfinding.Core.PathfindingResult FindPath(GridMap grid, Vector2Int startPos, Vector2Int targetPos)
        {
            EnsureWorkspace(grid.Width, grid.Height);
            BeginSearch();

            var result = new Pathfinding.Core.PathfindingResult();
            Stopwatch sw = Stopwatch.StartNew();

            Node startNode = GetNode(startPos.x, startPos.y);
            startNode.GCost = 0;
            _openSet.Add(startNode);

            while (_openSet.Count > 0)
            {
                Node currentNode = _openSet.RemoveFirst();
                currentNode.Closed = true;
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

                IdentifySuccessors(currentNode, targetPos, grid, result);
            }

            sw.Stop();
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
            result.ExecutionTicks = sw.ElapsedTicks;
            return result;
        }

        private void EnsureWorkspace(int width, int height)
        {
            if (_nodes != null && _width == width && _height == height)
                return;

            _width = width;
            _height = height;
            _nodes = new Node[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                    _nodes[x, y] = new Node(new Vector2Int(x, y));
            }

            _openSet = new MinHeap<Node>(width * height);
            _searchId = 0;
            _jumpPointBuffer.Clear();
        }

        private void BeginSearch()
        {
            _openSet.Clear();
            if (_searchId == int.MaxValue)
            {
                for (int x = 0; x < _width; x++)
                {
                    for (int y = 0; y < _height; y++)
                        _nodes[x, y].SearchId = 0;
                }

                _searchId = 1;
            }
            else
            {
                _searchId++;
            }
        }

        private Node GetNode(int x, int y)
        {
            Node node = _nodes[x, y];
            if (node.SearchId != _searchId)
            {
                node.SearchId = _searchId;
                node.GCost = int.MaxValue;
                node.HCost = 0;
                node.Parent = null;
                node.Closed = false;
                node.HeapIndex = -1;
            }

            return node;
        }

        private void IdentifySuccessors(Node currentNode, Vector2Int targetPos, GridMap grid,
            Pathfinding.Core.PathfindingResult result)
        {
            int neighborCount = FindNeighbors(currentNode, grid, _neighborBuffer);

            for (int i = 0; i < neighborCount; i++)
            {
                Vector2Int neighborPos = _neighborBuffer[i];
                Vector2Int dir = new Vector2Int(
                    Math.Sign(neighborPos.x - currentNode.Pos.x),
                    Math.Sign(neighborPos.y - currentNode.Pos.y)
                );

                Vector2Int? jumpPoint = Jump(currentNode.Pos, dir, targetPos, grid, result);

                if (jumpPoint.HasValue)
                {
                    Vector2Int jp = jumpPoint.Value;
                    Node jpNode = GetNode(jp.x, jp.y);
                    if (jpNode.Closed)
                        continue;

                    int moveCost = currentNode.GCost + GetOctagonalDistance(currentNode.Pos, jp);
                    bool inOpenSet = _openSet.Contains(jpNode);

                    if (moveCost < jpNode.GCost || !inOpenSet)
                    {
                        jpNode.GCost = moveCost;
                        jpNode.HCost = GetOctagonalDistance(jp, targetPos);
                        jpNode.Parent = currentNode;

                        if (!inOpenSet)
                            _openSet.Add(jpNode);
                        else
                            _openSet.UpdateItem(jpNode);
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

        private int FindNeighbors(Node node, GridMap grid, Vector2Int[] neighbors)
        {
            int count = 0;
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
                    if (grid.IsWalkable(x, y + dy))
                        count = AddUnique(neighbors, count, new Vector2Int(x, y + dy));
                    if (grid.IsWalkable(x + dx, y))
                        count = AddUnique(neighbors, count, new Vector2Int(x + dx, y));
                    if (grid.IsWalkable(x, y + dy) && grid.IsWalkable(x + dx, y))
                    {
                        if (grid.IsWalkable(x + dx, y + dy))
                            count = AddUnique(neighbors, count, new Vector2Int(x + dx, y + dy));
                    }
                    if (!grid.IsWalkable(x - dx, y) && grid.IsWalkable(x, y + dy))
                        count = AddUnique(neighbors, count, new Vector2Int(x - dx, y + dy));
                    if (!grid.IsWalkable(x, y - dy) && grid.IsWalkable(x + dx, y))
                        count = AddUnique(neighbors, count, new Vector2Int(x + dx, y - dy));
                }
                else
                {
                    if (dx == 0)
                    {
                        count = AddStraightNeighbors(
                            neighbors, count, grid, node.Pos, new Vector2Int(0, dy));
                    }
                    else
                    {
                        count = AddStraightNeighbors(
                            neighbors, count, grid, node.Pos, new Vector2Int(dx, 0));
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
                            count = AddUnique(
                                neighbors, count,
                                new Vector2Int(node.Pos.x + x, node.Pos.y + y));
                        }
                    }
                }
            }
            return count;
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

        private int AddStraightNeighbors(
            Vector2Int[] neighbors, int count, GridMap grid,
            Vector2Int pos, Vector2Int forwardDir)
        {
            count = AddIfCanMove(neighbors, count, grid, pos, forwardDir);

            if (forwardDir.x != 0)
            {
                count = AddSideOpeningNeighbors(
                    neighbors, count, grid, pos, forwardDir, new Vector2Int(0, 1));
                count = AddSideOpeningNeighbors(
                    neighbors, count, grid, pos, forwardDir, new Vector2Int(0, -1));
            }
            else
            {
                count = AddSideOpeningNeighbors(
                    neighbors, count, grid, pos, forwardDir, new Vector2Int(1, 0));
                count = AddSideOpeningNeighbors(
                    neighbors, count, grid, pos, forwardDir, new Vector2Int(-1, 0));
            }

            return count;
        }

        private int AddSideOpeningNeighbors(
            Vector2Int[] neighbors, int count, GridMap grid,
            Vector2Int pos, Vector2Int forwardDir, Vector2Int sideDir)
        {
            if (!HasNewSideOpening(grid, pos - forwardDir, pos, sideDir))
                return count;

            count = AddIfCanMove(neighbors, count, grid, pos, sideDir);
            return AddIfCanMove(neighbors, count, grid, pos, forwardDir + sideDir);
        }

        private int AddIfCanMove(
            Vector2Int[] neighbors, int count, GridMap grid,
            Vector2Int pos, Vector2Int dir)
        {
            return CanMove(grid, pos, dir)
                ? AddUnique(neighbors, count, pos + dir)
                : count;
        }

        private static int AddUnique(Vector2Int[] neighbors, int count, Vector2Int candidate)
        {
            for (int i = 0; i < count; i++)
            {
                if (neighbors[i] == candidate)
                    return count;
            }

            neighbors[count] = candidate;
            return count + 1;
        }

        private void RetracePath(Node startNode, Node endNode, Pathfinding.Core.PathfindingResult result)
        {
            _jumpPointBuffer.Clear();
            Node currentNode = endNode;
            int pathStepCount = 0;

            while (currentNode != startNode)
            {
                _jumpPointBuffer.Add(currentNode);
                pathStepCount += Math.Max(
                    Math.Abs(currentNode.Pos.x - currentNode.Parent.Pos.x),
                    Math.Abs(currentNode.Pos.y - currentNode.Parent.Pos.y));
                currentNode = currentNode.Parent;
            }
            
            // JPS zwraca jump points — rekonstruujemy pełną ścieżkę krok po kroku.
            // Między dwoma jump points poruszamy się: najpierw diagonalnie (min(dx,dy) kroków),
            // potem ortogonalnie (|dx-dy| kroków). To odpowiada formule GetOctagonalDistance (14*diag + 10*orth).
            List<Vector2Int> fullPath = result.Path;
            if (fullPath.Capacity < pathStepCount)
                fullPath.Capacity = pathStepCount;
            Vector2Int current = startNode.Pos;
            
            float length = 0;

            for (int i = _jumpPointBuffer.Count - 1; i >= 0; i--)
            {
                Vector2Int point = _jumpPointBuffer[i].Pos;
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
