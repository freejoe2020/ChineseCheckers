using System.Collections.Generic;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// Hexagonal Board Generator
    /// Responsibility: Only creates View components (Model provided by Factory)
    /// Separates View generation from Model creation (MV architecture)
    /// </summary>
    public class TQ_HexBoardGenerator : TQ_HexBoardBase
    {
        [Header("Generation Configuration")]
        [Tooltip("Side length of central hexagonal area (radius)")]
        public int hexagonSide = 4; // Central hexagon side length

        [Tooltip("Number of layers for triangular camp areas")]
        public int triangleLayers = 4; // Triangle camp layer count

        // Cached Factory instance (prevents repeated instantiation)
        /// <summary>
        /// Cached board factory instance (optimization: avoid repeated creation)
        /// </summary>
        private HexBoardFactory _boardFactory;

        #region Generation Entry Point
        /// <summary>
        /// Initializes complete board (Model + View)
        /// Main entry point for board generation
        /// </summary>
        public void InitBoard()
        {
            // 1. Clear old data and views (clean slate)
            if (boardModel != null) ClearBoardModel();
            ClearAllViews();

            // 2. Initialize Factory and generate complete Model (core logic reuse)
            _boardFactory = new HexBoardFactory(hexagonSide, triangleLayers);
            boardModel = _boardFactory.CreateBoardModel();

            // 3. Batch create Views based on Model (MV pattern implementation)
            BuildAllViews();

            DebugLog($"Board generation complete: Cells={boardModel.Cells.Count}, Player pieces={boardModel.PlayerPieces.Count}, Enemy pieces={boardModel.EnemyPieces.Count}");
        }
        #endregion

        #region Preserved Public Method: GetCampTriangle
        /// <summary>
        /// Gets coordinate list for specified camp (public exposed method)
        /// Reuses Factory logic to avoid code duplication
        /// </summary>
        /// <param name="campKey">Camp identifier (Top/Bottom/TopRight/BottomRight/TopLeft/BottomLeft)</param>
        /// <returns>List of coordinates for the specified camp (empty list if not found)</returns>
        public List<Vector2Int> GetCampTriangle(string campKey)
        {
            // Lazy load Factory (prevent coordinate access before InitBoard is called)
            if (_boardFactory == null)
            {
                _boardFactory = new HexBoardFactory(hexagonSide, triangleLayers);
            }

            // Reuse Factory's coordinate logic (DRY principle)
            return _boardFactory.GetCampCoordinates().TryGetValue(campKey, out var tri)
                ? tri
                : new List<Vector2Int>();
        }
        #endregion

        #region View Construction
        /// <summary>
        /// Builds all View components based on existing Model
        /// Implements MV pattern: View reflects Model state
        /// </summary>
        private void BuildAllViews()
        {
            // 1. Create all cell Views
            foreach (var cellKvp in boardModel.Cells)
            {
                CreateCellView(cellKvp.Value);
            }

            // 2. Create all piece Views
            foreach (var piece in boardModel.PlayerPieces)
            {
                CreatePieceView(piece);
            }
            foreach (var piece in boardModel.EnemyPieces)
            {
                CreatePieceView(piece);
            }
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Clears all board UI components
        /// Destroys cell and piece views (View-only cleanup)
        /// </summary>
        public void ClearBoardUI()
        {
            ClearAllViews();
        }
        #endregion
    }
}