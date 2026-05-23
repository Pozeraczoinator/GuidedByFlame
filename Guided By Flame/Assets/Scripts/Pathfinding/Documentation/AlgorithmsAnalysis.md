# Analiza Algorytmów Pathfindingu

> **Projekt**: Guided By Flame — Praca Magisterska  
> **Zakres**: implementacja, porównanie i benchmark algorytmów wyszukiwania ścieżki na siatce 2D  
> **Branch**: `feature/pathfinding-audit-determinism`

---

## 1. Cel dokumentu

Celem tego dokumentu jest opisanie, co zostało zaimplementowane w module pathfindingu, dlaczego wybrano takie rozwiązania oraz jak należy interpretować wyniki benchmarków. Dokument nie jest tylko listą algorytmów. Ma wyjaśniać, w jaki sposób algorytmy działają w projekcie, jakie założenia przyjęto dla map 2D oraz jakie kompromisy wynikają z konkretnych implementacji.

W projekcie porównywane są następujące algorytmy:

- **A\*** — algorytm referencyjny, zwykle bardzo dobry kompromis między jakością ścieżki a szybkością.
- **Dijkstra** — algorytm optymalny bez heurystyki, traktowany jako punkt odniesienia dla pełnego przeszukiwania kosztowego.
- **Greedy Best-First Search (GBFS)** — algorytm szybki i zachłanny, nastawiony na dojście w stronę celu, ale bez gwarancji najkrótszej ścieżki.
- **Custom Greedy / Weighted A\*** — własna odmiana A\*, która mocniej premiuje kierunek do celu i dodaje karę za skręty.
- **Jump Point Search (JPS)** — optymalizacja A\* dla map o jednolitym koszcie ruchu, szczególnie skuteczna na otwartych przestrzeniach.

Porównanie ma znaczenie praktyczne dla gry 2D: przeciwnicy lub inne jednostki powinny znajdować drogę szybko, powtarzalnie i w sposób, który daje naturalnie wyglądającą trasę. Nie zawsze najlepszy algorytm teoretycznie jest najlepszy w grze. Dlatego poza długością ścieżki mierzone są także czas wykonania, liczba odwiedzonych pól, alokacje pamięci oraz gładkość trasy.

---

## 2. Podstawowe pojęcia

### Grid / siatka

Mapa jest reprezentowana jako dwuwymiarowa siatka pól (`GridMap`). Każde pole ma współrzędne `(x, y)` i może być:

- **walkable** — można na nie wejść,
- **blocked / obstacle** — jest przeszkodą,
- opcjonalnie **ważone** — wejście na pole może mieć koszt większy niż standardowy.

Taka reprezentacja jest typowa dla gier 2D, ponieważ łatwo odpowiada tilemapom, planszom oraz mapom testowym zapisanym jako `0` i `1`.

### Węzeł

Węzeł to pojedyncze pole siatki rozpatrywane przez algorytm. W implementacji węzeł przechowuje zwykle:

- pozycję `X`, `Y` albo `Vector2Int`,
- koszt dotarcia `GCost`,
- koszt heurystyczny `HCost`,
- koszt łączny `FCost`,
- referencję do rodzica `Parent`, potrzebną do odtworzenia ścieżki.

### Koszt `g(n)`

`g(n)` oznacza rzeczywisty koszt dojścia od punktu startowego do danego węzła `n`. W projekcie ruch ortogonalny ma koszt `10`, a ruch po przekątnej koszt `14`. Są to całkowitoliczbowe odpowiedniki odległości `1.0` oraz około `1.414`, czyli pierwiastka z 2.

Użycie wartości `10/14` zamiast `1/1.414` upraszcza porównywanie kosztów i ogranicza błędy zmiennoprzecinkowe.

### Heurystyka `h(n)`

`h(n)` to oszacowanie odległości od węzła `n` do celu. Heurystyka nie sprawdza rzeczywistych przeszkód, lecz daje algorytmowi kierunek. Dobra heurystyka zmniejsza liczbę odwiedzonych pól, bo algorytm szybciej koncentruje się na obszarze prowadzącym do celu.

