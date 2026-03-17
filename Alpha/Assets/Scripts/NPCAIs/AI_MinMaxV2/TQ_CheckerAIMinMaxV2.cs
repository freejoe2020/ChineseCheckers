using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Diagnostics;

namespace Free.Checkers
{
    /// <summary>
    /// AI Minimax Algorithm Layer V2
    /// Endgame optimization: focus on pieces outside target area for massive performance boost
    /// Key improvements: endgame specialization, base camp force leave logic, hash state tracking
    /// </summary>
    public class TQ_CheckerAIManagerMinMaxV2 : TQ_CheckerAIManagerCoreV2
    {
        [Header("Minimax Algorithm Configuration")]
        [Tooltip("Current AI difficulty level (affects search depth and move selection)")]
        public TQ_AIDifficulty CurrentDifficulty;

        [Tooltip("Search depth for Easy difficulty (shallow = faster but less optimal)")]
        public int easySearchDepth = 2;

        [Tooltip("Search depth for Medium difficulty (balanced)")]
        public int mediumSearchDepth = 3;

        [Tooltip("Search depth for Hard difficulty (deep = more optimal but slower)")]
        public int hardSearchDepth = 4;

        [Header("Evaluation Weights")]
        [Tooltip("Weight for progress toward target area (primary objective). Higher so 'toward target' beats mobility/block.")]
        public float targetProgressWeight = 14f;

        [Tooltip("Weight for occupying target area cells")]
        public float targetAreaOccupyWeight = 8f;

        [Tooltip("Weight for piece mobility (number of valid moves)")]
        public float mobilityWeight = 3f;

        [Tooltip("Weight for blocking opponent's valid moves")]
        public float blockOpponentWeight = 5f;

        [Tooltip("Massive bonus for winning position (ensures win prioritization)")]
        public float winBonus = 10000f;

        [Tooltip("Massive penalty for losing position (avoids losing moves)")]
        public float losePenalty = -10000f;

        [Header("Performance Optimization")]
        [Tooltip("Maximum number of moves to evaluate per depth (prevents excessive computation)")]
        public int maxMoveCount = 20;

        [Tooltip("Maximum recursion depth (prevents stack overflow)")]
        public int maxRecursionDepth = 10;

        [Tooltip("Use transposition table to cache board evaluations (performance boost)")]
        public bool useTranspositionTable = true;

        // Algorithm core caches
        /// <summary>
        /// Transposition table: caches board evaluations to avoid redundant calculations
        /// Key: Board hash, Value: (evaluation score, search depth)
        /// </summary>
        protected Dictionary<ulong, (float score, int depth)> _transpositionTable;

        /// <summary>
        /// Enhanced move history stack: tracks hash state for faster restoration
        /// Includes board hash to avoid recalculation during undo
        /// </summary>
        protected Stack<(TQ_ChessPieceModel piece, Vector2Int oldPos, Vector2Int newPos, bool wasOccupied, ulong oldHash)> _moveHistory;

        /// <summary>
        /// Stopwatch for search performance monitoring
        /// Prevents algorithm from exceeding time limits
        /// </summary>
        protected Stopwatch _searchStopwatch;

        // Temporary data (per search)
        /// <summary>
        /// All valid moves for current search iteration
        /// Reused to avoid repeated calculations
        /// </summary>
        protected List<TQAI_AIMove> _allValidMoves;

        /// <summary>
        /// Counter for recursive steps (performance monitoring)
        /// </summary>
        protected int _recursiveStepCount;

        /// <summary>
        /// Number of first-layer moves (root level)
        /// </summary>
        protected int _firstLayerMoveCount;

        /// <summary>
        /// Current board hash (cached for performance)
        /// Avoids recalculating hash on every move simulation
        /// </summary>
        private ulong _currentBoardHash;

