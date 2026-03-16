using System.Collections.Generic;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// MCTS (Monte Carlo Tree Search) Node V3
    /// Enhanced version with Progressive Widening and move prioritization
    /// Core data structure for MCTS algorithm implementation
    /// </summary>
    public class TQ_MCTSNodeV3
    {
        /// <summary>
        /// The move that led to this node (null for root node)
        /// </summary>
        public TQAI_AIMove Move;

        /// <summary>
        /// Parent node in the MCTS tree (null for root node)
        /// </summary>
        public TQ_MCTSNodeV3 Parent;

        /// <summary>
        /// Child nodes (possible next moves)
        /// </summary>
        public List<TQ_MCTSNodeV3> Children = new List<TQ_MCTSNodeV3>();

        /// <summary>
        /// Cumulative value (W) - sum of evaluation scores from simulations
        /// Higher = better position for the current player
        /// </summary>
        public float TotalValue = 0;

        /// <summary>
        /// Visit count (N) - number of times this node has been visited
        /// Used for UCT calculation and progressive widening
        /// </summary>
        public int VisitCount = 0;

        /// <summary>
        /// Untried moves from this node (exploration pool)
        /// Populated during node expansion phase
        /// </summary>
        public List<TQAI_AIMove> UntriedMoves;

        /// <summary>
        /// Create new MCTS node
        /// </summary>
        /// <param name="move">Move that leads to this node (null for root)</param>
        /// <param name="parent">Parent node (null for root)</param>
        public TQ_MCTSNodeV3(TQAI_AIMove move = null, TQ_MCTSNodeV3 parent = null)
        {
            this.Move = move;
            this.Parent = parent;
        }

        /// <summary>
        /// Check if node is fully expanded (no untried moves left)
        /// Critical for MCTS tree traversal (selection phase)
        /// </summary>
        public bool IsFullyExpanded => UntriedMoves != null && UntriedMoves.Count == 0;

        /// <summary>
        /// Progressive Widening: Sort untried moves by score for prioritized exploration
        /// Implements requirement (1): Mimics V2 CalculateScore for initial sorting
        /// Higher score moves are explored first (popped from end of list)
        /// </summary>
        /// <param name="scoringFunc">Scoring function to evaluate move quality</param>
        public void SortUntriedMoves(System.Func<TQAI_AIMove, float> scoringFunc)
        {
            // Only sort if there are multiple untried moves to prioritize
            if (UntriedMoves != null && UntriedMoves.Count > 1)
            {
                // Sort by score ascending: 
                // - Lower score moves go to front of list
                // - Higher score moves go to end of list (popped first)
                // This ensures better moves are explored earlier in the search
                UntriedMoves.Sort((a, b) => scoringFunc(a).CompareTo(scoringFunc(b)));
            }
        }

        /// <summary>
        /// Get UCT (Upper Confidence Bound for Trees) score for node selection
        /// Balances exploitation (known good moves) and exploration (unknown moves)
        /// </summary>
        /// <param name="explorationConstant">Exploration parameter (typically ¡Ì2)</param>
        /// <returns>UCT score (higher = better candidate for selection)</returns>
        public float GetUCTScore(float explorationConstant = 1.414f)
        {
            // Handle unvisited nodes (infinite UCT to ensure exploration)
            if (VisitCount == 0)
                return float.MaxValue;

            // UCT formula: (W/N) + C * ¡Ì(ln(N_parent)/N)
            // W/N: Exploitation term (average value - known performance)
            // C*¡Ì(ln(N_parent)/N): Exploration term (encourages visiting new nodes)
            float exploitation = TotalValue / VisitCount;
            float exploration = explorationConstant *
                               Mathf.Sqrt(Mathf.Log(Parent.VisitCount) / VisitCount);

            return exploitation + exploration;
        }

        /// <summary>
        /// Select child node with highest UCT score (MCTS Selection phase)
        /// Used to traverse tree to find most promising unexpanded node
        /// </summary>
        /// <param name="explorationConstant">UCT exploration parameter</param>
        /// <returns>Child node with highest UCT score</returns>
        public TQ_MCTSNodeV3 SelectChild(float explorationConstant = 1.414f)
        {
            TQ_MCTSNodeV3 bestChild = null;
            float bestScore = float.MinValue;

            // Evaluate all children to find highest UCT score
            foreach (var child in Children)
            {
                float score = child.GetUCTScore(explorationConstant);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestChild = child;
                }
            }

            return bestChild;
        }

        /// <summary>
        /// Expand node by adding a new child from untried moves (MCTS Expansion phase)
        /// Uses progressive widening sorted moves (highest score first)
        /// </summary>
        /// <returns>New child node (expanded from untried move)</returns>
        public TQ_MCTSNodeV3 Expand()
        {
            // Safety check: ensure there are untried moves to expand
            if (IsFullyExpanded)
                return null;

            // Get highest priority move (last element in sorted list)
            TQAI_AIMove nextMove = UntriedMoves[UntriedMoves.Count - 1];
            UntriedMoves.RemoveAt(UntriedMoves.Count - 1);

            // Create new child node
            var newChild = new TQ_MCTSNodeV3(nextMove, this);
            Children.Add(newChild);

            return newChild;
        }

        /// <summary>
        /// Backpropagate simulation result up the tree (MCTS Backpropagation phase)
        /// Updates visit count and total value for all ancestor nodes
        /// </summary>
        /// <param name="simulationResult">Result from rollout (positive = current player win)</param>
        public void Backpropagate(float simulationResult)
        {
            // Update current node statistics
            VisitCount++;
            TotalValue += simulationResult;

            // Recursively backpropagate to parent (switch player perspective)
            Parent?.Backpropagate(-simulationResult);
        }

        /// <summary>
        /// Get best child node based on visit count (MCTS Best Move selection)
        /// Standard MCTS implementation: most visited node = best move
        /// </summary>
        /// <returns>Child node with highest visit count</returns>
        public TQ_MCTSNodeV3 GetBestChildByVisitCount()
        {
            TQ_MCTSNodeV3 bestChild = null;
            int maxVisits = -1;

            foreach (var child in Children)
            {
                if (child.VisitCount > maxVisits)
                {
                    maxVisits = child.VisitCount;
                    bestChild = child;
                }
            }

            return bestChild;
        }

        /// <summary>
        /// Get best child node based on average value (alternative selection method)
        /// Useful for endgame scenarios with high simulation quality
        /// </summary>
        /// <returns>Child node with highest average value (TotalValue/VisitCount)</returns>
        public TQ_MCTSNodeV3 GetBestChildByValue()
        {
            TQ_MCTSNodeV3 bestChild = null;
            float maxValue = float.MinValue;

            foreach (var child in Children)
            {
                if (child.VisitCount == 0)
                    continue;

                float avgValue = child.TotalValue / child.VisitCount;
                if (avgValue > maxValue)
                {
                    maxValue = avgValue;
                    bestChild = child;
                }
            }

            return bestChild;
        }
    }
}