### Heurystyki używane w projekcie

| Algorytm | Heurystyka | Uwagi |
|----------|------------|-------|
| A\* | Oktagonalna | `14 * min(dx, dy) + 10 * |dx - dy|`, zgodna z ruchem 8-kierunkowym. |
| Dijkstra | Brak | Priorytet bazuje tylko na `GCost`; metoda odległości liczy koszt ruchu, nie heurystykę do celu. |
| Greedy Best-First Search | Oktagonalna | Używana jako jedyny priorytet `HCost`. |
| Custom Greedy / Weighted A\* | Oktagonalna ważona | `HCost` jest mnożony przez `greedyWeight`. |
| Jump Point Search | Oktagonalna | Używana w priorytecie A\*-podobnym dla jump pointów. |

W kodzie algorytmów heurystyka jest zapisana w skali całkowitej `10/14`, bo te same wartości odpowiadają kosztom ruchu ortogonalnego i diagonalnego. W metrykach CSV `OctagonalDistance` jest zapisywana w skali geometrycznej `1.0/1.414`, aby dało się ją bezpośrednio porównać z `EuclideanDistance`, `PathLength` i `ReferenceShortestPathLength`.

### Koszt `f(n)`

W A\* i algorytmach pochodnych:

```text
f(n) = g(n) + h(n)
```

`f(n)` jest priorytetem w kolejce. Im mniejsza wartość `f(n)`, tym wcześniej węzeł zostanie sprawdzony.

### OpenSet i ClosedSet

Każdy z algorytmów działa na dwóch głównych zbiorach:

- **OpenSet** — węzły odkryte, ale jeszcze nie w pełni przetworzone. W projekcie jest to `MinHeap`, czyli kopiec pozwalający szybko pobierać najlepszy węzeł.
- **ClosedSet** — węzły już przetworzone. Trafienie do tego zbioru oznacza, że algorytm nie powinien ponownie analizować tego samego pola.

Takie rozdzielenie zapobiega niepotrzebnemu powtarzaniu pracy i jest standardowym mechanizmem w algorytmach grafowych.

### MinHeap

`MinHeap` to kolejka priorytetowa. W każdym kroku algorytm pobiera węzeł o najniższym koszcie według reguł konkretnego algorytmu. Dzięki temu nie trzeba za każdym razem sortować całej listy kandydatów.

Dla A\*, Dijkstry i Custom Greedy daje to typową złożoność:

```text
O(E log V)
```

gdzie:

- `V` to liczba węzłów, czyli pól siatki,
- `E` to liczba krawędzi, czyli możliwych przejść między polami.

### Ruch 8-kierunkowy

Algorytmy pozwalają na ruch:

- w górę,
- w dół,
- w lewo,
- w prawo,
- po czterech przekątnych.

Ruch po przekątnej jest droższy niż ruch prosty, ponieważ geometrycznie jest dłuższy.

### Zakaz ścinania rogów

W implementacji zabroniono przechodzenia po przekątnej przez narożnik dwóch przeszkód. Jeżeli jednostka chce przejść np. z `(x, y)` do `(x+1, y+1)`, to pola `(x+1, y)` oraz `(x, y+1)` muszą być wolne.

To jest ważne dla gry, ponieważ bez tej zasady postać mogłaby "przeciskać się" przez blokady, które wizualnie powinny być nieprzechodnie.

### Wagi terenu

`GridMap` przechowuje również koszt wejścia na pole (`GetMovementCost`). Domyślnie koszt wynosi `1.0`. Wartości większe niż `1.0` oznaczają trudniejszy teren, np. błoto, ogień, spowolnienie albo inne pole, którego algorytm powinien unikać, jeśli istnieje rozsądna alternatywa.

Wagi są uwzględniane przez A\*, Dijkstrę i Custom Greedy. GBFS ich nie uwzględnia, ponieważ nie posiada `GCost`. JPS również nie jest używany dla map ważonych, bo jego założenia optymalizacyjne wymagają jednolitego kosztu ruchu.

---

## 3. Wspólne założenia implementacji

