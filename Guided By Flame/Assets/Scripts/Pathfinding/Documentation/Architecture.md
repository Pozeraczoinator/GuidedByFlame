# GuidedByFlame — Architektura Systemu Pathfindingu

> **Projekt**: Guided By Flame (Unity, C#)
> **Cel**: Praca magisterska — porównanie algorytmów pathfindingu na siatce 2D

---

## 1. Struktura Katalogów

```
Pathfinding/
├── Core/                          # Fundamentalne abstrakcje i struktury danych
│   ├── IPathfindingAlgorithm.cs   # Interfejs Strategy (28 LOC)
│   ├── GridMap.cs                 # Mapa 2D z walkable/obstacle + koszty ruchu (171 LOC)
│   ├── MinHeap.cs                 # Generyczny kopiec minimalny (132 LOC)
│   ├── PathfindingResult.cs       # DTO z metrykami: ścieżka, czas, GC, smoothness (105 LOC)
│   └── BenchmarkMetrics.cs        # Agregator statystyk: Avg/Min/Max/StdDev + CSV (156 LOC)
│
├── Algorithms/                    # 5 implementacji IPathfindingAlgorithm
│   ├── AStarAlgorithm.cs          # A* z heurystyką oktagonalną
│   ├── DijkstraAlgorithm.cs       # Dijkstra (A* bez heurystyki)
│   ├── GreedyBestFirstAlgorithm.cs# GBFS — czysta heurystyka Manhattan
│   ├── CustomGreedyAlgorithm.cs   # Weighted A* (w=50) + kara za skręt
│   └── JumpPointSearchAlgorithm.cs# JPS na uniform gridzie
│
├── Benchmark/                     # Infrastruktura pomiarowa
│   ├── DynamicObstacleManager.cs  # DS1: toggle ścian
│   ├── MovingObstacleManager.cs   # DS2: patrol NPC (RandomWalk 1-3)
│   ├── WeightedTerrainManager.cs  # DS3: dynamiczne wagi (Random/Radial/Linear)
│   ├── TestPointSelector.cs       # Distance bucketing + BFS reachability
│   ├── HardwareMonitor.cs         # Temp CPU (WMI), GC.Collect wrapper
│   └── BatchGenerator.cs          # 4×4×4 = 64 mapy
│
├── MapGenerators/                 # Generatory map proceduralnych
│   ├── IMapGenerator.cs           # Interfejs Strategy
│   ├── MapExporter.cs             # GridMap → .txt
│   ├── OpenFieldGenerator.cs      # Perlin Noise klastry
│   ├── MazeGenerator.cs           # Recursive Backtracker DFS
│   ├── RoomCorridorGenerator.cs   # BSP + L-korytarze
│   └── ScatteredBlockGenerator.cs # Bloki NxN z collision detection
│
├── Tests/                         # Testy determinizmu
│   └── DeterminismTests.cs        # Testy weryfikujące powtarzalność wyników
│
├── Documentation/                 # Dokumentacja kontekstowa
│   ├── Architecture.md            # Ten plik
│   └── AlgorithmsAnalysis.md      # Szczegółowa analiza algorytmów
│
├── BenchmarkManager.cs            # Headless benchmark runner
└── PathfindingVisualizer.cs       # Unity MonoBehaviour z wizualizacją
```

---

## 2. Wzorce Projektowe

| Wzorzec | Gdzie | Cel |
|---------|-------|-----|
| **Strategy** | `IPathfindingAlgorithm`, `IMapGenerator` | Wymienialność algorytmów i generatorów |
| **DTO** | `PathfindingResult`, `BenchmarkMetrics` | Transport danych pomiarowych |
| **Min-Heap** | `MinHeap<T> where T : IHeapItem<T>` | O(log n) priorytet dla open set |
| **Factory Method** | `CreateSelectedAlgorithm()` w Visualizer | Instancjonowanie algorytmu z enuma |
| **Fisher-Yates** | `ShuffleList<T>()` | Eliminacja bias thermal throttling |

---

## 3. System Siatki (GridMap)

- **Typ**: `bool[,] _isWalkable` + `float[,] _movementCost`
- **Układ osi**: X = kolumna (→), Y = wiersz (↑), zgodny z Unity 2D
- **Ruch**: **8-kierunkowy** (ortogonalny + diagonalny)
- **Corner cutting**: **ZABLOKOWANY** we wszystkich algorytmach
- **Koszty ruchu**: 10 (ortogonalny), 14 (diagonalny) — skalowane przez `terrainCost`
- **Format pliku mapy**: `'0'` = walkable, `'1'` = obstacle, odczyt od dołu do góry

---

## 4. Wartości Liczbowe

| Parametr | Wartość |
|----------|---------|
| Koszt ortogonalny | 10 |
| Koszt diagonalny | 14 (≈10√2) |
| Długość ortogonalna (PathLength) | 1.0f |
| Długość diagonalna (PathLength) | 1.414f |
| Domyślny koszt terenu | 1.0f |
| Koszty DS3 | 1.0, 2.0, 5.0, 10.0 |

---

## 5. Metryki Zbierane

| Metryka | Typ | Opis |
|---------|-----|------|
| `PathFound` | bool | Czy ścieżka istnieje |
| `ExploredNodes` | int | Węzły dodane do closed set |
| `PathLength` | float | Geometryczna długość ścieżki |
| `ExecutionTimeMs` | double | Czas Stopwatch |
| `ExecutionTicks` | long | CPU ticks Stopwatch |
| `GCAllocBytes` | long | Delta GC.GetTotalMemory |
| `DirectionChanges` | int | Zmiany kierunku na ścieżce |
| `PathSmoothness` | float | DirectionChanges / PathLength |
| `ColdStartTimeMs` | double | Iteracja 0 (JIT warm-up) |
| `CPUTemperature` | float | Opcjonalnie WMI/OHM |

---

## 6. Scenariusze Testowe

| Scenariusz | Opis |
|------------|------|
| **Static** | Mapa niezmienna |
| **DS1** | Toggle ścian między testami |
| **DS2** | Patrol NPC (RandomWalk 1-3 kroków, ping-pong) |
| **DS3** | Dynamiczne wagi (Random/Radial/Linear) |

**JPS jest WYKLUCZONY z DS3** (nie wspiera weighted gridów).

---

## 7. Topologie Map Proceduralnych

| Topologia | Generator | Charakterystyka |
|-----------|-----------|----------------|
| **OpenField** | Perlin Noise | Organiczne klastry, duże otwarte przestrzenie |
| **Maze** | Recursive Backtracker | Wąskie korytarze 1-tile, jedna ścieżka |
| **RoomCorridor** | BSP | Pokoje + L-korytarze, chokepoints |
| **ScatteredBlock** | Losowe bloki NxN | Regularne przeszkody, symetria |

---

## 8. Gwarancje Determinizmu

Od wersji `feature/pathfinding-audit-determinism`:

1. **Deterministyczny tiebreak w MinHeap** — każdy algorytm rozstrzyga remisy
   priorytetów za pomocą pozycji węzła (X * 10000 + Y), co gwarantuje
   identyczne wyniki niezależnie od kolejności wstawiania do kopca.

2. **Stała kolejność iteracji sąsiadów** — pętla `for(x=-1..1, y=-1..1)`
   gwarantuje tę samą kolejność w każdym uruchomieniu.

3. **Deterministyczny seed RNG** — `System.Random(seed=42)` zapewnia
   powtarzalność map proceduralnych i scenariuszy dynamicznych.

4. **Brak iteracji po HashSet/Dictionary** — używane wyłącznie do Contains/TryGetValue.

---

## 9. Format Wyjściowy CSV

**Separator**: średnik (`;`)
**23 kolumny**:
```
TestID;Algorithm;StartX;StartY;TargetX;TargetY;Scenario;ObstacleDensity;
PathFound;ColdStartTimeMs;ColdStartTicks;ColdStartGCAllocBytes;
AvgExecutionTimeMs;MinExecutionTimeMs;MaxExecutionTimeMs;StdDevExecutionTimeMs;
AvgExecutionTicks;AvgGCAllocBytes;
ExploredNodes;PathLength;DirectionChanges;PathSmoothness;CPUTemperature
```

---

## 10. Kluczowe Parametry

| Parametr | Domyślna | Lokalizacja |
|----------|----------|-------------|
| `benchmarkIterations` | 30 | PathfindingVisualizer |
| `randomSeed` | 42 | PathfindingVisualizer, BenchmarkManager |
| `dynamicChangesCount` | 5 | PathfindingVisualizer |
| `movingObstacleCount` | 3 | PathfindingVisualizer |
| `patrolLength` | 6 | PathfindingVisualizer |
| `pairsPerBucket` | 30 | PathfindingVisualizer |
| `_greedyWeight` | 50.0f | CustomGreedyAlgorithm (konstruktor) |
| `_turnPenalty` | 2 | CustomGreedyAlgorithm (konstruktor) |
