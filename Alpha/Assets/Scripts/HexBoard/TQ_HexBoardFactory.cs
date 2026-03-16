using System.Collections.Generic;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// Hexagonal Board Factory Class
    /// Responsible for generating complete board models with cells, camps, and initial pieces
    /// Configurable via constructor parameters (decoupled from hardcoded values)
    /// </summary>
    public class HexBoardFactory
    {
        // Configuration parameters injected from external sources
        /// <summary>
        /// Radius of the central hexagonal area (configurable)
        /// </summary>
        private readonly int _hexagonSide;

        /// <summary>
        /// Number of layers for triangular camp areas (configurable)
        /// </summary>
        private readonly int _triangleLayers;

        /// <summary>
        /// Constructor: Initializes factory with board configuration
        /// </summary>
        /// <param name="hexagonSide">Radius of central hexagon</param>
        /// <param name="triangleLayers">Number of layers for camp triangles</param>
        public HexBoardFactory(int hexagonSide, int triangleLayers)
        {
            _hexagonSide = hexagonSide;
            _triangleLayers = triangleLayers;
        }

        /// <summary>
        /// Creates a complete board model with cells, camps, and initial pieces
        /// Main entry point for board generation
        /// </summary>
        /// <returns>Fully initialized hexagonal board model</returns>
        public TQ_HexBoardModel CreateBoardModel()
        {
            var boardModel = new TQ_HexBoardModel();

            // 1. Generate central hexagonal area
            GenerateCentralHexagon(boardModel);

            // 2. Generate 6 triangular camps (correctly sets CellType now)
            GenerateCampTriangles(boardModel);

            // 3. Precompute directional line cache (optimizes move calculations)
            PrecomputeDirectionalLines(boardModel);

            // 4. Generate initial game pieces (player/enemy)
            GenerateInitialPieces(boardModel);

            return boardModel;
        }

        /// <summary>
        /// Generates the central hexagonal grid area
        /// Creates normal cells (non-camp) in the center of the board
        /// </summary>
        /// <param name="boardModel">Board model to populate with cells</param>
        private void GenerateCentralHexagon(TQ_HexBoardModel boardModel)
        {
            int radius = _hexagonSide;

            // Iterate through all rows (R coordinate)
            for (int r = -radius; r <= radius; r++)
            {
                // Calculate number of columns (Q) for current row (hex grid offset logic)
                int qCount = radius * 2 + 1 - Mathf.Abs(r);
                int qStart = -(qCount - 1) / 2;

                // Create cells for current row
                for (int q = qStart; q < qStart + qCount; q++)
                {
                    var cellModel = new TQ_HexCellModel(q, r) { CellType = TQ_CellType.Normal };
                    boardModel.AddCell(cellModel);
                }
            }
        }

        /// <summary>
        /// Generates 6 triangular camp areas around the central hexagon
        /// Sets appropriate CellType for player/enemy camps
        /// </summary>
        /// <param name="boardModel">Board model to populate with camp cells</param>
        private void GenerateCampTriangles(TQ_HexBoardModel boardModel)
        {
            // Get predefined camp coordinates
            var campTriangles = GetCampCoordinates();

            // Create cells for each camp
            foreach (var kvp in campTriangles)
            {
                string campKey = kvp.Key;

                // Create cell for each coordinate in the camp
                foreach (var pos in kvp.Value)
                {
                    // Skip positions that fall within central hexagon
                    if (!IsInCentralHexagon(pos.x, pos.y))
                    {
                        // Determine cell type based on camp key
                        TQ_CellType type = TQ_CellType.Normal;
                        if (campKey == "Bottom") type = TQ_CellType.PlayerCamp;
                        else if (campKey == "Top") type = TQ_CellType.EnemyCamp;

                        // Create camp cell with appropriate type
                        var cellModel = new TQ_HexCellModel(pos.x, pos.y) { CellType = type };
                        boardModel.AddCell(cellModel);
                    }
                }
            }
        }

        /// <summary>
        /// Exposes camp coordinates (for reuse by Generator classes)
        /// Defines predefined positions for all 6 triangular camp areas
        /// </summary>
        /// <returns>Dictionary of camp names to coordinate lists</returns>
        public Dictionary<string, List<Vector2Int>> GetCampCoordinates()
        {
            return new Dictionary<string, List<Vector2Int>>
            {
                ["Top"] = new List<Vector2Int>
                {
                    new Vector2Int(0, -8),
                    new Vector2Int(0, -7), new Vector2Int(1, -7),
                    new Vector2Int(-1, -6), new Vector2Int(0, -6), new Vector2Int(1, -6),
                    new Vector2Int(-1, -5), new Vector2Int(0, -5), new Vector2Int(1, -5), new Vector2Int(2, -5)
                },
                ["Bottom"] = new List<Vector2Int>
                {
                    new Vector2Int(0, 8),
                    new Vector2Int(0, 7), new Vector2Int(1, 7),
                    new Vector2Int(-1, 6), new Vector2Int(0, 6), new Vector2Int(1, 6),
                    new Vector2Int(-1, 5), new Vector2Int(0, 5), new Vector2Int(1, 5), new Vector2Int(2, 5)
                },
                ["TopRight"] = new List<Vector2Int>
                {
                    new Vector2Int(6, -4),
                    new Vector2Int(5, -4), new Vector2Int(6, -3),
                    new Vector2Int(4, -4), new Vector2Int(5, -3), new Vector2Int(5, -2),
                    new Vector2Int(3, -4), new Vector2Int(4, -3), new Vector2Int(4, -2), new Vector2Int(5, -1)
                },
                ["BottomRight"] = new List<Vector2Int>
                {
                    new Vector2Int(6, 4),
                    new Vector2Int(5, 4), new Vector2Int(6, 3),
                    new Vector2Int(4, 4), new Vector2Int(5, 3), new Vector2Int(5, 2),
                    new Vector2Int(3, 4), new Vector2Int(4, 3), new Vector2Int(4, 2), new Vector2Int(5, 1)
                },
                ["TopLeft"] = new List<Vector2Int>
                {
                    new Vector2Int(-6, -4),
                    new Vector2Int(-5, -4), new Vector2Int(-5, -3),
                    new Vector2Int(-4, -4), new Vector2Int(-4, -3), new Vector2Int(-5, -2),
                    new Vector2Int(-3, -4), new Vector2Int(-3, -3), new Vector2Int(-4, -2), new Vector2Int(-4, -1)
                },
                ["BottomLeft"] = new List<Vector2Int>
                {
                    new Vector2Int(-6, 4),
                    new Vector2Int(-5, 4), new Vector2Int(-5, 3),
                    new Vector2Int(-4, 4), new Vector2Int(-4, 3), new Vector2Int(-5, 2),
                    new Vector2Int(-3, 4), new Vector2Int(-3, 3), new Vector2Int(-4, 2), new Vector2Int(-4, 1)
                }
            };
        }

        /// <summary>
        /// Checks if coordinates fall within the central hexagonal area
        /// Prevents camp cells from overlapping with central hexagon
        /// </summary>
        /// <param name="q">Hexagonal Q coordinate</param>
        /// <param name="r">Hexagonal R coordinate</param>
        /// <returns>True if coordinates are in central hexagon, false otherwise</returns>
        private bool IsInCentralHexagon(int q, int r)
        {
            int radius = _hexagonSide;

            // Check row boundary
            if (Mathf.Abs(r) > radius) return false;

            // Calculate column boundaries for current row
            int qCount = radius * 2 + 1 - Mathf.Abs(r);
            int qStart = -(qCount - 1) / 2;
            int qEnd = qStart + qCount - 1;

            // Check if column is within boundaries
            return q >= qStart && q <= qEnd;
        }

        /// <summary>
        /// Precomputes directional line cache for all cells
        /// Optimizes move validation by pre-calculating cell lines in all 6 directions
        /// </summary>
        /// <param name="boardModel">Board model to populate with directional data</param>
        public void PrecomputeDirectionalLines(TQ_HexBoardModel boardModel)
        {
            var boardInterface = (ITQ_HexBoard)boardModel;

            // Precompute lines for each cell in all 6 directions
            foreach (var cell in boardModel.Cells.Values)
            {
                for (int dirIndex = 0; dirIndex < 6; dirIndex++)
                {
                    // Clear existing directional data
                    cell.ClearDirectionalCells(dirIndex);

                    // Calculate and cache directional line
                    ComputeDirectionalLine(boardModel, boardInterface, cell, dirIndex);
                }
            }
        }

        /// <summary>
        /// Computes a single directional line for a cell
        /// Fills cell's directional cache with cells in specified direction
        /// </summary>
        /// <param name="boardModel">Board model containing cells</param>
        /// <param name="boardInterface">Board interface with direction definitions</param>
        /// <param name="cell">Cell to compute directional line for</param>
        /// <param name="dirIndex">Direction index (0-5 corresponding to 6 hex directions)</param>
        public void ComputeDirectionalLine(TQ_HexBoardModel boardModel, ITQ_HexBoard boardInterface, TQ_HexCellModel cell, int dirIndex)
        {
            // Validate direction index
            if (dirIndex < 0 || dirIndex >= 6)
            {
                Debug.LogWarning($"Invalid direction index: {dirIndex}, skipping calculation");
                return;
            }

            // Get direction vector from board interface
            var dir = boardInterface.Directions[dirIndex];
            int currentQ = cell.Q;
            int currentR = cell.R;
            int distance = 1;

            // Traverse in direction until edge of board
            while (true)
            {
                // Get adjusted target coordinates (compensates for staggered grid)
                var adjustedCoords = GetAdjustedTargetCoords(currentQ, currentR, dir);
                currentQ = adjustedCoords.q;
                currentR = adjustedCoords.r;

                // Get target cell from board model
                var targetCell = boardModel.GetCellByCoordinates(currentQ, currentR);
                if (targetCell == null) break; // Exit if edge of board reached

                // Add cell to directional cache with distance
                cell.AddDirectionalCell(dirIndex, distance, targetCell);
                distance++;

                // Safety limit to prevent infinite loops
                if (distance > 20) break;
            }
        }

        /// <summary>
        /// Gets adjusted target coordinates for hexagonal movement
        /// Ensures straight directional lines in staggered hex grid
        /// </summary>
        /// <param name="currentQ">Current Q coordinate</param>
        /// <param name="currentR">Current R coordinate</param>
        /// <param name="dir">Direction vector (from board interface)</param>
        /// <returns>Adjusted (Q,R) coordinates for target position</returns>
        public virtual (int q, int r) GetAdjustedTargetCoords(int currentQ, int currentR, int[] dir)
        {
            // Core hexagonal neighbor logic: add direction vector to current position
            int targetQ = currentQ + dir[0];
            int targetR = currentR + dir[1];

            // Apply row parity compensation for staggered grid alignment
            bool isEvenRow = currentR % 2 == 0;
            if (isEvenRow)
            {
                // Even row: compensate +1 to Q for bottom-left/bottom-right directions
                if (dir[1] == 1) targetQ += 1;   // Bottom Left / Bottom Right
            }
            else
            {
                // Odd row: compensate -1 to Q for top-left/top-right directions
                if (dir[1] == -1) targetQ -= 1; // Top Left / Top Right
            }

            return (targetQ, targetR);
        }

        /// <summary>
        /// Generates initial game pieces for player and enemy
        /// Places pieces in their respective camp areas
        /// </summary>
        /// <param name="boardModel">Board model to populate with pieces</param>
        private void GenerateInitialPieces(TQ_HexBoardModel boardModel)
        {
            // Get camp coordinates for piece placement
            var coords = GetCampCoordinates();

            // Generate player pieces (Bottom camp)
            foreach (var pos in coords["Bottom"])
            {
                var cell = boardModel.GetCellByCoordinates(pos.x, pos.y);
                if (cell == null) continue;

                // Create player piece and link to cell
                var piece = new TQ_ChessPieceModel(TQ_PieceOwner.Player) { CurrentCell = cell };
                cell.IsOccupied = true;
                cell.CurrentPiece = piece;
                boardModel.PlayerPieces.Add(piece);
            }

            // Generate AI/enemy pieces (Top camp)
            foreach (var pos in coords["Top"])
            {
                var cell = boardModel.GetCellByCoordinates(pos.x, pos.y);
                if (cell == null) continue;

                // Create enemy piece and link to cell
                var piece = new TQ_ChessPieceModel(TQ_PieceOwner.Enemy) { CurrentCell = cell };
                cell.IsOccupied = true;
                cell.CurrentPiece = piece;
                boardModel.EnemyPieces.Add(piece);
            }
        }
    }
}