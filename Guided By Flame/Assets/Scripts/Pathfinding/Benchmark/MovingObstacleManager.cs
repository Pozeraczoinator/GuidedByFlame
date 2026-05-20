using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding.Core;

namespace Pathfinding.Benchmark
{
    /// <summary>
    /// Ruchoma przeszkoda poruszająca się losowym spacerem (DS1).
    /// Trasa: RandomWalk 1-3 kroków w 8 kierunkach, ping-pong.
    /// Przeszkoda zajmuje 1 pole i BLOKUJE je (SetWalkable = false).
    /// </summary>
    public class MovingObstacle
    {
        public List<Vector2Int> PatrolRoute { get; private set; }
        public int CurrentWaypointIndex { get; private set; }
        public Vector2Int CurrentPosition => PatrolRoute[CurrentWaypointIndex];

        /// <summary>
        /// Zapamiętuje oryginalny stan walkable pól na trasie patrol.
        /// Klucz: pozycja, Wartość: czy pole było walkable PRZED umieszczeniem przeszkody.
        /// Zapobiega "naprawianiu" oryginalnych ścian na walkable.
        /// </summary>
        private Dictionary<Vector2Int, bool> _originalWalkableState;

        public MovingObstacle(List<Vector2Int> patrolRoute, int startIndex = 0)
        {
            if (patrolRoute == null || patrolRoute.Count < 2)
                throw new ArgumentException("Trasa patrol musi mieć min. 2 waypoint.");
            PatrolRoute = patrolRoute;
            CurrentWaypointIndex = startIndex % patrolRoute.Count;
            _originalWalkableState = new Dictionary<Vector2Int, bool>();
        }

        /// <summary>
        /// Zapamiętuje oryginalny stan mapy dla pól na trasie i umieszcza przeszkodę.
        /// </summary>
        public void PlaceOnGrid(GridMap grid)
        {
            // Zapamiętaj stan wszystkich pól na trasie
            foreach (var pos in PatrolRoute)
            {
                if (!_originalWalkableState.ContainsKey(pos))
                    _originalWalkableState[pos] = grid.IsWalkable(pos);
            }

            // Zablokuj aktualną pozycję
            grid.SetWalkable(CurrentPosition, false);
        }

        /// <summary>
        /// Przesuwa przeszkodę o jeden krok.
        /// Stara pozycja → przywrócony ORYGINALNY stan (nie zawsze walkable!).
        /// Nowa pozycja → zablokowana (false).
        /// </summary>
        public void Step(GridMap grid)
        {
            Vector2Int oldPos = CurrentPosition;

            // Przywróć ORYGINALNY stan starej pozycji (nie ślepo ustawiaj na true!)
            if (_originalWalkableState.TryGetValue(oldPos, out bool wasWalkable))
                grid.SetWalkable(oldPos, wasWalkable);
            else
                grid.SetWalkable(oldPos, true);

            // Przesuń
            CurrentWaypointIndex = (CurrentWaypointIndex + 1) % PatrolRoute.Count;

            // Zablokuj nową pozycję
            grid.SetWalkable(CurrentPosition, false);
        }

        public Vector2Int PredictPosition(int stepsAhead)
        {
            int futureIndex = (CurrentWaypointIndex + stepsAhead) % PatrolRoute.Count;
            return PatrolRoute[futureIndex];
        }

        /// <summary>
        /// Usuwa przeszkodę z mapy — przywraca oryginalny stan.
        /// </summary>
        public void RemoveFromGrid(GridMap grid)
        {
            if (_originalWalkableState.TryGetValue(CurrentPosition, out bool wasWalkable))
                grid.SetWalkable(CurrentPosition, wasWalkable);
            else
                grid.SetWalkable(CurrentPosition, true);
        }
    }

