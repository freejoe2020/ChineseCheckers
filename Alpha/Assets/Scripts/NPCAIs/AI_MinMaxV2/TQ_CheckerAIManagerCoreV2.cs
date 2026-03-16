using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Free.H2D;

namespace Free.Checkers
{
    /// <summary>
    /// AI Core Base Class V2
    /// Only responsible for calculating valid moves/paths for single pieces (decoupled from business logic)
    /// Key improvements: Base camp penalty system, performance optimization, complete decoupling from hardcoded coordinates
    /// </summary>
    public class TQ_CheckerAIManagerCoreV2 : ZFMonoBehaviour
    {
        [Header("Basic Configuration")]
        [Tooltip("Bonus weight for jump moves (encourages aggressive movement)")]
        public float jumpMoveBonus = 2f;

        [Tooltip("Penalty for idle moves outside target area (discourages pointless movement)")]
        public float targetAreaIdleMovePenalty = -5f;

        [Header("Base Camp Penalty Configuration")]
        [Tooltip("Heavy penalty for moving back to base camp from outside target area")]
        public float backToBasePenalty = -50f;

        [Tooltip("Reduce mobility score by this factor for moves within own base camp")]
        public float baseMobilityReduction = 0.5f;

        // Game core dependency (only for base camp detection)
        protected TQ_CheckerGameManager _gameManager;

        // AI-specific move context (stores movement paths)
        protected TQ_MoveContext _aiMoveContext;

        #region Lifecycle Management
        /// <summary>
        /// Awake: Initialize core AI components
        /// Sets up move context and validates game manager dependency
        /// </summary>
        protected virtual void Awake()
        {
            _aiMoveContext = new TQ_MoveContext();

            // Find game manager (critical for base camp detection)
            _gameManager = FindFirstObjectByType<TQ_CheckerGameManager>();
            if (_gameManager == null)
            {
                DebugLogError("TQ_CheckerGameManager not found!");
                enabled = false;
                return;
            }
        }

        /// <summary>
        /// Initialize core AI functionality (external interface)
        /// Caches target positions for performance
        /// </summary>
        /// <param name="initBoard">Initial board model</param>
        /// <param name="enemyTargetPositions">Enemy target area coordinates</param>
        public virtual void InitCore(TQ_HexBoardModel initBoard, List<Vector2Int> enemyTargetPositions)
        {
            // Cache target positions (avoids repeated calculations)
            CachedEnemyTargetPositions = enemyTargetPositions ?? new List<Vector2Int>();
        }
        #endregion

