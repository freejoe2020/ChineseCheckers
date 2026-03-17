using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Diagnostics;

namespace Free.Checkers
{
    /// <summary>
    /// AI Minimax Algorithm Layer (Version 1)
    /// Responsible for multi-depth weighted optimal move calculation (decoupled from business execution)
    /// Implements Minimax with Alpha-Beta pruning and iterative deepening for performance optimization
    /// </summary>
    public class TQ_CheckerAIManagerMinMaxV1 : TQ_CheckerAIManagerCoreV1
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
        [Tooltip("Weight for progress toward target area (primary objective)")]
        public float targetProgressWeight = 10f;

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
        /// Move history stack: tracks simulated moves for undo functionality
        /// Critical for backtracking in Minimax algorithm
        /// </summary>
        protected Stack<(TQ_ChessPieceModel piece, Vector2Int oldPos, Vector2Int newPos, bool wasOccupied)> _moveHistory;

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

        #region Lifecycle Management
        /// <summary>
        /// Awake: Initialize algorithm-specific resources
        /// Extends base class initialization with algorithm caches
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            // Initialize algorithm optimization structures
            _transpositionTable = new Dictionary<ulong, (float, int)>();
            _moveHistory = new Stack<(TQ_ChessPieceModel, Vector2Int, Vector2Int, bool)>();
            _searchStopwatch = new Stopwatch();
        }

        /// <summary>
        /// Initialize algorithm layer (extends base class with difficulty configuration)
        /// Sets up Minimax algorithm with board data and difficulty settings
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
        /// Calculate best move using Minimax with Alpha-Beta pruning
        /// Main entry point for algorithm layer
        /// </summary>
        /// <param name="board">Board snapshot (isolated from real game state)</param>
        /// <param name="enemyPieces">Valid AI-controlled pieces</param>
        /// <returns>Optimal move for AI (highest evaluated score)</returns>
        public virtual TQAI_AIMove CalculateBestMove(TQ_HexBoardModel board, List<TQ_ChessPieceModel> enemyPieces)
        {
            // 0. Backup initial board state (for validation/rollback)
            var initialState = BackupBoardState(board);
            DebugLog($"CalculateBestMove started, initial snapshot piece count: {initialState.enemyPieceCount}");

            // 1. Reset algorithm state for clean search
            ResetAlgorithmState();

            // 2. Generate all valid moves for enemy pieces
            _allValidMoves = GenerateAllEnemyValidMoves(board, enemyPieces);
            if (_allValidMoves.Count == 0) return null;

            // 3. Determine search depth based on difficulty
            var searchDepth = GetSearchDepthByDifficulty();
            _firstLayerMoveCount = _allValidMoves.Count;

            // 4. Iterative deepening search for optimal move (balances depth/performance)
            _searchStopwatch.Start();
            var bestMove = FindBestMoveWithIterativeDeepening(searchDepth, board);
            _searchStopwatch.Stop();

            // 5. Validate board state wasn't corrupted during calculation (critical!)
            var finalState = BackupBoardState(board);
            if (!ValidateBoardState(initialState, finalState))
            {
                DebugLogError("❌ Initial snapshot state corrupted! Calculation results untrustworthy");
                // Fallback: could recalculate or use simple move selection
            }
            else
            {
                DebugLog("✅ Initial snapshot state intact");
            }

            // 6. Log search statistics for optimization/debugging
            LogSearchStats(searchDepth);

            // Return best move or fallback based on difficulty
            return bestMove ?? SelectMoveByDifficulty();
        }

        /// <summary>
        /// Backup critical board state for validation
        /// Captures piece counts and positions to detect corruption
        /// </summary>
        /// <param name="board">Board to backup</param>
        /// <returns>Struct containing critical board state information</returns>
        private (int enemyPieceCount, int playerPieceCount, List<(int q, int r)> enemyPiecePositions) BackupBoardState(TQ_HexBoardModel board)
        {
            if (board == null) return (0, 0, new List<(int, int)>());

            // Capture enemy piece positions for validation
            var enemyPositions = board.EnemyPieces
                .Where(p => p != null && p.CurrentCell != null)
                .Select(p => (p.CurrentCell.Q, p.CurrentCell.R))
                .ToList();

            return (
                // Valid enemy piece count
                board.EnemyPieces.Count(p => p != null && p.CurrentCell != null),
                // Valid player piece count
                board.PlayerPieces.Count(p => p != null && p.CurrentCell != null),
                // Enemy piece positions for verification
                enemyPositions
            );
        }

        /// <summary>
        /// Validate board state integrity (before/after comparison)
        /// Ensures calculations didn't corrupt the board snapshot
        /// </summary>
        /// <param name="initial">Initial board state</param>
        /// <param name="final">Final board state after calculations</param>
        /// <returns>True if board state is unchanged (valid)</returns>
        private bool ValidateBoardState((int enemyPieceCount, int playerPieceCount, List<(int q, int r)> enemyPiecePositions) initial,
                                        (int enemyPieceCount, int playerPieceCount, List<(int q, int r)> enemyPiecePositions) final)
        {
            // Basic count validation
            if (initial.enemyPieceCount != final.enemyPieceCount) return false;
            if (initial.playerPieceCount != final.playerPieceCount) return false;
            if (initial.enemyPiecePositions.Count != final.enemyPiecePositions.Count) return false;

            // Position validation (critical for state integrity)
            for (int i = 0; i < initial.enemyPiecePositions.Count; i++)
            {
                if (initial.enemyPiecePositions[i].q != final.enemyPiecePositions[i].q ||
                    initial.enemyPiecePositions[i].r != final.enemyPiecePositions[i].r)
                    return false;
            }

            return true;
        }
        #endregion

        #region Minimax Core Algorithm
        /// <summary>
        /// Minimax algorithm with Alpha-Beta pruning
        /// Recursively evaluates board states to find optimal move
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
            // Stop if max depth reached, recursion limit hit, or game over
            if (depth == 0 || currentRecursionDepth >= maxRecursionDepth)
                return EvaluateBoardState(board);

            // Immediate return for terminal game states
            if (IsGameOver(TQ_PieceOwner.Enemy, board)) return winBonus;
            if (IsGameOver(TQ_PieceOwner.Player, board)) return losePenalty;

            // Transposition table lookup (performance optimization)
            if (useTranspositionTable)
            {
                var boardHash = GetBoardHash(board);
                if (_transpositionTable.TryGetValue(boardHash, out var cached))
                {
                    // Return cached value if it's from equal or deeper search
                    if (cached.depth >= depth) return cached.score;
                }
            }

            // Get all valid moves for current player
            var currentMoves = isMaxPlayer
                ? GenerateAllEnemyValidMoves(board, GetValidEnemyPieces(board))
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
                    // Create board snapshot for simulation
                    var snapshot = _boardStateService.CreateBoardSnapshot();

                    // Simulate move
                    MakeSimulatedMove(move, snapshot);

                    // Recursive Minimax call (opponent's turn)
                    eval = Minimax(depth - 1, alpha, beta, false, currentRecursionDepth + 1, snapshot);

                    // Undo simulated move (critical for backtracking)
                    UndoSimulatedMove(move, snapshot);

                    // Clean up snapshot
                    _boardStateService.ReleaseBoardSnapshot(snapshot);

                    // Update best score for maximizing player
                    maxEval = Mathf.Max(maxEval, eval);
                    alpha = Mathf.Max(alpha, eval);

                    // Alpha-Beta pruning (no need to evaluate further moves)
                    if (beta <= alpha) break;
                }

                // Update transposition table with best score
                if (useTranspositionTable)
                    _transpositionTable[GetBoardHash(board)] = (maxEval, depth);

                return maxEval;
            }
            else // Player turn (minimize score)
            {
                float minEval = float.MaxValue;
                foreach (var move in currentMoves)
                {
                    // Create board snapshot for simulation
                    var snapshot = _boardStateService.CreateBoardSnapshot();

                    // Simulate move
                    MakeSimulatedMove(move, snapshot);

                    // Recursive Minimax call (AI's turn)
                    eval = Minimax(depth - 1, alpha, beta, true, currentRecursionDepth + 1, snapshot);

                    // Undo simulated move (critical for backtracking)
                    UndoSimulatedMove(move, snapshot);

                    // Clean up snapshot
                    _boardStateService.ReleaseBoardSnapshot(snapshot);

                    // Update best score for minimizing player
                    minEval = Mathf.Min(minEval, eval);
                    beta = Mathf.Min(beta, eval);

                    // Alpha-Beta pruning (no need to evaluate further moves)
                    if (beta <= alpha) break;
                }

                // Update transposition table with best score
                if (useTranspositionTable)
                    _transpositionTable[GetBoardHash(board)] = (minEval, depth);

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
                // Create board snapshot for simulation
                var snapshot = _boardStateService.CreateBoardSnapshot();

                // Simulate move
                MakeSimulatedMove(move, snapshot);

                // Evaluate move with Minimax (opponent's turn next)
                var currentScore = Minimax(searchDepth - 1, alpha, beta, false, 1, snapshot);

                // Undo simulated move
                UndoSimulatedMove(move, snapshot);

                // Clean up snapshot
                _boardStateService.ReleaseBoardSnapshot(snapshot);

                // Update move score and track best move
                move.score = currentScore;
                if (currentScore > bestScore)
                {
                    bestScore = currentScore;
                    bestMove = move;
                }

                // Update alpha for Alpha-Beta pruning
                alpha = Mathf.Max(alpha, currentScore);
            }

            return bestMove;
        }
        #endregion

        #region Helper Methods (Algorithm-Specific)
        /// <summary>
        /// Generate all valid moves for all enemy pieces
        /// Uses core calculation method from base class
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <param name="enemyPieces">AI-controlled pieces</param>
        /// <returns>List of all valid moves for enemy pieces</returns>
        protected virtual List<TQAI_AIMove> GenerateAllEnemyValidMoves(TQ_HexBoardModel board, List<TQ_ChessPieceModel> enemyPieces)
        {
            var validMoves = new List<TQAI_AIMove>();

            // Calculate valid moves for each enemy piece
            foreach (var piece in enemyPieces)
                validMoves.AddRange(CalculatePieceValidMoves(piece, board));

            return validMoves;
        }

        /// <summary>
        /// Generate all valid moves for all player pieces (for opponent simulation)
        /// Used to predict opponent responses in Minimax
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <returns>List of all valid moves for player pieces</returns>
        protected virtual List<TQAI_AIMove> GenerateAllPlayerValidMoves(TQ_HexBoardModel board)
        {
            var validMoves = new List<TQAI_AIMove>();
            var playerPieces = board.PlayerPieces.Where(p => p != null && p.CurrentCell != null).ToList();

            foreach (var piece in playerPieces)
            {
                // Bind rule engine to current board snapshot
                BindRuleEngineToBoard(board);

                // Calculate valid moves for player piece
                _aiMoveContext.Clear();
                var validCells = _ruleEngine.GetValidMoves(piece, _aiMoveContext).Cast<TQ_HexCellModel>().ToList();

                // Create AI move objects for each valid player move
                foreach (var cell in validCells)
                {
                    validMoves.Add(new TQAI_AIMove(piece, cell, 0f, false, 0, new List<TQ_HexCellModel> { piece.CurrentCell, cell }));
                }
            }

            return validMoves;
        }

        /// <summary>
        /// Evaluate board state (multi-dimensional weighted scoring)
        /// Core heuristic function for Minimax algorithm
        /// </summary>
        /// <param name="board">Board snapshot to evaluate</param>
        /// <returns>Overall score for board state (higher = better for AI)</returns>
        protected virtual float EvaluateBoardState(TQ_HexBoardModel board)
        {
            if (board == null) return 0f;

            // 1. Target progress score (primary objective: move toward target area)
            var enemyPieces = GetValidEnemyPieces(board);
            float progressScore = enemyPieces.Sum(p => CalculateTargetProgressScore(p, p.CurrentCell, board))
                                 / Mathf.Max(1, enemyPieces.Count);

            // 2. Target area occupation score (reward for occupying target cells)
            int targetOccupyCount = enemyPieces.Count(p => IsPieceInTargetArea(p, board));
            float occupyScore = Mathf.Min((float)targetOccupyCount / 10f, 1f);

            // 3. Mobility score (reward for having more valid moves)
            float mobilityScore = enemyPieces.Sum(p => CalculatePieceValidMoves(p, board).Count)
                                 / Mathf.Max(1, enemyPieces.Count * 6f);

            // 4. Block opponent score (reward for limiting opponent's mobility)
            float blockScore = CalculateBlockOpponentScore(board);

            // Weighted combination of all factors
            return progressScore * targetProgressWeight
                   + occupyScore * targetAreaOccupyWeight
                   + mobilityScore * mobilityWeight
                   + blockScore * blockOpponentWeight;
        }

        /// <summary>
        /// Select move (fallback when Minimax fails). All difficulties use optimal move by score.
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
        /// Simulate move (simplified version: modifies snapshot directly)
        /// Critical for Minimax simulation - no effect on real game state
        /// </summary>
        /// <param name="move">Move to simulate</param>
        /// <param name="board">Board snapshot to modify</param>
        protected virtual void MakeSimulatedMove(TQAI_AIMove move, TQ_HexBoardModel board)
        {
            if (move == null || board == null) return;

            // Find piece in snapshot by coordinates (critical: snapshot object isolation)
            var pieceInSnapshot = board.EnemyPieces.FirstOrDefault(p =>
                p != null && p.CurrentCell != null &&
                p.CurrentCell.Q == move.piece.CurrentCell.Q &&
                p.CurrentCell.R == move.piece.CurrentCell.R) ??
                board.PlayerPieces.FirstOrDefault(p =>
                    p != null && p.CurrentCell != null &&
                    p.CurrentCell.Q == move.piece.CurrentCell.Q &&
                    p.CurrentCell.R == move.piece.CurrentCell.R);

            // Find target cell in snapshot by coordinates
            var targetInSnapshot = board.GetCellByCoordinates(move.targetCell.Q, move.targetCell.R);

            if (pieceInSnapshot == null || targetInSnapshot == null)
            {
                DebugLogWarning($"Simulated move failed: Piece ({move.piece.CurrentCell.Q},{move.piece.CurrentCell.R}) or target ({move.targetCell.Q},{move.targetCell.R}) not found in snapshot");
                return;
            }

            // Record move history for undo (critical for backtracking)
            var oldPos = new Vector2Int(pieceInSnapshot.CurrentCell.Q, pieceInSnapshot.CurrentCell.R);
            var newPos = new Vector2Int(targetInSnapshot.Q, targetInSnapshot.R);
            var wasOccupied = targetInSnapshot.IsOccupied;
            var oldCellInSnapshot = board.GetCellByCoordinates(oldPos.x, oldPos.y);

            // Execute simulated move (modify snapshot state)
            if (oldCellInSnapshot != null)
            {
                oldCellInSnapshot.IsOccupied = false;
                oldCellInSnapshot.CurrentPiece = null;
            }
            pieceInSnapshot.CurrentCell = targetInSnapshot;
            targetInSnapshot.IsOccupied = true;
            targetInSnapshot.CurrentPiece = pieceInSnapshot;

            // Save undo information to history stack
            _moveHistory.Push((pieceInSnapshot, oldPos, newPos, wasOccupied));
        }

        /// <summary>
        /// Undo simulated move (simplified version)
        /// Restores board snapshot to state before simulated move
        /// </summary>
        /// <param name="move">Move to undo</param>
        /// <param name="board">Board snapshot to restore</param>
        protected virtual void UndoSimulatedMove(TQAI_AIMove move, TQ_HexBoardModel board)
        {
            if (_moveHistory.Count == 0 || board == null) return;

            // Retrieve move history record
            var record = _moveHistory.Pop();
            var oldCell = board.GetCellByCoordinates(record.oldPos.x, record.oldPos.y);
            var newCell = board.GetCellByCoordinates(record.newPos.x, record.newPos.y);

            if (oldCell == null || newCell == null)
            {
                DebugLogWarning($"Undo simulated move failed: Cells not found in snapshot");
                return;
            }

            // Restore board state to pre-move condition
            newCell.CurrentPiece = null;
            newCell.IsOccupied = record.wasOccupied;
            oldCell.CurrentPiece = record.piece;
            oldCell.IsOccupied = true;

            // Restore piece position
            if (record.piece != null)
            {
                record.piece.CurrentCell = oldCell;
            }
        }

        /// <summary>
        /// Get search depth based on difficulty and game phase
        /// Adjusts depth dynamically for endgame (deeper search)
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

            // Increase depth for endgame (more critical moves)
            var enemyPieces = GetValidEnemyPieces(_boardStateService.CreateBoardSnapshot());
            var inEndgame = enemyPieces.Count(p => IsPieceInTargetArea(p, null)) >= 8;
            return inEndgame ? baseDepth + 1 : Mathf.Max(1, baseDepth);
        }

        /// <summary>
        /// Reset algorithm state (before each search)
        /// Prevents cross-search contamination of cached data
        /// </summary>
        protected virtual void ResetAlgorithmState()
        {
            _transpositionTable.Clear();
            _moveHistory.Clear();
            _recursiveStepCount = 0;
            _allValidMoves = new List<TQAI_AIMove>();
            _searchStopwatch.Reset();
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
        /// Generate board hash (unique identifier for transposition table)
        /// Simple hash based on enemy piece positions
        /// </summary>
        /// <param name="board">Board to hash</param>
        /// <returns>64-bit hash value</returns>
        protected virtual ulong GetBoardHash(TQ_HexBoardModel board)
        {
            ulong hash = 0;
            int index = 0;

            // Create hash from enemy piece positions
            foreach (var piece in GetValidEnemyPieces(board))
            {
                hash ^= (ulong)(piece.CurrentCell.Q * 100 + piece.CurrentCell.R) * (ulong)(index + 1);
                index++;
            }

            return hash;
        }

        /// <summary>
        /// Sort moves to improve Alpha-Beta pruning efficiency
        /// Orders moves by score to find good/bad moves early (maximizes pruning)
        /// </summary>
        /// <param name="moves">Moves to sort</param>
        /// <param name="isMaxPlayer">True if sorting for AI (max player)</param>
        /// <returns>Sorted list of moves</returns>
        protected virtual List<TQAI_AIMove> SortMoves(List<TQAI_AIMove> moves, bool isMaxPlayer)
        {
            return isMaxPlayer
                ? moves.OrderByDescending(m => m.score).ThenByDescending(m => m.isJumpMove).ToList()
                : moves.OrderBy(m => m.score).ThenBy(m => m.isJumpMove).ToList();
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
        /// Calculate block opponent score (reward for limiting opponent mobility)
        /// Measures how many of opponent's potential moves are blocked
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <returns>Block score (0-1, higher = better blocking)</returns>
        protected virtual float CalculateBlockOpponentScore(TQ_HexBoardModel board)
        {
            if (board == null) return 0f;

            var playerPieces = board.PlayerPieces.Where(p => p != null && p.CurrentCell != null).ToList();
            int totalMoves = 0, blockedMoves = 0;

            foreach (var piece in playerPieces)
            {
                var validMoves = CalculatePieceValidMoves(piece, board);
                totalMoves += validMoves.Count;
                blockedMoves += validMoves.Count(m => m.targetCell.IsOccupied);
            }

            // Return ratio of blocked moves (0 if no moves to block)
            return totalMoves > 0 ? (float)blockedMoves / totalMoves : 0f;
        }
        #endregion
    }
}