    /// <summary>
    /// Manager scenariusza DS1 — ruchome przeszkody (RandomWalk 1-3 kroków).
    /// 
    /// Każda przeszkoda robi losowy spacer po 8 kierunkach, 1-3 pola,
    /// a potem wraca tą samą drogą (ping-pong). Ruch deterministyczny (seed).
    /// 
    /// WAŻNE: Przeszkoda BLOKUJE pole na GridMap (SetWalkable = false).
    /// Algorytm pathfindingu MUSI widzieć ją jako ścianę.
    /// </summary>
    public class MovingObstacleManager
    {
        private readonly System.Random _rng;
        private readonly List<MovingObstacle> _obstacles;

        public IReadOnlyList<MovingObstacle> Obstacles => _obstacles;

        public MovingObstacleManager(int seed = 42)
        {
            _rng = new System.Random(seed);
            _obstacles = new List<MovingObstacle>();
        }

        /// <summary>
        /// Generuje K ruchomych przeszkód z trasą RandomWalk (1-3 kroków).
        /// </summary>
        public void GenerateObstacles(GridMap grid, int count, Vector2Int start,
            Vector2Int target, int patrolLength = 6)
        {
            _obstacles.Clear();
            int attempts = 0;
            int maxAttempts = count * 50;
            var occupiedPatrolCells = new HashSet<Vector2Int>();

            // Clampuj patrol length do 1-3
            patrolLength = Mathf.Clamp(patrolLength, 1, 3);

            while (_obstacles.Count < count && attempts < maxAttempts)
            {
                attempts++;
                var route = BuildRandomWalkRoute(grid, start, target, patrolLength);
                if (route != null && route.Count >= 2 && IsRouteFree(route, occupiedPatrolCells))
                {
                    var obstacle = new MovingObstacle(route);
                    obstacle.PlaceOnGrid(grid);
                    _obstacles.Add(obstacle);

                    foreach (var pos in route)
                        occupiedPatrolCells.Add(pos);
                }
            }

            // Weryfikacja: upewnij się, że wszystkie przeszkody BLOKUJĄ swoje pola
            VerifyObstaclePositions(grid);

            Debug.Log($"[DS1] Wygenerowano {_obstacles.Count}/{count} ruchomych przeszkód " +
                      $"(patrol: {patrolLength} kroków, seed: próby={attempts})");
        }

        /// <summary>
        /// Przesuwa wszystkie przeszkody o jeden krok.
        /// Zwraca listę par (staraPozycja, nowaPozycja) do płynnej wizualizacji.
        /// </summary>
        public List<(Vector2Int oldPos, Vector2Int newPos)> StepAll(GridMap grid)
        {
            var moves = new List<(Vector2Int oldPos, Vector2Int newPos)>(_obstacles.Count);
            foreach (var obs in _obstacles)
            {
                Vector2Int oldPos = obs.CurrentPosition;
                obs.Step(grid);
                moves.Add((oldPos, obs.CurrentPosition));
            }

            // Weryfikacja po kroku
            VerifyObstaclePositions(grid);

            return moves;
        }

        /// <summary>
        /// Weryfikuje, że WSZYSTKIE aktualne pozycje przeszkód są zablokowane na gridzie.
        /// Naprawia wszelkie niespójności (np. gdy dwie przeszkody się minęły).
        /// </summary>
        public void VerifyObstaclePositions(GridMap grid)
        {
            foreach (var obs in _obstacles)
            {
                if (grid.IsWalkable(obs.CurrentPosition))
                {
                    // BUG FIX: pole powinno być zablokowane ale nie jest!
                    grid.SetWalkable(obs.CurrentPosition, false);
                }
            }
        }

        /// <summary>
        /// Sprawdza czy którakolwiek przeszkoda blokuje podaną ścieżkę.
        /// </summary>
        public bool IsPathBlocked(List<Vector2Int> path)
        {
            if (path == null) return false;
            var obstaclePositions = new HashSet<Vector2Int>();
            foreach (var obs in _obstacles)
                obstaclePositions.Add(obs.CurrentPosition);

            foreach (var point in path)
                if (obstaclePositions.Contains(point))
                    return true;

            return false;
        }