Wszystkie algorytmy zostały dostosowane do tego samego środowiska testowego:

- pracują na `GridMap`,
- zwracają `PathfindingResult`,
- zapisują liczbę odwiedzonych węzłów,
- mierzą czas wykonania przez `Stopwatch`,
- odtwarzają ścieżkę przez wskaźniki `Parent`,
- opcjonalnie zapisują historię odwiedzonych pól do wizualizacji,
- respektują przeszkody oraz granice mapy.

Ścieżka jest odtwarzana dopiero po znalezieniu celu. Algorytm idzie wtedy od węzła końcowego przez kolejne referencje `Parent` aż do startu, a potem odwraca listę. Dzięki temu w trakcie wyszukiwania nie trzeba przechowywać osobnej listy ścieżki dla każdego kandydata.

Wynik zawiera również `PathLength`, czyli długość geometryczną ścieżki:

- ruch prosty dodaje `1.0`,
- ruch diagonalny dodaje `1.414`.

To różni się od kosztu wewnętrznego `10/14`, ale opisuje trasę w bardziej czytelnych jednostkach dla analizy.

---

## 4. A\* (A-Star)

### Co robi algorytm

A\* łączy dwie informacje:

```text
f(n) = g(n) + h(n)
```

`g(n)` mówi, ile już kosztowało dojście do danego pola, a `h(n)` szacuje, jak daleko jest jeszcze do celu. Dzięki temu A\* nie przeszukuje mapy równomiernie jak Dijkstra, tylko kieruje się w stronę celu, ale nadal kontroluje rzeczywisty koszt dotychczasowej drogi.

W praktyce oznacza to, że A\* jest bardzo dobrym algorytmem bazowym do gier: zwykle znajduje optymalną ścieżkę, a jednocześnie odwiedza mniej pól niż Dijkstra.

### Heurystyka: odległość oktagonalna

Dla siatki 8-kierunkowej użyto odległości oktagonalnej:

```text
h(n) = 14 * min(dx, dy) + 10 * |dx - dy|
```

gdzie:

- `dx = |x1 - x2|`,
- `dy = |y1 - y2|`.

Formuła oznacza: najpierw wykonaj tyle ruchów po przekątnej, ile się da, a pozostałą różnicę pokonaj ruchem prostym. Jest to naturalne dopasowanie do kosztów `10/14`.

### Dlaczego ta heurystyka

Na mapie, gdzie można chodzić po przekątnych, heurystyka Manhattan byłaby mniej trafna, bo zakładałaby tylko ruch w czterech kierunkach. Odległość oktagonalna lepiej odpowiada faktycznym zasadom ruchu w projekcie.

Heurystyka jest **dopuszczalna** i **konsystentna** dla map o jednolitym koszcie ruchu:

- **dopuszczalna** oznacza, że nie zawyża prawdziwego kosztu dojścia do celu,
- **konsystentna** oznacza, że koszt szacowany zachowuje się stabilnie między sąsiadującymi węzłami.

Te własności są istotne, bo dają A\* gwarancję optymalności przy standardowych kosztach.

### Priorytet w `MinHeap`

A\* wybiera węzły według kolejności:

1. najmniejszy `FCost`,
2. przy remisie najmniejszy `HCost`,
3. przy kolejnym remisie najmniejszy deterministyczny identyfikator pozycji `X * 10000 + Y`.

Remis po `HCost` sprawia, że przy takim samym koszcie łącznym algorytm preferuje pole bliższe celowi. Ostatni tie-break po pozycji zapewnia powtarzalność.

### Obsługa wag terenu

A\* uwzględnia koszt wejścia na sąsiednie pole:

```text
moveCost = current.GCost + distance(current, neighbor) * terrainCost
```

Dzięki temu algorytm może wybrać trasę geometrycznie dłuższą, jeśli omija kosztowny teren.

### Wnioski

A\* jest w projekcie najważniejszym punktem odniesienia. Powinien osiągać dobry balans między:

- jakością ścieżki,
- liczbą odwiedzonych węzłów,
- czasem wykonania,
- odpornością na różne topologie map.

---

## 5. Dijkstra

