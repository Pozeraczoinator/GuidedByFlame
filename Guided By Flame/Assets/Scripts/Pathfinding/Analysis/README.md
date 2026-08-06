# Analiza benchmarku wyszukiwania ścieżek

Skrypt `analyze_benchmark.py` analizuje plik `benchmark_results_official.csv` i generuje wykresy oraz tabele przeznaczone do dalszej analizy i wykorzystania w pracy magisterskiej.

## Uruchomienie

Z katalogu głównego repozytorium:

```powershell
python "Guided By Flame/Assets/Scripts/Pathfinding/Analysis/analyze_benchmark.py"
```

Z innym plikiem CSV lub katalogiem wynikowym:

```powershell
python "Guided By Flame/Assets/Scripts/Pathfinding/Analysis/analyze_benchmark.py" `
  --csv "Guided By Flame/benchmark_results_official.csv" `
  --output "Guided By Flame/Assets/Scripts/Pathfinding/Analysis/outputs"
```

Jeżeli tabele CSV są otwarte w Excelu i mają pozostać bez zmian, można wygenerować wyłącznie wykresy:

```powershell
python "Guided By Flame/Assets/Scripts/Pathfinding/Analysis/analyze_benchmark.py" --plots-only
```

## Nazwy prezentowane na wykresach

Kody z pliku CSV są używane tylko do filtrowania danych. Na wykresach występują pełne polskie nazwy:

- `Static`: Scenariusz statyczny,
- `DS1_MovingObstacles`: Scenariusz z ruchomymi przeszkodami,
- `DS2_PathObstruction`: Scenariusz z blokowaniem ścieżki,
- `DS3_EscapingTarget`: Scenariusz z uciekającym celem.

Typy map są podpisywane jako: Otwarta przestrzeń, Labirynt, Pokoje i korytarze oraz Rozproszone bloki.

## Wygenerowane wykresy

Każda słupkowa rodzina zawiera osiem plików: wariant `all_sizes`, warianty rozmiaru `32x32`, `64x64` i `128x128` oraz warianty gęstości `density_10pct`, `density_20pct`, `density_30pct` i `density_40pct`. Warianty gęstości obejmują wszystkie rozmiary map. Na osi X zawsze znajdują się typy map.

- `01_execution_time_by_topology_*`: średni czas wykonania,
- `03_explored_nodes_by_topology_*`: liczba odwiedzonych węzłów,
- `04_path_quality_by_topology_*`: jakość ścieżki,
- `05_dynamic_replanning_by_topology_*`: liczba ponownych wyznaczeń ścieżki,
- `07_cpu_temperature_by_topology_*`: średnia temperatura CPU,
- `08_cold_start_overhead_by_topology_*`: narzut pierwszego uruchomienia,
- `16_gc_allocation_by_topology_*`: średnia alokacja pamięci,
- `17_path_length_by_topology_*`: bezwzględna długość znalezionej ścieżki,
- `18_excess_path_cost_by_topology_*`: nadmiarowy koszt względem trasy referencyjnej w procentach.

Pozostałe wykresy zachowują swój dotychczasowy charakter:

- `02_execution_time_scaling_by_map_size`: liniowy wykres skalowania czasu,
- `06_path_found_rate_heatmap`: mapa cieplna odsetka znalezionych ścieżek,
- `10_speed_quality_tradeoff_dynamic`: punktowy wykres kompromisu czasu i jakości,
- `13_scaling_topology_*`: cztery liniowe wykresy skalowania rozdzielone według typu mapy,
- `14_density_topology_*`: cztery liniowe wykresy wpływu gęstości przeszkód.

Wykresy alokacji wykorzystują kolumnę `AvgGCAllocBytes`. Pokazują średnią liczbę bajtów pamięci zarządzanej alokowanych podczas wykonania algorytmu; nie jest to całkowite ani szczytowe użycie pamięci RAM przez proces Unity.

Wykresy kosztu wykorzystują `PathLength` wyłącznie dla rekordów z `PathFound=True`. Nadmiarowy koszt jest liczony jako `(PathLength / ReferenceShortestPathLength - 1) × 100%`. W scenariuszach dynamicznych jest to porównanie z początkową trasą referencyjną, a nie z dynamicznym optimum zmieniającej się mapy.

Łącznie dla obecnego zestawu danych powstają 83 różne wykresy. Każdy jest zapisywany jako PNG do szybkiego podglądu oraz jako wektorowy PDF do umieszczenia w pracy. Wszystkie wykresy słupkowe mają wspólny układ: scenariusze w osobnych panelach, algorytmy oznaczone stałymi kolorami i topologie map na osi X. Rozmiar lub gęstość mapy są wskazane w tytule. W czteropanelowych figurach wartości osi Y są widoczne na każdym panelu.

## Tabele i raport

- `summary_by_scenario_algorithm.csv`: tabela zbiorcza według scenariusza i algorytmu,
- `summary_by_map_scenario_algorithm.csv`: tabela zbiorcza według mapy, scenariusza i algorytmu,
- `analysis_report.md`: krótkie podsumowanie danych wejściowych.