        /// <summary>
        /// Sprawdza czy przeszkody zablokują ścieżkę w ciągu najbliższych stepsAhead kroków.
        /// </summary>
        public bool WillPathBeBlocked(List<Vector2Int> path, int stepsAhead)
        {
            if (path == null) return false;
            var pathSet = new HashSet<Vector2Int>(path);

            foreach (var obs in _obstacles)
            {
                for (int s = 0; s <= stepsAhead; s++)
                {
                    if (pathSet.Contains(obs.PredictPosition(s)))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Usuwa wszystkie przeszkody z mapy (przywraca oryginalny stan).
        /// </summary>
        public void RemoveAllFromGrid(GridMap grid)
        {
            foreach (var obs in _obstacles)
                obs.RemoveFromGrid(grid);
        }

        // ─── Generacja trasy: RandomWalk 1-3 kroków ───

        /// <summary>
        /// Buduje trasę RandomWalk: losowy spacer po 8 kierunkach, 1-3 pola.
        /// Trasa = forward path + reverse path (ping-pong).
        /// Deterministyczny z seed RNG.
        /// </summary>
        private List<Vector2Int> BuildRandomWalkRoute(GridMap grid, Vector2Int start,
            Vector2Int target, int length)
        {
            int maxTries = 30;

            // 8 kierunków: pion, poziom, skosy
            int[][] dirs = {
                new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 },
                new[] { 1, 1 }, new[] { 1, -1 }, new[] { -1, 1 }, new[] { -1, -1 }
            };

            for (int t = 0; t < maxTries; t++)
            {
                int originX = _rng.Next(2, grid.Width - 2);
                int originY = _rng.Next(2, grid.Height - 2);

                if (!grid.IsWalkable(originX, originY)) continue;

                var origin = new Vector2Int(originX, originY);
                if (IsNearPoint(origin, start, 2) || IsNearPoint(origin, target, 2))
                    continue;

                // Buduj forward path
                var forward = new List<Vector2Int> { origin };
                var visited = new HashSet<Vector2Int> { origin };
                int cx = originX, cy = originY;
                bool valid = true;

                for (int step = 0; step < length; step++)
                {
                    // Zbierz dostępnych sąsiadów
                    var candidates = new List<int[]>();
                    foreach (var d in dirs)
                    {
                        int nx = cx + d[0], ny = cy + d[1];
                        var np = new Vector2Int(nx, ny);
                        if (grid.IsValidCoordinate(nx, ny) && grid.IsWalkable(nx, ny) &&
                            !visited.Contains(np) &&
                            !IsNearPoint(np, start, 1) && !IsNearPoint(np, target, 1) &&
                            IsMoveAllowed(grid, cx, cy, d[0], d[1]))
                        {
                            candidates.Add(d);
                        }
                    }

                    if (candidates.Count == 0) { valid = false; break; }

                    var chosen = candidates[_rng.Next(candidates.Count)];
                    cx += chosen[0];
                    cy += chosen[1];
                    var newPos = new Vector2Int(cx, cy);
                    forward.Add(newPos);
                    visited.Add(newPos);
                }

                if (!valid || forward.Count < 2) continue;

                // Buduj pełną trasę ping-pong: forward + reverse (bez duplikatów na końcach)
                var route = new List<Vector2Int>(forward);
                for (int i = forward.Count - 2; i >= 1; i--)
                    route.Add(forward[i]);

                return route;
            }
            return null;
        }

        private bool IsRouteFree(List<Vector2Int> route, HashSet<Vector2Int> occupiedPatrolCells)
        {
            foreach (var pos in route)
            {
                if (occupiedPatrolCells.Contains(pos))
                    return false;
            }

            return true;
        }

        private bool IsMoveAllowed(GridMap grid, int x, int y, int dx, int dy)
        {
            if (dx == 0 || dy == 0)
                return true;

            return grid.IsWalkable(x + dx, y) && grid.IsWalkable(x, y + dy);
        }

        private bool IsNearPoint(Vector2Int a, Vector2Int b, int radius)
        {
            return Math.Abs(a.x - b.x) <= radius && Math.Abs(a.y - b.y) <= radius;
        }
    }
}
