using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// Hexagonal Board Data Model
    /// Implements the generic ITQ_HexBoard interface
    /// Contains all board state data (cells, pieces) and core data operations
    /// </summary>
    public class TQ_HexBoardModel : ITQ_HexBoard
    {
        /// <summary>
        /// All cells on the board (key: Vector2Int(Q,R) coordinates)
        /// Primary storage for board grid data
        /// </summary>
        public Dictionary<Vector2Int, TQ_HexCellModel> Cells = new Dictionary<Vector2Int, TQ_HexCellModel>();

        /// <summary>
        /// List of player-controlled pieces
        /// </summary>
        public List<TQ_ChessPieceModel> PlayerPieces = new List<TQ_ChessPieceModel>();

        /// <summary>
        /// List of enemy/AI-controlled pieces
        /// </summary>
        public List<TQ_ChessPieceModel> EnemyPieces = new List<TQ_ChessPieceModel>();

        // Interface Implementation: Direction Vectors
        /// <summary>
        /// Direction vectors for hexagonal grid movement (axial coordinate system)
        /// Implements ITQ_HexBoard interface
        /// Order: Right, Top-Right, Top-Left, Left, Bottom-Left, Bottom-Right
        /// </summary>
        int[][] ITQ_HexBoard.Directions => new int[][]
        {
            new int[] { 1, 0 },    // Right
            new int[] { 1, -1 },   // Top-Right
            new int[] { 0, -1 },   // Top-Left
            new int[] { -1, 0 },   // Left
            new int[] { 0, 1 },    // Bottom-Left
            new int[] { -1, 1 }    // Bottom-Right
        };

        // Interface Implementation: Get Cell by Coordinates
        /// <summary>
        /// Gets cell by hexagonal coordinates (Q,R)
        /// Implements ITQ_HexBoard interface (returns interface type)
        /// </summary>
        /// <param name="q">Hexagonal Q coordinate</param>
        /// <param name="r">Hexagonal R coordinate</param>
        /// <returns>Cell interface (null if not found)</returns>
        ITQ_HexCell ITQ_HexBoard.GetCellByCoordinates(int q, int r)
        {
            var key = new Vector2Int(q, r);
            return Cells.ContainsKey(key) ? Cells[key] : null;
        }

        // Interface Implementation: Get Pieces by Owner
        /// <summary>
        /// Gets all pieces for specified owner (Player/Enemy)
        /// Implements ITQ_HexBoard interface (returns interface list)
        /// </summary>
        /// <param name="owner">Piece owner (Player/Enemy)</param>
        /// <returns>List of piece interfaces for specified owner</returns>
        List<ITQ_ChessPiece> ITQ_HexBoard.GetPiecesByOwner(TQ_PieceOwner owner)
        {
            // Pattern matching switch for clean owner selection
            return owner switch
            {
                TQ_PieceOwner.Player => PlayerPieces.Cast<ITQ_ChessPiece>().ToList(),
                TQ_PieceOwner.Enemy => EnemyPieces.Cast<ITQ_ChessPiece>().ToList(),
                _ => new List<ITQ_ChessPiece>() // Empty list for unknown owner
            };
        }

        /// <summary>
        /// Resets all cell and piece states to default
        /// Implements ITQ_HexBoard interface
        /// Clears highlights, valid move targets, and piece selection states
        /// </summary>
        void ITQ_HexBoard.ResetAllCellStates()
        {
            // Reset cell visual states
            foreach (var cell in Cells.Values)
            {
                cell.IsHighlighted = false;
                cell.IsValidMoveTarget = false;
            }

            // Reset player piece states
            foreach (var piece in PlayerPieces)
            {
                piece.IsSelected = false;
                piece.ClearValidMoves();
            }

            // Reset enemy piece states
            foreach (var piece in EnemyPieces)
            {
                piece.IsSelected = false;
                piece.ClearValidMoves();
            }
        }

        /// <summary>
        /// Public wrapper for interface ResetAllCellStates method
        /// Provides type-safe access to interface implementation
        /// </summary>
        public void ResetAllCellStates()
        {
            (this as ITQ_HexBoard).ResetAllCellStates();
        }

        /// <summary>
        /// Creates coordinate key for cell dictionary lookup
        /// Standardizes key creation (prevents duplicate logic)
        /// </summary>
        /// <param name="q">Hexagonal Q coordinate</param>
        /// <param name="r">Hexagonal R coordinate</param>
        /// <returns>Vector2Int key for Cells dictionary</returns>
        public Vector2Int GetCellKey(int q, int r) => new Vector2Int(q, r);

        /// <summary>
        /// Type-safe method to get cell by coordinates (returns concrete type)
        /// More convenient for internal use than interface method
        /// </summary>
        /// <param name="q">Hexagonal Q coordinate</param>
        /// <param name="r">Hexagonal R coordinate</param>
        /// <returns>Concrete cell model (null if not found)</returns>
        public TQ_HexCellModel GetCellByCoordinates(int q, int r)
        {
            var key = GetCellKey(q, r);
            return Cells.ContainsKey(key) ? Cells[key] : null;
        }

        /// <summary>
        /// Adds a cell to the board model
        /// Updates Cells dictionary with standardized key
        /// </summary>
        /// <param name="cell">Cell model to add to board</param>
        public void AddCell(TQ_HexCellModel cell)
        {
            var key = GetCellKey(cell.Q, cell.R);
            Cells[key] = cell;
        }
    }
}