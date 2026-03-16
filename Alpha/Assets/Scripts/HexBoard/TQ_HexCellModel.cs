using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// Hexagonal Cell Data Model
    /// Implements the generic ITQ_HexCell interface
    /// Contains cell state data and directional line caching for movement calculations
    /// </summary>
    public class TQ_HexCellModel : ITQ_HexCell
    {
        /// <summary>
        /// Hexagonal Q coordinate (horizontal axis) - read-only
        /// Set during initialization, immutable
        /// </summary>
        public readonly int Q;

        /// <summary>
        /// Hexagonal R coordinate (vertical axis) - read-only
        /// Set during initialization, immutable
        /// </summary>
        public readonly int R;

        /// <summary>
        /// Cell type classification (Normal/PlayerCamp/EnemyCamp)
        /// Determines cell behavior and visualization
        /// </summary>
        public TQ_CellType CellType;

        /// <summary>
        /// Flag indicating if cell is occupied by a piece
        /// True when CurrentPiece is not null
        /// </summary>
        public bool IsOccupied { get; set; }

        /// <summary>
        /// Reference to piece currently occupying this cell (null if empty)
        /// Updates IsOccupied when set/cleared
        /// </summary>
        public TQ_ChessPieceModel CurrentPiece { get; set; }

        /// <summary>
        /// Visual highlight state (for selection/hover feedback)
        /// Controls cell color in view layer
        /// </summary>
        public bool IsHighlighted { get; set; }

        /// <summary>
        /// Valid move target state (for move visualization)
        /// Priority over IsHighlighted in view rendering
        /// </summary>
        public bool IsValidMoveTarget { get; set; }

        // Interface Implementation (explicit interface to isolate generic logic)
        /// <summary>
        /// Explicit interface implementation for Q coordinate
        /// Isolates generic interface logic from concrete implementation
        /// </summary>
        int ITQ_HexCell.Q => Q;

        /// <summary>
        /// Explicit interface implementation for R coordinate
        /// Isolates generic interface logic from concrete implementation
        /// </summary>
        int ITQ_HexCell.R => R;

        /// <summary>
        /// Explicit interface implementation for CurrentPiece
        /// Type-safe conversion between interface and concrete type
        /// </summary>
        ITQ_ChessPiece ITQ_HexCell.CurrentPiece
        {
            get => CurrentPiece;
            set => CurrentPiece = (TQ_ChessPieceModel)value;
        }

        // Directional cache (logic layer only)
        /// <summary>
        /// Directional line cache for movement optimization
        /// Structure: [direction index][distance] = target cell
        /// Precomputed to avoid recalculating lines during gameplay
        /// </summary>
        private Dictionary<int, Dictionary<int, TQ_HexCellModel>> _directionalLineCells;

        /// <summary>
        /// Constructor: Initializes cell with hexagonal coordinates
        /// Sets up directional line cache for 6 hex directions
        /// </summary>
        /// <param name="q">Hexagonal Q coordinate</param>
        /// <param name="r">Hexagonal R coordinate</param>
        public TQ_HexCellModel(int q, int r)
        {
            Q = q;
            R = r;

            // Initialize directional cache for 6 hex directions (0-5)
            _directionalLineCells = new Dictionary<int, Dictionary<int, TQ_HexCellModel>>();
            for (int i = 0; i < 6; i++)
            {
                _directionalLineCells[i] = new Dictionary<int, TQ_HexCellModel>();
            }
        }

        // Interface Implementation: Get cell in specified direction
        /// <summary>
        /// Gets cell at specified distance in given direction
        /// Uses precomputed directional cache for performance
        /// Implements ITQ_HexCell interface
        /// </summary>
        /// <param name="dirIndex">Direction index (0-5)</param>
        /// <param name="distance">Distance from current cell (1+)</param>
        /// <returns>Cell at specified direction/distance (null if not found)</returns>
        ITQ_HexCell ITQ_HexCell.GetCellInDirection(int dirIndex, int distance)
        {
            // Validate direction index range
            if (dirIndex < 0 || dirIndex >= 6) return null;

            // Look up cell in directional cache
            if (_directionalLineCells[dirIndex].TryGetValue(distance, out var cell))
                return cell;

            return null;
        }

        // Interface Implementation: Get all cells in specified direction
        /// <summary>
        /// Gets all cells in specified direction (full line)
        /// Uses precomputed directional cache for performance
        /// Implements ITQ_HexCell interface
        /// </summary>
        /// <param name="dirIndex">Direction index (0-5)</param>
        /// <returns>List of cells in specified direction (empty if invalid direction)</returns>
        List<ITQ_HexCell> ITQ_HexCell.GetAllCellsInDirection(int dirIndex)
        {
            // Validate direction index range
            if (dirIndex < 0 || dirIndex >= 6) return new List<ITQ_HexCell>();

            // Convert concrete cells to interface type
            return _directionalLineCells[dirIndex].Values.Cast<ITQ_HexCell>().ToList();
        }

        // Interface Implementation: Reset state
        /// <summary>
        /// Resets visual state properties to default
        /// Clears highlight and valid move target flags
        /// Implements ITQ_HexCell interface
        /// </summary>
        void ITQ_HexCell.ResetState()
        {
            IsHighlighted = false;
            IsValidMoveTarget = false;
        }

        // Original concrete logic (preserved)
        /// <summary>
        /// Adds cell to directional line cache
        /// Stores cell at specified distance in given direction
        /// </summary>
        /// <param name="dirIndex">Direction index (0-5)</param>
        /// <param name="distance">Distance from current cell (1+)</param>
        /// <param name="cell">Cell to add to directional cache</param>
        public void AddDirectionalCell(int dirIndex, int distance, TQ_HexCellModel cell)
        {
            // Validate direction index range
            if (dirIndex < 0 || dirIndex >= 6) return;

            // Add cell to directional cache at specified distance
            _directionalLineCells[dirIndex][distance] = cell;
        }

        /// <summary>
        /// Clears directional cache for specific direction
        /// Resets precomputed line for given direction
        /// </summary>
        /// <param name="dirIndex">Direction index (0-5) to clear</param>
        public virtual void ClearDirectionalCells(int dirIndex)
        {
            // Validate direction index range
            if (dirIndex < 0 || dirIndex >= 6)
                return;

            // Clear all cells in specified direction
            _directionalLineCells[dirIndex].Clear();
        }

        /// <summary>
        /// Clears all directional cache data
        /// Resets precomputed lines for all 6 directions
        /// </summary>
        public virtual void ClearAllDirectionalCells()
        {
            // Clear cache for each of the 6 directions
            for (int i = 0; i < 6; i++)
            {
                ClearDirectionalCells(i);
            }
        }

        /// <summary>
        /// Gets cell coordinates as Vector2Int
        /// Convenience method for dictionary lookups and coordinate passing
        /// </summary>
        /// <returns>Cell coordinates (Q,R) as Vector2Int</returns>
        public Vector2Int GetCellCoordinate() => new Vector2Int(Q, R);
    }
}