using Free.H2D;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// Hexagonal Board Manager
    /// Central coordinator for board system components (Generator, View, Model, RuleEngine)
    /// Initializes and connects all board-related systems
    /// </summary>
    public class TQ_HexBoardManager : ZFMonoBehaviour
    {
        [Header("Core Dependencies")]
        [Tooltip("Board generator component (creates Model and View)")]
        public TQ_HexBoardGenerator boardGenerator;

        [Tooltip("Board view component (visual representation)")]
        public TQ_HexBoardView boardView;

        [Tooltip("Concrete board model (data layer)")]
        public TQ_HexBoardModel boardModel; // Specific board model instance

        /// <summary>
        /// Game rule engine (handles move validation, game logic)
        /// Public read-only access with private initialization
        /// </summary>
        public TQ_RuleEngine RuleEngine { get; private set; }

        /// <summary>
        /// Initializes the board manager and all dependent systems
        /// Main entry point for board system setup
        /// </summary>
        public void InitBoardManager()
        {
            // 1. Initialize RuleEngine (game logic handler)
            RuleEngine = new TQ_RuleEngine();

            // 2. Inject View configuration (sync coordinate conversion parameters)
            // Ensures generator and view use consistent coordinate system settings
            boardGenerator.InjectBoardView(boardView);

            // 3. Instruct Generator to create populated Model (with cells/pieces)
            // Generator handles Model creation via Factory and View creation
            boardGenerator.InitBoard();

            // 4. Bind newly created Model to board manager
            // Provides centralized access to current board state
            boardModel = boardGenerator.BoardModel;

            // 5. Inject new Model into RuleEngine
            // Connects game logic to current board state
            RuleEngine.Init(boardModel);
        }
    }
}