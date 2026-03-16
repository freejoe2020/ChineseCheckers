using System.Collections.Generic;

namespace Free.Checkers
{
    /// <summary>
    /// Type classification for hexagonal cells on Chinese checkers board
    /// Determines cell behavior, visualization, and game logic rules
    /// </summary>
    public enum TQ_CellType
    {
        Normal,          // Standard playable cell (main game area)
        PlayerCamp,      // Player's home base triangle area (starting position)
        EnemyCamp,       // AI opponent's home base triangle area (starting position)
        PlayerTarget,    // Player's target area (opponent's home base - win condition)
        EnemyTarget      // Opponent's target area (player's home base - loss condition)
    }

    /// <summary>
    /// Ownership/affiliation of a checkers game piece
    /// Determines piece control (human/AI) and visual styling
    /// </summary>
    public enum TQ_PieceOwner
    {
        Player,   // Human player's controllable piece
        Enemy,    // AI opponent's controlled piece
        None      // Unowned/neutral piece (reserved for future use/neutral pieces)
    }

    /// <summary>
    /// Difficulty levels for AI opponent
    /// Determines AI decision-making complexity and move selection logic
    /// </summary>
    public enum TQ_AIDifficulty
    {
        Easy,     // Easy: Random valid moves with basic validation only
        Medium,   // Medium: Prioritize moves toward target area with simple strategy
        Hard      // Hard: Optimal pathfinding, strategic blocking, and win condition focus
    }

    /// <summary>
    /// Current game state and turn management
    /// Controls game flow and input handling permissions
    /// </summary>
    public enum TQ_GameState
    {
        PlayerTurn,         // Human player's active turn (input enabled)
        EnemyTurn,          // AI opponent's turn (input disabled, AI thinking)
        AnimationPlaying,   // Move animation in progress (input disabled)
        GameOver            // Game completed (win/loss condition met, input disabled)
    }

    /// <summary>
    /// Enumeration for the 6 triangular corner areas of the hexagonal star board
    /// Identifies home base positions for different players/teams
    /// </summary>
    public enum TQ_HexCorner
    {
        Bottom,       // Bottom corner (primary player's home base)
        Top,          // Top corner (primary AI opponent's home base)
        TopRight,     // Top-right corner (reserved for additional players/teams)
        BottomRight,  // Bottom-right corner (reserved for additional players/teams)
        TopLeft,      // Top-left corner (reserved for additional players/teams)
        BottomLeft    // Bottom-left corner (reserved for additional players/teams)
    }

    /// <summary>
    /// Generic hexagonal cell interface
    /// Defines core cell properties and operations for hexagonal grid systems
    /// Enables abstraction between data model and game logic
    /// </summary>
    public interface ITQ_HexCell
    {
        /// <summary>
        /// Hexagonal Q coordinate (horizontal axis in axial coordinate system)
        /// Read-only positional identifier
        /// </summary>
        int Q { get; }

        /// <summary>
        /// Hexagonal R coordinate (vertical axis in axial coordinate system)
        /// Read-only positional identifier
        /// </summary>
        int R { get; }

        /// <summary>
        /// Flag indicating if cell is occupied by a game piece
        /// True when CurrentPiece is not null
        /// </summary>
        bool IsOccupied { get; set; }

        /// <summary>
        /// Reference to piece currently occupying this cell (null if empty)
        /// Automatically updates IsOccupied when set/cleared
        /// </summary>
        ITQ_ChessPiece CurrentPiece { get; set; }

        /// <summary>
        /// Visual highlight state flag (for UI feedback)
        /// Used for selection, hover, or path visualization
        /// </summary>
        bool IsHighlighted { get; set; }

        /// <summary>
        /// Valid move target state flag (for move visualization)
        /// Marks cells that are valid destinations for selected piece
        /// </summary>
        bool IsValidMoveTarget { get; set; }

        // Direction cache related methods
        /// <summary>
        /// Retrieves cell at specified distance in given direction
        /// Uses precomputed directional cache for performance optimization
        /// </summary>
        /// <param name="dirIndex">Direction index (0-5 corresponding to 6 hex directions)</param>
        /// <param name="distance">Distance from current cell (1+)</param>
        /// <returns>Cell at specified direction/distance (null if out of bounds)</returns>
        ITQ_HexCell GetCellInDirection(int dirIndex, int distance);

        /// <summary>
        /// Retrieves all cells in specified direction (full line)
        /// Used for jump move validation and pathfinding
        /// </summary>
        /// <param name="dirIndex">Direction index (0-5)</param>
        /// <returns>List of cells in specified direction (empty if none)</returns>
        List<ITQ_HexCell> GetAllCellsInDirection(int dirIndex);

        /// <summary>
        /// Resets visual state properties to default values
        /// Clears highlight and valid move flags (UI cleanup)
        /// </summary>
        void ResetState();
    }

    /// <summary>
    /// Generic chess piece interface
    /// Defines core piece properties and operations for game pieces
    /// Enables abstraction between piece data and movement logic
    /// </summary>
    public interface ITQ_ChessPiece
    {
        /// <summary>
        /// Piece owner/affiliation (Player/Enemy/None)
        /// Determines control and movement rules
        /// </summary>
        TQ_PieceOwner Owner { get; }

        /// <summary>
        /// Current cell position of the piece on the board
        /// Updates cell occupancy when changed
        /// </summary>
        ITQ_HexCell CurrentCell { get; set; }

        /// <summary>
        /// Selection state flag (for UI feedback)
        /// True when piece is selected by player
        /// </summary>
        bool IsSelected { get; set; }

        /// <summary>
        /// List of valid move destinations for this piece
        /// Calculated by rule engine based on game rules
        /// </summary>
        List<ITQ_HexCell> ValidMoves { get; }

        /// <summary>
        /// Clears all valid move destinations
        /// Resets move calculation state
        /// </summary>
        void ClearValidMoves();

        /// <summary>
        /// Marks specified cells as valid move destinations
        /// Populates ValidMoves list for move visualization
        /// </summary>
        /// <param name="validCells">List of cells that are valid move targets</param>
        void MarkValidMoves(List<ITQ_HexCell> validCells);
    }

    /// <summary>
    /// Generic hexagonal board interface
    /// Defines core board properties and operations for hexagonal grid systems
    /// Enables abstraction between board data and game logic
    /// </summary>
    public interface ITQ_HexBoard
    {
        /// <summary>
        /// Direction vectors for hexagonal grid movement (axial coordinate system)
        /// Standard 6 directions for hex grid: Right, Top-Right, Top-Left, Left, Bottom-Left, Bottom-Right
        /// </summary>
        int[][] Directions { get; }

        // Data operation methods
        /// <summary>
        /// Retrieves cell by hexagonal coordinates (Q,R)
        /// Core lookup method for board cells
        /// </summary>
        /// <param name="q">Hexagonal Q coordinate</param>
        /// <param name="r">Hexagonal R coordinate</param>
        /// <returns>Cell at specified coordinates (null if out of bounds)</returns>
        ITQ_HexCell GetCellByCoordinates(int q, int r);

        /// <summary>
        /// Retrieves all pieces belonging to specified owner
        /// Used for turn management and win condition checking
        /// </summary>
        /// <param name="owner">Piece owner to filter by</param>
        /// <returns>List of pieces for specified owner (empty if none)</returns>
        List<ITQ_ChessPiece> GetPiecesByOwner(TQ_PieceOwner owner);

        /// <summary>
        /// Resets visual state for all cells on the board
        /// Clears highlights and valid move targets (UI cleanup)
        /// </summary>
        void ResetAllCellStates();
    }
}