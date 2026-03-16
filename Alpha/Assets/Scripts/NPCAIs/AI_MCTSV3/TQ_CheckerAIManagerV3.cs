using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using Free.H2D;

namespace Free.Checkers
{
    /// <summary>
    /// AI Manager V3 (MCTS-based)
    /// Integrates Monte Carlo Tree Search with A* endgame harvesting and V2 heuristics
    /// Fully compatible with existing game manager infrastructure
    /// </summary>
    public class TQ_CheckerAIManagerV3 : ZFMonoBehaviour, ICheckerAIManager
    {
        [Header("MCTS Core Configuration")]
        [Tooltip("Base iteration count (overridden by difficulty settings)")]
        public int iterations = 1500;

        [Tooltip("UCT exploration constant (¡Ì2 ¡Ö 1.414 is standard)")]
        public float explorationWeight = 1.414f;

        [Tooltip("Current AI difficulty level")]
        public TQ_AIDifficulty CurrentDifficulty;

        [Header("Difficulty Mapping")]
        [Tooltip("Iteration count for Easy difficulty (faster, less optimal)")]
        public int easyIterations = 400;

        [Tooltip("Iteration count for Medium difficulty (balanced)")]
        public int mediumIterations = 1000;

        [Tooltip("Iteration count for Hard difficulty (slower, more optimal)")]
        public int hardIterations = 2500;

        [Header("Performance Tuning")]
        [Tooltip("Batch size for parallel MCTS iterations (prevents frame drops)")]
        public int batchSize = 50;

        [Tooltip("Max frame time budget (ms) for MCTS processing")]
        public int maxFrameTimeMs = 15;

        // Core dependencies
        private TQ_MCTSProcessorV3 _processor;
        private TQ_BoardStateService _stateService;
        private TQ_CheckerGameManager _gameManager;
        private List<Vector2Int> _cachedTargets;
        private TQ_HexBoardManager _boardManager;

        #region Initialization & Setup
        /// <summary>
        /// Initialize AI manager (compatible with existing game infrastructure)
        /// </summary>
        /// <param name="boardManager">Game board manager reference</param>
        public void Init(TQ_HexBoardManager boardManager)
        {
            // Validate dependencies
            if (boardManager == null)
            {
                DebugLogError("BoardManager is null - AI initialization failed");
                return;
            }

            _boardManager = boardManager;

            // Initialize MCTS processor with exploration parameter
            _processor = new TQ_MCTSProcessorV3(explorationWeight);

            // Find required game services (compatible with existing setup)
            _stateService = FindFirstObjectByType<TQ_BoardStateService>();
            _gameManager = FindFirstObjectByType<TQ_CheckerGameManager>();

            // Validate critical services
            if (_stateService == null || _gameManager == null)
            {
                DebugLogError("Missing critical game services - AI may not function correctly");
            }

            // Get AI target positions (Player's home base)
            _cachedTargets = _boardManager.boardGenerator.GetCampTriangle("Bottom") ?? new List<Vector2Int>();

            // Initialize board state service and MCTS processor
            if (_stateService != null)
            {
                _stateService.Init(_boardManager.boardModel);
            }

            _processor.SetTargetPositions(_cachedTargets);

            DebugLog($"AI V3 initialized successfully - Target positions: {_cachedTargets.Count}");
        }

        /// <summary>
        /// Set AI difficulty (compatible with GameManager calls)
        /// Updates iteration count based on difficulty preset
        /// </summary>
        /// <param name="difficulty">New difficulty level</param>
        public void SetDifficulty(TQ_AIDifficulty difficulty)
        {
            CurrentDifficulty = difficulty;

            // Map difficulty to iteration count (performance/quality tradeoff)
            iterations = difficulty switch
            {
                TQ_AIDifficulty.Easy => easyIterations,
                TQ_AIDifficulty.Medium => mediumIterations,
                TQ_AIDifficulty.Hard => hardIterations,
                _ => mediumIterations // Default to medium for unknown difficulty
            };

            DebugLog($"AI difficulty updated: {difficulty} | Iterations: {iterations}");
        }
        #endregion

