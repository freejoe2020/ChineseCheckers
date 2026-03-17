using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Free.Checkers
{
    /// <summary>
    /// AI Business Layer V2
    /// Handles game business logic, state management, and move execution (decoupled from algorithms)
    /// Key responsibilities: game state validation, real board interaction, move execution
    /// </summary>
    public class TQ_CheckerAIManagerV2 : TQ_CheckerAIManagerEndgameV2, ICheckerAIManager
    {
        [Header("Business Configuration")]
        [Tooltip("AI thinking delay (simulates human decision time)")]
        public float aiThinkDelay = 1f;

        [Tooltip("Delay between AI move steps (animation timing)")]
        public float aiMoveAnimationDelay = 0.2f;

        // Game core dependencies (only held by business layer)
        /// <summary>
        /// Board manager reference (for real board interaction)
        /// </summary>
        private TQ_HexBoardManager _boardManager;

        #region Lifecycle Management
        /// <summary>
        /// Business layer initialization (external interface)
        /// Decouples algorithm layer from board manager dependency
        /// </summary>
        /// <param name="boardManager">Game board manager (source of real game state)</param>
        public void Init(TQ_HexBoardManager boardManager)
        {
            // Store board manager reference (critical for real game state access)
            _boardManager = boardManager;

            // Validate board model initialization
            if (_boardManager?.boardGenerator?.BoardModel == null)
            {
                DebugLogError("Board model not initialized! AI cannot function without valid board state");
                return;
            }

            // Initialize algorithm layer with decoupled data (only board model + target positions)
            // This separates business logic from algorithm implementation
            var enemyTargetPos = _boardManager.boardGenerator.GetCampTriangle("Bottom") ?? new List<Vector2Int>();
            base.InitMinMax(_boardManager.boardGenerator.BoardModel, enemyTargetPos, CurrentDifficulty);

            DebugLog($"AI initialized successfully - Target positions count: {enemyTargetPos.Count}");
        }
        #endregion

        #region Core Business Logic: Execute AI Turn
        /// <summary>
        /// Execute AI turn (primary external business interface)
        /// Validates game state and triggers AI decision process
        /// </summary>
        public void ExecuteAITurn()
        {
            // Critical state validation (prevent AI from acting out of turn)
            if (_gameManager == null || _gameManager.CurrentState != TQ_GameState.EnemyTurn)
            {
                DebugLogWarning($"AI turn skipped: Current game state is {_gameManager?.CurrentState} (expected: EnemyTurn)");

                // Safely switch to player turn if in invalid state
                _gameManager?.SwitchToPlayerTurn();
                return;
            }

            // Simulate human thinking with delay (improves game feel)
            // Cancel any pending AI moves to prevent duplicate execution
            CancelInvoke(nameof(DoAIMove));
            Invoke(nameof(DoAIMove), aiThinkDelay);

            DebugLog($"AI thinking... (delay: {aiThinkDelay}s)");
        }

        /// <summary>
        /// Actual AI move execution logic (decoupled from snapshot dependency)
        /// Works directly with real game board state
        /// </summary>
        private void DoAIMove()
        {
            // 1. Get real game board (source of truth)
            var realBoard = _boardManager?.boardGenerator?.BoardModel;
            if (realBoard == null)
            {
                DebugLogError("Real game board is null - cannot execute AI move");
                _gameManager?.SwitchToPlayerTurn();
                return;
            }

            // 2. Get valid enemy pieces from real board
            var enemyPieces = GetValidEnemyPieces(realBoard);
            if (enemyPieces.Count == 0)
            {
                DebugLogWarning("No valid AI pieces available to move");
                _gameManager?.SwitchToPlayerTurn();
                return;
            }

            // 3. Calculate optimal move using algorithm layer (works with real board state)
            var bestMove = CalculateBestMove(realBoard, enemyPieces);

            // 4. Execute optimal move (or switch to player turn if no valid move)
            if (bestMove != null)
            {
                ExecuteBestMove(bestMove);
            }
            else
            {
                DebugLogWarning("No valid AI move found - switching to player turn");
                _gameManager?.SwitchToPlayerTurn();
            }
        }

        /// <summary>
        /// Execute optimal move (core fix: coordinate mapping + real board validation)
        /// Critical: Validates all moves against real board state before execution
        /// </summary>
        /// <param name="bestMove">Optimal move calculated by algorithm layer</param>
        private void ExecuteBestMove(TQAI_AIMove bestMove)
        {
            // 1. Log move details for debugging/analytics
            LogBestMove(bestMove);

            // 2. Extract coordinates from move (algorithm layer result)
            int pieceQ = bestMove.piece.CurrentCell.Q;
            int pieceR = bestMove.piece.CurrentCell.R;
            int targetQ = bestMove.targetCell.Q;
            int targetR = bestMove.targetCell.R;

            // 3. Get real board reference (source of truth)
            var realBoard = _boardManager?.boardGenerator?.BoardModel;
            if (realBoard == null)
            {
                DebugLogError("Real board reference is null - cannot execute move");
                _gameManager?.SwitchToPlayerTurn();
                AIMovePool.Release(bestMove); // Critical: return to pool to prevent memory leaks
                return;
            }

            // 3.1 Find real piece on real board (coordinate mapping)
            // Critical: Algorithm layer pieces might not reference real game objects
            var realPiece = realBoard.EnemyPieces.FirstOrDefault(p =>
                p != null &&
                p.CurrentCell != null &&
                p.CurrentCell.Q == pieceQ &&
                p.CurrentCell.R == pieceR);

            if (realPiece == null)
            {
                DebugLogError($"Real piece not found at coordinates ({pieceQ},{pieceR}) - move aborted");
                _gameManager?.SwitchToPlayerTurn();
                AIMovePool.Release(bestMove);
                return;
            }

            // 3.2 Find real target cell on real board (coordinate mapping)
            var realTargetCell = realBoard.GetCellByCoordinates(targetQ, targetR);
            if (realTargetCell == null)
            {
                DebugLogError($"Real target cell not found at coordinates ({targetQ},{targetR}) - move aborted");
                _gameManager?.SwitchToPlayerTurn();
                AIMovePool.Release(bestMove);
                return;
            }

            // 4. Validate move against real board state (critical safety check)
            // Prevents invalid moves caused by state changes between calculation and execution
            if (!ValidateRealBoardMove(realPiece, realTargetCell, realBoard))
            {
                DebugLogWarning($"Move validation failed on real board: Piece({pieceQ},{pieceR}) → Target({targetQ},{targetR})");
                _gameManager?.SwitchToPlayerTurn();
                AIMovePool.Release(bestMove);
                return;
            }

            // 5. Execute move through game manager (business logic entry point)
            DebugLog($"Executing AI move: Real piece ({realPiece.CurrentCell.Q},{realPiece.CurrentCell.R}) → Target ({realTargetCell.Q},{realTargetCell.R})");
            var moveSuccess = _gameManager.MoveAIPiece(realPiece, realTargetCell);

            // 6. Handle move result
            if (!moveSuccess)
            {
                DebugLogWarning($"AI move execution failed: Real piece ({pieceQ},{pieceR}) → Real target ({targetQ},{targetR})");
                _gameManager?.SwitchToPlayerTurn();
            }

            // Critical: Return move object to pool (prevent memory leaks)
            AIMovePool.Release(bestMove);
            DebugLog("AI move object returned to pool");
        }

        /// <summary>
        /// Validate move against real board state (comprehensive validation)
        /// Ensures move complies with game rules before execution
        /// </summary>
        /// <param name="realPiece">Real game piece from board</param>
        /// <param name="realTargetCell">Real target cell from board</param>
        /// <param name="realBoard">Real game board (source of truth)</param>
        /// <returns>True if move is valid on real board</returns>
        private bool ValidateRealBoardMove(TQ_ChessPieceModel realPiece, TQ_HexCellModel realTargetCell, TQ_HexBoardModel realBoard)
        {
            // Basic null validation
            if (realPiece == null || realTargetCell == null || realBoard == null)
            {
                DebugLogError("Validation failed: Null reference detected");
                return false;
            }

            // Validation 1: Target cell must be unoccupied
            if (realTargetCell.IsOccupied)
            {
                DebugLogWarning($"Validation failed: Target cell ({realTargetCell.Q},{realTargetCell.R}) is occupied");
                return false;
            }

            // Validation 2: Target cell must be valid move for this piece (rule engine validation)
            // Use rule engine from object pool (performance optimization)
            var ruleEngine = RuleEnginePool.Get();
            try
            {
                ruleEngine.Init(realBoard);
                var tempContext = new TQ_MoveContext();

                // Get all valid moves for this piece on real board
                var validMoves = ruleEngine.GetValidMoves(realPiece, tempContext, realBoard);

                // Check if target cell is in valid moves list
                var isValidTarget = validMoves.Any(cell =>
                    cell != null &&
                    cell.Q == realTargetCell.Q &&
                    cell.R == realTargetCell.R);

                if (!isValidTarget)
                {
                    DebugLogWarning($"Validation failed: Target cell ({realTargetCell.Q},{realTargetCell.R}) is not a valid move for this piece");
                }

                return isValidTarget;
            }
            finally
            {
                // Critical: Return rule engine to pool (prevent memory leaks)
                RuleEnginePool.Release(ruleEngine);
            }
        }
        #endregion

        #region Business Helper Methods
        /// <summary>
        /// Set AI difficulty (external business interface)
        /// Allows dynamic difficulty adjustment during gameplay
        /// </summary>
        /// <param name="difficulty">New AI difficulty level</param>
        public void SetDifficulty(TQ_AIDifficulty difficulty)
        {
            CurrentDifficulty = difficulty;
            DebugLog($"AI difficulty updated to: {difficulty}");

            // Adjust search depth immediately (affects next AI turn)
            switch (difficulty)
            {
                case TQ_AIDifficulty.Easy:
                    easySearchDepth = 2;
                    break;
                case TQ_AIDifficulty.Medium:
                    mediumSearchDepth = 3;
                    break;
                case TQ_AIDifficulty.Hard:
                    hardSearchDepth = 4;
                    break;
            }
        }

        public void NotifyPositionAfterMove(TQ_HexBoardModel board)
        {
            if (board != null) RecordGamePosition(board);
        }

        /// <summary>
        /// Log optimal move details (debugging/analytics)
        /// Provides comprehensive move information for troubleshooting
        /// </summary>
        /// <param name="move">AI move to log</param>
        private void LogBestMove(TQAI_AIMove move)
        {
            if (move == null) return;

            // Format path string for readability
            var pathStr = move.movePath != null && move.movePath.Count > 0
                ? string.Join(" → ", move.movePath.Select(c => $"({c.Q},{c.R})"))
                : "No path";

            // Comprehensive move log
            var log = $"【AI Move Decision】" +
                      $"  Piece Position: ({move.piece.CurrentCell.Q},{move.piece.CurrentCell.R})" +
                      $"  Target Position: ({move.targetCell.Q},{move.targetCell.R})" +
                      $"  Move Score: {move.score:F2}" +
                      $"  Is Jump Move: {move.isJumpMove}" +
                      $"  Jump Steps: {move.jumpStepCount}" +
                      $"  Path: {pathStr}" +
                      $"  Difficulty: {CurrentDifficulty}";

            DebugLog(log);
        }
        #endregion
    }
}