        #region Lifecycle Management
        /// <summary>
        /// Awake: Initialize enhanced Minimax components
        /// Extends base class with hash-aware move history
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            // Initialize algorithm optimization structures
            _transpositionTable = new Dictionary<ulong, (float, int)>();
            _moveHistory = new Stack<(TQ_ChessPieceModel, Vector2Int, Vector2Int, bool, ulong)>();
            _searchStopwatch = new Stopwatch();
        }

        /// <summary>
        /// Initialize Minimax algorithm layer (extends base initialization)
        /// </summary>
        /// <param name="initBoard">Initial board model (snapshot source)</param>
        /// <param name="enemyTargetPositions">Enemy target area coordinates</param>
        /// <param name="initialDifficulty">Starting AI difficulty level</param>
        public virtual void InitMinMax(TQ_HexBoardModel initBoard, List<Vector2Int> enemyTargetPositions,
                                      TQ_AIDifficulty initialDifficulty = TQ_AIDifficulty.Medium)
        {
            // Initialize core calculation functionality
            base.InitCore(initBoard, enemyTargetPositions);

            // Set initial difficulty level
            CurrentDifficulty = initialDifficulty;
        }
        #endregion

        #region Core Functionality: Calculate Optimal Move
        /// <summary>
        /// Calculate best move with endgame optimization
        /// Specialized logic for 1-2 pieces remaining outside target area
        /// </summary>
        /// <param name="board">Board snapshot (isolated from real game state)</param>
        /// <param name="enemyPieces">Valid AI-controlled pieces</param>
        /// <returns>Optimal move for AI (highest evaluated score)</returns>
        public virtual TQAI_AIMove CalculateBestMove(TQ_HexBoardModel board, List<TQ_ChessPieceModel> enemyPieces)
        {
            // 1. Filter pieces outside target area (endgame optimization)
            var outOfTargetPieces = enemyPieces.Where(p => !IsPieceInTargetArea(p, board)).ToList();
            int outCount = outOfTargetPieces.Count;

            // 2. Endgame specialization: dedicated algorithm for 1-2 pieces remaining
            if (outCount == 1 || outCount == 2)
            {
                var endgameAI = new TQ_CheckerAIManagerEndgameV2();
                endgameAI.InitMinMax(board, CachedEnemyTargetPositions, CurrentDifficulty);
                var endgameMove = endgameAI.CalculateEndgameBestMove(board, outOfTargetPieces);
                if (endgameMove != null) return endgameMove;
            }

            // 3. Standard Minimax logic with endgame optimizations
            var initialHash = ZobristHash.CalculateBoardHash(board);
            ResetAlgorithmState();

            // Focus only on pieces outside target area when ≥2 remain
            _allValidMoves = outCount >= 2
                ? GenerateAllEnemyValidMoves(board, outOfTargetPieces)
                : GenerateAllEnemyValidMoves(board, enemyPieces);

            // Early exit if no valid moves
            if (_allValidMoves.Count == 0) return null;

            // Dynamic depth adjustment for endgame (deeper search for critical moves)
            int baseDepth = GetSearchDepthByDifficulty();
            int depthBonus = outCount == 1 ? 20 : (outCount == 2 ? 10 : 0);
            int finalDepth = baseDepth + depthBonus;

            _firstLayerMoveCount = _allValidMoves.Count;

            // Iterative deepening search with time limit
            _searchStopwatch.Start();
            var bestMove = FindBestMoveWithIterativeDeepening(finalDepth, board);
            _searchStopwatch.Stop();

            // Critical state validation: ensure board wasn't corrupted
            var finalHash = ZobristHash.CalculateBoardHash(board);
            if (initialHash != finalHash)
            {
                DebugLogError("❌ Board state corrupted! Hash values do not match");
            }
            else
            {
                DebugLog("✅ Board state intact");
            }

            // Log search statistics for optimization/debugging
            LogSearchStats(finalDepth);

            // Return best move or fallback based on difficulty
            return bestMove ?? SelectMoveByDifficulty();
        }
        #endregion

