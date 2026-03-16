using UnityEngine;
using Free.H2D;

namespace Free.Checkers
{
    /// <summary>
    /// Hexagonal Board Base Class
    /// Only encapsulates common data and utility methods (no business logic)
    /// Provides foundational functionality for hexagonal grid systems
    /// </summary>
    public class TQ_HexBoardBase : ZFMonoBehaviour
    {
        // Core configurable parameters (exposed in Inspector)
        [Header("Base Configuration")]
        [Tooltip("Main UI canvas for board rendering")]
        public Canvas gameCanvas;

        [Tooltip("Center position of the board in UI coordinates")]
        public Vector2 boardCenter = Vector2.zero;

        [Tooltip("Size (diameter) of each hex cell in pixels")]
        public float cellSize = 50f;

        [Header("Cell Spacing Configuration")]
        [Tooltip("Horizontal spacing ratio (default 0.8, 1=no compression, <1=reduce spacing, >1=increase spacing)")]
        [Range(0.1f, 2f)] // Restrict range to prevent board misalignment from extreme values
        public float horizontalSpacingRatio = 0.8f;

        [Tooltip("Vertical spacing ratio (default 0.8, 1=no compression, <1=reduce spacing, >1=increase spacing)")]
        [Range(0.1f, 2f)]
        public float verticalSpacingRatio = 0.8f;

        // Common data storage (Model layer)
        /// <summary>
        /// Underlying data model for the hexagonal board
        /// Contains cell/piece data without UI representation
        /// </summary>
        protected TQ_HexBoardModel boardModel;

        /// <summary>
        /// Public read-only access to board model
        /// </summary>
        public TQ_HexBoardModel BoardModel => boardModel;

        // Common UI mapping (View layer)
        /// <summary>
        /// View component for rendering the hexagonal board
        /// Handles visual representation of model data
        /// </summary>
        protected TQ_HexBoardView _boardView;

        /// <summary>
        /// Public read-only access to board view
        /// </summary>
        public TQ_HexBoardView BoardView => _boardView;

        // Hexagonal direction vectors (axial coordinates) - universal constants
        /// <summary>
        /// Direction vectors for hexagonal grid movement (axial coordinate system)
        /// Order: Right, Top-Right, Top-Left, Left, Bottom-Left, Bottom-Right
        /// </summary>
        public int[][] Directions = new int[][]
        {
            new int[] { 1, 0 },    // Right
            new int[] { 1, -1 },   // Top-Right
            new int[] { 0, -1 },   // Top-Left
            new int[] { -1, 0 },   // Left
            new int[] { 0, 1 },    // Bottom-Left
            new int[] { -1, 1 }    // Bottom-Right
        };

        /// <summary>
        /// Externally injects BoardView reference (decouples prefab dependencies)
        /// Synchronizes base configuration to the view component
        /// </summary>
        /// <param name="boardView">Board view component to inject</param>
        public virtual void InjectBoardView(TQ_HexBoardView boardView)
        {
            _boardView = boardView;

            // Sync base configuration to view
            if (_boardView != null)
            {
                _boardView.GameCanvas = gameCanvas;
                _boardView.BoardCenter = boardCenter;
                _boardView.CellSize = cellSize;

                // Sync spacing ratios for hex-specific view
                if (_boardView is TQ_HexBoardView hexBoardView)
                {
                    hexBoardView.HorizontalSpacingRatio = horizontalSpacingRatio;
                    hexBoardView.VerticalSpacingRatio = verticalSpacingRatio;
                }
            }
        }

        #region Common Coordinate Conversion (Core Utility Methods)
        /// <summary>
        /// Converts hexagonal coordinates (Q,R) to UI screen coordinates
        /// Implements universal conversion logic for hexagonal grids
        /// </summary>
        /// <param name="q">Hexagonal Q coordinate (horizontal axis)</param>
        /// <param name="r">Hexagonal R coordinate (vertical axis)</param>
        /// <returns>UI position in canvas coordinates</returns>
        public virtual Vector2 HexToUIPosition(int q, int r)
        {
            // Calculate hex grid step sizes with spacing ratios
            float hexHorizontalStep = cellSize * Mathf.Sqrt(3) * horizontalSpacingRatio;
            float hexVerticalStep = cellSize * 1.5f * verticalSpacingRatio;

            // Calculate row parity for offset hex grid
            int rowParity = Mathf.Abs(r) % 2;

            // Convert hex coordinates to UI position
            float x = hexHorizontalStep * (q - rowParity * 0.5f);
            float y = hexVerticalStep * r;

            // Invert Y axis (Unity UI coordinates) and add board center offset
            return new Vector2(x, -y) + boardCenter;
        }