        #region Core Functionality: Calculate Valid Moves + Paths for Single Piece
        /// <summary>
        /// Calculate all valid moves (with paths) for a single piece
        /// Core method - fully decoupled from business logic
        /// </summary>
        /// <param name="piece">Piece to calculate moves for</param>
        /// <param name="board">Board snapshot (isolated from real game state)</param>
        /// <returns>List of valid AI moves with complete path/score information</returns>
        public virtual List<TQAI_AIMove> CalculatePieceValidMoves(TQ_ChessPieceModel piece, TQ_HexBoardModel board)
        {
            var validMoves = new List<TQAI_AIMove>();

            // Quick validation (early exit for invalid inputs)
            if (!IsPieceValid(piece) || board == null) return validMoves;

            // Endgame optimization: no moves needed if piece is already in target area
            if (IsPieceInTargetArea(piece, board)) return validMoves;

            // Use rule engine from object pool (performance optimization)
            var localRuleEngine = RuleEnginePool.Get();
            try
            {
                // Initialize rule engine with board snapshot
                localRuleEngine.Init(board);
                var localMoveContext = new TQ_MoveContext();

                // Get valid move cells (interface → model conversion)
                var validCellsInterface = localRuleEngine.GetValidMovesPure(piece, localMoveContext, board);
                var validCells = validCellsInterface.Cast<TQ_HexCellModel>().ToList();

                // Early exit if no valid moves
                if (validCells.Count == 0) return validMoves;

                // Calculate jump paths (critical for multi-step moves)
                var jumpPaths = new Dictionary<TQ_HexCellModel, List<TQ_HexCellModel>>();
                CalculatePieceJumpPathsPure(piece, jumpPaths, board, localRuleEngine);

                // Process each valid target cell (avoid duplicates)
                var processedTargets = new HashSet<string>();
                foreach (var targetCell in validCells)
                {
                    var targetKey = $"{targetCell.Q}_{targetCell.R}";

                    // Skip duplicate/occupied targets (validity check)
                    if (processedTargets.Contains(targetKey) || targetCell.IsOccupied)
                        continue;
                    processedTargets.Add(targetKey);

                    // Get complete movement path (including jumps)
                    var movePath = GetPieceMovePath(piece, targetCell, jumpPaths, localMoveContext);

                    // Calculate base movement score (progress toward target)
                    var moveScore = CalculateTargetProgressScore(piece, targetCell, board);

                    // Add jump move bonus (encourage efficient movement)
                    var (isJump, jumpSteps) = CheckJumpInfo(movePath);
                    if (isJump) moveScore += jumpMoveBonus * jumpSteps;

                    // Apply target area penalties (discourage idle moves)
                    moveScore += CalculateTargetAreaPenalty(piece, targetCell, board);

                    // Apply base camp penalties (discourage retreating to base)
                    moveScore += CalculateBasePenalty(piece, targetCell, board);

                    // Calculate mobility score (adjust for base camp)
                    float mobilityScore = CalculateMobilityScoreOptimized(piece, board);
                    if (IsInOwnBase(targetCell, piece.Owner))
                    {
                        mobilityScore *= baseMobilityReduction;
                    }

                    // Get AI move from object pool (memory optimization)
                    var aiMove = AIMovePool.Get();
                    aiMove.piece = piece;
                    aiMove.targetCell = targetCell;
                    aiMove.score = moveScore + mobilityScore;
                    aiMove.isJumpMove = isJump;
                    aiMove.jumpStepCount = jumpSteps;
                    aiMove.movePath = movePath;

                    validMoves.Add(aiMove);
                }
            }
            finally
            {
                // Critical: return rule engine to pool (prevent memory leaks)
                RuleEnginePool.Release(localRuleEngine);
            }

            return validMoves;
        }

        /// <summary>
        /// Calculate jump paths for piece (pure calculation - no side effects)
        /// Recursively finds all possible jump paths for the piece
        /// </summary>
        /// <param name="piece">Piece to calculate jumps for</param>
        /// <param name="jumpPaths">Output: jump paths dictionary</param>
        /// <param name="board">Board snapshot</param>
        /// <param name="localRuleEngine">Pooled rule engine</param>
        protected virtual void CalculatePieceJumpPathsPure(TQ_ChessPieceModel piece,
                                                          Dictionary<TQ_HexCellModel, List<TQ_HexCellModel>> jumpPaths,
                                                          TQ_HexBoardModel board,
                                                          TQ_RuleEngine localRuleEngine)
        {
            // Null safety check
            if (piece == null || piece.CurrentCell == null || board == null) return;

            // Interface-compatible jump path calculation
            var jumpPathInterface = new Dictionary<ITQ_HexCell, List<ITQ_HexCell>>();

            // Check all 6 hex directions for possible jumps
            for (int dir = 0; dir < 6; dir++)
            {
                localRuleEngine.CheckLongJumpRecursivePure(
                    piece.CurrentCell, dir, jumpPathInterface, piece.CurrentCell);
            }

            // Convert interface results to model objects
            foreach (var kvp in jumpPathInterface)
            {
                if (kvp.Key is TQ_HexCellModel cell && kvp.Value != null)
                    jumpPaths[cell] = kvp.Value.Cast<TQ_HexCellModel>().ToList();
            }
        }

        /// <summary>
        /// Get complete movement path for piece to target
        /// Prioritizes pre-calculated jump paths, falls back to basic path
        /// </summary>
        /// <param name="piece">Moving piece</param>
        /// <param name="target">Target cell</param>
        /// <param name="jumpPaths">Pre-calculated jump paths</param>
        /// <param name="localMoveContext">Move context for path lookup</param>
        /// <returns>Complete movement path (list of cells)</returns>
        protected virtual List<TQ_HexCellModel> GetPieceMovePath(TQ_ChessPieceModel piece, TQ_HexCellModel target,
                                                                  Dictionary<TQ_HexCellModel, List<TQ_HexCellModel>> jumpPaths,
                                                                  TQ_MoveContext localMoveContext)
        {
            // Use pre-calculated jump path if available
            if (jumpPaths.TryGetValue(target, out var path) && path.Count > 0)
                return new List<TQ_HexCellModel>(path);

            // Fallback: get path from move context
            var interfacePath = localMoveContext.GetJumpPath(target);
            var modelPath = interfacePath.Cast<TQ_HexCellModel>().ToList();

            // Final fallback: basic direct path
            return modelPath.Count > 0 ? modelPath : new List<TQ_HexCellModel> { piece.CurrentCell, target };
        }
        #endregion