### Co robi algorytm

Dijkstra jest podobny do A\*, ale nie używa heurystyki:

```text
h(n) = 0
f(n) = g(n)
```

Algorytm zawsze wybiera pole o najniższym znanym koszcie dojścia od startu. Nie wie jednak, w którą stronę znajduje się cel. Z tego powodu często rozlewa eksplorację we wszystkich kierunkach.

### Dlaczego został dodany

Dijkstra jest bardzo dobrym algorytmem porównawczym. Pokazuje, ile pracy trzeba wykonać, gdy algorytm nie ma informacji kierunkowej. Jeżeli A\* odwiedza dużo mniej pól niż Dijkstra, widać realną wartość heurystyki.

### Priorytet w `MinHeap`

Dijkstra wybiera węzły według kolejności:

1. najmniejszy `GCost`,
2. przy remisie najmniejszy identyfikator pozycji `X * 10000 + Y`.

### Obsługa wag terenu

Dijkstra bardzo dobrze nadaje się do map ważonych, ponieważ z definicji pracuje na rzeczywistym koszcie dotarcia. Jeżeli teren ma różne koszty, algorytm nadal znajduje optymalną ścieżkę, zakładając nieujemne koszty ruchu.

### Wnioski

Dijkstra jest kompletny i optymalny, ale zwykle wolniejszy od A\*. W benchmarku powinien mieć wysoką liczbę `ExploredNodes`, zwłaszcza na dużych lub otwartych mapach.

---

## 6. Greedy Best-First Search (GBFS)

### Co robi algorytm

GBFS kieruje się wyłącznie heurystyką:

```text
priority(n) = h(n)
```

Nie analizuje rzeczywistego kosztu dojścia od startu. Wybiera po prostu pole, które wygląda na najbliższe celowi.

### Heurystyka

W implementacji użyto odległości oktagonalnej:

```text
h(n) = 14 * min(dx, dy) + 10 * |dx - dy|
```

Jest to szybka heurystyka dopasowana do ruchu 8-kierunkowego. W GBFS pełni rolę kierunkowskazu, a nie gwarancji optymalności.

### Dlaczego ten algorytm jest ciekawy

GBFS często działa szybko, bo agresywnie idzie w stronę celu. To może dawać dobre wyniki na prostych mapach. Problem pojawia się wtedy, gdy cel jest "za ścianą" albo mapa zawiera labiryntowe korytarze. Heurystyka może ciągle wskazywać kierunek, który wygląda dobrze matematycznie, ale prowadzi do przeszkody lub długiego obejścia.

### Priorytet w `MinHeap`

GBFS wybiera węzły według kolejności:

1. najmniejszy `HCost`,
2. przy remisie najmniejszy identyfikator pozycji `X * 10000 + Y`.

### Ograniczenia

GBFS nie posiada `GCost`, więc:

- nie porównuje alternatywnych dróg na podstawie kosztu dojścia,
- nie aktualizuje węzłów w `OpenSet`, jeśli później pojawi się lepsza trasa,
- nie uwzględnia wag terenu,
- nie gwarantuje najkrótszej ścieżki.

Na skończonej mapie z poprawnym `ClosedSet` algorytm jest kompletny, czyli jeśli istnieje połączenie, powinien je znaleźć. Nie oznacza to jednak, że będzie to dobra lub najkrótsza ścieżka.

### Wnioski

GBFS jest przykładem kompromisu "szybciej, ale mniej pewnie". W benchmarku warto obserwować, czy niższy czas wykonania nie jest okupiony większą długością ścieżki lub gorszą gładkością.

---

## 7. Custom Greedy (Weighted A\*)

### Co robi algorytm

Custom Greedy jest własną odmianą Weighted A\*. Bazuje na wzorze:

```text
f(n) = g(n) + w * h(n)
```

Domyślnie:

```text
w = 50.0
turnPenalty = 2
```

Oznacza to, że heurystyka ma znacznie większy wpływ niż w klasycznym A\*. Algorytm mocno preferuje pola prowadzące w stronę celu, ale nadal zachowuje `GCost`, więc nie jest tak ślepy jak czysty GBFS.

