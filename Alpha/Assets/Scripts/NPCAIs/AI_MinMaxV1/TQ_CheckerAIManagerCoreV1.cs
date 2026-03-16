using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Free.H2D;

namespace Free.Checkers
{
    /// <summary>
    /// AI Core Base Class (Version 1)
    /// Responsible only for calculating legal moves/paths for single pieces (decoupled from business/algorithm logic)
    /// Pure functional design: no game state modification, only calculation and return results
    /// </summary>
    public class TQ_CheckerAIManagerCoreV1 : ZFMonoBehaviour
    {
        [Header("Basic Configuration")]
        [Tooltip("Bonus weight for jump moves (prioritize moves that jump over pieces)")]
        public float jumpMoveBonus = 2f;

        [Tooltip("Penalty for ineffective moves from target area (discourage moving pieces out of target)")]
        public float targetAreaIdleMovePenalty = -5f;

        // Board state service (only for snapshot operations)
        /// <summary>
        /// Board state service reference (for board snapshot management)
        /// Isolated to prevent direct state modifications
        /// </summary>
        protected TQ_BoardStateService _boardStateService;

        // AI-specific move context (path storage)
        /// <summary>
        /// AI-specific move context (stores calculated paths)
        /// Member variable for base initialization, but local instances used in calculations
        /// </summary>
        protected TQ_MoveContext _aiMoveContext;

        // Rule engine (only for move legality checking)
        /// <summary>
        /// Rule engine reference (for move validation)
        /// Reinitialized per calculation to prevent state contamination
        /// </summary>
        protected TQ_RuleEngine _ruleEngine;

        #region Lifecycle Management
        /// <summary>
        /// Awake: Initialize core dependencies
        /// Creates essential services and ensures they persist between scenes
        /// </summary>
        protected virtual void Awake()
        {
            // Initialize core dependencies (decoupled from business logic)
            _aiMoveContext = new TQ_MoveContext();
            _ruleEngine = new TQ_RuleEngine();

            // Auto-find or create board state service (critical for AI calculations)
            _boardStateService = FindFirstObjectByType<TQ_BoardStateService>();
            if (_boardStateService == null)
            {
                // Create persistent service if none exists
                _boardStateService = new GameObject("TQ_BoardStateService")
                    .AddComponent<TQ_BoardStateService>();
                DontDestroyOnLoad(_boardStateService.gameObject);
            }
        }

        /// <summary>
        /// Initialize core (only receives board model, decoupled from BoardManager)
        /// Sets up AI with initial game state and target positions
        /// </summary>
        /// <param name="initBoard">Initial board model (snapshot source)</param>
        /// <param name="enemyTargetPositions">Enemy target area coordinates (cached for performance)</param>
        public virtual void InitCore(TQ_HexBoardModel initBoard, List<Vector2Int> enemyTargetPositions)
        {
            // Initialize board state service with game snapshot
            _boardStateService.Init(initBoard);

            // Initialize rule engine with board reference
            _ruleEngine.Init(initBoard);

            // Cache target positions (prevent repeated calculations)
            CachedEnemyTargetPositions = enemyTargetPositions ?? new List<Vector2Int>();
        }
        #endregion

        #region Core Functionality: Calculate Legal Moves + Paths for Single Piece
        /// <summary>
        /// Bind rule engine to specific board instance
        /// Creates new engine instance to avoid cache contamination
        /// </summary>
        /// <param name="board">Board to bind rule engine to</param>
        protected void BindRuleEngineToBoard(TQ_HexBoardModel board)
        {
            if (board == null) return;

            // Create new instance every time to completely avoid caching issues
            _ruleEngine = new TQ_RuleEngine();
            _ruleEngine.Init(board);

            // DebugLog($"RuleEngine temporarily bound to board hash: {board.GetHashCode()}");
        }