        #region Base Camp Detection & Penalties
        /// <summary>
        /// Determine if cell is in owner's base camp (decoupled from hardcoded coordinates)
        /// Uses CellType from GameManager for true decoupling
        /// </summary>
        /// <param name="cell">Cell to check</param>
        /// <param name="owner">Piece owner (Enemy/Player)</param>
        /// <returns>True if cell is in owner's base camp</returns>
        protected virtual bool IsInOwnBase(TQ_HexCellModel cell, TQ_PieceOwner owner)
        {
            // Null safety (critical for robustness)
            if (cell == null || _gameManager == null || _gameManager.boardManager?.boardModel == null)
            {
                DebugLogError("IsInOwnBase: Board model or GameManager is null - cannot determine base camp");
                return false;
            }

            // Core logic: Use CellType for base camp detection (complete decoupling)
            bool isInOwnBase = false;
            if (owner == TQ_PieceOwner.Enemy)
            {
                // AI (Enemy) base camp = cells marked as EnemyCamp
                isInOwnBase = cell.CellType == TQ_CellType.EnemyCamp;
            }
            else if (owner == TQ_PieceOwner.Player)
            {
                // Player base camp = cells marked as PlayerCamp
                isInOwnBase = cell.CellType == TQ_CellType.PlayerCamp;
            }

            return isInOwnBase;
        }

        /// <summary>
        /// Calculate base camp penalty (discourage retreating to base)
        /// Applies heavy penalty for moving back to base from outside target area
        /// </summary>
        /// <param name="piece">Moving piece</param>
        /// <param name="target">Target cell</param>
        /// <param name="board">Board snapshot</param>
        /// <returns>Penalty value (negative = bad move)</returns>
        protected virtual float CalculateBasePenalty(TQ_ChessPieceModel piece, TQ_HexCellModel target, TQ_HexBoardModel board)
        {
            // Null safety
            if (piece == null || target == null || board == null) return 0f;

            // Check if moving to base camp from outside target area
            bool targetInBase = IsInOwnBase(target, piece.Owner);
            bool pieceNotInTarget = !IsPieceInTargetArea(piece, board);

            // Apply heavy penalty for retreating to base
            return (targetInBase && pieceNotInTarget) ? backToBasePenalty : 0f;
        }
        #endregion

        #region Helper Methods (Optimized & Robust)
        /// <summary>
        /// Validate piece for AI calculations
        /// Only considers valid enemy pieces with valid positions
        /// </summary>
        /// <param name="piece">Piece to validate</param>
        /// <returns>True if piece is valid for AI calculations</returns>
        protected virtual bool IsPieceValid(TQ_ChessPieceModel piece)
        {
            return piece != null && piece.CurrentCell != null && piece.Owner == TQ_PieceOwner.Enemy;
        }

        /// <summary>
        /// Calculate target progress score (core movement heuristic)
        /// Measures distance reduction to target area
        /// </summary>
        /// <param name="piece">Moving piece</param>
        /// <param name="target">Target cell</param>
        /// <param name="board">Board snapshot</param>
        /// <returns>Progress score (higher = better progress)</returns>
        protected virtual float CalculateTargetProgressScore(TQ_ChessPieceModel piece, TQ_HexCellModel target, TQ_HexBoardModel board)
        {
            // Early exit if no target positions cached
            if (CachedEnemyTargetPositions.Count == 0) return 0f;

            // Calculate distance from piece's current position to target area
            var oldPos = new Vector2Int(piece.CurrentCell.Q, piece.CurrentCell.R);
            var oldDist = CachedEnemyTargetPositions.Min(pos => HexMetrics.Distance(oldPos, pos));

            // Calculate distance from target position to target area
            var newPos = new Vector2Int(target.Q, target.R);
            var newDist = CachedEnemyTargetPositions.Min(pos => HexMetrics.Distance(newPos, pos));

            // Score = distance reduction (higher = better progress)
            return Mathf.Max(0f, oldDist - newDist);
        }

