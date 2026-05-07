using Pathfinding.Core;

namespace Pathfinding.MapGenerators
{
    /// <summary>
    /// Interfejs dla generatorów map proceduralnych.
    /// Wzorzec Strategy — każdy typ topologii (Open, Maze, Room, Block) 
    /// implementuje ten interfejs, co pozwala na łatwą wymianę w benchmarkach.
    /// </summary>
    public interface IMapGenerator
    {
        /// <summary>
        /// Nazwa typu topologii (np. "OpenField", "Maze", "RoomCorridor", "ScatteredBlock").
        /// Używana do etykietowania wyników w CSV.
        /// </summary>
        string TopologyName { get; }

        /// <summary>
        /// Generuje mapę o zadanych wymiarach i zagęszczeniu przeszkód.
        /// </summary>
        /// <param name="width">Szerokość mapy</param>
        /// <param name="height">Wysokość mapy</param>
        /// <param name="obstacleDensity">Zagęszczenie przeszkód (0.0–1.0)</param>
        /// <param name="seed">Deterministyczny seed RNG</param>
        /// <returns>Wygenerowana GridMap</returns>
        GridMap Generate(int width, int height, float obstacleDensity, int seed);
    }
}