        /// <summary>
        /// Calculate all legal moves and paths for a single piece on specified board
        /// Pure calculation: no state modification, uses board snapshot
        /// </summary>
        /// <param name="piece">Target piece to calculate moves for</param>
        /// <param name="board">Calculation board (snapshot - no state changes)</param>
        /// <returns>All legal move options for the piece with scoring and path info</returns>
        public virtual List<TQAI_AIMove> CalculatePieceValidMoves(TQ_ChessPieceModel piece, TQ_HexBoardModel board)
        {
            var validMoves = new List<TQAI_AIMove>();

            // Basic validity checks
            if (!IsPieceValid(piece) || board == null) return validMoves;

            // 1. Create new RuleEngine and MoveContext every time to avoid reuse contamination
            // Critical: prevents cross-move data leakage
            var localRuleEngine = new TQ_RuleEngine();
            localRuleEngine.Init(board);
            var localMoveContext = new TQ_MoveContext(); // Local variable - no member reuse

            // 2. Explicitly pass board parameter, use pure calculation version
            var validCellsInterface = localRuleEngine.GetValidMovesPure(piece, localMoveContext, board);
            var validCells = validCellsInterface.Cast<TQ_HexCellModel>().ToList();

            // DebugLog($"CalculatePieceValidMoves: Piece({piece.CurrentCell.Q},{piece.CurrentCell.R}), Current board hash: {board.GetHashCode()}, Legal moves count: {validCells.Count}");

            if (validCells.Count == 0) return validMoves;

            // 3. Jump paths also use pure calculation version
            var jumpPaths = new Dictionary<TQ_HexCellModel, List<TQ_HexCellModel>>();
            CalculatePieceJumpPathsPure(piece, jumpPaths, board, localRuleEngine);

            // 4. Iterate through target cells to generate complete move options
            var processedTargets = new HashSet<string>();
            foreach (var targetCell in validCells)
            {
                // Create unique key for target cell to prevent duplicates
                var targetKey = $"{targetCell.Q}_{targetCell.R}";

                // Skip if target already processed or occupied
                if (processedTargets.Contains(targetKey) || targetCell.IsOccupied)
                    continue;

                processedTargets.Add(targetKey);

                // Get complete move path (basic or jump)
                var movePath = GetPieceMovePath(piece, targetCell, jumpPaths, localMoveContext);

                // Calculate strategic score for this move
                var moveScore = CalculateTargetProgressScore(piece, targetCell, board);

                // Check if move involves jumping and calculate bonus
                var (isJump, jumpSteps) = CheckJumpInfo(movePath);
                if (isJump) moveScore += jumpMoveBonus * jumpSteps;

                // Apply penalties for ineffective moves from target area
                moveScore += CalculateTargetAreaPenalty(piece, targetCell, board);

                // Create complete AI move object with all relevant information
                validMoves.Add(new TQAI_AIMove(
                    piece: piece,
                    targetCell: targetCell,
                    score: moveScore,
                    isJumpMove: isJump,
                    jumpStepCount: jumpSteps,
                    movePath: movePath
                ));
            }

            return validMoves;
        }

        /// <summary>
        /// Pure calculation version: Calculate jump paths for piece
        /// No state modification, only path calculation
        /// </summary>
        /// <param name="piece">Piece to calculate jump paths for</param>
        /// <param name="jumpPaths">Dictionary to store calculated jump paths</param>
        /// <param name="board">Calculation board (snapshot)</param>
        /// <param name="localRuleEngine">Local rule engine instance (no shared state)</param>
        protected virtual void CalculatePieceJumpPathsPure(TQ_ChessPieceModel piece,
                                                          Dictionary<TQ_HexCellModel, List<TQ_HexCellModel>> jumpPaths,
                                                          TQ_HexBoardModel board,
                                                          TQ_RuleEngine localRuleEngine)
        {
            // Basic null checks
            if (piece == null || piece.CurrentCell == null || board == null) return;

            // Calculate jump paths using interface types (pure calculation)
            var jumpPathInterface = new Dictionary<ITQ_HexCell, List<ITQ_HexCell>>();
            for (int dir = 0; dir < 6; dir++)
            {
                localRuleEngine.CheckLongJumpRecursivePure(
                    piece.CurrentCell, dir, jumpPathInterface, piece.CurrentCell);
            }

            // Convert interface results to concrete model types
            foreach (var kvp in jumpPathInterface)
            {
                if (kvp.Key is TQ_HexCellModel cell && kvp.Value != null)
                    jumpPaths[cell] = kvp.Value.Cast<TQ_HexCellModel>().ToList();
            }
        }

        /// <summary>
        /// Get complete move path for piece to target cell
        /// Prioritizes pre-calculated jump paths, falls back to basic path
        /// </summary>
        /// <param name="piece">Moving piece</param>
        /// <param name="target">Target cell</param>
        /// <param name="jumpPaths">Pre-calculated jump paths</param>
        /// <param name="localMoveContext">Local move context with path data</param>
        /// <returns>Complete move path (all intermediate cells)</returns>
        protected virtual List<TQ_HexCellModel> GetPieceMovePath(TQ_ChessPieceModel piece, TQ_HexCellModel target,
                                                                  Dictionary<TQ_HexCellModel, List<TQ_HexCellModel>> jumpPaths,
                                                                  TQ_MoveContext localMoveContext)
        {
            // Use pre-calculated jump path if available
            if (jumpPaths.TryGetValue(target, out var path) && path.Count > 0)
                return new List<TQ_HexCellModel>(path);

            // Fall back to context-stored path
            var interfacePath = localMoveContext.GetJumpPath(target);
            var modelPath = interfacePath.Cast<TQ_HexCellModel>().ToList();

            // Final fallback: basic direct path (start ¡ú target)
            return modelPath.Count > 0 ? modelPath : new List<TQ_HexCellModel> { piece.CurrentCell, target };
        }
        #endregion

