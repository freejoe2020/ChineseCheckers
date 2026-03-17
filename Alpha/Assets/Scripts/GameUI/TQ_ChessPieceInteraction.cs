using UnityEngine;
using UnityEngine.EventSystems;
using System.Linq;
using Free.H2D;

namespace Free.Checkers
{
    /// <summary>
    /// Handles chess piece interaction (drag/drop) for player-controlled pieces
    /// Implements Unity's event system interfaces for pointer input handling
    /// Follows MV (Model-View) architecture pattern
    /// </summary>
    public class TQ_ChessPieceInteraction : ZFMonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>
        /// Data model for the chess piece (contains position, owner, state data)
        /// </summary>
        private TQ_ChessPieceModel _model;

        /// <summary>
        /// Reference to main game controller (handles game logic)
        /// </summary>
        private TQ_CheckerGameManager _controller;

        /// <summary>
        /// Original UI position of the piece before dragging starts
        /// </summary>
        private Vector2 _originalPos;

        /// <summary>
        /// Flag to track if piece is currently being dragged
        /// </summary>
        private bool _isDragging;

        /// <summary>
        /// Cell that was highlighted under the piece last frame (for clearing when leaving)
        /// </summary>
        private TQ_HexCellModel _lastDragHoverCell;

        /// <summary>
        /// Initializes interaction component with model and controller references
        /// MV architecture implementation
        /// </summary>
        /// <param name="model">Chess piece data model</param>
        /// <param name="controller">Main game manager reference</param>
        public void Init(TQ_ChessPieceModel model, TQ_CheckerGameManager controller)
        {
            _model = model;
            _controller = controller;

            // Fix 1: Use View's conversion result directly for initial position (no BoardCenter subtraction)
            if (_controller.BoardView != null && model.CurrentCell != null)
            {
                Vector2 uiPos = _controller.BoardView.HexToUIPosition(model.CurrentCell.Q, model.CurrentCell.R);
                GetComponent<RectTransform>().anchoredPosition = uiPos; // Critical fix: Removed - BoardCenter
            }

            DebugLog($"Chess piece interaction initialized: {model.CurrentCell.Q},{model.CurrentCell.R}");
        }

        /// <summary>
        /// Called when player presses down on the piece (start of interaction)
        /// </summary>
        /// <param name="eventData">Pointer event data (position, input device, etc.)</param>
        public void OnPointerDown(PointerEventData eventData)
        {
            // Exit if interaction is not allowed
            if (!CanInteract()) return;

            // Start dragging state
            _isDragging = true;
            _originalPos = GetComponent<RectTransform>().anchoredPosition;

            // Highlight valid moves (MV pattern: only notify controller to modify Model, no direct View manipulation)
            _controller.HighlightValidMoves(_model);

            // Bring piece to front of render order
            transform.SetAsLastSibling();

            DebugLog($"Started dragging player piece: {_model.CurrentCell.Q},{_model.CurrentCell.R}");
        }

        /// <summary>
        /// Called continuously while player drags the piece
        /// Updates piece position to follow pointer
        /// </summary>
        /// <param name="eventData">Pointer event data with current position</param>
        public void OnDrag(PointerEventData eventData)
        {
            // Exit if interaction not allowed or not dragging
            if (!CanInteract() || !_isDragging) return;

            // Convert screen coordinates to UI local coordinates (relative to BoardParent)
            Vector2 localPos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _controller.BoardView.BoardParent,
                eventData.position,
                _controller.BoardView.GameCanvas.worldCamera,
                out localPos
            );

            GetComponent<RectTransform>().anchoredPosition = localPos;

