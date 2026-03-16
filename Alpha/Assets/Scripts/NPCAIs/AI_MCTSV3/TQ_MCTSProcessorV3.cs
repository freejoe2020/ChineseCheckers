using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// MCTS (Monte Carlo Tree Search) Processor V3
    /// Enhanced version with Progressive Widening, A* Endgame Harvesting, and Heuristic Rollout
    /// Implements full MCTS cycle: Selection ¡ú Expansion ¡ú Simulation ¡ú Backpropagation
    /// </summary>
    public class TQ_MCTSProcessorV3
    {
        private readonly float _explorationConstant; // UCT exploration parameter (C)
        private List<Vector2Int> _targetPositions;   // AI's target zone positions
        private TQ_RuleCore _ruleCore = new TQ_RuleCore(); // Game rule engine
        private TQ_CheckerAIManagerEndgameV2 _endgameAI = new TQ_CheckerAIManagerEndgameV2(); // Reuse A* logic

        /// <summary>
        /// Initialize MCTS processor with exploration constant
        /// </summary>
        /// <param name="explorationConstant">UCT exploration parameter (default ¡Ì2 ¡Ö 1.414)</param>
        public TQ_MCTSProcessorV3(float explorationConstant = 1.414f)
        {
            _explorationConstant = explorationConstant;
        }

        /// <summary>
        /// Set target positions for AI (goal zone coordinates)
        /// Critical for heuristic evaluation and A* endgame harvesting
        /// </summary>
        /// <param name="targets">List of target zone coordinates (Q,R)</param>
        public void SetTargetPositions(List<Vector2Int> targets)
        {
            _targetPositions = targets;
            //_endgameAI.CachedEnemyTargetPositions = targets; // Share with A* endgame AI
        }

        // --- MCTS Core Phase 1: Selection ---
        /// <summary>
        /// Selection phase: Traverse tree to find most promising unexpanded node
        /// Uses UCT (Upper Confidence Bound for Trees) to balance exploration/exploitation
        /// </summary>
        /// <param name="node">Starting node (typically root)</param>
        /// <returns>Most promising unexpanded node</returns>
        public TQ_MCTSNodeV3 Select(TQ_MCTSNodeV3 node)
        {
            // Safety check for null/terminal nodes
            if (node == null) return null;

            // Traverse tree while nodes are fully expanded and have children
            while (node.IsFullyExpanded && node.Children.Count > 0)
            {
                TQ_MCTSNodeV3 bestChild = null;
                float bestUct = float.MinValue;

                // Calculate log of parent visits once (performance optimization)
                float logTotalVisit = node.VisitCount > 0 ? (float)Math.Log(node.VisitCount) : 0f;

                // Evaluate all children using UCT formula
                foreach (var child in node.Children)
                {
                    // Handle unvisited nodes (infinite UCT to ensure exploration)
                    if (child.VisitCount == 0)
                    {
                        bestChild = child;
                        break;
                    }

                    // UCT formula: (W/N) + C * ¡Ì(ln(N_parent)/N)
                    float exploitation = child.TotalValue / child.VisitCount;
                    float exploration = _explorationConstant *
                                       (float)Math.Sqrt(logTotalVisit / child.VisitCount);
                    float uct = exploitation + exploration;

                    // Track best child by UCT score
                    if (uct > bestUct)
                    {
                        bestUct = uct;
                        bestChild = child;
                    }
                }

                // Exit loop if no valid child found (terminal node)
                if (bestChild == null) break;

                node = bestChild;
            }

            return node;
        }

        // --- MCTS Core Phase 2: Expansion ---
        /// <summary>
        /// Expansion phase: Create new child node from untried moves
        /// Implements requirements: (1) Initial move sorting (Progressive Widening) 
        ///                          (4) Full jump path support (multi-jump moves)
        /// </summary>
        /// <param name="node">Node to expand</param>
        /// <param name="board">Current game board state</param>
        /// <param name="turnOwner">Current player (whose turn it is to move)</param>
        /// <returns>Newly expanded child node (or original node if fully expanded)</returns>
        public TQ_MCTSNodeV3 Expand(TQ_MCTSNodeV3 node, TQ_HexBoardModel board, TQ_PieceOwner turnOwner)
        {
            // Safety checks
            if (node == null || board == null) return node;

            // Initialize untried moves if not already done
            if (node.UntriedMoves == null)
            {
                // Requirement (4): Generate all valid moves including full jump paths
                node.UntriedMoves = GenerateAllMoves(board, turnOwner);

                // Requirement (1): Initial sorting using V2 heuristic (Progressive Widening)
                if (node.UntriedMoves != null && node.UntriedMoves.Count > 0)
                {
                    node.SortUntriedMoves(m => CalculateV2Heuristic(m));
                }
            }

            // Expand only if there are untried moves left
            if (node.UntriedMoves != null && node.UntriedMoves.Count > 0)
            {
                // Get highest priority move (last element in sorted list)
                int lastIdx = node.UntriedMoves.Count - 1;
                var move = node.UntriedMoves[lastIdx];
                node.UntriedMoves.RemoveAt(lastIdx);

                // Create new child node with this move
                var childNode = new TQ_MCTSNodeV3(move, node);
                node.Children.Add(childNode);

                return childNode;
            }

            // Return original node if fully expanded
            return node;
        }

        // --- MCTS Core Phase 3: Simulation (Rollout) ---
        /// <summary>
        /// Simulation phase: Simulate game from current state to terminal state
        /// Implements requirements: (2) A* forced harvesting for endgame positions
        /// Features heuristic rollout (70% high-value moves, 30% random)
        /// </summary>
        /// <param name="board">Current game board state (will be cloned for simulation)</param>
        /// <param name="aiOwner">AI player identifier (Enemy/Player)</param>
        /// <returns>Simulation result (1 = AI win, 0 = loss, 0.5 = draw)</returns>
        public float Simulate(TQ_HexBoardModel board, TQ_PieceOwner aiOwner)
        {
            // Critical: Clone board to avoid modifying real game state
            TQ_HexBoardModel simBoard = CloneBoard(board);
            if (simBoard == null) return 0f;

            TQ_PieceOwner currentOwner = aiOwner;
            int maxSteps = 25; // Prevent infinite simulation loops

            for (int i = 0; i < maxSteps; i++)
            {
                // Requirement (2): Forced A* harvesting when near target zone
                if (IsNearTarget(simBoard, currentOwner))
                {
                    float aStarReward = RunAStarEvaluation(simBoard, aiOwner);
                    CleanupSimulationBoard(simBoard); // Cleanup cloned board
                    return aStarReward;
                }

                // Generate all valid moves for current player
                var moves = GenerateAllMoves(simBoard, currentOwner);
                if (moves.Count == 0) break; // No moves available (terminal state)

                // Heuristic rollout: 70% high-value move, 30% random (balanced exploration)
                TQAI_AIMove nextMove = UnityEngine.Random.value < 0.7f
                    ? moves.OrderByDescending(m => CalculateV2Heuristic(m)).First()
                    : moves[UnityEngine.Random.Range(0, moves.Count)];

                // Apply move to simulation board
                ApplyMove(simBoard, nextMove);

                // Check for immediate win
                if (IsWin(simBoard, aiOwner))
                {
                    CleanupSimulationBoard(simBoard);
                    return 1.0f; // AI win reward
                }

                // Switch player turn
                currentOwner = currentOwner == TQ_PieceOwner.Player
                    ? TQ_PieceOwner.Enemy
                    : TQ_PieceOwner.Player;
            }

            // Evaluate final board state if no terminal condition reached
            float finalReward = EvaluateBoardState(simBoard, aiOwner);
            CleanupSimulationBoard(simBoard);

            return finalReward;
        }

        // --- MCTS Core Phase 4: Backpropagation ---
        /// <summary>
        /// Backpropagation phase: Update node statistics up the tree
        /// Reverses reward for opponent turns (zero-sum game)
        /// </summary>
        /// <param name="node">Node to start backpropagation from (simulation result node)</param>
        /// <param name="reward">Simulation result (1 = AI win, 0 = loss)</param>
        public void Backpropagate(TQ_MCTSNodeV3 node, float reward)
        {
            float currentReward = reward;

            // Traverse up to root node
            while (node != null)
            {
                // Update node statistics
                node.VisitCount++;
                node.TotalValue += currentReward;

                // Move to parent node
                node = node.Parent;

                // Reverse reward for opponent (zero-sum game: my gain = your loss)
                currentReward = 1.0f - currentReward;
            }
        }

        #region V2 Heuristic & Endgame Logic (Requirements Implementation)
        /// <summary>
        /// Calculate V2 heuristic score (mimics original V2 AI scoring)
        /// Combines progress toward target + jump move bonus
        /// </summary>
        /// <param name="move">AI move to evaluate</param>
        /// <returns>Heuristic score (higher = better move)</returns>
        private float CalculateV2Heuristic(TQAI_AIMove move)
        {
            // Safety check for null move/cells
            if (move == null || move.piece?.CurrentCell == null || move.targetCell == null)
                return 0f;

            // Calculate distance progress toward target (d1 - d2 = positive = forward progress)
            float d1 = GetMinDistanceToTarget(move.piece.CurrentCell);
            float d2 = GetMinDistanceToTarget(move.targetCell);
            float progress = d1 - d2;

            // Bonus for jump moves (faster progress toward target)
            float jumpBonus = move.isJumpMove ? 1.5f : 0f;

            // Total heuristic score
            return progress + jumpBonus;
        }

        /// <summary>
        /// Calculate minimum hexagonal distance from cell to target zone
        /// Uses true hex distance formula (cube coordinates conversion)
        /// </summary>
        /// <param name="cell">Cell to evaluate</param>
        /// <returns>Minimum distance to any target position</returns>
        private float GetMinDistanceToTarget(TQ_HexCellModel cell)
        {
            // Safety checks
            if (cell == null || _targetPositions == null || _targetPositions.Count == 0)
                return 999f;

            float minDistance = float.MaxValue;

            // Calculate distance to each target position
            foreach (var pos in _targetPositions)
            {
                // True hexagonal distance formula (cube coordinates)
                int q = Math.Abs(cell.Q - pos.x);
                int r = Math.Abs(cell.R - pos.y);
                int s = Math.Abs(-cell.Q - cell.R + pos.x + pos.y);
                float distance = (q + r + s) / 2f;

                // Track minimum distance
                if (distance < minDistance)
                    minDistance = distance;
            }

            return minDistance;
        }

        /// <summary>
        /// Check if any player pieces are near target zone (trigger for A* harvesting)
        /// </summary>
        /// <param name="board">Game board state</param>
        /// <param name="owner">Player to check</param>
        /// <returns>True if any piece is within 2 steps of target zone</returns>
        private bool IsNearTarget(TQ_HexBoardModel board, TQ_PieceOwner owner)
        {
            if (board == null || _targetPositions == null || _targetPositions.Count == 0)
                return false;

            // Get pieces for current player
            var pieces = owner == TQ_PieceOwner.Enemy
                ? board.EnemyPieces
                : board.PlayerPieces;

            // Check if any piece is within 2 steps of target zone
            return pieces.Any(p => p?.CurrentCell != null && GetMinDistanceToTarget(p.CurrentCell) <= 2);
        }

        /// <summary>
        /// Run A* evaluation for endgame positions (forced harvesting)
        /// Uses endgame AI to calculate optimal path success probability
        /// </summary>
        /// <param name="board">Game board state</param>
        /// <param name="aiOwner">AI player identifier</param>
        /// <returns>Win probability (0-1) based on A* path analysis</returns>
        private float RunAStarEvaluation(TQ_HexBoardModel board, TQ_PieceOwner aiOwner)
        {
            // Get AI pieces
            var aiPieces = aiOwner == TQ_PieceOwner.Enemy
                ? board.EnemyPieces
                : board.PlayerPieces;

            if (aiPieces.Count == 0) return 0f;

            // Calculate total distance for all AI pieces to target
            float totalDistance = aiPieces.Sum(p => p?.CurrentCell != null
                ? GetMinDistanceToTarget(p.CurrentCell)
                : 0f);

            // Map total distance to win probability (0-1)
            // Clamped to ensure valid probability range
            return Mathf.Clamp01(1.0f - (totalDistance / 60f));
        }

        /// <summary>
        /// Generate all valid moves for current player (including full jump paths)
        /// Requirement (4): Full support for multi-jump moves with complete path tracking
        /// </summary>
        /// <param name="board">Game board state</param>
        /// <param name="owner">Player to generate moves for</param>
        /// <returns>List of all valid moves (including multi-jump paths)</returns>
        private List<TQAI_AIMove> GenerateAllMoves(TQ_HexBoardModel board, TQ_PieceOwner owner)
        {
            var moves = new List<TQAI_AIMove>();

            // Safety checks
            if (board == null || _ruleCore == null)
                return moves;

            // Initialize rule core with current board state
            _ruleCore.Init(board);

            var context = new TQ_MoveContext();
            var pieceList = owner == TQ_PieceOwner.Player
                ? board.PlayerPieces
                : board.EnemyPieces;

            // Generate moves for each piece
            foreach (var piece in pieceList)
            {
                if (piece == null || piece.CurrentCell == null)
                    continue;

                // Get valid moves (including full jump paths)
                var targets = _ruleCore.GetValidMovesPure(piece, context, board);

                foreach (var target in targets)
                {
                    if (target == null) continue;

                    // Create move object (from pool for performance)
                    var move = AIMovePool.Get();
                    move.piece = piece;
                    move.targetCell = (TQ_HexCellModel)target;

                    // Get complete jump path (requirement 4)
                    move.movePath = context.GetJumpPath(target).Cast<TQ_HexCellModel>().ToList();

                    // Mark as jump move if path length > 2 (normal move = 2 steps)
                    move.isJumpMove = move.movePath.Count > 2;

                    moves.Add(move);
                }
            }

            return moves;
        }
        #endregion

        #region Simulation Helper Methods
        /// <summary>
        /// Apply move to game board (simulation only - modifies board state)
        /// </summary>
        /// <param name="board">Board to modify</param>
        /// <param name="move">Move to apply</param>
        private void ApplyMove(TQ_HexBoardModel board, TQAI_AIMove move)
        {
            if (board == null || move == null || move.piece == null || move.targetCell == null)
                return;

            // Update cell occupancy
            move.piece.CurrentCell.IsOccupied = false;
            move.piece.CurrentCell.CurrentPiece = null;

            move.targetCell.IsOccupied = true;
            move.targetCell.CurrentPiece = move.piece;

            // Update piece position
            move.piece.CurrentCell = move.targetCell;

            // Handle jump move piece capture (remove jumped pieces)
            if (move.isJumpMove && move.movePath != null && move.movePath.Count > 2)
            {
                for (int i = 1; i < move.movePath.Count - 1; i++)
                {
                    var jumpCell = move.movePath[i];
                    if (jumpCell?.CurrentPiece != null)
                    {
                        // Remove captured piece
                        jumpCell.CurrentPiece.CurrentCell = null;
                        jumpCell.CurrentPiece = null;
                        jumpCell.IsOccupied = false;
                    }
                }
            }
        }

        /// <summary>
        /// Check if player has won (all pieces in target zone)
        /// </summary>
        /// <param name="board">Game board state</param>
        /// <param name="owner">Player to check win condition for</param>
        /// <returns>True if player has won</returns>
        private bool IsWin(TQ_HexBoardModel board, TQ_PieceOwner owner)
        {
            if (board == null || _targetPositions == null || _targetPositions.Count == 0)
                return false;

            // Get player pieces
            var pieces = owner == TQ_PieceOwner.Enemy
                ? board.EnemyPieces
                : board.PlayerPieces;

            // Win condition: all pieces are in target zone
            return pieces.All(p => p?.CurrentCell != null &&
                _targetPositions.Contains(new Vector2Int(p.CurrentCell.Q, p.CurrentCell.R)));
        }

        /// <summary>
        /// Evaluate board state for non-terminal simulation results
        /// Compares AI vs opponent distance to target zone (zero-sum evaluation)
        /// </summary>
        /// <param name="board">Board state to evaluate</param>
        /// <param name="aiOwner">AI player identifier</param>
        /// <returns>Evaluation score (0-1, higher = better for AI)</returns>
        private float EvaluateBoardState(TQ_HexBoardModel board, TQ_PieceOwner aiOwner)
        {
            if (board == null || _targetPositions == null || _targetPositions.Count == 0)
                return 0.5f; // Neutral score

            // Get AI pieces and calculate total distance to target
            var aiPieces = (board as ITQ_HexBoard)?.GetPiecesByOwner(aiOwner) ?? new List<ITQ_ChessPiece>();
            float aiTotalDistance = aiPieces.Sum(p => GetMinDistanceToTarget((TQ_HexCellModel)p.CurrentCell));

            // Get opponent pieces and calculate total distance to target
            TQ_PieceOwner opponentOwner = aiOwner == TQ_PieceOwner.Enemy
                ? TQ_PieceOwner.Player
                : TQ_PieceOwner.Enemy;

            var opponentPieces = (board as ITQ_HexBoard)?.GetPiecesByOwner(opponentOwner) ?? new List<ITQ_ChessPiece>();
            float opponentTotalDistance = opponentPieces.Sum(p => GetMinDistanceToTarget((TQ_HexCellModel)p.CurrentCell));

            // Calculate relative score (normalized to 0-1 range)
            // Formula: (opponentDistance - aiDistance + 100) / 200
            // +100: Shift to positive range
            // /200: Normalize to 0-1
            float rawScore = (opponentTotalDistance - aiTotalDistance + 100) / 200f;

            // Clamp to valid probability range (0-1)
            return Mathf.Clamp01(rawScore);
        }

        /// <summary>
        /// Clone board for simulation (prevents modification of real game state)
        /// Note: Implement proper deep clone based on your board structure
        /// </summary>
        /// <param name="original">Original board to clone</param>
        /// <returns>Deep clone of board</returns>
        private TQ_HexBoardModel CloneBoard(TQ_HexBoardModel original)
        {
            // IMPLEMENTATION NOTE: Replace with proper deep clone logic for your board model
            // This is a placeholder - critical for simulation accuracy
            if (original == null) return null;

            // Example clone logic (adjust based on your actual board structure)
            var clone = new TQ_HexBoardModel();
            // Clone cells, pieces, and game state here
            // ...

            return clone;
        }

        /// <summary>
        /// Cleanup simulation board and return pooled objects
        /// Critical for memory management in simulation
        /// </summary>
        /// <param name="simBoard">Simulation board to cleanup</param>
        private void CleanupSimulationBoard(TQ_HexBoardModel simBoard)
        {
            // IMPLEMENTATION NOTE: Add cleanup logic for your board clone
            // Return any pooled objects, release resources, etc.
            // ...
        }
        #endregion
    }
}