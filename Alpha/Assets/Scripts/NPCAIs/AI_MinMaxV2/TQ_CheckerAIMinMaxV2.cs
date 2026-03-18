using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Diagnostics;

namespace Free.Checkers
{
    /// <summary>
    /// Transposition table entry type for correct alpha-beta use.
    /// Exact = full window score; LowerBound = at least score; UpperBound = at most score.
    /// </summary>
    public enum TTEntryType { Exact, LowerBound, UpperBound }

    /// <summary>
    /// AI Minimax Algorithm Layer V2
    /// Endgame optimization: focus on pieces outside target area for massive performance boost
    /// Key improvements: endgame specialization, base camp force leave logic, hash state tracking,
    /// PV + history heuristic, killer moves, TT entry type, anti-repetition.
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

        [Tooltip("Position history size for anti-repetition (number of half-moves to track)")]
        public int positionHistoryCapacity = 32;

        [Tooltip("Small penalty for first repetition of a position")]
        public float repetitionPenaltyOnce = -2f;

        [Tooltip("Large penalty for third repetition (draw-like)")]
        public float repetitionPenaltyThree = -500f;

        [Tooltip("Very strong penalty for 5+ repetitions (avoid endless back-and-forth)")]
        public float repetitionPenaltyFiveOrMore = -3000f;

        [Tooltip("Bonus per free entry cell (target-area empty cell reachable in one step from outside; encourages unblocking)")]
        public float entryCellBonusWeight = 3f;

        // Algorithm core caches
        /// <summary>
        /// Transposition table: (score, depth, entry type, stored alpha/beta for correct cutoffs)
        /// </summary>
        protected Dictionary<ulong, (float score, int depth, TTEntryType type, float storedAlpha, float storedBeta)> _transpositionTable;

        /// <summary>
        /// Enhanced move history stack: tracks hash state for faster restoration
        /// Includes board hash to avoid recalculation during undo
        /// </summary>
        protected Stack<(TQ_ChessPieceModel piece, Vector2Int oldPos, Vector2Int newPos, bool wasOccupied, ulong oldHash)> _moveHistory;

        /// <summary> Principal variation from previous iteration (root moves); used to reorder root moves. </summary>
        protected List<TQAI_AIMove> _principalVariation;

        /// <summary> History heuristic: (fromQ, fromR, toQ, toR) -> bonus when move caused beta cutoff. </summary>
        protected Dictionary<(int, int, int, int), int> _historyScore;

        /// <summary> Killer move slot 1 per depth (depthFromRoot -> (from, to)). </summary>
        protected Dictionary<int, (Vector2Int from, Vector2Int to)> _killer1;

        /// <summary> Killer move slot 2 per depth. </summary>
        protected Dictionary<int, (Vector2Int from, Vector2Int to)> _killer2;

        /// <summary> Position hashes along current path (for anti-repetition). Push at node enter, pop at leave. </summary>
        protected List<ulong> _positionHistory;

        /// <summary> Game-level position hashes (after each real move). Persists across searches; used to penalize repeating game positions. </summary>
        protected List<ulong> _gamePositionHashes;

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
        private int _simulatedMoveDepth = 0;

