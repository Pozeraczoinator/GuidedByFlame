# Analiza Algorytmów Pathfindingu

> **Projekt**: Guided By Flame — Praca Magisterska
> **Branch**: `feature/pathfinding-audit-determinism`

---

## 1. A* (A-Star)

### Opis
A* łączy koszt dotarcia g(n) z heurystyką h(n) → f(n) = g(n) + h(n).
Jest **kompletny** i **optymalny** (heurystyka dopuszczalna i konsystentna).

### Heurystyka: Odległość Oktagonalna
```
h(n) = 14 × min(dx, dy) + 10 × |dx - dy|
```
Dopuszczalna na siatce 8-kierunkowej z kosztem 10/14. ✅

### Priorytet MinHeap
1. FCost (min) → 2. HCost (min) → 3. Pozycja X*10000+Y (min)

### Wspiera wagi terenu (DS3): ✅

---

## 2. Dijkstra

### Opis
Specjalny przypadek A* z h(n) = 0. Eksploruje wyłącznie po g(n).
**Kompletny** i **optymalny**, ale eksploruje więcej węzłów.

### Priorytet MinHeap
1. GCost (min) → 2. Pozycja X*10000+Y (min)

### Wspiera wagi terenu (DS3): ✅

---

## 3. Greedy Best-First Search (GBFS)

### Opis
Kieruje się wyłącznie heurystyką h(n). **Kompletny** na skończonym grafie,
**NIE optymalny** — może znaleźć suboptymalne ścieżki.

### Heurystyka: Manhattan Distance
```
h(n) = |dx| + |dy|
```

### Priorytet MinHeap
1. HCost (min) → 2. Pozycja X*10000+Y (min)

### Uwagi
- Nie używa GCost → nie aktualizuje węzłów w OpenSet
- **Nie uwzględnia wag terenu** (zamierzony — GBFS nie ma GCost)

### Wspiera wagi terenu (DS3): ❌ (ignoruje)

---

## 4. Custom Greedy (Weighted A*)

### Opis
Weighted A*: f(n) = g(n) + **w** × h(n), gdzie **w = 50.0** (domyślnie).
Plus kara za zmianę kierunku (**turnPenalty = 2** domyślnie).

### Parametry (konfigurowalne przez konstruktor)
| Parametr | Domyślna | Opis |
|----------|----------|------|
| `greedyWeight` | 50.0f | Waga heurystyki |
| `turnPenalty` | 2 | Kara za skręt |

### Priorytet MinHeap
1. FCost (min, float) → 2. HCost (min, float) → 3. Pozycja X*10000+Y (min)

### Wspiera wagi terenu (DS3): ✅

---

## 5. Jump Point Search (JPS)

### Opis
Optymalizacja A* na **uniform cost gridzie**. Przeskakuje puste obszary
do jump pointów — punktów z forced neighbors.

### Mechanizm
1. **FindNeighbors**: pruning — zachowaj tylko natural + forced neighbors
2. **Jump**: rekurencyjna eksploracja w kierunku do momentu znalezienia JP
3. **RetracePath**: interpolacja pełnej ścieżki między jump points

### Priorytet MinHeap
1. FCost (min) → 2. HCost (min) → 3. Pozycja Pos.x*10000+Pos.y (min)

### Ograniczenia
- **NIE wspiera weighted gridów** (DS3) — wymaga uniform cost
- Rekurencja w Jump() bez limitu głębokości

### Wspiera wagi terenu (DS3): ❌ (wykluczony z benchmarku)

---

## 6. Porównanie

| Cecha | A* | Dijkstra | GBFS | CustomGreedy | JPS |
|-------|----|---------:|------|-------------|-----|
| Optymalny | ✅ | ✅ | ❌ | ❌ | ✅* |
| Kompletny | ✅ | ✅ | ✅ | ✅ | ✅ |
| Heurystyka | Oktagonalna | Brak | Manhattan | Oktagonalna×50 | Oktagonalna |
| DS3 (wagi) | ✅ | ✅ | ❌ | ✅ | ❌ |
| Złożoność | O(E log V) | O(E log V) | O(V log V) | O(E log V) | O(k log k)** |

*JPS optymalny na uniform gridach
**k = liczba jump points << V

---

## 7. Gwarancje Determinizmu (po audycie)

Każdy algorytm posiada **deterministyczny tiebreak** w CompareTo:
```csharp
if (compare == 0)
{
    int posA = X * 10000 + Y;
    int posB = other.X * 10000 + other.Y;
    compare = posA.CompareTo(posB);
}
```

Gwarantuje to: **ten sam input → ta sama ścieżka → te same metryki**.

Weryfikacja: `Tests/DeterminismTests.cs` — automatyczne testy
uruchamiające każdy algorytm 100× na tych samych danych.