            // Drag hover: highlight cell under piece (green=valid, red=invalid) when within threshold.
            // Use BoardParent local space so it works for both Overlay and Camera canvas (worldCamera can be null).
            Vector2 pieceLocalPos = GetComponent<RectTransform>().anchoredPosition;
            var boardView = _controller.BoardView;
            float threshold = _controller.dragHoverCellThreshold;
            if (boardView != null && threshold > 0f)
            {
                TQ_HexCellModel hoverCell = null;
                float minDist = float.MaxValue;
                foreach (var kvp in boardView.CellViewMap)
                {
                    TQ_HexCellModel cell = kvp.Key;
                    TQ_HexCellView cellView = kvp.Value;
                    if (cellView?.Rect == null) continue;
                    Vector2 cellLocalPos = cellView.Rect.anchoredPosition;
                    float d = Vector2.Distance(pieceLocalPos, cellLocalPos);
                    if (d < minDist && d < threshold)
                    {
                        minDist = d;
                        hoverCell = cell;
                    }
                }

                if (hoverCell != _lastDragHoverCell)
                {
                    if (_lastDragHoverCell != null)
                        boardView.SetCellDragHover(_lastDragHoverCell, false, false);
                    if (hoverCell != null)
                    {
                        bool isValid = _model.ValidMoves != null && _model.ValidMoves.Any(c => c.Q == hoverCell.Q && c.R == hoverCell.R);
                        boardView.SetCellDragHover(hoverCell, true, isValid);
                    }
                    _lastDragHoverCell = hoverCell;
                }
            }
        }

        /// <summary>
        /// Called when player releases the piece (end of drag interaction)
        /// Processes drop position and move validation
        /// </summary>
        /// <param name="eventData">Pointer event data</param>
        public void OnPointerUp(PointerEventData eventData)
        {
            // Exit if not dragging
            if (!_isDragging) return;
            _isDragging = false;

            try
            {
                // Clear drag hover first so all cells restore to normal
                _controller.BoardView?.ClearAllDragHover();
                _lastDragHoverCell = null;

                // [Core Fix] Step 1: Read drop position first (critical - do not reorder)
                Vector2 dropUIPos = GetComponent<RectTransform>().anchoredPosition;

                // Step 2: Clear highlights and reset states (may modify position)
                _controller.ClearHighlights();

                // Step 3: Calculate logical coordinates from pre-read drop position
                Vector2Int hexPos = ConvertUIPosToHex(dropUIPos);
                TQ_HexCellModel targetCell = _controller.boardManager.boardModel.GetCellByCoordinates(hexPos.x, hexPos.y);
                //DebugLog($"Drag ended: UI position {dropUIPos} ùù Logical coordinates {hexPos} ùù Target cell {(targetCell != null ? $"{targetCell.Q},{targetCell.R}" : "null")}");

                // Step 4: Execute move logic + reset/sync position
                _controller.MovePlayerPiece(_model, targetCell);
                bool moveSuccess = _model.CurrentCell == targetCell;

                if (!moveSuccess)
                {
                    // Manually reset position only if move failed
                    GetComponent<RectTransform>().anchoredPosition = _originalPos;
                }
                else
                {
                    // Sync to target position if move succeeded
                    if (_controller.BoardView.PieceViewMap.TryGetValue(_model, out var pieceView))
                    {
                        pieceView.UpdatePosition(_controller.BoardView.HexToUIPosition(_model.CurrentCell.Q, _model.CurrentCell.R));
                    }
                }
            }
            catch (System.Exception ex)
            {
                // Error handling: reset position on exception
                Debug.LogError($"Drag processing failed: {ex.Message}");
                GetComponent<RectTransform>().anchoredPosition = _originalPos;
            }
            finally
            {
                // Ensure highlights are cleared even if error occurs
                if (_controller != null)
                {
                    _controller.ClearHighlights();
                }
            }
        }

        /// <summary>
        /// Validates if piece interaction is allowed (MV architecture version - reads only Model state)
        /// </summary>
        /// <returns>True if interaction is allowed, false otherwise</returns>
        private bool CanInteract()
        {
            // Validate controller reference
            if (_controller == null)
            {
                Debug.LogError("Controller not initialized - cannot interact");
                return false;
            }

            // Check if it's player's turn
            if (_controller.CurrentState != TQ_GameState.PlayerTurn)
            {
                Debug.LogWarning("Not player's turn - cannot interact");
                return false;
            }

            // Check if player interaction is locked
            if (_controller.IsPlayerInteractionLocked)
            {
                Debug.LogWarning("Player interaction locked - cannot interact");
                return false;
            }

            // Validate piece model and ownership
            if (_model == null || _model.Owner != TQ_PieceOwner.Player)
            {
                Debug.LogWarning("Piece model null or not player-owned - cannot interact");
                return false;
            }

            // All conditions met - interaction allowed
            return true;
        }

        /// <summary>
        /// Converts UI position to hexagonal logical coordinates
        /// Matches HexBoardView's conversion logic with configured spacing
        /// </summary>
        /// <param name="uiPos">UI position (anchored position of RectTransform)</param>
        /// <returns>Hexagonal coordinates (Q,R) as Vector2Int</returns>
        private Vector2Int ConvertUIPosToHex(Vector2 uiPos)
        {
            // Validate BoardView reference
            if (_controller.BoardView == null)
            {
                Debug.LogError("BoardView null - cannot convert coordinates");
                return Vector2Int.zero;
            }

            // Fix 6: Use View's configured spacing ratio instead of hardcoded 0.8
            float hexHorizontalStep = _controller.BoardView.CellSize * Mathf.Sqrt(3) * _controller.BoardView.HorizontalSpacingRatio;
            float hexVerticalStep = _controller.BoardView.CellSize * 1.5f * _controller.BoardView.VerticalSpacingRatio;

            // Fix 7: Subtract BoardCenter only once (exact reverse of HexToUIPosition logic)
            Vector2 rawPos = uiPos - _controller.BoardView.BoardCenter;
            float y = -rawPos.y / hexVerticalStep;
            int r = Mathf.RoundToInt(y);

            // Calculate row parity for offset hex grid
            int rowParity = Mathf.Abs(r) % 2;
            float x = rawPos.x / hexHorizontalStep + rowParity * 0.5f;
            int q = Mathf.RoundToInt(x);

            // Return hexagonal coordinates (Q,R)
            return new Vector2Int(q, r);
        }
    }
}