### Po co zastosowano wagę heurystyki

W grach czasem nie jest potrzebna matematycznie najkrótsza ścieżka. Często ważniejsze jest, żeby jednostka szybko podjęła decyzję i poruszała się w sposób wystarczająco dobry. Weighted A\* celowo zwiększa wpływ heurystyki, aby zmniejszyć eksplorację mapy.

Konsekwencją jest utrata gwarancji optymalności. Algorytm może znaleźć trasę dłuższą niż A\*, ale potencjalnie szybciej.

### Kara za zmianę kierunku

Do kosztu ruchu dodano `turnPenalty`, jeżeli kierunek ruchu zmienia się względem poprzedniego kroku. Celem jest preferowanie gładszych tras:

- mniej ostrych zygzaków,
- bardziej naturalny ruch NPC,
- potencjalnie czytelniejsza animacja i sterowanie.

Ta kara również wpływa na optymalność względem czystej długości geometrycznej. Algorytm nie szuka już tylko najkrótszej trasy, lecz trasy, która łączy długość, kierunek do celu i gładkość.

### Parametry

| Parametr | Domyślna wartość | Znaczenie |
|----------|------------------|-----------|
| `greedyWeight` | `50.0f` | Waga heurystyki. Im większa, tym bardziej zachłanne zachowanie. |
| `turnPenalty` | `2` | Dodatkowy koszt za zmianę kierunku. Im większy, tym większa preferencja dla prostszych tras. |

### Priorytet w `MinHeap`

Custom Greedy wybiera węzły według kolejności:

1. najmniejszy `FCost`,
2. przy remisie najmniejszy `HCost`,
3. przy kolejnym remisie najmniejszy identyfikator pozycji `X * 10000 + Y`.

### Obsługa wag terenu

Algorytm uwzględnia `GetMovementCost`, tak samo jak A\* i Dijkstra. Dzięki temu może reagować na trudniejszy teren, chociaż silna waga heurystyki może czasem sprawić, że nadal będzie mocno ciągnął w stronę celu.

### Wnioski

Custom Greedy jest algorytmem eksperymentalnym. Jego zadaniem jest sprawdzić, czy dla gry bardziej opłaca się wybrać trasę trochę mniej optymalną, ale wyliczoną szybciej i potencjalnie płynniejszą.

---

## 8. Jump Point Search (JPS)

### Co robi algorytm

Jump Point Search jest optymalizacją A\* dla siatek o jednolitym koszcie ruchu. Zamiast sprawdzać każde kolejne pole na długim pustym odcinku, algorytm "przeskakuje" przez oczywiste fragmenty i zatrzymuje się dopiero w ważnych punktach, czyli jump pointach.

Jump point to pole, w którym dalsza decyzja przestaje być oczywista. Najczęściej dzieje się tak przy przeszkodzie, zakręcie, wymuszonym sąsiedzie albo celu.

### Mechanizm działania

Implementacja składa się z trzech głównych elementów:

1. **FindNeighbors** — wybiera tylko tych sąsiadów, którzy są potrzebni z punktu widzenia kierunku ruchu. To redukuje liczbę gałęzi do sprawdzenia.
2. **Jump** — rekurencyjnie idzie w wybranym kierunku, dopóki nie znajdzie jump pointa, celu albo przeszkody.
3. **RetracePath** — odtwarza pełną ścieżkę, interpolując pola między jump pointami.

### Dlaczego JPS może być szybki

Na otwartej mapie wiele pól jest symetrycznych: przejście przez pole `(x, y)` nie daje żadnej nowej informacji, jeśli i tak idziemy po prostej lub przekątnej. JPS pomija takie pola podczas wyszukiwania. Dzięki temu `ExploredNodes` może być dużo mniejsze niż w A\*.

### Priorytet w `MinHeap`

JPS używa priorytetu podobnego do A\*:

1. najmniejszy `FCost`,
2. przy remisie najmniejszy `HCost`,
3. przy kolejnym remisie najmniejszy identyfikator pozycji `Pos.x * 10000 + Pos.y`.