        #region Minimax Core Algorithm (Enhanced)
        /// <summary>
        /// Enhanced Minimax algorithm with Alpha-Beta pruning
        /// Optimized for endgame by focusing on pieces outside target area
        /// </summary>
        /// <param name="depth">Remaining search depth</param>
        /// <param name="alpha">Alpha value for pruning (max player's best score)</param>
        /// <param name="beta">Beta value for pruning (min player's best score)</param>
        /// <param name="isMaxPlayer">True if AI (maximizing player), False if opponent (minimizing)</param>
        /// <param name="currentRecursionDepth">Current recursion depth (for safety limits)</param>
        /// <param name="board">Board snapshot for evaluation</param>
        /// <returns>Evaluated score for current board state</returns>
        protected virtual float Minimax(int depth, float alpha, float beta, bool isMaxPlayer,
                                       int currentRecursionDepth, TQ_HexBoardModel board)
        {
            _recursiveStepCount++;

            // Termination conditions (base cases)
            if (depth == 0 || currentRecursionDepth >= maxRecursionDepth)
                return EvaluateBoardState(board);

            // Immediate return for terminal game states
            if (IsGameOver(TQ_PieceOwner.Enemy, board)) return winBonus;
            if (IsGameOver(TQ_PieceOwner.Player, board)) return losePenalty;

            // Optimized hash lookup (cached when available)
            ulong boardHash = _currentBoardHash != 0 ? _currentBoardHash : ZobristHash.CalculateBoardHash(board);

            // Transposition table lookup (performance optimization)
            if (useTranspositionTable && _transpositionTable.TryGetValue(boardHash, out var cached))
            {
                if (cached.depth >= depth) return cached.score;
            }

            // Get valid moves with endgame optimization
            var enemyPieces = GetValidEnemyPieces(board);
            var outOfTargetPieces = enemyPieces.Where(p => !IsPieceInTargetArea(p, board)).ToList();
            int outCount = outOfTargetPieces.Count;

            var currentMoves = isMaxPlayer
                ? (outCount >= 2 ? GenerateAllEnemyValidMoves(board, outOfTargetPieces) : GenerateAllEnemyValidMoves(board, enemyPieces))
                : GenerateAllPlayerValidMoves(board);

            // No valid moves = terminal state for current player
            if (currentMoves.Count == 0)
                return isMaxPlayer ? losePenalty : winBonus;

            // Sort moves to improve Alpha-Beta pruning efficiency
            currentMoves = SortMoves(currentMoves, isMaxPlayer);

            // Limit move count for performance (prevents excessive computation)
            if (currentMoves.Count > maxMoveCount)
                currentMoves = currentMoves.Take(maxMoveCount).ToList();

            float eval;
            if (isMaxPlayer) // AI turn (maximize score)
            {
                float maxEval = float.MinValue;
                foreach (var move in currentMoves)
                {
                    MakeSimulatedMove(move, board);
                    eval = Minimax(depth - 1, alpha, beta, false, currentRecursionDepth + 1, board);
                    UndoSimulatedMove(move, board);

                    maxEval = Mathf.Max(maxEval, eval);
                    alpha = Mathf.Max(alpha, eval);
                    if (beta <= alpha) break; // Alpha-Beta pruning
                }

                // Update transposition table
                if (useTranspositionTable)
                    _transpositionTable[boardHash] = (maxEval, depth);

                return maxEval;
            }
            else // Player turn (minimize score)
            {
                float minEval = float.MaxValue;
                foreach (var move in currentMoves)
                {
                    MakeSimulatedMove(move, board);
                    eval = Minimax(depth - 1, alpha, beta, true, currentRecursionDepth + 1, board);
                    UndoSimulatedMove(move, board);

                    minEval = Mathf.Min(minEval, eval);
                    beta = Mathf.Min(beta, eval);
                    if (beta <= alpha) break; // Alpha-Beta pruning
                }

                // Update transposition table
                if (useTranspositionTable)
                    _transpositionTable[boardHash] = (minEval, depth);

                return minEval;
            }
        }