        /// <summary>
        /// Converts UI screen coordinates back to hexagonal coordinates (Q,R)
        /// Reverse conversion of HexToUIPosition
        /// </summary>
        /// <param name="uiPos">UI position in canvas coordinates</param>
        /// <returns>Hexagonal coordinates as Vector2Int (Q,R)</returns>
        public virtual Vector2Int UIPositionToHex(Vector2 uiPos)
        {
            // Remove board center offset
            uiPos -= boardCenter;

            // Calculate hex grid step sizes with spacing ratios
            float hexHorizontalStep = cellSize * Mathf.Sqrt(3) * horizontalSpacingRatio;
            float hexVerticalStep = cellSize * 1.5f * verticalSpacingRatio;

            // Convert Y coordinate (invert for Unity UI)
            float y = -uiPos.y / hexVerticalStep;

            // Calculate X with row parity adjustment
            float x = (uiPos.x / hexHorizontalStep) + (Mathf.Abs(Mathf.RoundToInt(y)) % 2) * 0.5f;

            // Round to integer hex coordinates
            int r = Mathf.RoundToInt(y);
            int q = Mathf.RoundToInt(x);

            return new Vector2Int(q, r);
        }
        #endregion

        #region Common Data Operations (Model Layer)
        /// <summary>
        /// Safely retrieves a cell model by hexagonal coordinates
        /// Null-safe access to board model cells
        /// </summary>
        /// <param name="q">Hexagonal Q coordinate</param>
        /// <param name="r">Hexagonal R coordinate</param>
        /// <returns>Cell model at specified coordinates (null if not found)</returns>
        public virtual TQ_HexCellModel GetCellModelByCoordinates(int q, int r)
        {
            return boardModel.GetCellByCoordinates(q, r);
        }

        /// <summary>
        /// Adds a cell model to the board model
        /// Integrates new cells into the data model
        /// </summary>
        /// <param name="cellModel">Cell model to add</param>
        public virtual void AddCellModel(TQ_HexCellModel cellModel)
        {
            boardModel.AddCell(cellModel);
        }

        /// <summary>
        /// Clears all data from the board model
        /// Resets cells, player pieces, and enemy pieces collections
        /// </summary>
        public virtual void ClearBoardModel()
        {
            boardModel.Cells.Clear();
            boardModel.PlayerPieces.Clear();
            boardModel.EnemyPieces.Clear();
        }
        #endregion

        #region Common View Operations
        /// <summary>
        /// Creates a cell view component and binds it to a cell model
        /// Implements MV pattern (Model → View binding)
        /// </summary>
        /// <param name="cellModel">Cell model to bind to view</param>
        /// <returns>Created cell view (null if board view not initialized)</returns>
        public virtual TQ_HexCellView CreateCellView(TQ_HexCellModel cellModel)
        {
            if (_boardView == null) return null;
            return _boardView.CreateCellView(cellModel);
        }

        /// <summary>
        /// Creates a piece view component and binds it to a piece model
        /// Implements MV pattern (Model → View binding)
        /// </summary>
        /// <param name="pieceModel">Piece model to bind to view</param>
        /// <returns>Created piece view (null if board view not initialized)</returns>
        public virtual TQ_ChessPieceView CreatePieceView(TQ_ChessPieceModel pieceModel)
        {
            if (_boardView == null) return null;
            return _boardView.CreatePieceView(pieceModel);
        }

        /// <summary>
        /// Clears all view components from the board
        /// Destroys cell and piece views (memory cleanup)
        /// </summary>
        public virtual void ClearAllViews()
        {
            _boardView?.ClearAllViews();
        }
        #endregion
    }
}