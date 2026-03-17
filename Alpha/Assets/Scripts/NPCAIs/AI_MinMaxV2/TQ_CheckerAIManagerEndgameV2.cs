using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// Endgame Specialized AI V2
    /// Optimal path search for 1-2 remaining pieces (replaces Minimax for endgame)
    /// Uses A* algorithm optimized for hexagonal grid with jump move prioritization
    /// </summary>
    public class TQ_CheckerAIManagerEndgameV2 : TQ_CheckerAIManagerMinMaxV2
    {
        [Header("Endgame Algorithm Configuration")]
        [Tooltip("Weight for hexagonal heuristic function (higher = more focus on target)")]
        public float hexHeuristicWeight = 1.2f;

        [Tooltip("Maximum search steps (prevents infinite loops in complex scenarios)")]
        public int maxEndgameSearchSteps = 1000;

        /// <summary>
        /// A* Algorithm Core Node Structure
        /// Stores pathfinding data for hexagonal grid navigation
        /// </summary>
        private class AStarNode
        {
            /// <summary>
            /// Hex cell coordinates (Q, R)
            /// </summary>
            public Vector2Int Position;

            /// <summary>
            /// Cost from start node to current node (actual movement cost)
            /// </summary>
            public float GCost;

            /// <summary>
            /// Heuristic cost from current node to target (estimated)
            /// </summary>
            public float HCost;

            /// <summary>
            /// Total cost (GCost + HCost) - primary sorting key
            /// </summary>
            public float FCost => GCost + HCost;

            /// <summary>
            /// Parent node for path reconstruction
            /// </summary>
            public AStarNode Parent;

            /// <summary>
            /// Associated game piece (for movement validation)
            /// </summary>
            public TQ_ChessPieceModel Piece;

            /// <summary>
            /// Create new A* node with pathfinding data
            /// </summary>
            /// <param name="pos">Hex coordinates (Q, R)</param>
            /// <param name="g">GCost (actual cost from start)</param>
            /// <param name="h">HCost (heuristic cost to target)</param>
            /// <param name="parent">Parent node (path tracking)</param>
            /// <param name="piece">Associated game piece</param>
            public AStarNode(Vector2Int pos, float g, float h, AStarNode parent, TQ_ChessPieceModel piece)
            {
                Position = pos;
                GCost = g;
                HCost = h;
                Parent = parent;
                Piece = piece;
            }
        }

        #region Core Entry: Endgame Optimal Move Calculation
        /// <summary>
        /// Endgame-specialized: Calculate optimal move for 1-2 remaining pieces
        /// Replaces Minimax with A* pathfinding for superior endgame performance
        /// </summary>
        /// <param name="board">Board snapshot (isolated game state)</param>
        /// <param name="remainingPieces">Pieces still outside target area (1-2)</param>
        /// <returns>Optimal endgame move with complete path information</returns>
        public TQAI_AIMove CalculateEndgameBestMove(TQ_HexBoardModel board, List<TQ_ChessPieceModel> remainingPieces)
        {
            // Null safety & validation (early exit for invalid inputs)
            if (remainingPieces == null || remainingPieces.Count == 0 || board == null) return null;

            // 1. Get empty target cells (positions AI needs to occupy)
            var targetEmptyCells = GetTargetEmptyCells(board);
            if (targetEmptyCells.Count == 0) return null;

            // 2. Select appropriate algorithm based on remaining piece count
            TQAI_AIMove bestMove = null;
            if (remainingPieces.Count == 1)
            {
                bestMove = CalculateSinglePieceEndgameMove(board, remainingPieces[0], targetEmptyCells);
            }
            else if (remainingPieces.Count == 2)
            {
                bestMove = CalculateDualPieceEndgameMove(board, remainingPieces, targetEmptyCells);
            }

            // 3. Endgame depth bonus marker (for outer Minimax integration)
            if (bestMove != null)
            {
                // Apply scoring bonus for endgame priority moves
                bestMove.score *= 2; // Double score weight for endgame moves
            }

            return bestMove;
        }

        /// <summary>
        /// Check if no out-of-target piece can reach any empty target cell (blocked position).
        /// Used to decide when to allow target-area pieces to move (to unblock).
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <param name="outOfTargetPieces">Pieces outside target area</param>
        /// <returns>True if blocked (no piece can reach any empty target via legal path)</returns>
        public bool IsOutOfTargetBlocked(TQ_HexBoardModel board, List<TQ_ChessPieceModel> outOfTargetPieces)
        {
            if (board == null || outOfTargetPieces == null || outOfTargetPieces.Count == 0) return false;
            var targetEmptyCells = GetTargetEmptyCells(board);
            if (targetEmptyCells.Count == 0) return true; // No empty target = blocked
            foreach (var piece in outOfTargetPieces)
            {
                if (piece?.CurrentCell == null) continue;
                foreach (var targetCell in targetEmptyCells)
                {
                    if (targetCell == null) continue;
                    var path = AStarSearch(board, piece, targetCell);
                    if (path != null && path.Count >= 2) return false; // At least one path exists
                }
            }
            return true;
        }
        #endregion

        #region Single Piece Endgame: A* Optimal Pathfinding
        /// <summary>
        /// Calculate optimal move for single remaining piece (pure A* pathfinding)
        /// Focuses on shortest path to nearest empty target cell
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <param name="piece">Single remaining piece</param>
        /// <param name="targetEmptyCells">Empty target area cells</param>
        /// <returns>Optimal move for single piece</returns>
        private TQAI_AIMove CalculateSinglePieceEndgameMove(TQ_HexBoardModel board, TQ_ChessPieceModel piece, List<TQ_HexCellModel> targetEmptyCells)
        {
            // Null safety
            if (piece?.CurrentCell == null || targetEmptyCells.Count == 0) return null;

            // 1. Find nearest empty target cell (minimize path length)
            var nearestTarget = FindNearestTargetCell(piece.CurrentCell, targetEmptyCells);
            if (nearestTarget == null) return null;

            // 2. A* search for shortest path to target (jump moves prioritized)
            var path = AStarSearch(board, piece, nearestTarget);
            if (path == null || path.Count < 2) return null;

            // 3. Generate first move from path (jump moves prioritized)
            return GenerateEndgameMoveFromPath(board, piece, path);
        }

        /// <summary>
        /// A* Algorithm for Hexagonal Grid Shortest Path
        /// Optimized for checkers jump moves (lower cost for jumps)
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <param name="piece">Piece to pathfind for</param>
        /// <param name="target">Target cell in goal area</param>
        /// <returns>Shortest path (list of cells) or null if no path exists</returns>
        private List<TQ_HexCellModel> AStarSearch(TQ_HexBoardModel board, TQ_ChessPieceModel piece, TQ_HexCellModel target)
        {
            var startCell = piece.CurrentCell;

            // Null safety (early exit for invalid inputs)
            if (startCell == null || target == null || board == null) return null;

            // A* core data structures
            var openSet = new List<AStarNode>(); // Nodes to evaluate
            var closedSet = new HashSet<Vector2Int>(); // Nodes already evaluated

            // Initialize start node (zero cost from start)
            var startNode = new AStarNode(
                new Vector2Int(startCell.Q, startCell.R),
                0,
                HexMetrics.Distance(new Vector2Int(startCell.Q, startCell.R), new Vector2Int(target.Q, target.R)) * hexHeuristicWeight,
                null,
                piece
            );
            openSet.Add(startNode);

            // Main search loop with step limit (prevent infinite loops)
            int searchSteps = 0;
            while (openSet.Count > 0 && searchSteps < maxEndgameSearchSteps)
            {
                searchSteps++;

                // Get node with lowest FCost (primary sorting key)
                var currentNode = openSet.OrderBy(n => n.FCost).ThenBy(n => n.HCost).First();
                openSet.Remove(currentNode);
                closedSet.Add(currentNode.Position);

                // Termination condition: reached target cell
                if (currentNode.Position.x == target.Q && currentNode.Position.y == target.R)
                {
                    return ReconstructPath(currentNode, board);
                }

                // Explore all 6 hexagonal directions
                foreach (var dir in GetHexDirections())
                {
                    // Calculate neighbor position
                    var neighborPos = new Vector2Int(
                        currentNode.Position.x + dir.x,
                        currentNode.Position.y + dir.y
                    );

                    // Get actual cell from board (null if out of bounds)
                    var neighborCell = board.GetCellByCoordinates(neighborPos.x, neighborPos.y);

                    // Skip invalid cells:
                    // - Null (out of bounds)
                    // - Occupied (blocked)
                    // - Already evaluated (closed set)
                    if (neighborCell == null || neighborCell.IsOccupied || closedSet.Contains(neighborPos))
                        continue;

                    // Calculate movement cost (optimization: jump moves cost less)
                    // Jump moves (1) are prioritized over normal moves (2)
                    float moveCost = IsJumpMove(currentNode.Position, neighborPos, board) ? 1f : 2f;
                    float newGCost = currentNode.GCost + moveCost;

                    // Check if neighbor node already exists in open set
                    var neighborNode = openSet.FirstOrDefault(n => n.Position == neighborPos);

                    if (neighborNode == null)
                    {
                        // Create new node if not exists
                        neighborNode = new AStarNode(
                            neighborPos,
                            newGCost,
                            HexMetrics.Distance(neighborPos, new Vector2Int(target.Q, target.R)) * hexHeuristicWeight,
                            currentNode,
                            piece
                        );
                        openSet.Add(neighborNode);
                    }
                    else if (newGCost < neighborNode.GCost)
                    {
                        // Update existing node with better path (lower cost)
                        neighborNode.GCost = newGCost;
                        neighborNode.Parent = currentNode;
                    }
                }
            }

            // Return null if search exhausted or exceeded step limit
            return null;
        }
        #endregion

        #region Dual Piece Endgame: Cooperative A* Pathfinding
        /// <summary>
        /// Calculate optimal move for two remaining pieces (cooperative pathfinding)
        /// Prioritizes piece with longer path to minimize total turns
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <param name="pieces">Two remaining pieces</param>
        /// <param name="targetEmptyCells">Empty target area cells</param>
        /// <returns>Optimal move for one of the two pieces</returns>
        private TQAI_AIMove CalculateDualPieceEndgameMove(TQ_HexBoardModel board, List<TQ_ChessPieceModel> pieces, List<TQ_HexCellModel> targetEmptyCells)
        {
            // Validation (early exit for invalid inputs)
            if (pieces.Count != 2 || targetEmptyCells.Count < 2)
            {
                // Fallback to single piece logic if insufficient targets
                return CalculateSinglePieceEndgameMove(board, pieces[0], targetEmptyCells);
            }

            // 1. Assign pieces to nearest unique targets (avoid path conflict)
            var piece1 = pieces[0];
            var piece2 = pieces[1];

            var target1 = FindNearestTargetCell(piece1.CurrentCell, targetEmptyCells);
            var remainingTargets = targetEmptyCells.Where(t => t != target1).ToList();
            var target2 = FindNearestTargetCell(piece2.CurrentCell, remainingTargets);

            // Fallback if second target not found
            if (target2 == null) target2 = target1;

            // 2. Calculate paths for both pieces
            var path1 = AStarSearch(board, piece1, target1);
            var path2 = AStarSearch(board, piece2, target2);

            // Calculate path lengths (use max value for null paths)
            int steps1 = path1?.Count ?? int.MaxValue;
            int steps2 = path2?.Count ?? int.MaxValue;

            // 3. Strategic move selection:
            // - Prioritize piece with longer path (minimizes total game turns)
            // - Fallback to valid path if one path is invalid
            if (steps1 >= steps2 && path1 != null)
            {
                return GenerateEndgameMoveFromPath(board, piece1, path1);
            }
            else if (path2 != null)
            {
                return GenerateEndgameMoveFromPath(board, piece2, path2);
            }

            // Final fallback: use first piece if both paths failed
            return CalculateSinglePieceEndgameMove(board, piece1, targetEmptyCells);
        }
        #endregion

        #region Helper Methods (Hex Grid & Pathfinding)
        // Hex distance: reuse HexMetrics.Distance (axial/cube, same as rest of AI V2)

        /// <summary>
        /// Reconstruct path from A* end node to start node
        /// Traces parent nodes backward then reverses for forward path
        /// </summary>
        /// <param name="endNode">Final node at target</param>
        /// <param name="board">Board snapshot for cell lookup</param>
        /// <returns>Complete path from start to target (list of cells)</returns>
        private List<TQ_HexCellModel> ReconstructPath(AStarNode endNode, TQ_HexBoardModel board)
        {
            var path = new List<TQ_HexCellModel>();
            var currentNode = endNode;

            // Trace path backward from end to start
            while (currentNode != null)
            {
                var cell = board.GetCellByCoordinates(currentNode.Position.x, currentNode.Position.y);
                if (cell != null) path.Add(cell);
                currentNode = currentNode.Parent;
            }

            // Reverse to get forward path (start �� target)
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Generate endgame move from complete path (jump move prioritization)
        /// Extracts first valid move (jump preferred) from path
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <param name="piece">Moving piece</param>
        /// <param name="path">Complete path from A* search</param>
        /// <returns>AI move object with optimal first step</returns>
        private TQAI_AIMove GenerateEndgameMoveFromPath(TQ_HexBoardModel board, TQ_ChessPieceModel piece, List<TQ_HexCellModel> path)
        {
            // Null safety
            if (piece == null || path.Count < 2) return null;

            // 1. Find first jump move in path (prioritize efficient movement)
            TQ_HexCellModel targetCell = null;
            for (int i = 1; i < path.Count; i++)
            {
                if (IsJumpMove(piece.CurrentCell, path[i], board))
                {
                    targetCell = path[i];
                    break; // Take first jump move found
                }
            }

            // 2. Fallback to first step if no jump move available
            if (targetCell == null) targetCell = path[1];

            // 3. Create optimized endgame move (from object pool)
            var move = AIMovePool.Get();
            move.piece = piece;
            move.targetCell = targetCell;
            move.score = CalculateTargetProgressScore(piece, targetCell, board) * 2; // Endgame priority
            move.isJumpMove = IsJumpMove(piece.CurrentCell, targetCell, board);
            move.movePath = path; // Store complete path for animation/validation

            // Calculate jump steps (for scoring)
            move.jumpStepCount = move.isJumpMove ? 1 : 0;

            return move;
        }

        /// <summary>
        /// Check if move between two cells is a jump move (checkers rule)
        /// Valid jump requires occupied middle cell
        /// </summary>
        /// <param name="from">Starting cell</param>
        /// <param name="to">Target cell</param>
        /// <param name="board">Board snapshot for validation</param>
        /// <returns>True if move is a valid jump</returns>
        private bool IsJumpMove(TQ_HexCellModel from, TQ_HexCellModel to, TQ_HexBoardModel board)
        {
            if (from == null || to == null) return false;
            return IsJumpMove(new Vector2Int(from.Q, from.R), new Vector2Int(to.Q, to.R), board);
        }

        /// <summary>
        /// Overload: Check jump move using coordinates (performance optimized)
        /// </summary>
        /// <param name="from">Start coordinates (Q, R)</param>
        /// <param name="to">Target coordinates (Q, R)</param>
        /// <param name="board">Board snapshot for middle cell lookup</param>
        /// <returns>True if valid jump move</returns>
        private bool IsJumpMove(Vector2Int from, Vector2Int to, TQ_HexBoardModel board)
        {
            // Jump move requires exactly 2 steps in one direction
            int deltaQ = to.x - from.x;
            int deltaR = to.y - from.y;

            // Filter non-jump moves (not 2 steps)
            if (Mathf.Abs(deltaQ) != 2 && Mathf.Abs(deltaR) != 2) return false;

            // Calculate middle cell coordinates
            int midQ = (from.x + to.x) / 2;
            int midR = (from.y + to.y) / 2;

            // Jump valid only if middle cell exists and is occupied
            var midCell = board.GetCellByCoordinates(midQ, midR);
            return midCell != null && midCell.IsOccupied;
        }

        /// <summary>
        /// Get empty cells in target area (AI's goal positions)
        /// Filters target positions to only include unoccupied cells
        /// </summary>
        /// <param name="board">Board snapshot</param>
        /// <returns>List of empty target area cells</returns>
        private List<TQ_HexCellModel> GetTargetEmptyCells(TQ_HexBoardModel board)
        {
            // Null safety for cached target positions
            if (CachedEnemyTargetPositions == null || board == null) return new List<TQ_HexCellModel>();

            return CachedEnemyTargetPositions
                .Select(pos => board.GetCellByCoordinates(pos.x, pos.y))
                .Where(cell => cell != null && !cell.IsOccupied) // Only empty cells
                .ToList();
        }

        /// <summary>
        /// Find nearest target cell using hexagonal distance
        /// Critical for minimizing path length in endgame
        /// </summary>
        /// <param name="from">Starting cell</param>
        /// <param name="targets">List of potential target cells</param>
        /// <returns>Nearest target cell (minimum heuristic distance)</returns>
        private TQ_HexCellModel FindNearestTargetCell(TQ_HexCellModel from, List<TQ_HexCellModel> targets)
        {
            // Null safety
            if (from == null || targets == null || targets.Count == 0) return null;

            TQ_HexCellModel nearest = null;
            float minDist = float.MaxValue;

            // Find target with minimum hexagonal distance
            foreach (var target in targets)
            {
                float dist = HexMetrics.Distance(new Vector2Int(from.Q, from.R), new Vector2Int(target.Q, target.R));
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = target;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Get all 6 hexagonal grid directions (Q, R)
        /// Standard axial hex coordinate directions
        /// </summary>
        /// <returns>List of 6 direction vectors for hex grid</returns>
        private List<Vector2Int> GetHexDirections()
        {
            return new List<Vector2Int>
            {
                new Vector2Int(1, 0),   // Right
                new Vector2Int(1, -1),  // Right-Up
                new Vector2Int(0, -1),  // Left-Up
                new Vector2Int(-1, 0),  // Left
                new Vector2Int(-1, 1),  // Left-Down
                new Vector2Int(0, 1)    // Right-Down
            };
        }
        #endregion
    }
}