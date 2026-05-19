namespace Pathfinding.Core
{
    /// <summary>
    /// Runtime-only switches shared by pathfinding algorithms.
    /// They do not change search behavior, only optional diagnostic data.
    /// </summary>
    public static class PathfindingRuntimeOptions
    {
        public static bool RecordExploredNodesHistory = true;
    }
}
