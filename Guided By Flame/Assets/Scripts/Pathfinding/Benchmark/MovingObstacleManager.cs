using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding.Core;

namespace Pathfinding.Benchmark
{
    /// <summary>
    /// Ruchoma przeszkoda poruszająca się losowym spacerem (DS1).
    /// Trasa: konfigurowalny RandomWalk w 8 kierunkach, ping-pong.
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
        private int _startDelayTicks;

        public MovingObstacle(
            List<Vector2Int> patrolRoute, int startIndex = 0, int startDelayTicks = 0)
        {
            if (patrolRoute == null || patrolRoute.Count < 2)
                throw new ArgumentException("Trasa patrol musi mieć min. 2 waypoint.");
            PatrolRoute = patrolRoute;
            CurrentWaypointIndex = startIndex % patrolRoute.Count;
            _originalWalkableState = new Dictionary<Vector2Int, bool>();
            _startDelayTicks = Mathf.Max(0, startDelayTicks);
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
        public void Step(GridMap grid, Vector2Int? occupiedByAgent = null)
        {
            if (_startDelayTicks > 0)
            {
                _startDelayTicks--;
                return;
            }

            Vector2Int oldPos = CurrentPosition;
            int nextWaypointIndex = (CurrentWaypointIndex + 1) % PatrolRoute.Count;

            // Ruchoma przeszkoda nie może wejść na pole zajmowane przez agenta.
            // W takim ticku pozostaje na swoim aktualnym waypointcie.
            if (occupiedByAgent.HasValue && PatrolRoute[nextWaypointIndex] == occupiedByAgent.Value)
                return;

            // Przywróć ORYGINALNY stan starej pozycji (nie ślepo ustawiaj na true!)
            if (_originalWalkableState.TryGetValue(oldPos, out bool wasWalkable))
                grid.SetWalkable(oldPos, wasWalkable);
            else
                grid.SetWalkable(oldPos, true);

            // Przesuń
            CurrentWaypointIndex = nextWaypointIndex;

            // Zablokuj nową pozycję
            grid.SetWalkable(CurrentPosition, false);
        }

        public Vector2Int PredictPosition(int stepsAhead)
        {
            int movementSteps = Mathf.Max(0, stepsAhead - _startDelayTicks);
            int futureIndex = (CurrentWaypointIndex + movementSteps) % PatrolRoute.Count;
            return PatrolRoute[futureIndex];
        }

        internal MovingObstacle CloneInitial()
        {
            var clone = new MovingObstacle(
                new List<Vector2Int>(PatrolRoute),
                CurrentWaypointIndex,
                _startDelayTicks);
            clone._originalWalkableState =
                new Dictionary<Vector2Int, bool>(_originalWalkableState);
            return clone;
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
    /// Manager scenariusza DS1 — ruchome przeszkody na trasach RandomWalk.
    /// 
    /// Każda przeszkoda robi losowy spacer po 8 kierunkach,
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

        public MovingObstacleManager CloneInitialForGrid(GridMap grid)
        {
            MovingObstacleManager clone = CloneInitial();
            foreach (MovingObstacle obstacle in clone._obstacles)
                obstacle.PlaceOnGrid(grid);
            return clone;
        }

        public MovingObstacleManager CloneInitial()
        {
            var clone = new MovingObstacleManager(0);
            foreach (MovingObstacle obstacle in _obstacles)
                clone._obstacles.Add(obstacle.CloneInitial());
            return clone;
        }

        /// <summary>
        /// Generuje K ruchomych przeszkód z konfigurowalną trasą RandomWalk.
        /// </summary>
        public void GenerateObstacles(GridMap grid, int count, Vector2Int start,
            Vector2Int target, int patrolLength = 6, bool logResult = true,
            IReadOnlyList<Vector2Int> preferredCrossingCells = null)
        {
            _obstacles.Clear();
            int attempts = 0;
            int maxAttempts = count * 50;
            var occupiedPatrolCells = new HashSet<Vector2Int>();

            patrolLength = Mathf.Clamp(patrolLength, 1,
                Mathf.Max(1, Mathf.Min(20, Mathf.Min(grid.Width, grid.Height) - 4)));

            while (_obstacles.Count < count && attempts < maxAttempts)
            {
                attempts++;
                IReadOnlyList<Vector2Int> crossingWindow = BuildCrossingWindow(
                    preferredCrossingCells, _obstacles.Count, count);
                var route = BuildRandomWalkRoute(grid, start, target, patrolLength,
                    crossingWindow);
                if (route != null && route.Count >= 2 && IsRouteFree(route, occupiedPatrolCells))
                {
                    int startIndex = 0;
                    int startDelay = 0;
                    if (preferredCrossingCells != null && preferredCrossingCells.Count > 0)
                    {
                        if (!TryFindScheduledCrossing(
                                route, preferredCrossingCells, _obstacles.Count, count,
                                out int crossingRouteIndex, out int crossingReferenceIndex))
                            continue;

                        startIndex = FindInitialWaypointAwayFromReference(
                            route, preferredCrossingCells);
                        if (startIndex < 0)
                            continue;

                        int stepsToCrossing =
                            (crossingRouteIndex - startIndex + route.Count) % route.Count;
                        if (stepsToCrossing == 0)
                            stepsToCrossing = route.Count;

                        int expectedAgentStep = crossingReferenceIndex + 1;
                        startDelay = Mathf.Max(0, expectedAgentStep - stepsToCrossing);
                    }

                    var obstacle = new MovingObstacle(route, startIndex, startDelay);
                    obstacle.PlaceOnGrid(grid);
                    _obstacles.Add(obstacle);

                    foreach (var pos in route)
                        occupiedPatrolCells.Add(pos);
                }
            }

            // Weryfikacja: upewnij się, że wszystkie przeszkody BLOKUJĄ swoje pola
            VerifyObstaclePositions(grid);

            if (logResult)
            {
                Debug.Log($"[DS1] Wygenerowano {_obstacles.Count}/{count} ruchomych przeszkód " +
                          $"(patrol: {patrolLength} kroków, seed: próby={attempts})");
            }

            if (logResult && _obstacles.Count < count)
            {
                Debug.LogWarning($"[DS1] Nie udało się wygenerować wymaganej liczby przeszkód: " +
                                 $"{_obstacles.Count}/{count}. Mapa ma zbyt mało rozłącznych tras patrolowych.");
            }
        }

        private IReadOnlyList<Vector2Int> BuildCrossingWindow(
            IReadOnlyList<Vector2Int> referencePath, int obstacleIndex, int obstacleCount)
        {
            if (referencePath == null || referencePath.Count == 0)
                return referencePath;

            // Rozkładamy przecięcia wzdłuż całej trasy zamiast losować je
            // wszystkie z tego samego fragmentu. Małe okno zachowuje deterministyczne
            // zróżnicowanie przy ponawianiu prób generacji.
            float progress = (obstacleIndex + 1f) / (obstacleCount + 1f);
            int center = Mathf.RoundToInt(progress * (referencePath.Count - 1));
            int radius = Mathf.Max(1, referencePath.Count / Mathf.Max(8, obstacleCount * 4));
            int from = Mathf.Max(0, center - radius);
            int to = Mathf.Min(referencePath.Count - 1, center + radius);

            var window = new List<Vector2Int>(to - from + 1);
            for (int i = from; i <= to; i++)
                window.Add(referencePath[i]);
            return window;
        }

        private int FindInitialWaypointAwayFromReference(
            List<Vector2Int> route, IReadOnlyList<Vector2Int> referencePath)
        {
            if (referencePath == null || referencePath.Count == 0)
                return 0;

            var referenceCells = new HashSet<Vector2Int>(referencePath);
            int bestIndex = -1;
            int bestDistance = -1;

            for (int i = 0; i < route.Count; i++)
            {
                if (referenceCells.Contains(route[i]))
                    continue;

                int nearestDistance = int.MaxValue;
                foreach (Vector2Int referenceCell in referencePath)
                {
                    int distance = Mathf.Max(
                        Math.Abs(route[i].x - referenceCell.x),
                        Math.Abs(route[i].y - referenceCell.y));
                    nearestDistance = Math.Min(nearestDistance, distance);
                }

                if (nearestDistance > bestDistance)
                {
                    bestDistance = nearestDistance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private bool TryFindScheduledCrossing(
            List<Vector2Int> route,
            IReadOnlyList<Vector2Int> referencePath,
            int obstacleIndex,
            int obstacleCount,
            out int routeIndex,
            out int referenceIndex)
        {
            routeIndex = -1;
            referenceIndex = -1;
            if (referencePath == null || referencePath.Count == 0)
                return false;

            float progress = (obstacleIndex + 1f) / (obstacleCount + 1f);
            int desiredReferenceIndex = Mathf.RoundToInt(
                progress * (referencePath.Count - 1));
            int bestDifference = int.MaxValue;

            for (int routeCandidate = 0; routeCandidate < route.Count; routeCandidate++)
            {
                for (int referenceCandidate = 0;
                     referenceCandidate < referencePath.Count;
                     referenceCandidate++)
                {
                    if (route[routeCandidate] != referencePath[referenceCandidate])
                        continue;

                    int difference = Math.Abs(referenceCandidate - desiredReferenceIndex);
                    if (difference >= bestDifference)
                        continue;

                    bestDifference = difference;
                    routeIndex = routeCandidate;
                    referenceIndex = referenceCandidate;
                }
            }

            return routeIndex >= 0;
        }

        /// <summary>
        /// Przesuwa wszystkie przeszkody o jeden krok.
        /// Zwraca listę par (staraPozycja, nowaPozycja) do płynnej wizualizacji.
        /// </summary>
        public List<(Vector2Int oldPos, Vector2Int newPos)> StepAll(
            GridMap grid, Vector2Int? occupiedByAgent = null)
        {
            var moves = new List<(Vector2Int oldPos, Vector2Int newPos)>(_obstacles.Count);
            foreach (var obs in _obstacles)
            {
                Vector2Int oldPos = obs.CurrentPosition;
                obs.Step(grid, occupiedByAgent);
                moves.Add((oldPos, obs.CurrentPosition));
            }

            // Weryfikacja po kroku
            VerifyObstaclePositions(grid);

            return moves;
        }

        /// <summary>
        /// Wariant bez historii ruchów używany przez benchmark headless. Stan mapy
        /// i przeszkód jest identyczny jak po StepAll, ale nie powstaje lista alokowana
        /// w każdym ticku symulacji.
        /// </summary>
        public void StepAllWithoutTracking(
            GridMap grid, Vector2Int? occupiedByAgent = null)
        {
            foreach (MovingObstacle obstacle in _obstacles)
                obstacle.Step(grid, occupiedByAgent);

            VerifyObstaclePositions(grid);
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

        // ─── Generacja trasy RandomWalk ───

        /// <summary>
        /// Buduje trasę RandomWalk: losowy spacer po 8 kierunkach.
        /// Trasa = forward path + reverse path (ping-pong).
        /// Deterministyczny z seed RNG.
        /// </summary>
        private List<Vector2Int> BuildRandomWalkRoute(GridMap grid, Vector2Int start,
            Vector2Int target, int length, IReadOnlyList<Vector2Int> preferredCrossingCells)
        {
            int maxTries = 30;

            // 8 kierunków: pion, poziom, skosy
            int[][] dirs = {
                new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 },
                new[] { 1, 1 }, new[] { 1, -1 }, new[] { -1, 1 }, new[] { -1, -1 }
            };

            for (int t = 0; t < maxTries; t++)
            {
                int originX;
                int originY;
                if (preferredCrossingCells != null && preferredCrossingCells.Count > 0)
                {
                    Vector2Int preferred = preferredCrossingCells[_rng.Next(preferredCrossingCells.Count)];
                    originX = preferred.x;
                    originY = preferred.y;
                }
                else
                {
                    originX = _rng.Next(2, grid.Width - 2);
                    originY = _rng.Next(2, grid.Height - 2);
                }

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