        #region Helper Methods (Pure Functionality, No Business Logic)
        /// <summary>
        /// Validate piece for AI calculation
        /// Ensures piece is AI-controlled and has valid position
        /// </summary>
        /// <param name="piece">Piece to validate</param>
        /// <returns>True if piece is valid for AI calculations</returns>
        protected virtual bool IsPieceValid(TQ_ChessPieceModel piece)
        {
            // Valid if: not null, has position, and is AI-controlled (Enemy)
            return piece != null && piece.CurrentCell != null && piece.Owner == TQ_PieceOwner.Enemy;
        }

        /// <summary>
        /// Calculate target progress score for move
        /// Higher score for moves that bring piece closer to target area
        /// </summary>
        /// <param name="piece">Moving piece</param>
        /// <param name="target">Target cell</param>
        /// <param name="board">Game board</param>
        /// <returns>Progress score (higher = better progress toward target)</returns>
        protected virtual float CalculateTargetProgressScore(TQ_ChessPieceModel piece, TQ_HexCellModel target, TQ_HexBoardModel board)
        {
            // No target positions = no score
            if (CachedEnemyTargetPositions.Count == 0) return 0f;

            // Calculate distance from original position to target area
            var oldPos = new Vector2Int(piece.CurrentCell.Q, piece.CurrentCell.R);
            var oldDist = CachedEnemyTargetPositions.Min(pos => HexMetrics.Distance(oldPos, pos));

            // Calculate distance from new position to target area
            var newPos = new Vector2Int(target.Q, target.R);
            var newDist = CachedEnemyTargetPositions.Min(pos => HexMetrics.Distance(newPos, pos));

            // Score = progress toward target (never negative)
            return Mathf.Max(0f, oldDist - newDist);
        }

        /// <summary>
        /// Check if move involves jumping and count jump steps
        /// Jump moves identified by path length > 2 (start ¡ú intermediate ¡ú target)
        /// </summary>
        /// <param name="movePath">Complete move path</param>
        /// <returns>Tuple: (isJumpMove, jumpStepCount)</returns>
        protected virtual (bool isJump, int jumpSteps) CheckJumpInfo(List<TQ_HexCellModel> movePath)
        {
            // Jump move = path with more than 2 cells (basic move = 2 cells)
            var isJump = movePath.Count > 2;
            var jumpSteps = isJump ? movePath.Count - 1 : 0;
            return (isJump, jumpSteps);
        }

        /// <summary>
        /// Calculate penalty for moving pieces out of target area
        /// Discourages AI from moving pieces that are already in target area
        /// </summary>
        /// <param name="piece">Moving piece</param>
        /// <param name="target">Target cell</param>
        /// <param name="board">Game board</param>
        /// <returns>Penalty value (negative for bad moves, 0 for neutral/good moves)</returns>
        protected virtual float CalculateTargetAreaPenalty(TQ_ChessPieceModel piece, TQ_HexCellModel target, TQ_HexBoardModel board)
        {
            // Penalty only if: piece was in target area AND moving out of target area
            if (!IsPieceInTargetArea(piece, board) || IsPieceInTargetArea(target, board))
                return 0f;

            // Apply penalty for moving out of target area
            return targetAreaIdleMovePenalty;
        }

        /// <summary>
        /// Check if piece is in target area
        /// Uses cached target positions for performance
        /// </summary>
        /// <param name="piece">Piece to check</param>
        /// <param name="board">Game board</param>
        /// <returns>True if piece is in target area</returns>
        protected virtual bool IsPieceInTargetArea(TQ_ChessPieceModel piece, TQ_HexBoardModel board)
        {
            return piece != null && IsPieceInTargetArea(piece.CurrentCell, board);
        }

        /// <summary>
        /// Check if cell is in target area
        /// Core implementation for target area checks
        /// </summary>
        /// <param name="cell">Cell to check</param>
        /// <param name="board">Game board</param>
        /// <returns>True if cell is in target area</returns>
        protected virtual bool IsPieceInTargetArea(TQ_HexCellModel cell, TQ_HexBoardModel board)
        {
            // Basic null checks
            if (cell == null || CachedEnemyTargetPositions.Count == 0) return false;

            // Check if cell coordinates exist in target positions list
            return CachedEnemyTargetPositions.Contains(new Vector2Int(cell.Q, cell.R));
        }
        #endregion

        #region Cached Data (Decoupled from Business Logic)
        /// <summary>
        /// Cached enemy target area coordinates
        /// Prevents repeated coordinate calculations during AI move evaluation
        /// </summary>
        protected List<Vector2Int> CachedEnemyTargetPositions { get; private set; }
        #endregion
    }
}