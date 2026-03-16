using System.Collections.Generic;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// AI Game Phase Enumeration
    /// Classifies game progression for adaptive AI strategy
    /// Different phases use different move selection heuristics
    /// </summary>
    public enum TQAI_GamePhase
    {
        Opening,   // Opening phase: pieces haven't left home base
                   // AI Strategy: Prioritize moving pieces out of starting position
        Midgame,   // Midgame phase: pieces advancing toward target area
                   // AI Strategy: Balance between advancement and blocking
        Endgame    // Endgame phase: most pieces in target area
                   // AI Strategy: Focus on moving remaining pieces to target
    }

    /// <summary>
    /// AI Move Data Model
    /// Stores complete information about a potential AI move
    /// Used for move evaluation, scoring, and selection
    /// </summary>
    public class TQAI_AIMove
    {
        /// <summary>
        /// Piece to be moved (AI-controlled)
        /// </summary>
        public TQ_ChessPieceModel piece;

        /// <summary>
        /// Target cell for the move
        /// </summary>
        public TQ_HexCellModel targetCell;

        /// <summary>
        /// Strategic score for this move (higher = better)
        /// Calculated based on distance to target, game phase, etc.
        /// </summary>
        public float score;

        /// <summary>
        /// Flag indicating if this move involves jumping over other pieces
        /// Jump moves are prioritized as they cover more distance
        /// </summary>
        public bool isJumpMove;

        /// <summary>
        /// Number of jump steps in this move (for multi-jump moves)
        /// More steps = more distance covered
        /// </summary>
        public int jumpStepCount;

        /// <summary>
        /// Complete path for the move (all intermediate cells)
        /// Used for animation playback and path validation
        /// </summary>
        public List<TQ_HexCellModel> movePath;

        /// <summary>
        /// Constructor 1: Basic initialization
        /// Provides default values for optional parameters
        /// </summary>
        /// <param name="piece">Piece to move</param>
        /// <param name="targetCell">Target cell for the move</param>
        /// <param name="score">Strategic score for this move</param>
        /// <param name="isJumpMove">Whether move involves jumping</param>
        /// <param name="jumpStepCount">Number of jump steps</param>
        /// <param name="movePath">Complete move path (intermediate cells)</param>
        public TQAI_AIMove(TQ_ChessPieceModel piece, TQ_HexCellModel targetCell, float score,
                          bool isJumpMove = false, int jumpStepCount = 0, List<TQ_HexCellModel> movePath = null)
        {
            this.piece = piece;
            this.targetCell = targetCell;
            this.score = score;
            this.isJumpMove = isJumpMove;
            this.jumpStepCount = jumpStepCount;
            // Safe initialization: create empty list if path is null
            this.movePath = movePath ?? new List<TQ_HexCellModel>();
        }

        /// <summary>
        /// Constructor 2: Deep copy
        /// Creates independent copy of original move data
        /// Prevents unintended side effects from shared references
        /// </summary>
        /// <param name="original">Original move to copy</param>
        public TQAI_AIMove(TQAI_AIMove original)
        {
            // Null safety check
            if (original == null) return;

            // Copy value types and references
            this.piece = original.piece;
            this.targetCell = original.targetCell;
            this.score = original.score;
            this.isJumpMove = original.isJumpMove;
            this.jumpStepCount = original.jumpStepCount;

            // Deep copy path list (critical for independent move objects)
            this.movePath = original.movePath != null ? new List<TQ_HexCellModel>(original.movePath) : new List<TQ_HexCellModel>();
        }

        /// <summary>
        /// Resets all move properties to default values
        /// Allows object reuse without creating new instances
        /// Prevents memory allocation overhead
        /// </summary>
        public void Reset()
        {
            piece = null;
            targetCell = null;
            score = 0f;
            isJumpMove = false;
            jumpStepCount = 0;

            // Safe list reset: clear existing or create new empty list
            if (movePath != null)
            {
                movePath.Clear();
            }
            else
            {
                movePath = new List<TQ_HexCellModel>();
            }
        }
    }

    /// <summary>
    /// Standardized Move Command
    /// Common interface for AI and player move execution
    /// Contains all information needed to execute and visualize a move
    /// Value type (struct) for efficient memory usage and passing
    /// </summary>
    public struct TQ_MoveCommand
    {
        /// <summary>
        /// Piece to be moved (player or AI-controlled)
        /// </summary>
        public TQ_ChessPieceModel Piece;

        /// <summary>
        /// Complete movement path (includes all intermediate nodes)
        /// Critical for step-by-step animation playback
        /// </summary>
        public List<TQ_HexCellModel> MovePath;

        /// <summary>
        /// Whether to play step-by-step animation for this move
        /// False = instant position update (AI moves can be fast)
        /// </summary>
        public bool PlayAnimation;

        /// <summary>
        /// Duration (seconds) for each animation step
        /// Only effective when PlayAnimation = true
        /// </summary>
        public float AnimationStepDuration;

        /// <summary>
        /// Constructor: Creates standardized move command
        /// Provides sensible defaults for animation parameters
        /// </summary>
        /// <param name="piece">Piece to move</param>
        /// <param name="path">Complete move path (intermediate cells)</param>
        /// <param name="playAnimation">Whether to play animation</param>
        /// <param name="stepDuration">Duration per animation step (seconds)</param>
        public TQ_MoveCommand(TQ_ChessPieceModel piece, List<TQ_HexCellModel> path, bool playAnimation = true, float stepDuration = 0.2f)
        {
            Piece = piece;
            // Safe path initialization: create empty list if null
            MovePath = path ?? new List<TQ_HexCellModel>();
            PlayAnimation = playAnimation;
            AnimationStepDuration = stepDuration;
        }
    }

    /// <summary>
    /// Hexagonal Grid Utility Class
    /// Static helper methods for hexagonal coordinate calculations
    /// No state, pure mathematical functions for hex grid operations
    /// </summary>
    public static class HexMetrics
    {
        /// <summary>
        /// Calculates distance between two hexagonal cells (cube coordinate system)
        /// Uses axial coordinates (Q,R) converted to cube (Q,R,S) where S = -Q-R
        /// Correct hex grid distance calculation (not Euclidean distance)
        /// </summary>
        /// <param name="a">First cell coordinates (Q,R) as Vector2Int</param>
        /// <param name="b">Second cell coordinates (Q,R) as Vector2Int</param>
        /// <returns>Hex grid distance between cells (float to maintain precision)</returns>
        public static float Distance(Vector2Int a, Vector2Int b)
        {
            int q1 = a.x, r1 = a.y;
            int q2 = b.x, r2 = b.y;

            // Convert axial coordinates to cube coordinates
            int s1 = -q1 - r1;
            int s2 = -q2 - r2;

            // Hex grid distance formula (cube coordinates)
            return (Mathf.Abs(q1 - q2) + Mathf.Abs(r1 - r2) + Mathf.Abs(s1 - s2)) / 2f;
        }

        /// <summary>
        /// Gets coordinates of middle hex cell between two cells
        /// Useful for jump move calculations (finding piece being jumped over)
        /// Only valid for cells with even distance between them
        /// </summary>
        /// <param name="a">Start cell coordinates</param>
        /// <param name="b">End cell coordinates</param>
        /// <returns>Middle cell coordinates (integer division)</returns>
        public static Vector2Int GetMiddleHex(Vector2Int a, Vector2Int b)
        {
            // Integer division ensures valid hex coordinates
            return new Vector2Int((a.x + b.x) / 2, (a.y + b.y) / 2);
        }
    }

    public enum AIVersion
    {
        MinMaxV1,
        MinMaxV2,
        MCTSV3
    }

    public interface ICheckerAIManager
    {
        void Init(TQ_HexBoardManager boardManager);

        void ExecuteAITurn();

        void SetDifficulty(TQ_AIDifficulty difficulty);

        void StopAllCoroutines();
    }
}