        /// <summary>
        /// Iterative deepening search (balances depth and performance)
        /// Gradually increases search depth until time limit or max depth reached
        /// </summary>
        /// <param name="maxDepth">Maximum search depth</param>
        /// <param name="board">Board snapshot</param>
        /// <returns>Optimal move found within time/depth constraints</returns>
        protected virtual TQAI_AIMove FindBestMoveWithIterativeDeepening(int maxDepth, TQ_HexBoardModel board)
        {
            TQAI_AIMove bestMove = null;
            float bestScore = float.MinValue;

            // Gradually increase search depth (improves move quality incrementally)
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                var currentBest = FindBestMoveCore(depth, board);
                if (currentBest == null) continue;

                // Update best move if current depth found better option
                if (currentBest.score > bestScore)
                {
                    bestScore = currentBest.score;
                    bestMove = currentBest;
                }

                // Time limit protection (prevents unresponsive AI)
                if (_searchStopwatch.Elapsed.TotalSeconds > 1.5f)
                {
                    DebugLog($"[Minimax] Time limit exceeded, current depth: {depth}");
                    break;
                }
            }

            return bestMove;
        }

        /// <summary>
        /// Core best move search for specific depth
        /// Evaluates all valid moves at root level with specified search depth
        /// </summary>
        /// <param name="searchDepth">Search depth for Minimax</param>
        /// <param name="board">Board snapshot</param>
        /// <returns>Best move for specified depth</returns>
        protected virtual TQAI_AIMove FindBestMoveCore(int searchDepth, TQ_HexBoardModel board)
        {
            TQAI_AIMove bestMove = null;
            float bestScore = float.MinValue;
            float alpha = float.MinValue;
            float beta = float.MaxValue;

            // Evaluate all valid moves for AI
            foreach (var move in _allValidMoves)
            {
                MakeSimulatedMove(move, board);
                var currentScore = Minimax(searchDepth - 1, alpha, beta, false, 1, board);
                UndoSimulatedMove(move, board);

                // Update move score and track best move
                move.score = currentScore;
                if (currentScore > bestScore)
                {
                    bestScore = currentScore;
                    bestMove = move;
                }

                alpha = Mathf.Max(alpha, currentScore);
            }

            return bestMove;
        }
        #endregion

        #region Helper Methods (Enhanced with Endgame Optimization)
        /// <summary>
        /// Generate all valid moves for enemy pieces with endgame/base camp optimization
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <param name="enemyPieces">AI-controlled pieces</param>
        /// <returns>List of valid enemy moves with optimization applied</returns>
        protected virtual List<TQAI_AIMove> GenerateAllEnemyValidMoves(TQ_HexBoardModel board, List<TQ_ChessPieceModel> enemyPieces)
        {
            var validMoves = new List<TQAI_AIMove>();

            // Check if force leave home is required (game phase rule)
            bool needForceLeave = IsForceLeaveHomeRequired(_gameManager._currentRound, board);

            foreach (var piece in enemyPieces)
            {
                // 1. Force leave logic: only consider pieces still in base camp
                if (needForceLeave)
                {
                    if (!IsInOwnBase(piece.CurrentCell, piece.Owner))
                        continue; // Skip pieces already outside base
                }
                else
                {
                    // 2. Endgame optimization: ignore pieces in target area when ≥2 remain outside
                    var outOfTargetPieces = enemyPieces.Where(p => !IsPieceInTargetArea(p, board)).ToList();
                    if (outOfTargetPieces.Count >= 2 && IsPieceInTargetArea(piece, board))
                        continue;
                }

                // Calculate valid moves for eligible pieces
                validMoves.AddRange(CalculatePieceValidMoves(piece, board));
            }

            return validMoves;
        }

        /// <summary>
        /// Generate all valid moves for player pieces (opponent simulation)
        /// Uses object pool for rule engine (performance optimization)
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <returns>List of valid player moves</returns>
        protected virtual List<TQAI_AIMove> GenerateAllPlayerValidMoves(TQ_HexBoardModel board)
        {
            var validMoves = new List<TQAI_AIMove>();
            var playerPieces = board.PlayerPieces.Where(p => p != null && p.CurrentCell != null).ToList();

            // Use rule engine from object pool (critical performance optimization)
            var ruleEngine = RuleEnginePool.Get();
            try
            {
                ruleEngine.Init(board);

                foreach (var piece in playerPieces)
                {
                    _aiMoveContext.Clear();
                    var validCells = ruleEngine.GetValidMoves(piece, _aiMoveContext, board).Cast<TQ_HexCellModel>().ToList();

                    foreach (var cell in validCells)
                    {
                        // Get AI move from object pool (memory optimization)
                        var move = AIMovePool.Get();
                        move.piece = piece;
                        move.targetCell = cell;
                        move.score = 0f;
                        move.isJumpMove = false;
                        move.jumpStepCount = 0;
                        move.movePath = new List<TQ_HexCellModel> { piece.CurrentCell, cell };

                        validMoves.Add(move);
                    }
                }
            }
            finally
            {
                // Critical: return rule engine to pool (prevent memory leaks)
                RuleEnginePool.Release(ruleEngine);
            }

            return validMoves;
        }

        /// <summary>
        /// Optimized board evaluation (endgame focused)
        /// Only evaluates pieces outside target area (massive performance boost)
        /// </summary>
        /// <param name="board">Board snapshot to evaluate</param>
        /// <returns>Overall score for board state (higher = better for AI)</returns>
        protected virtual float EvaluateBoardState(TQ_HexBoardModel board)
        {
            if (board == null) return 0f;

            var enemyPieces = GetValidEnemyPieces(board);
            // Endgame optimization: only evaluate pieces outside target area
            var outOfTargetPieces = enemyPieces.Where(p => !IsPieceInTargetArea(p, board)).ToList();
            int outCount = outOfTargetPieces.Count;

            // Immediate win if no pieces left outside target area
            if (outCount == 0) return winBonus;

            // 1. Target progress score (only for pieces outside target area)
            float progressScore = outOfTargetPieces.Sum(p => CalculateTargetProgressScore(p, p.CurrentCell, board))
                                 / Mathf.Max(1, outCount);

            // 2. Target area occupation score (reward for occupied target cells)
            int targetOccupyCount = enemyPieces.Count(p => IsPieceInTargetArea(p, board));
            float occupyScore = Mathf.Min((float)targetOccupyCount / 10f, 1f);

            // 3. Mobility score (optimized for base camp penalty)
            float mobilityScore = 0f;
            foreach (var p in outOfTargetPieces)
            {
                int mob = CalculateMobilityScoreOptimized(p, board);
                // Reduce mobility score for pieces in own base camp
                if (IsInOwnBase(p.CurrentCell, p.Owner))
                {
                    mob = Mathf.RoundToInt(mob * baseMobilityReduction);
                }
                mobilityScore += mob;
            }
            mobilityScore /= Mathf.Max(1, outCount * 6f);

            // 4. Block opponent score (limit opponent's mobility)
            float blockScore = CalculateBlockOpponentScore(board);

            // 5. Base camp penalty (discourage staying in base)
            float basePenalty = outOfTargetPieces.Sum(p =>
            {
                return IsInOwnBase(p.CurrentCell, p.Owner) ? backToBasePenalty / 10f : 0f;
            });

            // Weighted combination of all evaluation factors
            return progressScore * targetProgressWeight
                   + occupyScore * targetAreaOccupyWeight
                   + mobilityScore * mobilityWeight
                   + blockScore * blockOpponentWeight
                   + basePenalty;
        }

        /// <summary>
        /// Select move based on difficulty (fallback when Minimax fails)
        /// Implements different move selection strategies for each difficulty
        /// </summary>
        /// <returns>Selected move based on current difficulty</returns>
        protected virtual TQAI_AIMove SelectMoveByDifficulty()
        {
            if (_allValidMoves.Count == 0) return null;

            return CurrentDifficulty switch
            {
                TQ_AIDifficulty.Easy => SelectEasyMove(),
                TQ_AIDifficulty.Medium => SelectMediumMove(),
                TQ_AIDifficulty.Hard => _allValidMoves.OrderByDescending(m => m.score).FirstOrDefault(),
                _ => _allValidMoves.FirstOrDefault()
            };
        }

        /// <summary>
        /// Get valid enemy pieces (AI-controlled, non-null, with valid position)
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <returns>List of valid enemy pieces</returns>
        protected virtual List<TQ_ChessPieceModel> GetValidEnemyPieces(TQ_HexBoardModel board)
        {
            return board?.EnemyPieces.Where(IsPieceValid).ToList() ?? new List<TQ_ChessPieceModel>();
        }

        /// <summary>
        /// Enhanced simulated move with hash tracking
        /// Maintains board hash state for transposition table optimization
        /// </summary>
        /// <param name="move">Move to simulate</param>
        /// <param name="board">Board snapshot to modify</param>
        protected virtual void MakeSimulatedMove(TQAI_AIMove move, TQ_HexBoardModel board)
        {
            if (move == null || board == null) return;

            var piece = move.piece;
            var targetCell = move.targetCell;

            // Null safety checks
            if (piece == null || targetCell == null || piece.CurrentCell == null)
                return;

            // Optimized hash management
            if (_currentBoardHash == 0)
            {
                _currentBoardHash = ZobristHash.CalculateBoardHash(board);
            }
            else
            {
                // Incremental hash update (performance optimization)
                _currentBoardHash = ZobristHash.UpdateHashAfterMove(
                    _currentBoardHash,
                    piece,
                    piece.CurrentCell.Q, piece.CurrentCell.R,
                    targetCell.Q, targetCell.R
                );
            }

            // Capture current hash state for undo
            ulong currentHash = ZobristHash.CalculateBoardHash(board);

            // Record move details for undo
            var oldPos = new Vector2Int(piece.CurrentCell.Q, piece.CurrentCell.R);
            var newPos = new Vector2Int(targetCell.Q, targetCell.R);
            var wasOccupied = targetCell.IsOccupied;

            // Execute simulated move (modify snapshot state)
            var oldCell = board.GetCellByCoordinates(oldPos.x, oldPos.y);
            if (oldCell != null)
            {
                oldCell.IsOccupied = false;
                oldCell.CurrentPiece = null;
            }

            piece.CurrentCell = targetCell;
            targetCell.IsOccupied = true;
            targetCell.CurrentPiece = piece;

            // Save complete move history (including hash) for undo
            _moveHistory.Push((piece, oldPos, newPos, wasOccupied, currentHash));
        }

        /// <summary>
        /// Enhanced undo simulated move with hash restoration
        /// Restores board hash state for transposition table consistency
        /// </summary>
        /// <param name="move">Move to undo</param>
        /// <param name="board">Board snapshot to restore</param>
        protected virtual void UndoSimulatedMove(TQAI_AIMove move, TQ_HexBoardModel board)
        {
            if (_moveHistory.Count == 0 || board == null) return;

            // Retrieve complete move history record
            var record = _moveHistory.Pop();
            var oldCell = board.GetCellByCoordinates(record.oldPos.x, record.oldPos.y);
            var newCell = board.GetCellByCoordinates(record.newPos.x, record.newPos.y);

            // Null safety checks
            if (oldCell == null || newCell == null || record.piece == null)
                return;

            // Restore hash state (critical for transposition table)
            _currentBoardHash = record.oldHash;

            // Restore board state to pre-move condition
            newCell.CurrentPiece = null;
            newCell.IsOccupied = record.wasOccupied;
            oldCell.CurrentPiece = record.piece;
            oldCell.IsOccupied = true;

            // Restore piece position
            record.piece.CurrentCell = oldCell;
        }

        /// <summary>
        /// Get search depth based on difficulty and game phase
        /// Increased depth for endgame (more critical moves)
        /// </summary>
        /// <returns>Calculated search depth</returns>
        protected virtual int GetSearchDepthByDifficulty()
        {
            // Base depth based on difficulty
            var baseDepth = CurrentDifficulty switch
            {
                TQ_AIDifficulty.Easy => easySearchDepth,
                TQ_AIDifficulty.Medium => mediumSearchDepth,
                TQ_AIDifficulty.Hard => hardSearchDepth,
                _ => mediumSearchDepth
            };

            // Increase depth for endgame (6+ pieces in target area)
            var enemyPieces = GetValidEnemyPieces(board);
            var inEndgame = enemyPieces.Count(p => IsPieceInTargetArea(p, null)) >= 6;
            return inEndgame ? baseDepth + 2 : Mathf.Max(1, baseDepth);
        }

        /// <summary>
        /// Reset algorithm state (before each search)
        /// Includes hash state reset for transposition table consistency
        /// </summary>
        protected virtual void ResetAlgorithmState()
        {
            _transpositionTable.Clear();
            _moveHistory.Clear();
            _recursiveStepCount = 0;
            _allValidMoves = new List<TQAI_AIMove>();
            _searchStopwatch.Reset();
            _currentBoardHash = 0; // Reset hash state
        }

        /// <summary>
        /// Check if game is over (all pieces in target area)
        /// Terminal state detection for Minimax
        /// </summary>
        /// <param name="winner">Potential winning player</param>
        /// <param name="board">Board snapshot</param>
        /// <returns>True if specified player has won</returns>
        protected virtual bool IsGameOver(TQ_PieceOwner winner, TQ_HexBoardModel board)
        {
            if (board == null) return false;

            // Get all pieces for potential winner
            var pieces = winner == TQ_PieceOwner.Enemy
                ? GetValidEnemyPieces(board)
                : board.PlayerPieces.Where(p => p != null && p.CurrentCell != null).ToList();

            // Win condition: all pieces in target area
            return pieces.All(p => IsPieceInTargetArea(p, board));
        }

        /// <summary>
        /// Enhanced move sorting (prioritizes jump moves)
        /// Improves Alpha-Beta pruning efficiency by evaluating better moves first
        /// </summary>
        /// <param name="moves">Moves to sort</param>
        /// <param name="isMaxPlayer">True if sorting for AI (max player)</param>
        /// <returns>Sorted list of moves</returns>
        protected virtual List<TQAI_AIMove> SortMoves(List<TQAI_AIMove> moves, bool isMaxPlayer)
        {
            if (isMaxPlayer)
            {
                // AI: prioritize jump moves then higher scores
                return moves.OrderByDescending(m => m.isJumpMove)
                            .ThenByDescending(m => m.score)
                            .ToList();
            }
            else
            {
                // Player: prioritize jump moves then lower scores (worse for AI)
                return moves.OrderByDescending(m => m.isJumpMove)
                            .ThenBy(m => m.score)
                            .ToList();
            }
        }

        /// <summary>
        /// Log search statistics for debugging/optimization
        /// Provides insight into algorithm performance
        /// </summary>
        /// <param name="searchDepth">Final search depth</param>
        protected virtual void LogSearchStats(int searchDepth)
        {
            var log = $"【Minimax Statistics】Depth: {searchDepth} | Recursive Steps: {_recursiveStepCount} | " +
                      $"Time: {_searchStopwatch.Elapsed.TotalMilliseconds:F2}ms";
            DebugLog(log);
        }

        /// <summary>
        /// Select move for Easy difficulty (suboptimal/random)
        /// Makes AI easier to beat by choosing worse moves
        /// </summary>
        /// <returns>Easy difficulty move selection</returns>
        protected virtual TQAI_AIMove SelectEasyMove()
        {
            // Select from lower half of scored moves (worse options)
            var lowScoreMoves = _allValidMoves.OrderBy(m => m.score).Skip(Mathf.Max(0, _allValidMoves.Count / 2)).ToList();

            // 50% chance for bad move, 50% chance for random move
            return lowScoreMoves.Count > 0
                ? (Random.value > 0.5f ? lowScoreMoves[0] : _allValidMoves[Random.Range(0, _allValidMoves.Count)])
                : _allValidMoves[Random.Range(0, _allValidMoves.Count)];
        }

        /// <summary>
        /// Select move for Medium difficulty (semi-optimal)
        /// Balances between optimal play and mistakes
        /// </summary>
        /// <returns>Medium difficulty move selection</returns>
        protected virtual TQAI_AIMove SelectMediumMove()
        {
            // Select from top half of scored moves (better options) with randomness
            var midScoreMoves = _allValidMoves.OrderByDescending(m => m.score).Take(Mathf.Max(1, _allValidMoves.Count / 2)).ToList();
            return midScoreMoves[Random.Range(0, midScoreMoves.Count)];
        }

        /// <summary>
        /// Enhanced block opponent score calculation
        /// Measures opponent mobility restriction (optimized formula)
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <returns>Block score (0-1, higher = better blocking)</returns>
        protected virtual float CalculateBlockOpponentScore(TQ_HexBoardModel board)
        {
            if (board == null) return 0f;

            var playerPieces = board.PlayerPieces.Where(p => p != null && p.CurrentCell != null).ToList();
            int totalMobility = 0, blockedMobility = 0;

            foreach (var piece in playerPieces)
            {
                int mobility = CalculateMobilityScoreOptimized(piece, board);
                totalMobility += mobility;
                blockedMobility += 6 - mobility; // Total possible moves (6) minus available
            }

            // Return ratio of blocked moves (0 if no moves to block)
            return totalMobility > 0 ? (float)blockedMobility / totalMobility : 0f;
        }

        /// <summary>
        /// Count AI pieces that have left their base camp
        /// Uses new base camp detection logic (CellType based)
        /// </summary>
        /// <param name="board">Board model</param>
        /// <returns>Number of AI pieces outside base camp</returns>
        protected int GetAI_OutHomeCount(TQ_HexBoardModel board)
        {
            if (board == null) return 0;

            var enemyPieces = GetValidEnemyPieces(board);
            int outCount = 0;

            foreach (var piece in enemyPieces)
            {
                // Count pieces not in their own base camp
                if (!IsInOwnBase(piece.CurrentCell, piece.Owner))
                {
                    outCount++;
                }
            }

            return outCount;
        }

        /// <summary>
        /// Determine if force leave home is required (game phase rule)
        /// Progressive requirement based on game round
        /// </summary>
        /// <param name="currentRound">Current game round</param>
        /// <param name="board">Board model</param>
        /// <returns>True if AI must move pieces out of base camp</returns>
        protected virtual bool IsForceLeaveHomeRequired(int currentRound, TQ_HexBoardModel board)
        {
            if (board == null || currentRound < 0) return false;
            int outCount = GetAI_OutHomeCount(board);

            // Progressive force leave requirements based on game round
            if (currentRound < 20 && outCount < 5) return true;
            if (currentRound >= 20 && currentRound < 25 && outCount < 8) return true;
            if (currentRound >= 25 && currentRound < 30 && outCount < 10) return true;

            return false;
        }

        /// <summary>
        /// Board property (null placeholder for derived classes)
        /// </summary>
        protected TQ_HexBoardModel board => null;
        #endregion
    }
}