### Ograniczenia

Klasyczny JPS działa poprawnie przy założeniu jednolitego kosztu ruchu. W tej implementacji dodatkowo obowiązuje zakaz ścinania rogów, dlatego JPS jest traktowany jako osobny wariant algorytmu, a nie jako drugi wzorzec optymalności obok A\* i Dijkstry. Różnice długości względem A\* są raportowane w testach jako informacja, nie jako błąd determinizmu.

Jeżeli pola mają różne koszty, przeskakiwanie przez odcinki mogłoby pominąć ważne informacje o tańszej lub droższej trasie. Dlatego JPS nie jest używany dla map ważonych.

Drugim ograniczeniem jest rekurencja w `Jump()`. Na bardzo dużych, pustych mapach głębokość rekurencji może być duża. W praktycznych rozmiarach testowych jest to akceptowalne, ale warto o tym pamiętać przy skalowaniu.

### Wnioski

JPS powinien być szczególnie mocny na mapach otwartych. Jego przewaga maleje w labiryntach i wąskich korytarzach, bo tam algorytm ma mniej okazji do długich skoków.

---

## 9. Determinizm

### Co zostało zrobione

Każdy algorytm posiada deterministyczne rozstrzyganie remisów w `CompareTo`. Gdy dwa węzły mają taki sam priorytet, o kolejności decyduje pozycja:

```csharp
if (compare == 0)
{
    int posA = X * 10000 + Y;
    int posB = other.X * 10000 + other.Y;
    compare = posA.CompareTo(posB);
}
```

Dla JPS używany jest analogiczny zapis:

```csharp
int posA = Pos.x * 10000 + Pos.y;
int posB = other.Pos.x * 10000 + other.Pos.y;
```

### Dlaczego to ważne

Bez deterministycznego tie-breaka dwa pola o takim samym koszcie mogłyby być wybierane w różnej kolejności zależnie od kolejności dodania do kopca lub szczegółów implementacyjnych. To mogłoby powodować:

- różne ścieżki dla tego samego testu,
- różne metryki między uruchomieniami,
- trudniejsze porównywanie algorytmów,
- problemy z powtarzalnością wyników pracy magisterskiej.

Po audycie obowiązuje zasada:

```text
ten sam input -> ta sama ścieżka -> te same metryki algorytmiczne
```

### Weryfikacja

Deterministyczność sprawdzają testy w:

```text
Assets/Scripts/Pathfinding/Tests/DeterminismTests.cs
```

Testy uruchamiają algorytmy wielokrotnie na tych samych danych i porównują wyniki. Dzięki temu można wykryć sytuacje, w których algorytm raz wybiera jedną równoważną trasę, a innym razem inną.

---

## 10. Benchmark i metryki

### Protokół pomiarowy

Benchmark uruchamia każdy algorytm wiele razy dla tych samych par start-cel. Pierwsza iteracja jest traktowana jako **cold start**, a kolejne jako iteracje "rozgrzane".

To rozróżnienie jest ważne w Unity i C#, ponieważ pierwsze uruchomienie może obejmować dodatkowy koszt JIT, inicjalizacji klas lub alokacji. Gdyby mieszać cold start ze zwykłymi iteracjami, średnia mogłaby nie opisywać typowego działania algorytmu w runtime.

### Randomizacja kolejności algorytmów

Kolejność algorytmów jest mieszana algorytmem Fisher-Yates. Celem jest ograniczenie wpływu kolejności testowania, np. obciążenia CPU, temperatury, cache lub chwilowych zmian wydajności.

### Zapisywane metryki

Benchmark zapisuje do CSV między innymi:

