# Zmiany metodologii benchmarku pathfindingu

> Stan obowiązujący dla wyników generowanych po commicie `17b7950`.
> Dokument jest checklistą do aktualizacji części badawczej pracy magisterskiej.

## 1. Najważniejsza zasada porównywalności

Wyników z wcześniejszych uruchomień nie należy łączyć z nowym plikiem CSV. Zmieniły się jednocześnie:

- liczba iteracji pomiarowych;
- definicja i intensywność scenariuszy dynamicznych;
- limity zakończenia DS1;
- sposób pomiaru alokacji pamięci;
- sposób wyznaczania i raportowania kosztu ścieżki;
- alokacje wewnętrzne czterech algorytmów dzięki ponownemu użyciu buforów.

Checkpoint zawiera fingerprint konfiguracji i wersję metody pomiaru pamięci. Jeżeli fingerprint nie pasuje, benchmark nie kontynuuje starego eksperymentu.

## 2. Ostateczna konfiguracja pełnego eksperymentu

| Element | Wartość |
|---|---:|
| Iteracje na algorytm i przypadek | 5 |
| Cold start | iteracja 0 |
| Warm start | iteracje 1–4 |
| Rozmiary map | 32×32, 64×64, 128×128 |
| Topologie | OpenField, Maze, RoomCorridor, ScatteredBlock |
| Gęstości | 0.1, 0.2, 0.3, 0.4 |
| Seedy map | 42, 123, 256, 789 |
| Pary na przedział dystansu | 35 |
| Przedziały dystansu | Short, Medium, Long |
| Scenariusze | Static, DS1, DS2, DS3 |
| Algorytmy | A*, Dijkstra, GBFS, Custom Greedy, JPS |
| Liczba przypadków `TestID` | 80 640 |
| Plik wynikowy | `benchmark_results_5iter_ds1limits.csv` |

Łącznie daje to 80 640 konfiguracji start–cel–scenariusz oraz pięć algorytmów wykonywanych po pięć razy. Scenariusz dynamiczny może dodatkowo uruchomić algorytm wielokrotnie w ramach jednego replanującego przebiegu.

Pięć iteracji jest kompromisem czasowym. Dla pojedynczego punktu dostępne są tylko cztery próbki warm start, dlatego jego odchylenie standardowe należy traktować opisowo. Wnioski statystyczne powinny bazować przede wszystkim na rozkładzie wyników pomiędzy wieloma mapami, seedami i parami start–cel, a nie traktować pięciu powtórzeń tego samego wejścia jako pięciu niezależnych przypadków badawczych.

## 3. Ścieżka referencyjna

Ścieżka referencyjna jest wyznaczana przez kanoniczną implementację Dijkstry w `TestPointSelector.TryGetShortestPath`. Nie jest to wynik żadnego z pięciu badanych obiektów tworzonych przez runner benchmarku.

Model ruchu referencji:

- siatka ośmiokierunkowa;
- koszt 10 dla ruchu pionowego lub poziomego;
- koszt 14 dla ruchu po przekątnej;
- zakaz przechodzenia po przekątnej przez zablokowany róg;
- deterministyczne rozstrzyganie remisów;
- zwracana lista nie zawiera pola startowego.

Referencja służy do:

- sprawdzenia osiągalności pary start–cel;
- obliczenia `ReferenceShortestPathLength`;
- przypisania par do przedziałów Short, Medium i Long;
- rozłożenia ruchomych przeszkód DS1 wzdłuż całej trasy;
- przygotowania harmonogramu blokad DS2;
- wyznaczenia deterministycznego limitu ticków DS1 i DS2.

`ReferenceShortestPathLength` jest długością geometryczną w skali 1/1.414. `PathCost10_14` jest natomiast całkowitoliczbowym kosztem faktycznej drogi badanego algorytmu w skali 10/14.

## 4. Zmiany scenariuszy dynamicznych

### DS1 — ruchome przeszkody

