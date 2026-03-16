using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Free.Checkers
{
    /// <summary>
    /// AI Business Layer (Version 1)
    /// Responsible for game business logic, state management, and move execution (decoupled from algorithm logic)
    /// Bridges algorithm calculations with actual game state modifications
    /// </summary>
    public class TQ_CheckerAIManagerV1 : TQ_CheckerAIManagerMinMaxV1, ICheckerAIManager
    {
        [Header("Business Configuration")]
        [Tooltip("Delay (seconds) before AI makes a move (simulates thinking time)")]
        public float aiThinkDelay = 1f;

        [Tooltip("Animation delay (seconds) for each AI move step")]
        public float aiMoveAnimationDelay = 0.2f;

        // Core game dependencies (only held by business layer)
        /// <summary>
        /// Reference to game manager (controls game state/flow)
        /// Critical for turn management and game state transitions
        /// </summary>
        private TQ_CheckerGameManager _gameManager;

        /// <summary>
        /// Reference to board manager (controls physical board representation)
        /// Used to access real game board state and execute moves
        /// </summary>
        private TQ_HexBoardManager _boardManager;

        #region Lifecycle Management
        /// <summary>
        /// Awake: Initialize business layer dependencies
        /// Finds critical game services and validates their existence
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            // Find game manager (critical for business logic)
            _gameManager = FindFirstObjectByType<TQ_CheckerGameManager>();
            if (_gameManager == null)
            {
                DebugLogError("TQ_CheckerGameManager not found!");
                enabled = false;
                return;
            }
        }

        /// <summary>
        /// Business layer initialization (external interface)
        /// Sets up AI with board reference and initializes algorithm layer
        /// </summary>
        /// <param name="boardManager">Board manager reference (access to real game board)</param>
        public void Init(TQ_HexBoardManager boardManager)
        {
            _boardManager = boardManager;

            // Validate board model initialization
            if (_boardManager?.boardGenerator?.BoardModel == null)
            {
                DebugLogError("Board model not initialized!");
                return;
            }

            // Initialize algorithm layer (decoupled from BoardManager - only pass data)
            // Get enemy target positions (bottom camp for standard game)
            var enemyTargetPos = _boardManager.boardGenerator.GetCampTriangle("Bottom") ?? new List<Vector2Int>();

            // Initialize MinMax algorithm with board model and target positions
            base.InitMinMax(_boardManager.boardGenerator.BoardModel, enemyTargetPos, TQ_AIDifficulty.Medium);
        }
        #endregion

        #region Core Business Logic: Execute AI Turn
        /// <summary>
        /// Execute AI turn (external business interface)
        /// Main entry point for AI move execution
        /// </summary>
        public void ExecuteAITurn()
        {
            // State validation: only execute if it's AI's turn
            if (_gameManager == null || _gameManager.CurrentState != TQ_GameState.EnemyTurn)
            {
                DebugLogWarning($"AI turn skipped: Current state {_gameManager?.CurrentState}");
                _gameManager?.SwitchToPlayerTurn();
                return;
            }

            // Simulate thinking delay (human-like behavior)
            CancelInvoke(nameof(DoAIMove));
            Invoke(nameof(DoAIMove), aiThinkDelay);
        }

        /// <summary>
        /// Actual AI move execution logic
        /// Isolates AI calculations from real game state using snapshots
        /// </summary>
        private void DoAIMove()
        {
            // 0. Get reference to real game board (for validation)
            var realBoard = _boardManager?.boardGenerator?.BoardModel;

            // 1. Create board snapshot (isolated from real game state)
            // Critical: prevents AI calculations from modifying real game state
            var boardSnapshot = _boardStateService.CreateBoardSnapshot();
            if (boardSnapshot == null)
            {
                _gameManager.SwitchToPlayerTurn();
                return;
            }

            // 2. Get valid enemy pieces from snapshot (AI-controlled pieces)
            var enemyPieces = GetValidEnemyPieces(boardSnapshot);
            if (enemyPieces.Count == 0)
            {
                DebugLogWarning("No valid AI pieces");
                _gameManager.SwitchToPlayerTurn();
                _boardStateService.ReleaseBoardSnapshot(boardSnapshot);
                return;
            }

            // 3. Call algorithm layer to calculate best move (only coordinate information)
            var bestMove = CalculateBestMove(boardSnapshot, enemyPieces);

            // Clean up snapshot (memory management)
            _boardStateService.ReleaseBoardSnapshot(boardSnapshot);

            // 4. Execute best move if found (with coordinate mapping to real board)
            if (bestMove != null)
                ExecuteBestMove(bestMove);
            else
                _gameManager.SwitchToPlayerTurn();
        }

        /// <summary>
        /// Execute best move (core fix: coordinate mapping + real board validation)
        /// Translates snapshot-based move to real game board execution
        /// </summary>
        /// <param name="snapshotBestMove">Best move calculated from board snapshot</param>
        private void ExecuteBestMove(TQAI_AIMove snapshotBestMove)
        {
            // 1. Log best move information (business layer only handles logging)
            LogBestMove(snapshotBestMove);

            // 2. Extract coordinates from snapshot result (critical: no snapshot object passing)
            var pieceQ = snapshotBestMove.piece.CurrentCell.Q;
            var pieceR = snapshotBestMove.piece.CurrentCell.R;
            var targetQ = snapshotBestMove.targetCell.Q;
            var targetR = snapshotBestMove.targetCell.R;

            // 3. Get reference to real game board
            var realBoard = _boardManager?.boardGenerator?.BoardModel;
            if (realBoard == null)
            {
                DebugLogError("Real board not initialized!");
                _gameManager.SwitchToPlayerTurn();
                return;
            }

            // 3.1 Find real piece matching snapshot coordinates
            var realPiece = realBoard.EnemyPieces.FirstOrDefault(p =>
                p != null && p.CurrentCell != null &&
                p.CurrentCell.Q == pieceQ && p.CurrentCell.R == pieceR);

            if (realPiece == null)
            {
                DebugLogError($"Real piece not found: Coordinates ({pieceQ},{pieceR})");
                _gameManager.SwitchToPlayerTurn();
                return;
            }

            // 3.2 Find real target cell matching snapshot coordinates
            var realTargetCell = realBoard.GetCellByCoordinates(targetQ, targetR);
            if (realTargetCell == null)
            {
                DebugLogError($"Real target cell not found: Coordinates ({targetQ},{targetR})");
                _gameManager.SwitchToPlayerTurn();
                return;
            }

            // 4. Re-validate move on real board (critical: prevent state inconsistency)
            if (!ValidateRealBoardMove(realPiece, realTargetCell, realBoard))
            {
                DebugLogWarning($"Real board move validation failed: Piece ({pieceQ},{pieceR}) → Target ({targetQ},{targetR})");
                _gameManager.SwitchToPlayerTurn();
                return;
            }

            // 5. Execute AI move through GameManager (core modification: use MoveAIPiece)
            var moveSuccess = _gameManager.MoveAIPiece(realPiece, realTargetCell);

            if (!moveSuccess)
            {
                DebugLogWarning($"AI move failed: Real piece ({pieceQ},{pieceR}) → Real target ({targetQ},{targetR})");
                _gameManager.SwitchToPlayerTurn();
            }
            else
            {
                // DebugLog("AI move command submitted, waiting for execution/animation completion");
            }
        }

        /// <summary>
        /// Validate move on real game board
        /// Ensures calculated move is still valid on actual game state
        /// </summary>
        /// <param name="realPiece">Real AI-controlled piece</param>
        /// <param name="realTargetCell">Real target cell</param>
        /// <param name="realBoard">Real game board</param>
        /// <returns>True if move is valid on real board</returns>
        private bool ValidateRealBoardMove(TQ_ChessPieceModel realPiece, TQ_HexCellModel realTargetCell, TQ_HexBoardModel realBoard)
        {
            // Basic null validation
            if (realPiece == null || realTargetCell == null || realBoard == null)
                return false;

            // Validation 1: Target cell is unoccupied
            if (realTargetCell.IsOccupied)
            {
                DebugLogWarning($"Real target cell occupied: ({realTargetCell.Q},{realTargetCell.R})");
                return false;
            }

            // Validation 2: Target cell is in piece's valid moves on real board
            var tempContext = new TQ_MoveContext();
            var validMoves = _ruleEngine.GetValidMoves(realPiece, tempContext, realBoard);
            var isValidTarget = validMoves.Any(cell =>
                cell != null && cell.Q == realTargetCell.Q && cell.R == realTargetCell.R);

            if (!isValidTarget)
            {
                DebugLogWarning($"Real target cell not a valid move: ({realTargetCell.Q},{realTargetCell.R})");
            }

            return isValidTarget;
        }
        #endregion

        #region Business Helper Methods
        /// <summary>
        /// Set AI difficulty (external business interface)
        /// Updates algorithm difficulty setting
        /// </summary>
        /// <param name="difficulty">New AI difficulty level</param>
        public void SetDifficulty(TQ_AIDifficulty difficulty)
        {
            CurrentDifficulty = difficulty;
            DebugLog($"AI difficulty set to: {difficulty}");
        }

        /// <summary>
        /// Log best move information for debugging/monitoring
        /// Formats move data for human-readable logging
        /// </summary>
        /// <param name="move">AI move to log</param>
        private void LogBestMove(TQAI_AIMove move)
        {
            if (move == null) return;

            // Format path as human-readable string
            var pathStr = string.Join(" → ", move.movePath.Select(c => $"({c.Q},{c.R})"));

            // Create comprehensive log message
            var log = $"【AI Move】Piece: ({move.piece.CurrentCell.Q},{move.piece.CurrentCell.R}) → " +
                      $"Target: ({move.targetCell.Q},{move.targetCell.R}) | Score: {move.score:F2} | " +
                      $"Path: {pathStr}";

            DebugLog(log);
        }
        #endregion

        #region Deprecated Validation Methods (Preserved for Reference)
        /// <summary>
        /// Log all piece positions on board (for debugging/validation)
        /// </summary>
        private void LogBoardPiecePositions(TQ_HexBoardModel board, string boardName)
        {
            if (board == null)
            {
                DebugLogWarning($"{boardName} is null, cannot log piece positions");
                return;
            }

            // Log enemy pieces
            DebugLog($"{boardName} - Enemy pieces ({board.EnemyPieces.Count}):");
            foreach (var piece in board.EnemyPieces)
            {
                if (piece != null && piece.CurrentCell != null)
                {
                    DebugLog($"  - Enemy piece: ({piece.CurrentCell.Q},{piece.CurrentCell.R})");
                }
            }

            // Log player pieces
            DebugLog($"{boardName} - Player pieces ({board.PlayerPieces.Count}):");
            foreach (var piece in board.PlayerPieces)
            {
                if (piece != null && piece.CurrentCell != null)
                {
                    DebugLog($"  - Player piece: ({piece.CurrentCell.Q},{piece.CurrentCell.R})");
                }
            }
        }

        /// <summary>
        /// Compare piece positions between two boards (for snapshot validation)
        /// </summary>
        private bool CompareBoardPiecePositions(TQ_HexBoardModel realBoard, TQ_HexBoardModel snapshotBoard)
        {
            if (realBoard == null || snapshotBoard == null)
                return false;

            // Compare enemy piece count
            if (realBoard.EnemyPieces.Count != snapshotBoard.EnemyPieces.Count)
            {
                DebugLogError($"Enemy piece count mismatch: Real={realBoard.EnemyPieces.Count}, Snapshot={snapshotBoard.EnemyPieces.Count}");
                return false;
            }

            // Compare player piece count
            if (realBoard.PlayerPieces.Count != snapshotBoard.PlayerPieces.Count)
            {
                DebugLogError($"Player piece count mismatch: Real={realBoard.PlayerPieces.Count}, Snapshot={snapshotBoard.PlayerPieces.Count}");
                return false;
            }

            // Compare each enemy piece position
            for (int i = 0; i < realBoard.EnemyPieces.Count; i++)
            {
                var realPiece = realBoard.EnemyPieces[i];
                var snapshotPiece = snapshotBoard.EnemyPieces[i];

                if (realPiece == null || snapshotPiece == null ||
                    realPiece.CurrentCell == null || snapshotPiece.CurrentCell == null ||
                    realPiece.CurrentCell.Q != snapshotPiece.CurrentCell.Q ||
                    realPiece.CurrentCell.R != snapshotPiece.CurrentCell.R)
                {
                    DebugLogError($"Enemy piece position mismatch: Index={i}, Real=({realPiece?.CurrentCell?.Q},{realPiece?.CurrentCell?.R}), Snapshot=({snapshotPiece?.CurrentCell?.Q},{snapshotPiece?.CurrentCell?.R})");
                    return false;
                }
            }

            // Compare each player piece position
            for (int i = 0; i < realBoard.PlayerPieces.Count; i++)
            {
                var realPiece = realBoard.PlayerPieces[i];
                var snapshotPiece = snapshotBoard.PlayerPieces[i];

                if (realPiece == null || snapshotPiece == null ||
                    realPiece.CurrentCell == null || snapshotPiece.CurrentCell == null ||
                    realPiece.CurrentCell.Q != snapshotPiece.CurrentCell.Q ||
                    realPiece.CurrentCell.R != snapshotPiece.CurrentCell.R)
                {
                    DebugLogError($"Player piece position mismatch: Index={i}, Real=({realPiece?.CurrentCell?.Q},{realPiece?.CurrentCell?.R}), Snapshot=({snapshotPiece?.CurrentCell?.Q},{snapshotPiece?.CurrentCell?.R})");
                    return false;
                }
            }

            DebugLog("✅ Snapshot board matches real board piece positions exactly");
            return true;
        }
        #endregion
    }
}