| Metryka | Znaczenie |
|---------|-----------|
| `DistanceBucket` | Kategoria pary testowej: `Short`, `Medium` albo `Long`, liczona po realnej długości najkrótszej ścieżki. |
| `EuclideanDistance` | Odległość w linii prostej między startem i celem; metryka pomocnicza. |
| `OctagonalDistance` | Odległość oktagonalna między startem i celem, liczona dla ruchu 8-kierunkowego bez przeszkód. |
| `ReferenceShortestPathLength` | Referencyjna długość najkrótszej ścieżki użyta przy bucketowaniu test case. |
| `PathFound` | Czy udało się znaleźć ścieżkę. |
| `ColdStartTimeMs` | Czas pierwszego uruchomienia. |
| `AvgExecutionTimeMs` | Średni czas kolejnych uruchomień. |
| `MinExecutionTimeMs` | Najlepszy zmierzony czas. |
| `MaxExecutionTimeMs` | Najgorszy zmierzony czas. |
| `StdDevExecutionTimeMs` | Stabilność pomiaru czasu. |
| `AvgExecutionTicks` | Czas w tickach `Stopwatch`, przydatny dla bardzo szybkich operacji. |
| `AvgGCAllocBytes` | Średnie alokacje pamięci widziane przez GC. |
| `ExploredNodes` | Liczba węzłów faktycznie przetworzonych przez algorytm. |
| `PathLength` | Długość znalezionej ścieżki w jednostkach geometrycznych. |
| `DirectionChanges` | Liczba zmian kierunku na trasie. |
| `PathSmoothness` | `DirectionChanges / PathLength`; im bliżej zera, tym gładsza ścieżka. |
| `PathRecalculations` | Liczba wymuszonych rekalkulacji ścieżki w scenariuszach dynamicznych. |

### Dlaczego `ExploredNodes` jest bardzo ważne

Czas wykonania zależy od sprzętu, obciążenia systemu i środowiska Unity. `ExploredNodes` jest bardziej algorytmiczną metryką: pokazuje, ile pól algorytm musiał naprawdę przeanalizować.

Jeżeli dwa algorytmy mają podobny czas, ale jeden odwiedza znacznie mniej pól, może być bardziej skalowalny na większych mapach.

### Dlaczego mierzona jest gładkość

W grach sama długość ścieżki nie wystarcza. Dwie trasy mogą mieć podobną długość, ale jedna może wyglądać naturalnie, a druga może mieć wiele krótkich zygzaków.

Metryki:

```text
DirectionChanges
PathSmoothness = DirectionChanges / PathLength
```

pomagają ocenić, czy ścieżka jest przyjazna dla ruchu NPC i animacji.

---

## 11. Typy map testowych

### Open Field

`OpenFieldGenerator` tworzy mapy z użyciem Perlin Noise. Przeszkody tworzą bardziej naturalne, organiczne klastry zamiast czysto losowych pojedynczych pól.

Ten typ mapy sprawdza, jak algorytmy radzą sobie na dużych otwartych obszarach. Spodziewane zachowanie:

- JPS powinien mieć dużą przewagę dzięki długim skokom,
- Dijkstra będzie odwiedzał dużo pól,
- A\* powinien utrzymać dobry balans,
- GBFS może być szybki, jeśli heurystyka prowadzi bezpośrednio do celu.

### Scattered Block

`ScatteredBlockGenerator` rozmieszcza regularne bloki przeszkód, np. kwadraty `3x3`. Tworzy to mapy z przewidywalnymi, symetrycznymi przeszkodami.

Ten typ mapy jest użyteczny do testowania:

- zachowania przy częściowo otwartej przestrzeni,
- omijania regularnych blokad,
- skuteczności redukcji symetrii w JPS.

### Room Corridor

`RoomCorridorGenerator` używa podziału BSP, aby tworzyć pokoje połączone korytarzami. Najpierw dzieli mapę na partycje, potem w liściach tworzy pokoje i łączy je korytarzami w kształcie litery L.

Ten typ mapy jest bliski układowi dungeonów. Testuje:

- wąskie gardła,
- sytuacje, gdzie heurystyka wskazuje przez ścianę,
- konieczność znalezienia wejścia do korytarza,
- zachowanie algorytmów w przestrzeniach mieszanych: otwarte pokoje plus wąskie przejścia.

### Maze

`MazeGenerator` tworzy labirynt metodą Recursive Backtracker, czyli DFS z losowym wyborem sąsiadów. Podstawowy labirynt ma dokładnie jedną ścieżkę między wieloma punktami, a późniejszy etap może usuwać część ścian, żeby dodać alternatywne przejścia.

