using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Pathfinding.Core;
using CorePathfindingResult = Pathfinding.Core.PathfindingResult;

namespace Pathfinding.Algorithms
{
    public class CustomGreedyAlgorithm : IPathfindingAlgorithm
    {
        public string AlgorithmName => "CustomGreedy";
        private readonly float _greedyWeight;
        private readonly int _turnPenalty;

        public CustomGreedyAlgorithm(float greedyWeight = 50f, int turnPenalty = 2)
        {
            _greedyWeight = greedyWeight;
            _turnPenalty = turnPenalty;
        }

        private sealed class Node : IHeapItem<Node>
        {
            public readonly int X;
            public readonly int Y;
            public int GCost;
            public float HCost;
            public Node Parent;
            public bool Closed;
            public int SearchId;
            public int HeapIndex { get; set; } = -1;
            public float FCost => GCost + HCost;
            public Node(int x, int y) { X = x; Y = y; }

            public int CompareTo(Node other)
            {
                int compare = FCost.CompareTo(other.FCost);
                if (compare == 0) compare = HCost.CompareTo(other.HCost);
                if (compare == 0)
                {
                    compare = X.CompareTo(other.X);
                    if (compare == 0) compare = Y.CompareTo(other.Y);
                }
                return -compare;
            }
        }

        private Node[,] _nodes;
        private MinHeap<Node> _openSet;
        private int _width;
        private int _height;
        private int _searchId;

        public CorePathfindingResult FindPath(
            GridMap grid, Vector2Int startPos, Vector2Int targetPos)
        {
            EnsureWorkspace(grid.Width, grid.Height);
            BeginSearch();
            var result = new CorePathfindingResult();
            Stopwatch sw = Stopwatch.StartNew();
            Node startNode = GetNode(startPos.x, startPos.y);
            startNode.GCost = 0;
            _openSet.Add(startNode);

            while (_openSet.Count > 0)
            {
                Node current = _openSet.RemoveFirst();
                current.Closed = true;
                var currentPos = new Vector2Int(current.X, current.Y);
                result.ExploredNodes++;
                if (PathfindingRuntimeOptions.RecordExploredNodesHistory)
                    result.ExploredNodesHistory.Add(currentPos);

                if (current.X == targetPos.x && current.Y == targetPos.y)
                {
                    result.PathFound = true;
                    RetracePath(startNode, current, result);
                    FinishTiming(sw, result);
                    return result;
                }

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = current.X + dx;
                        int y = current.Y + dy;
                        if (!grid.IsValidCoordinate(x, y)) continue;
                        if (dx != 0 && dy != 0 &&
                            (!grid.IsWalkable(current.X + dx, current.Y) ||
                             !grid.IsWalkable(current.X, current.Y + dy)))
                            continue;
                        if (!grid.IsWalkable(x, y)) continue;

                        Node neighbor = GetNode(x, y);
                        if (neighbor.Closed) continue;
                        bool inOpenSet = _openSet.Contains(neighbor);
                        int stepCost = dx != 0 && dy != 0 ? 14 : 10;
                        int newCost = current.GCost +
                            (int)(stepCost * grid.GetMovementCost(x, y));
                        if (current.Parent != null)
                        {
                            int oldDx = current.X - current.Parent.X;
                            int oldDy = current.Y - current.Parent.Y;
                            if (oldDx != dx || oldDy != dy) newCost += _turnPenalty;
                        }

                        if (newCost < neighbor.GCost || !inOpenSet)
                        {
                            neighbor.GCost = newCost;
                            neighbor.HCost = OctagonalDistance(
                                x, y, targetPos.x, targetPos.y) * _greedyWeight;
                            neighbor.Parent = current;
                            if (!inOpenSet) _openSet.Add(neighbor);
                            else _openSet.UpdateItem(neighbor);
                        }
                    }
                }
            }

            FinishTiming(sw, result);
            return result;
        }

        private void EnsureWorkspace(int width, int height)
        {
            if (_nodes != null && _width == width && _height == height) return;
            _width = width; _height = height;
            _nodes = new Node[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++) _nodes[x, y] = new Node(x, y);
            _openSet = new MinHeap<Node>(width * height);
            _searchId = 0;
        }

        private void BeginSearch()
        {
            _openSet.Clear();
            if (_searchId == int.MaxValue)
            {
                for (int x = 0; x < _width; x++)
                    for (int y = 0; y < _height; y++) _nodes[x, y].SearchId = 0;
                _searchId = 1;
            }
            else _searchId++;
        }

        private Node GetNode(int x, int y)
        {
            Node node = _nodes[x, y];
            if (node.SearchId != _searchId)
            {
                node.SearchId = _searchId;
                node.GCost = int.MaxValue;
                node.HCost = 0f;
                node.Parent = null;
                node.Closed = false;
                node.HeapIndex = -1;
            }
            return node;
        }

        private static void RetracePath(Node start, Node end, CorePathfindingResult result)
        {
            var path = new List<Vector2Int>();
            float length = 0f;
            for (Node node = end; node != start; node = node.Parent)
            {
                path.Add(new Vector2Int(node.X, node.Y));
                length += node.X != node.Parent.X && node.Y != node.Parent.Y ? 1.414f : 1f;
            }
            path.Reverse(); result.Path = path; result.PathLength = length;
        }

        private static int OctagonalDistance(int ax, int ay, int bx, int by)
        {
            int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
            return dx > dy ? 14 * dy + 10 * (dx - dy) : 14 * dx + 10 * (dy - dx);
        }

        private static void FinishTiming(Stopwatch sw, CorePathfindingResult result)
        {
            sw.Stop(); result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
            result.ExecutionTicks = sw.ElapsedTicks;
        }
    }
}