        #region AI Turn Execution (Main Entry Points)
        /// <summary>
        /// Main AI turn entry point (called by GameManager)
        /// Triggers MCTS thinking process
        /// </summary>
        public void ExecuteAITurn()
        {
            // Validate game state
            if (_gameManager == null || _gameManager.CurrentState != TQ_GameState.EnemyTurn)
            {
                DebugLogWarning($"AI turn skipped - Invalid game state: {_gameManager?.CurrentState}");
                _gameManager?.SwitchToPlayerTurn();
                return;
            }

            StartAI();
        }

        /// <summary>
        /// Start AI thinking process (coroutine-based to prevent frame drops)
        /// </summary>
        public void StartAI()
        {
            // Stop any existing AI processes to prevent conflicts
            StopAllCoroutines();

            // Start main thinking coroutine
            StartCoroutine(ThinkAndMoveRoutine());

            DebugLog($"AI V3 thinking started - Target iterations: {iterations}");
        }
        #endregion

        #region Core MCTS Processing Coroutine
        /// <summary>
        /// Main AI thinking coroutine (frame-safe MCTS processing)
        /// Uses batched async processing to maintain frame rate
        /// </summary>
        /// <returns>Coroutine enumerator</returns>
        private IEnumerator ThinkAndMoveRoutine()
        {
            // Critical dependency validation
            if (_stateService == null || _processor == null || _boardManager == null)
            {
                DebugLogError("Missing critical dependencies - AI move aborted");
                _gameManager?.SwitchToPlayerTurn();
                yield break;
            }

            // 1. Initialize MCTS root node and board snapshot
            TQ_HexBoardModel rootBoard = _stateService.CreateBoardSnapshot();
            if (rootBoard == null)
            {
                DebugLogError("Failed to create board snapshot - AI move aborted");
                _gameManager?.SwitchToPlayerTurn();
                yield break;
            }

            TQ_MCTSNodeV3 rootNode = new TQ_MCTSNodeV3();
            int completedIterations = 0;
            Stopwatch frameTimer = Stopwatch.StartNew();

            // 2. Run MCTS iterations in batched, frame-safe manner
            while (completedIterations < iterations)
            {
                // Process batch of iterations in background task (prevents main thread blocking)
                var batchTask = Task.Run(() =>
                {
                    for (int i = 0; i < batchSize && (completedIterations + i) < iterations; i++)
                    {
                        // Create board snapshot for simulation (isolated state)
                        TQ_HexBoardModel simBoard = _stateService.CreateBoardSnapshot();
                        if (simBoard == null) continue;

                        // Execute full MCTS cycle for this iteration
                        try
                        {
                            TQ_MCTSNodeV3 leaf = _processor.Select(rootNode);
                            TQ_MCTSNodeV3 expanded = _processor.Expand(leaf, simBoard, TQ_PieceOwner.Enemy);
                            float reward = _processor.Simulate(simBoard, TQ_PieceOwner.Enemy);
                            _processor.Backpropagate(expanded, reward);
                        }
                        catch (System.Exception ex)
                        {
                            DebugLogError($"MCTS iteration error: {ex.Message}");
                        }

                        // Release snapshot to prevent memory leaks
                        _stateService.ReleaseBoardSnapshot(simBoard);
                    }
                });

                // Yield to main thread while batch processes (frame-safe)
                while (!batchTask.IsCompleted)
                {
                    yield return null;
                }

                // Update iteration counter
                completedIterations += batchSize;

                // Frame rate management: yield if we've exceeded time budget
                if (frameTimer.ElapsedMilliseconds > maxFrameTimeMs)
                {
                    yield return null; // Let Unity render frame
                    frameTimer.Restart();
                }

                // Log progress (optional)
                if (completedIterations % 500 == 0)
                {
                    DebugLog($"MCTS progress: {completedIterations}/{iterations} iterations");
                }
            }

            // 3. Select best move from MCTS results (most visited child = best move)
            TQ_MCTSNodeV3 bestChild = rootNode.Children
                .OrderByDescending(c => c.VisitCount)
                .FirstOrDefault();

            // 4. Execute best move (or switch to player turn if no valid move)
            if (bestChild != null && bestChild.Move != null)
            {
                DebugLog($"MCTS completed - Selected move with {bestChild.VisitCount} visits");
                CommitMove(bestChild.Move);
            }
            else
            {
                DebugLogWarning("No valid AI move found - switching to player turn");
                _gameManager?.SwitchToPlayerTurn();
            }

            // Cleanup: release root board snapshot
            _stateService.ReleaseBoardSnapshot(rootBoard);

            // Return move object to pool (critical for memory management)
            if (bestChild?.Move != null)
            {
                AIMovePool.Release(bestChild.Move);
            }
        }
        #endregion

