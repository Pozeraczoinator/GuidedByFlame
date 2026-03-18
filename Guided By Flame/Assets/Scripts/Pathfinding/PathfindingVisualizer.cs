using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Pathfinding.Core;
using Pathfinding.Algorithms;

namespace Pathfinding.Visualization
{
    public class PathfindingVisualizer : MonoBehaviour
    {
        public enum AlgorithmChoice { AStar, Dijkstra, GreedyBestFirst, CustomGreedy, JumpPointSearch }

        [Header("Konfiguracja Algorytmu")]
        public AlgorithmChoice selectedAlgorithm = AlgorithmChoice.AStar;
        public string mapFileName = "Map.txt";
        public string testCasesFileName = "TestCases.csv";
        
        [Header("Wizualizacja (Własny Grid ze skryptu)")]
        [Tooltip("Prefabrykat kwadratu bazowego mapy (zwykły Sprite)")]
        public GameObject basemapPrefab;
        [Tooltip("Prefabrykat poruszającego się agenta (kostki)")]
        public GameObject agentPrefab;
        public float visualizationStepDelay = 0.05f; // Wydłużone dla przejrzystości
        public float agentMoveSpeed = 10.0f; // Szybkość kostki

        [Header("Kolory Bazy")]
        public Color colorWalkable = Color.white;
        public Color colorObstacle = new Color(0.1f, 0.1f, 0.1f); // Ciemny szary / czarny
        public Color colorExplored = new Color(0.6f, 0.8f, 1f, 0.8f); // Jasnoniebieski po odwiedzeniu
        public Color colorStart = Color.red;                          // Start: Czerwony
        public Color colorTarget = Color.green;                       // Cel: Zielony

        private GridMap _gridMap;
        private List<TestCase> _testCases = new List<TestCase>();
        private int _currentTestCaseIndex = 0;
        
        // Pula zoptymalizowana do wyświetlania mapy bazowej
        private SpriteRenderer[,] _basemapRenderers;
        private GameObject _agentObject;
        private bool _isVisualizing = false;
        private bool _isAutoRunning = false;

        private struct TestCase
        {
            public int startX, startY;
            public int targetX, targetY;
        }

        private void Start()
        {
            LoadTestCases();
            if (LoadGridMap())
            {
                GenerateBasemapVisuals();
                Debug.Log("PathfindingVisualizer gotowy. Wciśnij SPACJĘ aby uruchomić tryb automatyczny!");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space) && !_isAutoRunning)
            {
                _isAutoRunning = true;
                StartCoroutine(AutoRunAllCases());
            }
        }

        private IEnumerator AutoRunAllCases()
        {
            Debug.Log("[Visualizer] Start automatycznego testowania po kolei...");
            string resultsPath = Path.Combine(Application.dataPath, "..", $"{selectedAlgorithm}_results.csv");
            
            // Otwieramy plik do logowania. Nadpisujemy z 'false' na starcie.
            using (StreamWriter writer = new StreamWriter(resultsPath, false))
            {
                writer.AutoFlush = true; // Gwarancja zapisu
                writer.WriteLine("TestID;StartX;StartY;TargetX;TargetY;PathFound;ExecutionTimeMs;ExploredNodes;PathLength;SimulatedFPS");

                while (_currentTestCaseIndex < _testCases.Count)
                {
                    RunNextTestCase(writer);
                    
                    // Czekaj aż wizualizacja pojedynczej ścieżki i animacja kostki się zakończy
                    while (_isVisualizing)
                    {
                        yield return null;
                    }
                    
                    // Odczekaj 2 sekundy przed rozpoczęciem kolejnej ścieżki
                    yield return new WaitForSeconds(2.0f);
                }
            }
            
            Debug.Log($"[Visualizer] Wszystkie trasy ukończone i zapisane do pliku {resultsPath}");
            _isAutoRunning = false;
        }

