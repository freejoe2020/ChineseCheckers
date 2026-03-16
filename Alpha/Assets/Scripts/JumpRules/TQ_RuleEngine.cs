using System.Collections.Generic;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// Game Rule Engine (Player-Side Subclass)
    /// Inherits pure calculation core from TQ_RuleCore
    /// Extends with player-side state modification logic
    /// Separates pure AI calculations from state-changing player operations
    /// </summary>
    public class TQ_RuleEngine : TQ_RuleCore
    {
        #region Player-Side Core Methods (With State Modifications)
        /// <summary>
        /// Player-side: Calculate valid moves + modify game state (highlights, temporary states)
        /// Combines pure calculation from parent class with state changes for player feedback
        /// </summary>
        /// <param name="piece">Selected piece to calculate moves for</param>
        /// <param name="moveContext">Context to store move paths</param>
        /// <param name="board">Game board interface (uses initialized board if null)</param>
        /// <returns>List of valid move target cells</returns>
        public List<ITQ_HexCell> GetValidMoves(ITQ_ChessPiece piece, TQ_MoveContext moveContext, ITQ_HexBoard board = null)
        {
            var boardToUse = board ?? _board;
            ITQ_HexCell initialCell = piece.CurrentCell;

            // Comprehensive null validation
            if (piece == null || initialCell == null || moveContext == null || boardToUse == null)
                return new List<ITQ_HexCell>();

            // 1. Player-side: Reset board visual states (pure calculation parent doesn't do this)
            // Clears previous highlights/valid move markers for clean UI feedback
            boardToUse.ResetAllCellStates();

            // 2. Reuse parent pure calculation: Get valid moves (no state changes)
            moveContext.Clear();
            moveContext.SelectedPiece = piece;
            var validMoves = new List<ITQ_HexCell>();

            // Basic moves: reuse parent's pure calculation method
            validMoves.AddRange(base.CalculateBasicMovesPure(piece, moveContext, boardToUse));

            // 3. Jump moves: call parent pure recursion + player-side temporary state modification
            var jumpPathMap = new Dictionary<ITQ_HexCell, List<ITQ_HexCell>>();
            bool originalOccupiedState = initialCell.IsOccupied;

            // State modification: use try/finally to guarantee state restoration (player-side core)
            // Critical: prevents state pollution if calculation is interrupted
            try
            {
                // Temporarily mark initial cell as unoccupied for jump calculation
                // This allows recursive jump checks to work correctly
                initialCell.IsOccupied = false;

                // Check jump possibilities in all 6 directions using parent's pure method
                for (int dirIndex = 0; dirIndex < 6; dirIndex++)
                {
                    base.CheckLongJumpRecursivePure(initialCell, dirIndex, jumpPathMap, initialCell);
                }
            }
            finally
            {
                // Guaranteed state restoration: revert to original occupancy state
                // Prevents permanent state changes from temporary calculation state
                initialCell.IsOccupied = originalOccupiedState;
            }

            // Merge jump move results into valid moves list
            foreach (var kvp in jumpPathMap)
            {
                if (!validMoves.Contains(kvp.Key))
                {
                    validMoves.Add(kvp.Key);
                }
                // Store jump path in context for animation playback
                moveContext.AddJumpPath(kvp.Key, kvp.Value);
            }

            // 4. Player-side: Mark valid moves on piece (parent pure calculation doesn't do this)
            // Updates piece state for UI visualization of valid moves
            piece.MarkValidMoves(validMoves);
            return validMoves;
        }

        /// <summary>
        /// Player-side: Execute move + modify board/piece state
        /// Validates move with parent pure calculation before making state changes
        /// </summary>
        /// <param name="piece">Piece to move</param>
        /// <param name="targetCell">Target cell to move piece to</param>
        /// <param name="board">Game board interface (uses initialized board if null)</param>
        /// <returns>True if move was successfully executed</returns>
        public bool ExecuteMove(ITQ_ChessPiece piece, ITQ_HexCell targetCell, ITQ_HexBoard board = null)
        {
            var boardToUse = board ?? _board;

            // Basic validity checks with logging
            if (piece == null || targetCell == null || targetCell.IsOccupied || boardToUse == null)
            {
                Debug.LogWarning($"Move failed: Invalid parameters or target cell occupied");
                return false;
            }

            // First validate with parent pure calculation: ensures logical consistency
            // Critical: prevents invalid state changes from illegal moves
            if (!base.ExecuteMovePure(piece, targetCell, boardToUse))
            {
                Debug.LogWarning($"Move failed: Target not in valid moves list");
                return false;
            }

            // Debug.Log($"Move successful: ({piece.CurrentCell.Q},{piece.CurrentCell.R}) → ({targetCell.Q},{targetCell.R})");

            // Player-side: Modify game state (parent pure calculation doesn't do this)
            // Update source cell state
            piece.CurrentCell.IsOccupied = false;
            piece.CurrentCell.CurrentPiece = null;

            // Update piece position
            piece.CurrentCell = targetCell;

            // Update target cell state
            targetCell.IsOccupied = true;
            targetCell.CurrentPiece = piece;

            return true;
        }

        /// <summary>
        /// Player-side: Check win condition (reuses parent pure calculation)
        /// Extends parent logic with player-side logging/feedback
        /// </summary>
        /// <param name="owner">Player to check win condition for</param>
        /// <param name="targetCamp">Target camp coordinates for win condition</param>
        /// <param name="board">Game board interface (uses initialized board if null)</param>
        /// <returns>True if win condition is met</returns>
        public bool CheckWinCondition(TQ_PieceOwner owner, List<Vector2Int> targetCamp, ITQ_HexBoard board = null)
        {
            var boardToUse = board ?? _board;

            // Fully reuse parent pure calculation logic for consistency
            bool isWin = base.CheckWinConditionPure(owner, targetCamp, boardToUse);

            // Player-side extension: add win condition logging
            if (isWin)
            {
                Debug.Log($"[{owner}] has met the win condition!");
            }

            return isWin;
        }
        #endregion
    }
}