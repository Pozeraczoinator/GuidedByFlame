using UnityEngine;
using Pathfinding.Core;

namespace Pathfinding.MapGenerators
{
    /// <summary>
    /// Generator map typu "Open Field" z szumem Perlin Noise.
    /// 
    /// Produkuje naturalne, organiczne klastry przeszkód zamiast jednolitego 
    /// losowego rozkładu. Perlin Noise gwarantuje koherencję przestrzenną —
    /// przeszkody tworzą wyspy i ciągłe formy, co jest bardziej realistyczne
    /// niż czysto losowy szum.
    /// 
    /// Kluczowe cechy dla benchmarku:
    /// - JPS powinien dominować na tym typie map (duże otwarte przestrzenie = długie skoki)
    /// - Dijkstra eksploruje nieproporcjonalnie dużo węzłów (brak heurystyki)
    /// - A* i GBFS powinny wykazywać zbliżoną wydajność
    /// 
    /// Parametr scale kontroluje "ziarnistość" szumu:
    /// - Mały scale (0.05) = duże, gładkie wyspy przeszkód
    /// - Duży scale (0.3) = drobne, rozproszone przeszkody
    /// </summary>
    public class OpenFieldGenerator : IMapGenerator
    {
        public string TopologyName => "OpenField";

        /// <summary>
        /// Skala szumu Perlin. Kontroluje rozmiar klastrów przeszkód.
        /// Domyślnie 0.15 — kompromis między dużymi wyspami a drobnymi przeszkodami.
        /// </summary>
        private readonly float _noiseScale;

        public OpenFieldGenerator(float noiseScale = 0.15f)
        {
            _noiseScale = noiseScale;
        }

        public GridMap Generate(int width, int height, float obstacleDensity, int seed)
        {
            obstacleDensity = Mathf.Clamp01(obstacleDensity);
            bool[,] walkable = new bool[width, height];

            // Offset oparty na seed — Perlin Noise w Unity jest deterministyczny
            // dla tych samych współrzędnych, więc offset przesuwa "okno" szumu
            float offsetX = seed * 100.0f;
            float offsetY = seed * 100.0f + 50.0f;

            // Kalibracja progu: Perlin Noise w Unity zwraca wartości ~0.0–1.0
            // z rozkładem zbliżonym do normalnego wokół 0.5.
            // Próg dobieramy tak, aby ~obstacleDensity% pól było przeszkodami.
            float threshold = obstacleDensity;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float noiseValue = Mathf.PerlinNoise(
                        x * _noiseScale + offsetX,
                        y * _noiseScale + offsetY
                    );

                    // Pole jest walkable jeśli szum >= próg (przeszkoda gdy < próg)
                    walkable[x, y] = (noiseValue >= threshold);
                }
            }

            return new GridMap(walkable);
        }
    }
}