- Liczba przeszkód skaluje się z długością referencji: co najmniej 3 i około jedna przeszkoda na 12 kroków, maksymalnie 64.
- Trasy patrolowe są rozłożone w różnych częściach ścieżki referencyjnej, aby dynamika nie skupiała się tylko w jednym miejscu.
- Przeszkody poruszają się deterministycznie po trasach ping-pong.
- Każdy tick ma kolejność: ruch środowiska → obserwacja → ewentualny replan → ruch agenta.
- Replan jest liczony tylko po planie początkowym i następuje, gdy bieżący plan nie ma legalnego następnego kroku.
- Cofnięcie agenta nie jest operacją techniczną symulatora. Może wystąpić wyłącznie wtedy, gdy algorytm zwróci cofający krok jako część nowej legalnej trasy.
- Maksymalna liczba replanów wynosi 120.
- Po 20 kolejnych replanach bez znalezienia drogi i bez ruchu przypadek kończy się niepowodzeniem.
- Obowiązuje również limit ticków: `max(64, ceil(referenceLength × 8) + 32)`.

Limity są deterministyczne. Oznaczają jednak, że `PathFound=false` może opisywać zarówno rzeczywisty brak drogi, jak i nieosiągnięcie celu w przyjętym budżecie symulacji. Należy zaznaczyć to przy interpretacji skuteczności DS1.

### DS2 — trwałe blokady trasy

- Harmonogram blokad powstaje przed pomiarem na podstawie mapy bazowej i referencji Dijkstry.
- Blokady są trwałe i rozłożone wzdłuż postępu agenta.
- Odstęp zdarzeń wynosi 8 kroków, a górny limit liczby zablokowanych pól wynosi 40.
- Replan następuje dopiero po zastosowaniu zdarzenia i wykryciu, że następny krok jest niedozwolony albo dotychczasowy plan się zakończył.
- Brak drogi po trwałej blokadzie kończy przypadek, ponieważ DS2 nie usuwa przeszkód.

Takie uporządkowanie usuwa efekt wizualnego replanu jeszcze przed rzeczywistą kolizją z nową blokadą.

### DS3 — uciekający cel

- Cel próbuje przesunąć się o jedno legalne pole po każdych dwóch krokach agenta.
- Ruch celu nie może zmniejszyć jego odległości oktagonalnej od początkowej pozycji agenta.
- Kierunki są sortowane, a następnie wybierane przez deterministyczny generator pseudolosowy zależny od mapy i pary start–cel.
- Maksymalna liczba ucieczek wynosi 50.
- Każda skuteczna ucieczka celu wymusza replan.

Dijkstra wyznacza najtańszą drogę do aktualnej pozycji celu w każdym replanie, ale nie optymalizuje globalnej przyszłej trajektorii ruchomego celu. Dlatego w DS3 może uzyskać gorszy skumulowany koszt lub długość od innego algorytmu bez naruszenia własności optymalności Dijkstry dla pojedynczego, statycznego zapytania.

## 5. Koszt i jakość ścieżki

Nowa kolumna `PathCost10_14` jest obliczana z pełnej zapisanej trajektorii agenta:

```text
koszt = 10 × liczba kroków prostych + 14 × liczba kroków diagonalnych
```

W scenariuszach dynamicznych obejmuje ona całą rzeczywiście przebytą trasę po wszystkich replanach, włącznie z ewentualnym ruchem oddalającym od celu. Nie jest to koszt ostatniego planu ani ścieżki referencyjnej.

`PathLength` pozostaje długością geometryczną w skali 1/1.414. Koszt 10/14 jest zalecany do porównań dyskretnych, ponieważ nie kumuluje błędu reprezentacji `float`.

`PathCost10_14`, `DirectionChanges` i `PathSmoothness` są wyliczane tylko dla iteracji cold start. CSV świadomie pobiera te wartości z iteracji 0. W pozostałych iteracjach pełna sekwencja pól jest nadal porównywana z cold startem, więc brak ponownego liczenia metryk nie osłabia kontroli determinizmu.

## 6. Pomiar czasu i pamięci

### Czas

- `ColdStartTimeMs` i `ColdStartTicks` pochodzą z iteracji 0.
- Średnia, minimum, maksimum i odchylenie standardowe czasu pochodzą z iteracji 1–4.
- Dla DS1, DS2 i DS3 czas wyniku jest sumą czasów wszystkich wywołań algorytmu `FindPath` w danej symulacji.
- Ruch przeszkód, animacja, zapis CSV i oczekiwanie Unity nie są dodawane do `ExecutionTimeMs`.

### Alokacje