Labirynty są trudne dla algorytmów zachłannych, ponieważ cel może być blisko geometrycznie, ale daleko topologicznie. GBFS może iść w kierunku celu, mimo że prawdziwa droga wymaga oddalenia się od niego.

---

## 12. Porównanie algorytmów

| Cecha | A\* | Dijkstra | GBFS | Custom Greedy | JPS |
|-------|-----|----------|------|---------------|-----|
| Gwarancja znalezienia ścieżki na skończonej mapie | Tak | Tak | Tak | Tak | Tak |
| Optymalność na mapie bez wag | Tak | Tak | Nie | Nie | Teoretycznie tak; w tej implementacji weryfikowany osobno |
| Używa heurystyki | Tak | Nie | Tak | Tak | Tak |
| Używa rzeczywistego kosztu dojścia `GCost` | Tak | Tak | Nie | Tak | Tak |
| Obsługa wag terenu | Tak | Tak | Nie | Tak | Nie |
| Typowy koszt obliczeniowy | `O(E log V)` | `O(E log V)` | `O(V log V)` | `O(E log V)` | zależny od liczby jump pointów |
| Największa zaleta | Dobry balans | Optymalny punkt odniesienia | Szybkie kierowanie do celu | Mniej eksploracji i gładsze trasy | Bardzo szybki na otwartych mapach |
| Największe ryzyko | Zależny od jakości heurystyki | Dużo eksploracji | Suboptymalne trasy | Utrata optymalności | Ograniczenie do uniform cost grid i różnice względem A\* przy zakazie ścinania rogów |

---

## 13. Interpretacja wyników

Przy analizie CSV nie należy patrzeć tylko na jedną kolumnę. Najważniejsze zależności:

- **Niski czas + długa ścieżka** może oznaczać algorytm szybki, ale mało jakościowy.
- **Niski `ExploredNodes` + dobra długość ścieżki** oznacza wysoką skuteczność heurystyki lub optymalizacji.
- **Wysoki `StdDevExecutionTimeMs`** oznacza niestabilny pomiar i wymaga ostrożności przy interpretacji.
- **Wysokie `DirectionChanges`** oznacza trasę z wieloma skrętami.
- **Niski `PathSmoothness`** oznacza trasę bardziej płynną.
- **Duże `AvgGCAllocBytes`** może być problemem w Unity, bo alokacje zwiększają ryzyko przerw związanych z Garbage Collectorem.

Dla pracy magisterskiej szczególnie wartościowe jest zestawienie metryk jakościowych i wydajnościowych. Algorytm najlepszy pod względem czasu nie musi być najlepszy dla gry, jeśli generuje gorsze ścieżki lub działa dobrze tylko na jednej topologii mapy.

---

## 14. Podsumowanie

W module pathfindingu zaimplementowano zestaw algorytmów reprezentujących różne podejścia do wyszukiwania ścieżki:

- Dijkstra pokazuje koszt pełnego, optymalnego przeszukiwania bez heurystyki.
- A\* pokazuje klasyczny kompromis między kosztem dotarcia a przewidywaniem kierunku do celu.
- GBFS pokazuje zachowanie algorytmu czysto zachłannego.
- Custom Greedy bada praktyczny kompromis dla gry: większa szybkość i gładsza ścieżka kosztem optymalności.
- JPS pokazuje, ile można zyskać przez wykorzystanie struktury siatki i redukcję symetrii.

Dodano też mechanizmy ważne dla rzetelnego porównania:

- deterministyczne rozstrzyganie remisów,
- wspólny format wyniku,
- pomiar cold startu i iteracji rozgrzanych,
- pomiar alokacji GC,
- metryki gładkości ścieżki,
- proceduralne topologie map testowych.

Dzięki temu benchmark nie odpowiada wyłącznie na pytanie "który algorytm jest najszybszy?", ale również na pytanie ważniejsze dla gry: który algorytm daje najlepszy kompromis między szybkością, stabilnością, jakością trasy i odpornością na różne typy map.
