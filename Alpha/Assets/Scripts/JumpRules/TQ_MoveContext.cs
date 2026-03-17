using System.Collections.Generic;

namespace Free.Checkers
{
    /// <summary>
    /// Movement Context
    /// Stores temporary data for a single move operation (valid moves, jump paths)
    /// Lifecycle: Piece selection �� Move calculation �� Move completion (immediate clearing)
    /// Isolated context prevents cross-contamination between different move operations
    /// </summary>
    public class TQ_MoveContext
    {
        /// <summary>
        /// Currently selected piece for movement
        /// Serves as the source piece for all path calculations in this context
        /// </summary>
        public ITQ_ChessPiece SelectedPiece { get; set; }

        /// <summary>
        /// Mapping of valid move targets to their corresponding jump paths
        /// Key: Target cell | Value: List of cells forming the jump path to target
        /// Optimizes animation by precomputing movement paths
        /// </summary>
        public Dictionary<ITQ_HexCell, List<ITQ_HexCell>> JumpPathMap { get; private set; }

        /// <summary>
        /// Constructor: Initializes empty movement context
        /// Sets up jump path dictionary for path storage
        /// </summary>
        public TQ_MoveContext()
        {
            JumpPathMap = new Dictionary<ITQ_HexCell, List<ITQ_HexCell>>();
        }

        /// <summary>
        /// Adds valid move target and its corresponding jump path
        /// Performs null/empty validation to prevent invalid path storage
        /// </summary>
        /// <param name="targetCell">Valid move destination cell</param>
        /// <param name="path">List of cells forming the jump path to target (intermediate steps)</param>
        public void AddJumpPath(ITQ_HexCell targetCell, List<ITQ_HexCell> path)
        {
            // Safety validation: Skip null/invalid paths to prevent runtime errors
            if (targetCell == null || path == null || path.Count == 0) return;

            // Store path for target cell (overwrites existing path if duplicate)
            JumpPathMap[targetCell] = path;
        }

        /// <summary>
        /// Retrieves precomputed jump path for specified target cell
        /// Returns empty list if target has no path (safe fallback)
        /// </summary>
        /// <param name="targetCell">Destination cell to get path for</param>
        /// <returns>Jump path to target (empty list if no path exists)</returns>
        public List<ITQ_HexCell> GetJumpPath(ITQ_HexCell targetCell)
        {
            // Safety check: Return empty list for null target or missing path
            if (targetCell == null || !JumpPathMap.ContainsKey(targetCell))
            {
                return new List<ITQ_HexCell>();
            }

            // Return precomputed path for valid target
            return JumpPathMap[targetCell];
        }

        /// <summary>
        /// Clears all context data (call after move completion/cancellation)
        /// Prevents memory leaks and context contamination between moves
        /// Resets to initial state for reuse
        /// </summary>
        public void Clear()
        {
            // Clear selected piece reference
            SelectedPiece = null;

            // Clear all path data (main memory cleanup)
            JumpPathMap.Clear();
        }
    }
}