`GCAllocBytes` jest obecnie różnicą dwóch odczytów `GC.GetAllocatedBytesForCurrentThread()`. Mierzy liczbę bajtów zaalokowanych na głównym wątku podczas operacji, również wtedy, gdy obiekty zostały później zwolnione. Jest to szybsze i trafniejsze dla pytania o presję na GC niż wcześniejsza różnica `GC.GetTotalMemory(false)`, która mierzyła zmianę zajętości całej sterty i mogła dawać wartości ujemne.

W scenariuszu statycznym zakres pomiaru obejmuje zasadniczo jedno wyszukiwanie. W scenariuszach dynamicznych obejmuje całe wywołanie symulacji, w tym jej struktury pomocnicze i wszystkie replany. Dlatego alokacje należy porównywać między algorytmami w obrębie tego samego scenariusza; różnicy Static–DS1/DS2/DS3 nie należy interpretować jako czystej różnicy implementacji algorytmu.

`ColdStartGCAllocBytes` zawiera pierwszą inicjalizację buforów. `AvgGCAllocBytes` jest średnią z iteracji warm start.

## 7. Ponowne używanie struktur

A*, Dijkstra, Greedy Best-First Search, Custom Greedy i JPS ponownie wykorzystują:

- tablicę węzłów `Node[,]`;
- kopiec `MinHeap`;
- znaczniki generacji wyszukiwania zamiast czyszczenia całych słowników i zbiorów.

Bufory są ponownie używane pomiędzy iteracjami i replanami tej samej instancji algorytmu. Zmniejsza to liczbę alokacji i pracę GC. JPS zachowuje własną logikę skoków, ale ponownie używa tablicy węzłów, kopca, bufora sąsiadów i bufora jump pointów. Stan wyszukiwania jest izolowany znacznikiem generacji, więc ponowne użycie instancji nie przenosi wyniku między wywołaniami.

Ta zmiana wpływa zarówno na czas warm start, jak i `AvgGCAllocBytes`, dlatego wymaga nowego eksperymentu zamiast kontynuacji wcześniejszego CSV.

## 8. Temperatura, odporność i postęp

- Temperatura jest odświeżana asynchronicznie i odczytywana z cache; odczyt wiersza CSV nie wykonuje blokującego WMI/HTTP.
- Preferowane są zewnętrzne Web API lub WMI Libre/OpenHardwareMonitor. Bezpośrednie otwieranie biblioteki sensorów wewnątrz Unity zostało pominięte ze względu na ryzyko native crash.
- Niedostępny sensor jest oznaczany wartością `-1`, a nie zatrzymaniem benchmarku.
- CSV jest okresowo opróżniany na dysk.
- Checkpoint zapisywany jest co 50 `TestID`.
- Postęp liczbowy jest logowany co 50 przypadków. Dodatkowy heartbeat jest planowany co około 30 sekund i emitowany przy najbliższej granicy pomiędzy porcjami pomiaru.

## 9. Kontrola jakości

Po zmianach zestaw testów deterministyczności zakończył się wynikiem:

```text
112 passed / 0 failed
```

Test powtarzalności naprzemiennie używa świeżej instancji algorytmu i tej samej instancji z buforami, dzięki czemu wykrywa przenoszenie stanu między wyszukiwaniami. Sprawdzane są również pełne ścieżki i produkcyjne symulacje DS1, DS2 oraz DS3.

## 10. Proponowany zapis do pracy

> Każdy przypadek eksperymentalny wykonano pięciokrotnie. Pierwsze uruchomienie raportowano oddzielnie jako cold start, natomiast cztery kolejne uruchomienia stanowiły podstawę statystyk warm start. Algorytmy i scenariusze dynamiczne były deterministyczne dla ustalonego seeda, mapy oraz pary start–cel. Powtarzalność weryfikowano przez porównanie pełnej sekwencji pól, liczby odwiedzonych węzłów i liczby replanów. Wnioski statystyczne formułowano na podstawie wielu niezależnych konfiguracji map i par start–cel, a odchylenie standardowe czterech pomiarów warm start traktowano jako lokalną miarę szumu czasowego.

> Koszt drogi raportowano w całkowitoliczbowej skali 10/14, gdzie krok ortogonalny kosztował 10, a diagonalny 14. Ścieżkę referencyjną wyznaczała deterministyczna Dijkstra na siatce ośmiokierunkowej bez ścinania narożników. Pomiar alokacji wykonywano jako przyrost licznika bajtów zaalokowanych na bieżącym wątku w czasie mierzonej operacji.
