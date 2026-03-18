using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// Rule Core Base Class
    /// Implements pure calculation logic for AI side only (no side effects)
    /// Extracts all common sub-methods for reuse by subclasses
    /// Stateless design: only holds board reference for calculation context
    /// </summary>
    public class TQ_RuleCore
    {
        // Only keep board reference needed for AI calculations (no state dependencies)
        /// <summary>
        /// Reference to game board interface (for calculation context only)
        /// Set during initialization, no state modifications through this reference
        /// </summary>
        protected ITQ_HexBoard _board;

        /// <summary>
        /// Initializes rule core with board reference
        /// Pure initialization (no state changes to board)
        /// </summary>
        /// <param name="board">Game board interface reference</param>
        public virtual void Init(ITQ_HexBoard board)
        {
            _board = board;
        }

        #region Common Pure Calculation Sub-Methods (Reusable by Subclasses)
        /// <summary>
        /// Pure calculation: Basic moves (adjacent cells in 6 directions)
        /// No state modifications to piece/board, only calculates valid moves
        /// </summary>
        /// <param name="piece">Piece to calculate moves for</param>
        /// <param name="moveContext">Context to store calculated paths</param>
        /// <param name="board">Game board interface</param>
        /// <returns>List of valid basic move target cells</returns>
        protected List<ITQ_HexCell> CalculateBasicMovesPure(ITQ_ChessPiece piece, TQ_MoveContext moveContext, ITQ_HexBoard board)
        {
            var validMoves = new List<ITQ_HexCell>();

            // Check all 6 hexagonal directions for adjacent empty cells
            for (int dirIndex = 0; dirIndex < 6; dirIndex++)
            {
                var targetCell = piece.CurrentCell.GetCellInDirection(dirIndex, 1);

                // Valid if target exists and is unoccupied
                if (targetCell != null && !targetCell.IsOccupied)
                {
                    validMoves.Add(targetCell);

                    // Basic move path: start cell → target cell (for animation)
                    var basicPath = new List<ITQ_HexCell> { piece.CurrentCell, targetCell };
                    moveContext.AddJumpPath(targetCell, basicPath);
                }
            }

            return validMoves;
        }

        /// <summary>
        /// Pure calculation: Validate jump path validity (core common logic)
        /// Checks if jump landing path is clear of occupied cells
        /// </summary>
        /// <param name="dirLineCells">All cells in the direction line</param>
        /// <param name="bIndex">Index of piece being jumped over</param>
        /// <param name="pIndex">Index of potential landing position</param>
        /// <param name="originalStartCell">Original starting cell (to ignore in checks)</param>
        /// <returns>True if jump path is valid (clear of occupied cells)</returns>
        protected bool CheckJumpPathValidity(List<ITQ_HexCell> dirLineCells, int bIndex, int pIndex, ITQ_HexCell originalStartCell)
        {
            // Out of bounds check
            if (pIndex >= dirLineCells.Count) return false;

            // Scenario 1: N=0 (A and B are adjacent) → Only check if landing spot is empty
            // Ignores original start cell in occupancy check
            if (bIndex == 0)
            {
                ITQ_HexCell targetCell = dirLineCells[pIndex];
                bool targetOccupied = targetCell.IsOccupied && targetCell != originalStartCell;
                return targetCell != null && !targetOccupied;
            }
            // Scenario 2: N>0 → Validate all cells between B and P are continuously empty
            else
            {
                // Check each cell between jumped piece and landing spot
                for (int i = bIndex + 1; i <= pIndex; i++)
                {
                    var checkCell = dirLineCells[i];
                    bool isOccupied = checkCell.IsOccupied && checkCell != originalStartCell;

                    // Invalid if any cell in path is occupied
                    if (isOccupied) return false;
                }
                return true;
            }
        }
        #endregion

        #region AI Core Pure Calculation Methods (Direct Call by Subclasses)
        /// <summary>
        /// Pure calculation version of GetValidMoves
        /// No state modifications to piece or board - only returns valid move list
        /// </summary>
        /// <param name="piece">Piece to calculate valid moves for</param>
        /// <param name="moveContext">Context to store jump paths</param>
        /// <param name="board">Game board interface</param>
        /// <returns>List of all valid move targets (basic + jump moves)</returns>
        public virtual List<ITQ_HexCell> GetValidMovesPure(ITQ_ChessPiece piece, TQ_MoveContext moveContext, ITQ_HexBoard board)
        {
            // Comprehensive null safety check
            if (piece == null || piece.CurrentCell == null || moveContext == null || board == null)
                return new List<ITQ_HexCell>();

            // 1. Clear context (no board state modification)
            moveContext.Clear();
            moveContext.SelectedPiece = piece;

            var validMoves = new List<ITQ_HexCell>();
            var initialCell = piece.CurrentCell;

            // 2. Reuse common sub-method: Calculate basic adjacent moves
            validMoves.AddRange(CalculateBasicMovesPure(piece, moveContext, board));

            // 3. Calculate jump moves (pure calculation, no state changes)
            var jumpPathMap = new Dictionary<ITQ_HexCell, List<ITQ_HexCell>>();

            // Check jump possibilities in all 6 directions.
            // IMPORTANT: For consistency with player-side GetValidMoves, temporarily mark the initial cell as unoccupied
            // during jump recursion, then restore it. This prevents self/occupied artifacts in pure results.
            bool originalOccupiedState = initialCell.IsOccupied;
            try
            {
                initialCell.IsOccupied = false;
                for (int dirIndex = 0; dirIndex < 6; dirIndex++)
                {
                    CheckLongJumpRecursivePure(initialCell, dirIndex, jumpPathMap, initialCell);
                }
            }
            finally
            {
                initialCell.IsOccupied = originalOccupiedState;
            }

            // Merge jump move results into valid moves list
            foreach (var kvp in jumpPathMap)
            {
                if (!validMoves.Contains(kvp.Key))
                {
                    validMoves.Add(kvp.Key);
                }

                // check point for debugging
                var path = kvp.Value;
                var pathLast = path != null && path.Count > 0 ? path[path.Count - 1] : null;
                if (pathLast == null || pathLast.Q != kvp.Key.Q || pathLast.R != kvp.Key.R)
                {
                    Debug.LogWarning($"[RuleCore] MoveContext path mismatch: targetKey=({kvp.Key.Q},{kvp.Key.R}), pathLast=({pathLast?.Q},{pathLast?.R}), pathCount={path?.Count ?? 0}. " +
                        "Path: " + (path != null ? string.Join("→", path.Select(c => $"({c.Q},{c.R})")) : "null"));
                }

                // Store jump path in context for animation use
                moveContext.AddJumpPath(kvp.Key, kvp.Value);
            }

            // 4. Core pure calculation principle: return only legal target cells (never self, never occupied).
            // This keeps pure results consistent with real rule validation and prevents illegal AI moves.
            validMoves = validMoves
                .Where(c => c != null && c != initialCell && !c.IsOccupied)
                .Distinct()
                .ToList();

            return validMoves;
        }

        /// <summary>
        /// Pure calculation version of recursive jump check
        /// No state modifications, only calculates valid jump paths
        /// Handles multi-step continuous jumps (Chinese checkers core mechanic)
        /// </summary>
        /// <param name="currentCell">Current position in jump sequence</param>
        /// <param name="dirIndex">Direction index (0-5) to check</param>
        /// <param name="jumpPathMap">Map to store valid jump targets and paths</param>
        /// <param name="originalStartCell">Original starting cell (for occupancy check exclusion)</param>
        /// <param name="visitedCells">Track visited cells (prevent infinite recursion)</param>
        /// <param name="currentPath">Current jump path being built</param>
        /// <param name="recursionDepth">Current recursion depth (prevent stack overflow)</param>
        /// <param name="previousCell">Previous cell (prevent backtracking)</param>
        public virtual void CheckLongJumpRecursivePure(
            ITQ_HexCell currentCell,
            int dirIndex,
            Dictionary<ITQ_HexCell, List<ITQ_HexCell>> jumpPathMap,
            ITQ_HexCell originalStartCell,
            HashSet<ITQ_HexCell> visitedCells = null,
            List<ITQ_HexCell> currentPath = null,
            int recursionDepth = 0,
            ITQ_HexCell previousCell = null)
        {
            // 1. Basic safety checks
            // Prevent infinite recursion with depth limit (20 steps max)
            if (recursionDepth > 20) return;

            // Lazy initialize visited cells set (track to prevent cycles)
            visitedCells ??= new HashSet<ITQ_HexCell>();

            // Lazy initialize current path (starts with current cell)
            currentPath ??= new List<ITQ_HexCell> { currentCell };

            // Prevent revisiting same cell in current jump sequence
            if (!visitedCells.Add(currentCell)) return;

            // 2. Get all cells in the specified direction line
            var dirLineCells = currentCell.GetAllCellsInDirection(dirIndex);

            // No cells in this direction → backtrack
            if (dirLineCells.Count == 0)
            {
                visitedCells.Remove(currentCell);
                return;
            }

            // 3. Find piece to jump over (ignore original start cell in occupancy check)
            int emptyCountA2B = 0;
            ITQ_HexCell pieceBCell = null;

            // Search for first occupied cell in direction line (piece to jump over)
            for (int i = 0; i < dirLineCells.Count; i++)
            {
                var checkCell = dirLineCells[i];
                bool isOccupied = checkCell.IsOccupied && checkCell != originalStartCell;

                if (isOccupied)
                {
                    pieceBCell = checkCell;
                    emptyCountA2B = i;
                    break;
                }
            }

            // No piece to jump over → terminate recursion for this direction
            if (pieceBCell == null)
            {
                visitedCells.Remove(currentCell);
                return;
            }

            // 4. Reuse common sub-method: Validate jump path validity
            int bIndex = emptyCountA2B;
            int pIndex = bIndex + emptyCountA2B + 1;
            bool isPathValid = CheckJumpPathValidity(dirLineCells, bIndex, pIndex, originalStartCell);

            // 5. If path valid → record jump and check for continuous jumps
            if (isPathValid && pIndex < dirLineCells.Count)
            {
                ITQ_HexCell jumpTargetCell = dirLineCells[pIndex];
                bool targetOccupied = jumpTargetCell.IsOccupied && jumpTargetCell != originalStartCell;

                // Valid jump target: exists, unoccupied, and not previous position
                if (jumpTargetCell != null && !targetOccupied && jumpTargetCell != previousCell)
                {
                    // Create new path with jump target added
                    var newPath = new List<ITQ_HexCell>(currentPath) { jumpTargetCell };
                    //jumpPathMap[jumpTargetCell] = newPath;
                    if (!jumpPathMap.TryGetValue(jumpTargetCell, out var existing) || newPath.Count < existing.Count)
                        jumpPathMap[jumpTargetCell] = newPath;

                    // check point for debug
                    var lastCell = newPath[newPath.Count - 1];
                    if (lastCell.Q != jumpTargetCell.Q || lastCell.R != jumpTargetCell.R)
                    {
                        Debug.LogWarning($"[RuleCore] Path key mismatch: key=({jumpTargetCell.Q},{jumpTargetCell.R}), path last=({lastCell.Q},{lastCell.R}), pathCount={newPath.Count}, recursionDepth={recursionDepth}, dirIndex={dirIndex}. " +
                            "Path: " + string.Join("→", newPath.Select(c => $"({c.Q},{c.R})")));
                    }

                    // Recursively check for continuous jumps from new position
                    // Check all 6 directions for additional jump possibilities
                    for (int newDirIndex = 0; newDirIndex < 6; newDirIndex++)
                    {
                        CheckLongJumpRecursivePure(
                            jumpTargetCell,
                            newDirIndex,
                            jumpPathMap,
                            originalStartCell,
                            new HashSet<ITQ_HexCell>(visitedCells), // New visited set for each branch
                            newPath,
                            recursionDepth + 1,
                            currentCell); // Prevent backtracking to previous position
                    }
                }
            }

            // Backtrack: remove current cell from visited set for other branches
            visitedCells.Remove(currentCell);
        }
        #endregion

        #region General Utility Methods (Pure Calculation)
        /// <summary>
        /// Pure calculation: Validate if move is legal (no state changes)
        /// Checks if target cell is in piece's valid moves list
        /// </summary>
        /// <param name="piece">Piece to move</param>
        /// <param name="targetCell">Target cell to move to</param>
        /// <param name="board">Game board interface</param>
        /// <returns>True if move is valid/legal</returns>
        public virtual bool ExecuteMovePure(ITQ_ChessPiece piece, ITQ_HexCell targetCell, ITQ_HexBoard board)
        {
            // Basic validity checks
            if (piece == null || targetCell == null || targetCell.IsOccupied || board == null)
                return false;

            // Pure calculation: only validate, no state modification
            var tempContext = new TQ_MoveContext();
            var validMoves = GetValidMovesPure(piece, tempContext, board);

            // Check if target cell exists in valid moves list (matching coordinates)
            return validMoves.Any(cell => cell.Q == targetCell.Q && cell.R == targetCell.R);
        }

        /// <summary>
        /// Pure calculation: Check win condition (no state changes)
        /// Chinese checkers win condition: 10+ pieces in target camp
        /// </summary>
        /// <param name="owner">Player to check win condition for</param>
        /// <param name="targetCamp">Target camp coordinates</param>
        /// <param name="board">Game board interface</param>
        /// <returns>True if win condition is met</returns>
        public virtual bool CheckWinConditionPure(TQ_PieceOwner owner, List<Vector2Int> targetCamp, ITQ_HexBoard board)
        {
            if (board == null) return false;

            // Get all pieces for the player
            var pieces = board.GetPiecesByOwner(owner);

            // Count pieces in target camp (10+ pieces to win)
            int count = pieces.Count(p => targetCamp.Contains(new Vector2Int(p.CurrentCell.Q, p.CurrentCell.R)));
            return count >= 10;
        }
        #endregion
    }
}