        /// <summary>
        /// Check if move is a jump and count jump steps
        /// Jump = path with more than 2 cells (current → intermediate → target)
        /// </summary>
        /// <param name="movePath">Complete movement path</param>
        /// <returns>Tuple: (isJump, jumpSteps)</returns>
        protected virtual (bool isJump, int jumpSteps) CheckJumpInfo(List<TQ_HexCellModel> movePath)
        {
            var isJump = movePath.Count > 2;
            var jumpSteps = isJump ? movePath.Count - 1 : 0;
            return (isJump, jumpSteps);
        }

        /// <summary>
        /// Calculate target area penalty (discourage idle moves)
        /// Penalizes moves that don't progress toward target area
        /// </summary>
        /// <param name="piece">Moving piece</param>
        /// <param name="target">Target cell</param>
        /// <param name="board">Board snapshot</param>
        /// <returns>Penalty value (negative = bad move)</returns>
        protected virtual float CalculateTargetAreaPenalty(TQ_ChessPieceModel piece, TQ_HexCellModel target, TQ_HexBoardModel board)
        {
            // No penalty if:
            // - Piece is already in target area, OR
            // - Move leads to target area
            if (!IsPieceInTargetArea(piece, board) || IsPieceInTargetArea(target, board))
                return 0f;

            // Apply idle move penalty
            return targetAreaIdleMovePenalty;
        }

        /// <summary>
        /// Check if piece is in target area (piece overload)
        /// </summary>
        /// <param name="piece">Piece to check</param>
        /// <param name="board">Board snapshot</param>
        /// <returns>True if piece is in target area</returns>
        protected virtual bool IsPieceInTargetArea(TQ_ChessPieceModel piece, TQ_HexBoardModel board)
        {
            return piece != null && IsPieceInTargetArea(piece.CurrentCell, board);
        }

        /// <summary>
        /// Check if cell is in target area (core implementation)
        /// Uses cached target positions for performance
        /// </summary>
        /// <param name="cell">Cell to check</param>
        /// <param name="board">Board snapshot</param>
        /// <returns>True if cell is in target area</returns>
        protected virtual bool IsPieceInTargetArea(TQ_HexCellModel cell, TQ_HexBoardModel board)
        {
            if (cell == null || CachedEnemyTargetPositions.Count == 0) return false;
            return CachedEnemyTargetPositions.Contains(new Vector2Int(cell.Q, cell.R));
        }

        /// <summary>
        /// Optimized mobility score calculation
        /// Counts available adjacent cells (performance optimized)
        /// </summary>
        /// <param name="piece">Piece to evaluate</param>
        /// <param name="board">Board snapshot</param>
        /// <returns>Mobility score (number of available adjacent cells)</returns>
        protected virtual int CalculateMobilityScoreOptimized(TQ_ChessPieceModel piece, TQ_HexBoardModel board)
        {
            // Null safety
            if (piece == null || piece.CurrentCell == null || board == null) return 0;

            int mobility = 0;
            int q = piece.CurrentCell.Q;
            int r = piece.CurrentCell.R;

            // Hex grid directions (all 6 possible moves)
            var directions = new List<Vector2Int>
            {
                new Vector2Int(1, 0), new Vector2Int(1, -1),
                new Vector2Int(0, -1), new Vector2Int(-1, 0),
                new Vector2Int(-1, 1), new Vector2Int(0, 1)
            };

            // Check each direction for available cells
            foreach (var dir in directions)
            {
                var neighbor = board.GetCellByCoordinates(q + dir.x, r + dir.y);
                if (neighbor != null && !neighbor.IsOccupied)
                {
                    mobility++;
                }
            }

            return mobility;
        }
        #endregion

        #region Cached Data (Performance Optimization)
        /// <summary>
        /// Cached enemy target positions (avoids repeated lookup)
        /// Critical for performance in path calculation
        /// </summary>
        protected List<Vector2Int> CachedEnemyTargetPositions { get; private set; }
        #endregion
    }
}