        private bool LoadGridMap()
        {
            string path = mapFileName;
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "..", mapFileName);
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "../..", mapFileName);

            if (!File.Exists(path))
            {
                Debug.LogError($"Nie znaleziono pliku mapy txt: {path}");
                return false;
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) return false;

            int height = lines.Length;
            int width = lines[0].Length;

            bool[,] collisionData = new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                string line = lines[height - 1 - y];
                for (int x = 0; x < width; x++)
                {
                    if (x < line.Length)
                    {
                        // 0 = wolne, 1 = sciana
                        collisionData[x, y] = (line[x] == '0');
                    }
                    else collisionData[x, y] = false;
                }
            }

            _gridMap = new GridMap(collisionData);
            return true;
        }

        private void GenerateBasemapVisuals()
        {
            int width = _gridMap.Width;
            int height = _gridMap.Height;
            _basemapRenderers = new SpriteRenderer[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector3 worldPos = new Vector3(x, y, 0);
                    GameObject cell = Instantiate(basemapPrefab, worldPos, Quaternion.identity, this.transform);
                    cell.name = $"Basemap_{x}_{y}";
                    cell.transform.localScale = new Vector3(0.95f, 0.95f, 1f); // Margin
                    
                    SpriteRenderer sr = cell.GetComponent<SpriteRenderer>();
                    sr.color = _gridMap.IsWalkable(x, y) ? colorWalkable : colorObstacle;

                    _basemapRenderers[x, y] = sr;
                }
            }

            // Stworzenie agenta i ukrycie go na początku
            if (agentPrefab != null)
            {
                _agentObject = Instantiate(agentPrefab, Vector3.zero, Quaternion.identity);
                _agentObject.name = "PathfindingAgent";
                _agentObject.SetActive(false);
            }
        }

        private void LoadTestCases()
        {
            string path = testCasesFileName;
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "..", testCasesFileName);
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "../..", testCasesFileName);
            
            if (!File.Exists(path))
            {
                Debug.LogError($"Nie znaleziono pliku: {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var columns = lines[i].Split(',');
                if (columns.Length >= 4)
                {
                    _testCases.Add(new TestCase
                    {
                        startX = int.Parse(columns[0]),
                        startY = int.Parse(columns[1]),
                        targetX = int.Parse(columns[2]),
                        targetY = int.Parse(columns[3])
                    });
                }
            }
        }

        private void RunNextTestCase(StreamWriter csvWriter = null)
        {
            if (_testCases.Count == 0 || _gridMap == null) return;
            if (_currentTestCaseIndex >= _testCases.Count)
            {
                Debug.Log("Zakończono wszystkie testy z pliku CSV! Reset.");
                _currentTestCaseIndex = 0;
            }

            int testId = _currentTestCaseIndex;
            TestCase testCase = _testCases[_currentTestCaseIndex];
            _currentTestCaseIndex++;

            Vector2Int startPos = new Vector2Int(testCase.startX, testCase.startY);
            Vector2Int targetPos = new Vector2Int(testCase.targetX, testCase.targetY);

            IPathfindingAlgorithm algorithm = selectedAlgorithm switch
            {
                AlgorithmChoice.AStar => new AStarAlgorithm(),
                AlgorithmChoice.Dijkstra => new DijkstraAlgorithm(),
                AlgorithmChoice.GreedyBestFirst => new GreedyBestFirstAlgorithm(),
                AlgorithmChoice.CustomGreedy => new CustomGreedyAlgorithm(),
                AlgorithmChoice.JumpPointSearch => new JumpPointSearchAlgorithm(),
                _ => new AStarAlgorithm()
            };

            Debug.Log($"[Visualizer] Rozpoczęto poszukiwanie algorytmem {selectedAlgorithm} dla start={startPos}, cel={targetPos}...");
            Pathfinding.Core.PathfindingResult result = algorithm.FindPath(_gridMap, startPos, targetPos);
            
            // Zapisz na bieżąco do pliku CSV dla aktualnie oglądanej trasy
            if (csvWriter != null)
            {
                double simulatedFrameTime = 10.0 + result.ExecutionTimeMs;
                double simulatedFPS = 1000.0 / simulatedFrameTime;
                csvWriter.WriteLine($"{testId};{startPos.x};{startPos.y};{targetPos.x};{targetPos.y};{result.PathFound};{result.ExecutionTimeMs.ToString("F4")};{result.ExploredNodes};{result.PathLength.ToString("F2")};{simulatedFPS.ToString("F2")}");
            }

            if (result.PathFound) 
            {
                Debug.Log($"[Visualizer] ZNALEZIONO DROGĘ! Długość: {result.PathLength}, Węzłów odwiedzonych: {result.ExploredNodes}.");
                StartCoroutine(VisualizeRoutine(result, startPos, targetPos));
            }
            else
            {
                Debug.LogWarning($"[Visualizer] BRAK DROGI! Ścieżka zablokowana lub poza mapą.");
                StartCoroutine(WaitEmptyVisualization());
            }
        }

        // Dummy Coroutine aby w przypadku ślepego zaułka automat poczekał chwilę by oko ludzkie zdążyło to zarejestrować
        private IEnumerator WaitEmptyVisualization()
        {
            _isVisualizing = true;
            yield return new WaitForSeconds(0.2f);
            _isVisualizing = false;
        }

        private IEnumerator VisualizeRoutine(Pathfinding.Core.PathfindingResult result, Vector2Int startPos, Vector2Int targetPos)
        {
            _isVisualizing = true;

            // Zresetuj wszystkie podświetlenia przed nowym seansem
            for (int x = 0; x < _gridMap.Width; x++)
            {
                for (int y = 0; y < _gridMap.Height; y++)
                {
                    _basemapRenderers[x, y].color = _gridMap.IsWalkable(x, y) ? colorWalkable : colorObstacle;
                }
            }

            // Oznacz START i KONIEC trwale (aby się wyróżniały - Punkt A: Czerwony, Punkt B: Zielony)
            if (startPos.x < _gridMap.Width && startPos.y < _gridMap.Height && startPos.x >= 0 && startPos.y >= 0)
                _basemapRenderers[startPos.x, startPos.y].color = colorStart;
                
            if (targetPos.x < _gridMap.Width && targetPos.y < _gridMap.Height && targetPos.x >= 0 && targetPos.y >= 0)
                _basemapRenderers[targetPos.x, targetPos.y].color = colorTarget;

            if (_agentObject != null) _agentObject.SetActive(false);

            if (!result.PathFound)
            {
                _isVisualizing = false;
                yield break;
            }

            // 1. Opcjonalnie: pokazywanie odwiedzonych punktów (Wylanie wody / Explored)
            foreach (Vector2Int pos in result.ExploredNodesHistory)
            {
                if (pos != startPos && pos != targetPos)
                {
                    if (pos.x < _gridMap.Width && pos.y < _gridMap.Height && pos.x >= 0 && pos.y >= 0)
                    {
                        _basemapRenderers[pos.x, pos.y].color = colorExplored;
                        yield return new WaitForSeconds(visualizationStepDelay);
                    }
                }
            }

            yield return new WaitForSeconds(0.2f);

            // 2. Animacja poruszającej się kostki
            if (_agentObject != null)
            {
                _agentObject.SetActive(true);
                _agentObject.transform.position = new Vector3(startPos.x, startPos.y, -2f); // Wyraźne wysunięcie do kamery Z=-2
                
                // Przemieszczaj się punkt po punkcie
                foreach (Vector2Int step in result.Path)
                {
                    Vector3 nextPosition = new Vector3(step.x, step.y, -2f);
                    
                    while (Vector3.Distance(_agentObject.transform.position, nextPosition) > 0.01f)
                    {
                        _agentObject.transform.position = Vector3.MoveTowards(_agentObject.transform.position, nextPosition, agentMoveSpeed * Time.deltaTime);
                        yield return null;
                    }
                    _agentObject.transform.position = nextPosition; // snap
                }
            }
            else
            {
                Debug.LogWarning("[Visualizer] Brak podpiętego Agent Prefab w inspektorze! Kwadrat nie będzie się poruszać.");
            }

            Debug.Log($"[Visualizer] Pokaz animacji kostki zakończony.");
            _isVisualizing = false;
        }
    }
}
