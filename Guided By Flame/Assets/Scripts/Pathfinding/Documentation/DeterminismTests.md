# Testy Determinizmu Systemu Pathfindingu

Niniejszy dokument wyjaśnia metodologię i cel testów jednostkowych napisanych w pliku `DeterminismTests.cs`. Testy te stanowią kręgosłup badawczy pracy magisterskiej, gwarantując, że pozyskane dane analityczne (na podstawie których tworzone będą np. wykresy) są powtarzalne, matematycznie poprawne i obiektywne.

## Dlaczego determinizm jest kluczowy?
W badaniach naukowych algorytm deterministyczny to taki, który dla takich samych danych wejściowych **zawsze** zwraca dokładnie ten sam wynik. Wiele standardowych algorytmów (szczególnie opartych na strukturach takich jak `MinHeap`) w języku C# ulega tzw. losowości sprzętowej (ang. *hardware non-determinism*). Wynika to z faktu, że gdy dwa węzły mają taki sam priorytet, system kolejkuje je losowo na podstawie adresów w pamięci lub kolejności iteracji pętli. Prowadzi to do powstawania różnych wariantów najkrótszej ścieżki i zaburza odczyt metryk takich jak liczba skrzyżowań czy kroków. Nasz system używa mechanizmu **"Positional Tiebreaking"** (rozwiązywanie remisów na podstawie sztywnej pozycji X/Y), co eliminuje tę wadę.

Aby potwierdzić to naukowo, zbudowano 6-fazowy moduł testujący:

---

## Zestawy Testowe (Opis i Metodologia)

### 1. Zestaw 1: Powtarzalność (Repeatability)
- **Co testujemy?** Każdy z 5 algorytmów jest uruchamiany `100 razy` na całkowicie nowych (pustych) instancjach na tej samej mapie od punktu A do B.
- **Jak weryfikujemy?** Rejestrujemy wynik pierwszej próby (tzw. próbki referencyjnej) i porównujemy z nią 99 kolejnych odtworzeń. 
- **Czego oczekujemy?** Każda próba musi zwrócić dokładnie taką samą wartość zbadanych węzłów (`ExploredNodes`), ułamkową długość trasy (`PathLength`) i tę samą flagę istnienia ścieżki (`PathFound`). To żelazny dowód na eliminację losowości we wszystkich algorytmach.

### 2. Zestaw 2: Konsystencja Optymalnych (Optimal Consistency)
- **Co testujemy?** Algorytmy `A*` i `Dijkstra` to klasy tzw. algorytmów optymalnych (gwarantują matematycznie najkrótszą drogę). Testujemy czy na konkretnej mapie oba dojdą do tego samego wniosku.
- **Jak weryfikujemy?** Badamy kilka mocno zróżnicowanych tras start-meta i w każdym przypadku odpalamy `A*`, `Dijkstra` i `JPS`. Porównujemy zmiennoprzecinkową długość wyznaczonej trasy `PathLength` używając tolerancji `0.01f`.
- **Czego oczekujemy?** `A*` musi uzyskać długość trasy co do milimetra taką samą jak `Dijkstra` (nawet jeśli oba algorytmy odwiedziły w poszukiwaniu inną liczbę węzłów).
- **Uwaga naukowa dotycząca JPS:** Algorytm JPS działa na odmiennych zasadach "ścinania rogów" (corner cutting) niż A*. Z tego powodu, z naukowego punktu widzenia, JPS może znajdować odmienne geometrycznie ścieżki i nie musi osiągać takiej samej długości co A*. W teście traktujemy tę różnicę jako `INFO`, by udokumentować odmienne zachowanie JPS.

### 3. Zestaw 3: Topologie Map (Topology Determinism)
- **Co testujemy?** Stabilność algorytmów na radykalnie zróżnicowanych kształtach otoczenia. 
- **Jak weryfikujemy?** Generujemy proceduralnie 3 rodzaje map o parametrach typowych dla testów badawczych (Labirynt, Złożone Pokoje-Korytarze, Rozrzucone Bloki). Moduł wybiera start i cel, a następnie wykonuje cichy skan `BFS`, by sprawdzić, czy trasa istnieje.
- **Czego oczekujemy?** Algorytmy muszą działać deterministycznie na każdej z topologii, niezależnie od stopnia uwięzienia i zagęszczenia ścian.

### 4. Zestaw 4: Wagi Terenu (Terrain Costs)
- **Co testujemy?** Nie wszystkie pola na mapie są "równe" (np. ruch przez bagno kosztuje x5, przez las x2). Testujemy czy algorytmy potrafią zachować determinizm, gdy nawigują po zróżnicowanych "kosztach wejścia" pól.
- **Jak weryfikujemy?** Rozrzucamy na planszy pola kosztujące x2, x5 lub x10 ze sztywnym Seedem generatora losowego. Weryfikujemy `A*`, `Dijkstrę` i `Greedy` (JPS nie obsługuje wag).
- **Czego oczekujemy?** Po 50 wywołaniach każdy z algorytmów nadal wyznacza identyczną ścieżkę – matematyka floating-point połączona z priorytetami z sąsiedztwa i heuristic priority nie psuje zasady determinizmu.

### 5. Zestaw 5: Przypadki Brzegowe (Edge Cases)
- **Co testujemy?** Wytrzymałość na dane wejściowe rzadkie/zepsute.
- **Jak weryfikujemy?** Szykujemy trzy specyficzne operacje:
  - Ustawiamy Start w tym samym miejscu co Cel.
  - Ustawiamy Cel na kratce obok Startu.
  - Budujemy niezniszczalny mur dookoła Celu, blokując do niego jakikolwiek dostęp.
- **Czego oczekujemy?** Algorytmy nie mogą ulec awarii (crash, exception, memory leak, infinite loop). Brak trasy (zamknięcie w klatce) powinien zostać łagodnie zaraportowany jako `PathFound=false` ze zmierzoną liczbą odwiedzonych ślepych zaułków, a ścieżka zerowa powinna mieć `PathLength=0`.

### 6. Zestaw 6: Determinizm Pełnej Ścieżki
- **Co testujemy?** Jest to najbardziej rygorystyczny test geometrii ruchu.
- **Jak weryfikujemy?** Podczas sprawdzania determinizmu, algorytm iteruje po każdym jednym kroku zwróconej ścieżki i upewnia się `Path[step] == Reference[step]`. 
- **Czego oczekujemy?** Nie wystarczy, że długość ułamkowa drogi była identyczna na końcu pomiarów. Determinizm ścieżki potwierdza, że wektorowo postać poszła za każdym uruchomieniem od początku do końca w taki sam układ zakrętów. Postać nigdy nie wykona losowego skosu "bo na remis algorytm tym razem zadecydował inaczej". Wyniki w tablicach wygenerowane z tak ścisłego rygoru nadają się na wysokiej jakości publikacje w inżynierii programowania i data science.