        #region Lifecycle Management
        /// <summary>
        /// Awake: Initialize enhanced Minimax components
        /// Extends base class with hash-aware move history
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            // Initialize algorithm optimization structures
            _transpositionTable = new Dictionary<ulong, (float, int, TTEntryType, float, float)>();
            _moveHistory = new Stack<(TQ_ChessPieceModel, Vector2Int, Vector2Int, bool, ulong)>();
            _principalVariation = new List<TQAI_AIMove>();
            _historyScore = new Dictionary<(int, int, int, int), int>();
            _killer1 = new Dictionary<int, (Vector2Int, Vector2Int)>();
            _killer2 = new Dictionary<int, (Vector2Int, Vector2Int)>();
            _positionHistory = new List<ulong>(positionHistoryCapacity);
            _gamePositionHashes = new List<ulong>(positionHistoryCapacity);
            _searchStopwatch = new Stopwatch();
        }

        /// <summary>
        /// Initialize Minimax algorithm layer (extends base initialization)
        /// Clears game position history on new game so anti-repetition starts fresh.
        /// </summary>
        public override void InitCore(TQ_HexBoardModel initBoard, List<Vector2Int> enemyTargetPositions)
        {
            base.InitCore(initBoard, enemyTargetPositions);
            _gamePositionHashes?.Clear();
        }

        /// <summary>
        /// Record a game position hash after a move is applied (AI or player).
        /// Used for game-level anti-repetition; call from NotifyPositionAfterMove.
        /// </summary>
        public virtual void RecordGamePosition(TQ_HexBoardModel board)
        {
            if (board == null || _gamePositionHashes == null) return;
            ulong h = ZobristHash.CalculateBoardHash(board);
            _gamePositionHashes.Add(h);
            while (_gamePositionHashes.Count > positionHistoryCapacity)
                _gamePositionHashes.RemoveAt(0);
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

            // 2. Endgame specialization: use this component as EndgameV2 (never 'new' a MonoBehaviour)
            var endgameAI = this as TQ_CheckerAIManagerEndgameV2;
            if (endgameAI != null && (outCount == 1 || outCount == 2))
            {
                endgameAI.InitMinMax(board, CachedEnemyTargetPositions, CurrentDifficulty);
                var endgameMove = endgameAI.CalculateEndgameBestMove(board, outOfTargetPieces);
                if (endgameMove != null) return endgameMove;
                // Fall through: no path found (blocked), use full Minimax with target-area moves
            }

            // 3. Blocked: (a) outCount>=2 and no piece can reach any empty target, or (b) endgame had no path (1-2 pieces)
            bool blocked = false;
            if (outCount == 1 || outCount == 2)
                blocked = true; // Endgame returned null = no path → use full Minimax with target-area moves
            else if (outCount >= 2 && endgameAI != null)
            {
                endgameAI.InitMinMax(board, CachedEnemyTargetPositions, CurrentDifficulty);
                blocked = endgameAI.IsOutOfTargetBlocked(board, outOfTargetPieces);
            }

            // 4. Standard Minimax logic: when blocked (or endgame null), include target-area moves
            var initialHash = ZobristHash.CalculateBoardHash(board);
            ResetAlgorithmState();

            _allValidMoves = blocked
                ? GenerateAllEnemyValidMoves(board, enemyPieces, allowTargetAreaMoves: true)
                : (outCount >= 2
                    ? GenerateAllEnemyValidMoves(board, outOfTargetPieces)
                    : GenerateAllEnemyValidMoves(board, enemyPieces));

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

            if (_moveHistory.Count != 0 || _simulatedMoveDepth != 0) UnityEngine.Debug.LogError($"[Minimax] Make/Undo mismatch: _moveHistory.Count={_moveHistory.Count}, _simulatedMoveDepth={_simulatedMoveDepth}");

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

            // Hash and position history (for TT and anti-repetition)
            ulong boardHash = _currentBoardHash != 0 ? _currentBoardHash : ZobristHash.CalculateBoardHash(board);
            if (_positionHistory.Count < positionHistoryCapacity)
                _positionHistory.Add(boardHash);

            try
            {
            // Termination conditions (base cases)
            if (depth == 0 || currentRecursionDepth >= maxRecursionDepth)
                return EvaluateBoardState(board);

            // Immediate return for terminal game states
            if (IsGameOver(TQ_PieceOwner.Enemy, board)) return winBonus;
            if (IsGameOver(TQ_PieceOwner.Player, board)) return losePenalty;

            // Transposition table lookup (with entry type for correct alpha-beta use)
            if (useTranspositionTable && _transpositionTable.TryGetValue(boardHash, out var cached))
            {
                if (cached.depth >= depth)
                {
                    switch (cached.type)
                    {
                        case TTEntryType.Exact:
                            return cached.score;
                        case TTEntryType.LowerBound:
                            if (cached.score >= beta) return cached.score;
                            alpha = Mathf.Max(alpha, cached.score);
                            break;
                        case TTEntryType.UpperBound:
                            if (cached.score <= alpha) return cached.score;
                            beta = Mathf.Min(beta, cached.score);
                            break;
                    }
                }
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

            // Sort moves: killer + history + jump + score (depthFromRoot for killer lookup)
            currentMoves = SortMoves(currentMoves, isMaxPlayer, currentRecursionDepth);

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

                // Update transposition table (entry type for correct reuse)
                if (useTranspositionTable)
                {
                    var ttType = maxEval >= beta ? TTEntryType.LowerBound : (maxEval <= alpha ? TTEntryType.UpperBound : TTEntryType.Exact);
                    _transpositionTable[boardHash] = (maxEval, depth, ttType, alpha, beta);
                }
                return maxEval;
            }
            else // Player turn (minimize score)
            {
                float minEval = float.MaxValue;
                foreach (var move in currentMoves)
                {
                    var fromPos = new Vector2Int(move.piece.CurrentCell.Q, move.piece.CurrentCell.R);
                    var toPos = new Vector2Int(move.targetCell.Q, move.targetCell.R);

                    MakeSimulatedMove(move, board);
                    eval = Minimax(depth - 1, alpha, beta, true, currentRecursionDepth + 1, board);
                    UndoSimulatedMove(move, board);

                    minEval = Mathf.Min(minEval, eval);
                    beta = Mathf.Min(beta, eval);
                    if (beta <= alpha)
                    {
                        // Killer: this move caused beta cutoff at this depth
                        _killer2[currentRecursionDepth] = _killer1.TryGetValue(currentRecursionDepth, out var k1) ? k1 : (fromPos, toPos);
                        _killer1[currentRecursionDepth] = (fromPos, toPos);
                        var histKey = (fromPos.x, fromPos.y, toPos.x, toPos.y);
                        _historyScore[histKey] = _historyScore.GetValueOrDefault(histKey, 0) + depth * depth;
                        break;
                    }
                }

                if (useTranspositionTable)
                {
                    var ttType = minEval <= alpha ? TTEntryType.UpperBound : (minEval >= beta ? TTEntryType.LowerBound : TTEntryType.Exact);
                    _transpositionTable[boardHash] = (minEval, depth, ttType, alpha, beta);
                }
                return minEval;
            }
            }
            finally
            {
                if (_positionHistory.Count > 0)
                    _positionHistory.RemoveAt(_positionHistory.Count - 1);
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
                // Reorder root moves so PV from previous iteration is searched first
                if (depth > 1 && _principalVariation.Count > 0 && _allValidMoves != null && _allValidMoves.Count > 1)
                {
                    var pvMove = _principalVariation[0];
                    int idx = _allValidMoves.FindIndex(m => m != null && pvMove != null && m.piece == pvMove.piece && m.targetCell == pvMove.targetCell);
                    if (idx > 0)
                    {
                        var move = _allValidMoves[idx];
                        _allValidMoves.RemoveAt(idx);
                        _allValidMoves.Insert(0, move);
                    }
                }
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

            // Store PV (best root move) for next iteration's move ordering
            _principalVariation.Clear();
            if (bestMove != null)
                _principalVariation.Add(bestMove);

            return bestMove;
        }
        #endregion

        #region Helper Methods (Enhanced with Endgame Optimization)
        /// <summary>
        /// Generate all valid moves for enemy pieces with endgame/base camp optimization.
        /// When allowTargetAreaMoves is true (blocked position), include moves for pieces already in target area (target-only moves).
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <param name="enemyPieces">AI-controlled pieces</param>
        /// <param name="allowTargetAreaMoves">If true, do not skip target-area pieces; generate target-only moves for them (unblocking)</param>
        /// <returns>List of valid enemy moves</returns>
        protected virtual List<TQAI_AIMove> GenerateAllEnemyValidMoves(TQ_HexBoardModel board, List<TQ_ChessPieceModel> enemyPieces, bool allowTargetAreaMoves = false)
        {
            var validMoves = new List<TQAI_AIMove>();

            bool needForceLeave = IsForceLeaveHomeRequired(_gameManager._currentRound, board);

            foreach (var piece in enemyPieces)
            {
                if (needForceLeave)
                {
                    if (!IsInOwnBase(piece.CurrentCell, piece.Owner))
                        continue;
                }
                else if (!allowTargetAreaMoves)
                {
                    // Normal: skip pieces in target area when ≥2 remain outside
                    var outOfTargetPieces = enemyPieces.Where(p => !IsPieceInTargetArea(p, board)).ToList();
                    if (outOfTargetPieces.Count >= 2 && IsPieceInTargetArea(piece, board))
                        continue;
                }

                // For target-area pieces when allowTargetAreaMoves: use target-only overload
                bool targetOnly = allowTargetAreaMoves && IsPieceInTargetArea(piece, board);
                validMoves.AddRange(CalculatePieceValidMoves(piece, board, targetOnly));
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
                        // Hard checks: never generate self-target or occupied targets for simulation.
                        // This prevents illegal moves reaching Minimax -> MakeSimulatedMove.
                        if (cell == null) continue;
                        if (piece.CurrentCell == null) continue;

                        bool isSelfTarget = cell.Q == piece.CurrentCell.Q && cell.R == piece.CurrentCell.R;
                        if (isSelfTarget || cell.IsOccupied)
                        {
                            DebugLog(
                                $"[PlayerGen][HardSkip] illegal target generated. " +
                                $"piece=({piece.CurrentCell.Q},{piece.CurrentCell.R}) owner={piece.Owner} " +
                                $"cell=({cell.Q},{cell.R}) cell.IsOccupied={cell.IsOccupied} isSelfTarget={isSelfTarget} " +
                                $"cell.CurrentPiece={(cell.CurrentPiece != null ? $"({cell.CurrentPiece.CurrentCell?.Q},{cell.CurrentPiece.CurrentCell?.R}) owner={cell.CurrentPiece.Owner}" : "null")}"
                            );
                            continue;
                        }

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

            // 1. Target progress score: reward pieces being closer to target area (区外子离目标区越近越好)
            float progressScore = outOfTargetPieces.Sum(p => GetPositionProgressScore(p, board))
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

            // 6. Anti-repetition: penalize positions in current path AND in game history (stops back-and-forth loops)
            ulong posHash = ZobristHash.CalculateBoardHash(board);
            int repeatCount = 0;
            for (int i = 0; i < _positionHistory.Count; i++)
                if (_positionHistory[i] == posHash) repeatCount++;
            if (_gamePositionHashes != null)
                for (int i = 0; i < _gamePositionHashes.Count; i++)
                    if (_gamePositionHashes[i] == posHash) repeatCount++;
            float repetitionPenalty = repeatCount >= 5 ? repetitionPenaltyFiveOrMore
                : (repeatCount >= 3 ? repetitionPenaltyThree : (repeatCount >= 2 ? repetitionPenaltyOnce : 0f));

            // 7. Entry cell bonus: reward empty target cells that are one step reachable from outside (unblocking)
            int freeEntryCount = CountFreeEntryCells(board);
            float entryBonus = freeEntryCount * entryCellBonusWeight;

            // Weighted combination of all evaluation factors
            return progressScore * targetProgressWeight
                   + occupyScore * targetAreaOccupyWeight
                   + mobilityScore * mobilityWeight
                   + blockScore * blockOpponentWeight
                   + basePenalty
                   + repetitionPenalty
                   + entryBonus;
        }

        /// <summary>
        /// Select move when Minimax has no result (fallback). All difficulties use optimal move by score.
        /// </summary>
        /// <returns>Best move by score, or null if none</returns>
        protected virtual TQAI_AIMove SelectMoveByDifficulty()
        {
            if (_allValidMoves.Count == 0) return null;
            return _allValidMoves.OrderByDescending(m => m.score).FirstOrDefault();
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
            var targetCellPieceBefore = targetCell.IsOccupied ? targetCell.CurrentPiece : null;

            // Checkpoint C: verify that the move's piece/oldPos and board state agree before applying.
            // This helps detect reference reuse / snapshot pollution.
            var oldCellCheck = board.GetCellByCoordinates(oldPos.x, oldPos.y);
            if (oldCellCheck == null)
            {
                UnityEngine.Debug.LogError($"[Minimax][CheckpointC] oldCell not found in board. oldPos=({oldPos.x},{oldPos.y}) boardHash={_currentBoardHash} simDepthBefore={_simulatedMoveDepth}");
            }
            else
            {
                bool oldCellPieceOk = oldCellCheck.CurrentPiece == piece;
                if (!oldCellPieceOk || !oldCellCheck.IsOccupied)
                {
                    UnityEngine.Debug.LogError(
                        $"[Minimax][CheckpointC] board-state mismatch before make. " +
                        $"oldPos=({oldPos.x},{oldPos.y}) oldCell.IsOccupied={oldCellCheck.IsOccupied} " +
                        $"oldCell.CurrentPiece={(oldCellCheck.CurrentPiece != null ? $"({oldCellCheck.CurrentPiece.CurrentCell?.Q},{oldCellCheck.CurrentPiece.CurrentCell?.R}) owner={oldCellCheck.CurrentPiece.Owner}" : "null")} " +
                        $"piece.CurrentCell=({piece.CurrentCell?.Q},{piece.CurrentCell?.R}) piece.owner={(piece.Owner != null ? piece.Owner.ToString() : "null")} simDepthBefore={_simulatedMoveDepth} boardHash={_currentBoardHash}"
                    );
                }
            }

            // Debug sanity: a legal move must never be self-target, and must not target an occupied cell.
            if ((piece.CurrentCell != null && piece.CurrentCell.Q == targetCell.Q && piece.CurrentCell.R == targetCell.R) || targetCell.IsOccupied)
            {
                bool isSelfTarget = piece.CurrentCell != null && piece.CurrentCell.Q == targetCell.Q && piece.CurrentCell.R == targetCell.R;
                var fromPieceCell = piece.CurrentCell;

                UnityEngine.Debug.LogError(
                    $"[Minimax][MakeSimulatedMove] invalid target state before make. " +
                    $"from=({oldPos.x},{oldPos.y}) to=({newPos.x},{newPos.y}) " +
                    $"wasOccupied={wasOccupied} target.IsOccupied={targetCell.IsOccupied} " +
                    $"isSelfTarget={isSelfTarget} " +
                    $"move.isJumpMove={move.isJumpMove} move.jumpStepCount={move.jumpStepCount} " +
                    $"piece.owner={(piece.Owner != null ? piece.Owner.ToString() : "空")} " +
                    $"piece.CurrentCell=({fromPieceCell?.Q},{fromPieceCell?.R}) " +
                    $"target.CurrentPiece={(targetCellPieceBefore != null ? $"({targetCellPieceBefore.CurrentCell?.Q},{targetCellPieceBefore.CurrentCell?.R}) owner={(targetCellPieceBefore.Owner != null ? targetCellPieceBefore.Owner.ToString() : "空")}" : "空")} " +
                    $"moveId={move.GetHashCode()} pieceId={piece.GetHashCode()} targetCellId={targetCell.GetHashCode()} " +
                    $"simDepthBefore={_simulatedMoveDepth} boardHash={_currentBoardHash}"
                );
            }

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

            _simulatedMoveDepth++;
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
            _simulatedMoveDepth--;
            var oldCell = board.GetCellByCoordinates(record.oldPos.x, record.oldPos.y);
            var newCell = board.GetCellByCoordinates(record.newPos.x, record.newPos.y);

            // Null safety checks
            if (oldCell == null || newCell == null || record.piece == null)
            {
                UnityEngine.Debug.LogError($"UndoSimulatedMove: {record.oldPos} {record.newPos} {record.piece} {_moveHistory.Count} Can not recovery board after pop , board maybe wrong..");
                return;
            }

            // Restore hash state (critical for transposition table)
            _currentBoardHash = record.oldHash;

            // Restore board state to pre-move condition
            // NOTE: 当前实现没有恢复 newCell 原先的 CurrentPiece 引用；
            // 这里用严格日志帮助你确认是否因为该问题导致规则引擎/走法生成不一致。
            bool expectedNewCellOccupied = record.wasOccupied;
            var prevNewCellPiece = newCell.CurrentPiece;
            newCell.CurrentPiece = null;
            newCell.IsOccupied = expectedNewCellOccupied;
            oldCell.CurrentPiece = record.piece;
            oldCell.IsOccupied = true;

            // Restore piece position
            record.piece.CurrentCell = oldCell;

            // Strict consistency check (local)
            bool mismatch = false;

            // oldCell must restore back to (occupied + record.piece) and piece.CurrentCell must map to oldCell
            if (!oldCell.IsOccupied) mismatch = true;
            if (oldCell.CurrentPiece != record.piece) mismatch = true;
            if (record.piece.CurrentCell != oldCell) mismatch = true;
            if (oldCell.CurrentPiece != null && oldCell.CurrentPiece.CurrentCell != oldCell) mismatch = true;

            // newCell occupancy & piece invariants
            if (newCell.IsOccupied != expectedNewCellOccupied) mismatch = true;
            if (newCell.IsOccupied)
            {
                if (newCell.CurrentPiece == null) mismatch = true;
                else if (newCell.CurrentPiece.CurrentCell != newCell) mismatch = true;
            }
            else
            {
                if (newCell.CurrentPiece != null) mismatch = true;
            }

            if (mismatch)
            {
                UnityEngine.Debug.LogError($"[Minimax][UndoSimulatedMove] rollback mismatch after undo. " +
                               $"old=({record.oldPos.x},{record.oldPos.y}) IsOccupied={oldCell.IsOccupied} " +
                               $"old.CurrentPiece={(oldCell.CurrentPiece != null ? $"({oldCell.CurrentPiece.CurrentCell?.Q},{oldCell.CurrentPiece.CurrentCell?.R})" : "null")} " +
                               $"piece.CurrentCell={(record.piece.CurrentCell != null ? $"({record.piece.CurrentCell.Q},{record.piece.CurrentCell.R})" : "null")} " +
                               $"new=({record.newPos.x},{record.newPos.y}) new.IsOccupied={newCell.IsOccupied} " +
                               $"new.CurrentPiece={(newCell.CurrentPiece != null ? $"({newCell.CurrentPiece.CurrentCell?.Q},{newCell.CurrentPiece.CurrentCell?.R})" : "null")} " +
                               $"expectedNewOccupied={expectedNewCellOccupied} prevNewPiece={(prevNewCellPiece != null ? $"({prevNewCellPiece.CurrentCell?.Q},{prevNewCellPiece.CurrentCell?.R})" : "null")}");
            }
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
            _principalVariation.Clear();
            _historyScore.Clear();
            _killer1.Clear();
            _killer2.Clear();
            _positionHistory.Clear();
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
        /// Enhanced move sorting: killer first, then history heuristic, then jump, then score.
        /// </summary>
        /// <param name="moves">Moves to sort</param>
        /// <param name="isMaxPlayer">True if sorting for AI (max player)</param>
        /// <param name="depthFromRoot">Current recursion depth (for killer lookup); use -1 to skip killer</param>
        /// <returns>Sorted list of moves</returns>
        protected virtual List<TQAI_AIMove> SortMoves(List<TQAI_AIMove> moves, bool isMaxPlayer, int depthFromRoot = -1)
        {
            int killerOrder(TQAI_AIMove m)
            {
                if (depthFromRoot < 0) return 0;
                var from = m.piece?.CurrentCell != null ? new Vector2Int(m.piece.CurrentCell.Q, m.piece.CurrentCell.R) : default;
                var to = m.targetCell != null ? new Vector2Int(m.targetCell.Q, m.targetCell.R) : default;
                if (_killer1.TryGetValue(depthFromRoot, out var k1) && k1.from == from && k1.to == to) return 2;
                if (_killer2.TryGetValue(depthFromRoot, out var k2) && k2.from == from && k2.to == to) return 1;
                return 0;
            }
            int historyScore(TQAI_AIMove m)
            {
                if (m.piece?.CurrentCell == null || m.targetCell == null) return 0;
                var key = (m.piece.CurrentCell.Q, m.piece.CurrentCell.R, m.targetCell.Q, m.targetCell.R);
                return _historyScore.GetValueOrDefault(key, 0);
            }
            if (isMaxPlayer)
                return moves.OrderByDescending(m => killerOrder(m))
                            .ThenByDescending(m => historyScore(m))
                            .ThenByDescending(m => m.isJumpMove)
                            .ThenByDescending(m => m.score)
                            .ToList();
            else
                return moves.OrderByDescending(m => killerOrder(m))
                            .ThenByDescending(m => historyScore(m))
                            .ThenByDescending(m => m.isJumpMove)
                            .ThenBy(m => m.score)
                            .ToList();
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
        /// Select move for Easy difficulty. All difficulties use optimal move by score.
        /// </summary>
        protected virtual TQAI_AIMove SelectEasyMove()
        {
            if (_allValidMoves.Count == 0) return null;
            return _allValidMoves.OrderByDescending(m => m.score).FirstOrDefault();
        }

        /// <summary>
        /// Select move for Medium difficulty. All difficulties use optimal move by score.
        /// </summary>
        protected virtual TQAI_AIMove SelectMediumMove()
        {
            if (_allValidMoves.Count == 0) return null;
            return _allValidMoves.OrderByDescending(m => m.score).FirstOrDefault();
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