        #region Move Execution & Validation
        /// <summary>
        /// Adapter: Convert AI move to GameManager-compatible move command
        /// Integrates with existing GameManager move execution logic
        /// </summary>
        /// <param name="aiMove">AI-selected move</param>
        private void CommitMove(TQAI_AIMove aiMove)
        {
            // Validate move and dependencies
            if (aiMove == null || _gameManager == null || aiMove.piece == null || aiMove.movePath == null)
            {
                DebugLogError("Invalid AI move or missing GameManager - move aborted");
                _gameManager?.SwitchToPlayerTurn();
                return;
            }

            // Validate move against real board state (critical safety check)
            var realBoard = _boardManager?.boardModel;
            if (!ValidateRealBoardMove(aiMove.piece, aiMove.targetCell, realBoard))
            {
                DebugLogWarning("AI move failed real board validation - switching to player turn");
                _gameManager?.SwitchToPlayerTurn();
                return;
            }

            // Create standard move command compatible with GameManager
            var moveCommand = new TQ_MoveCommand(
                aiMove.piece,                // Moving piece
                aiMove.movePath,             // Complete move path (supports multi-jump)
                true,                        // Is AI move flag
                0.2f                         // Animation delay (matches existing AI behavior)
            );

            // Execute move using GameManager's existing coroutine system
            // Uses string-based coroutine call for compatibility with non-public methods
            try
            {
                _gameManager.StartCoroutine("ExecuteMoveRoutine", moveCommand);
                DebugLog($"AI move committed - Piece: ({aiMove.piece.CurrentCell.Q},{aiMove.piece.CurrentCell.R}) ¡ú Target: ({aiMove.targetCell.Q},{aiMove.targetCell.R})");
            }
            catch (System.Exception ex)
            {
                DebugLogError($"Failed to execute AI move: {ex.Message}");
                _gameManager?.SwitchToPlayerTurn();
            }
        }

        /// <summary>
        /// Validate AI move against real board state (prevents invalid moves)
        /// Ensures move complies with game rules before execution
        /// </summary>
        /// <param name="realPiece">Real game piece</param>
        /// <param name="realTargetCell">Target cell on real board</param>
        /// <param name="realBoard">Real game board state</param>
        /// <returns>True if move is valid on real board</returns>
        private bool ValidateRealBoardMove(TQ_ChessPieceModel realPiece, TQ_HexCellModel realTargetCell, TQ_HexBoardModel realBoard)
        {
            // Basic null validation
            if (realPiece == null || realTargetCell == null || realBoard == null)
            {
                DebugLogError("Null reference in move validation");
                return false;
            }

            // Target cell must be unoccupied
            if (realTargetCell.IsOccupied)
            {
                DebugLogWarning($"Target cell ({realTargetCell.Q},{realTargetCell.R}) is occupied");
                return false;
            }

            // Use rule engine to validate move (matches game rule enforcement)
            var ruleEngine = RuleEnginePool.Get();
            try
            {
                ruleEngine.Init(realBoard);
                var tempContext = new TQ_MoveContext();
                var validMoves = ruleEngine.GetValidMoves(realPiece, tempContext, realBoard);

                // Check if target cell is in valid moves list
                bool isValid = validMoves.Any(cell =>
                    cell != null && cell.Q == realTargetCell.Q && cell.R == realTargetCell.R);

                if (!isValid)
                {
                    DebugLogWarning($"Move validation failed: Piece ({realPiece.CurrentCell.Q},{realPiece.CurrentCell.R}) ¡ú Target ({realTargetCell.Q},{realTargetCell.R}) is not a valid move");
                }

                return isValid;
            }
            catch (System.Exception ex)
            {
                DebugLogError($"Rule engine validation error: {ex.Message}");
                return false;
            }
            finally
            {
                // Critical: return rule engine to pool to prevent memory leaks
                RuleEnginePool.Release(ruleEngine);
            }
        }
        #endregion
    }
}