using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using Free.H2D;

namespace Free.Checkers
{
    /// <summary>
    /// Hexagonal Cell View Component
    /// Visual representation of TQ_HexCellModel (MV pattern)
    /// Handles cell rendering, state synchronization, and visual styling
    /// </summary>
    public class TQ_HexCellView : ZFMonoBehaviour
    {
        [Header("UI Components")]
        [Tooltip("RectTransform for position/size control")]
        public RectTransform Rect;

        [Tooltip("Image component for cell visual appearance")]
        public Image Image;

        [Header("Style Configuration")]
        [Tooltip("Default color for normal cell state")]
        public Color NormalColor = Color.white;

        [Tooltip("Color when cell is highlighted (selected/hovered)")]
        public Color HighlightColor = Color.yellow;

        [Tooltip("Color when cell is a valid move target")]
        public Color ValidMoveColor = Color.green;

        /// <summary>
        /// Bound data model for this cell view
        /// </summary>
        private TQ_HexCellModel _model;

        /// <summary>
        /// Local cache of highlight state (optimization)
        /// </summary>
        private bool _isHighlighted;

        /// <summary>
        /// Drag hover state: when true, cell is highlighted under dragged piece (green=valid, red=invalid)
        /// </summary>
        private bool _dragHoverActive;
        private bool _dragHoverValid;

        /// <summary>
        /// Alpha for drag hover highlight (100/255)
        /// </summary>
        private const float DragHoverAlpha = 100f / 255f;

        /// <summary>
        /// Binds view to data model (MV pattern)
        /// Establishes connection between visual and data layer
        /// </summary>
        /// <param name="model">Cell data model to bind</param>
        public void Bind(TQ_HexCellModel model)
        {
            _model = model;
            SyncModelState();
        }

        /// <summary>
        /// Synchronizes view state with bound model state
        /// Updates visual appearance to reflect model properties
        /// </summary>
        public void SyncModelState()
        {
            // Safety check: Ensure model and image exist
            if (_model == null || Image == null) return;

            // When in drag hover, keep showing drag hover color until cleared
            if (_dragHoverActive)
            {
                Image.color = _dragHoverValid
                    ? new Color(ValidMoveColor.r, ValidMoveColor.g, ValidMoveColor.b, DragHoverAlpha)
                    : new Color(1f, 0f, 0f, DragHoverAlpha);
                return;
            }

            // Update color based on model state (valid move takes priority)
            if (_model.IsValidMoveTarget)
            {
                Image.color = ValidMoveColor;
            }
            else if (_model.IsHighlighted)
            {
                Image.color = HighlightColor;
            }
            else
            {
                Image.color = NormalColor;
            }

            // Update local highlight cache
            _isHighlighted = _model.IsHighlighted;
        }

        /// <summary>
        /// Sets drag hover state (piece dragged over this cell). Green if valid move, red otherwise. Alpha 100/255.
        /// </summary>
        /// <param name="hover">True when piece is within threshold of this cell</param>
        /// <param name="isValid">True when this cell is a valid move target for the dragged piece</param>
        public void SetDragHover(bool hover, bool isValid)
        {
            _dragHoverActive = hover;
            _dragHoverValid = isValid;
            SyncModelState();
        }

        /// <summary>
        /// Sets highlight state (updates model and syncs view)
        /// Two-way binding: view modifies model, then syncs visual state
        /// </summary>
        /// <param name="value">New highlight state (true = highlighted)</param>
        public void SetHighlighted(bool value)
        {
            if (_model != null)
            {
                _model.IsHighlighted = value;
            }
            SyncModelState();
        }

        /// <summary>
        /// Sets cell sprite and color
        /// Direct visual modification (for initialization)
        /// </summary>
        /// <param name="sprite">Sprite to display</param>
        /// <param name="color">Color for the sprite</param>
        public void SetSprite(Sprite sprite, Color color)
        {
            if (Image == null) return;
            Image.sprite = sprite;
            Image.color = color;
        }
    }

    /// <summary>
    /// Chess Piece View Component
    /// Visual representation of TQ_ChessPieceModel (MV pattern)
    /// Handles piece rendering, position updates, and selection state
    /// </summary>
    public class TQ_ChessPieceView : MonoBehaviour
    {
        [Header("UI Components")]
        [Tooltip("RectTransform for position/size control")]
        public RectTransform Rect;

        [Tooltip("Image component for piece visual appearance")]
        public Image Image;

        /// <summary>
        /// Bound data model for this piece view
        /// </summary>
        private TQ_ChessPieceModel _model;

        /// <summary>
        /// Binds view to data model (MV pattern)
        /// Establishes connection between visual and data layer
        /// </summary>
        /// <param name="model">Piece data model to bind</param>
        public void Bind(TQ_ChessPieceModel model)
        {
            _model = model;
            SyncModelState();
        }

        /// <summary>
        /// Synchronizes view state with bound model state
        /// Updates visual appearance (transparency) for selection state
        /// </summary>
        public void SyncModelState()
        {
            if (_model == null || Image == null) return;
            // Adjust transparency for selected state (80% opacity when selected)
            Image.color = _model.IsSelected ? new Color(1, 1, 1, 0.8f) : Color.white;
        }

        /// <summary>
        /// Updates piece position in UI space
        /// Converts logical position to visual position
        /// </summary>
        /// <param name="uiPos">Target UI position (anchored position)</param>
        public void UpdatePosition(Vector2 uiPos)
        {
            if (Rect != null)
            {
                Rect.anchoredPosition = uiPos;
            }
        }

        /// <summary>
        /// Sets piece selection state (updates model and syncs view)
        /// Two-way binding: view modifies model, then syncs visual state
        /// </summary>
        /// <param name="value">New selection state (true = selected)</param>
        public void SetSelected(bool value)
        {
            if (_model != null)
            {
                _model.IsSelected = value;
            }
            SyncModelState();
        }
    }

    /// <summary>
    /// Temporary Animation Piece
    /// Non-model-bound piece for move animation visualization
    /// Prevents model modification during animation playback
    /// </summary>
    public class TQ_AnimationPiece
    {
        /// <summary>
        /// RectTransform for position/size control
        /// </summary>
        public RectTransform Rect { get; set; }

        /// <summary>
        /// Image component for visual appearance
        /// </summary>
        public Image Image { get; set; }

        /// <summary>
        /// Piece owner (Player/Enemy) for sprite selection
        /// </summary>
        public TQ_PieceOwner Owner { get; set; }

        /// <summary>
        /// Starting position of animation
        /// </summary>
        public Vector2 StartPos { get; set; }

        /// <summary>
        /// Target position of animation
        /// </summary>
        public Vector2 TargetPos { get; set; }

        /// <summary>
        /// Destroys temporary animation piece
        /// Cleans up UI object to prevent memory leaks
        /// </summary>
        public void Destroy()
        {
            if (Rect != null && Rect.gameObject != null)
            {
                Object.Destroy(Rect.gameObject);
            }
        }
    }

    /// <summary>
    /// Hexagonal Board View Manager
    /// Central controller for board visualization
    /// Handles view creation, state synchronization, and animation
    /// Implements MV pattern for board visualization
    /// </summary>
    public class TQ_HexBoardView : ZFMonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Main UI canvas for board rendering")]
        public Canvas GameCanvas;

        [Tooltip("Center position of the board in UI coordinates")]
        public Vector2 BoardCenter = Vector2.zero;

        /// <summary>
        /// Size of each hex cell (pixels)
        /// Public getter with private setter (controlled via injection)
        /// </summary>
        public float CellSize { get; set; }

        [HideInInspector]
        [Tooltip("Horizontal spacing ratio (0.8 = 80% of default spacing)")]
        public float HorizontalSpacingRatio = 0.8f;

        [HideInInspector]
        [Tooltip("Vertical spacing ratio (0.8 = 80% of default spacing)")]
        public float VerticalSpacingRatio = 0.8f;

        [Header("Prefabs")]
        [Tooltip("Prefab for hex cell UI elements")]
        public GameObject CellPrefab;

        [Tooltip("Prefab for chess piece UI elements")]
        public GameObject PiecePrefab;

        [Tooltip("Parent transform for all board UI elements")]
        public RectTransform BoardParent;

        [Header("Sprites")]
        [Tooltip("Sprite for normal grid cells")]
        public Sprite NormalGridSprite;

        [Tooltip("Sprite for highlighted grid cells")]
        public Sprite HighlightGridSprite;

        [Tooltip("Sprite for player-controlled pieces")]
        public Sprite PlayerPieceSprite;

        [Tooltip("Sprite for enemy/AI-controlled pieces")]
        public Sprite EnemyPieceSprite;

        [Header("Move Animation Configuration")]
        [Tooltip("Enable/disable piece movement animations")]
        public bool EnableAnimation = false;

        [Tooltip("Time interval between animation steps (seconds)")]
        public float MoveStepInterval = 0.5f;

        /// <summary>
        /// Coroutine reference for move animation (to allow cancellation)
        /// </summary>
        private Coroutine _moveCoroutine;

        /// <summary>
        /// Mapping between cell models and their views (MV pattern)
        /// Enables quick lookup for state synchronization
        /// </summary>
        public Dictionary<TQ_HexCellModel, TQ_HexCellView> CellViewMap = new Dictionary<TQ_HexCellModel, TQ_HexCellView>();

        /// <summary>
        /// Mapping between piece models and their views (MV pattern)
        /// Enables quick lookup for state synchronization
        /// </summary>
        public Dictionary<TQ_ChessPieceModel, TQ_ChessPieceView> PieceViewMap = new Dictionary<TQ_ChessPieceModel, TQ_ChessPieceView>();

        /// <summary>
        /// Reference to game manager (for interaction control)
        /// </summary>
        protected TQ_CheckerGameManager _tqgm;

        /// <summary>
        /// Temporary animation piece (for move visualization)
        /// </summary>
        private TQ_AnimationPiece _tempAnimationPiece;

        /// <summary>
        /// Initializes view dependencies on awake
        /// Finds game manager reference
        /// </summary>
        protected virtual void Awake()
        {
            _tqgm = FindFirstObjectByType<TQ_CheckerGameManager>();
            if (_tqgm == null) Debug.LogError($"TQ_HexBoardView.Awake: Checker Game Manager not found.");
        }

        /// <summary>
        /// Converts hexagonal coordinates (Q,R) to UI screen coordinates
        /// Core coordinate transformation for hex grid rendering
        /// </summary>
        /// <param name="q">Hexagonal Q coordinate</param>
        /// <param name="r">Hexagonal R coordinate</param>
        /// <returns>UI position in canvas coordinates</returns>
        public Vector2 HexToUIPosition(int q, int r)
        {
            // Calculate hex grid step sizes with spacing ratios
            float hexHorizontalStep = CellSize * Mathf.Sqrt(3) * HorizontalSpacingRatio;
            float hexVerticalStep = CellSize * 1.5f * VerticalSpacingRatio;

            // Calculate row parity for offset hex grid
            int rowParity = Mathf.Abs(r) % 2;

            // Convert hex coordinates to UI position
            float x = hexHorizontalStep * (q - rowParity * 0.5f);
            float y = hexVerticalStep * r;

            // Invert Y axis (Unity UI coordinates) and add board center offset
            return new Vector2(x, -y) + BoardCenter;
        }

        /// <summary>
        /// Creates and configures cell view for a cell model
        /// Implements MV pattern: creates view bound to model
        /// </summary>
        /// <param name="model">Cell model to create view for</param>
        /// <returns>Configured cell view component</returns>
        public TQ_HexCellView CreateCellView(TQ_HexCellModel model)
        {
            // Instantiate cell prefab as child of board parent
            var cellObj = Instantiate(CellPrefab, BoardParent);
            cellObj.name = $"Cell_{model.Q}_{model.R}";

            // Get or add cell view component
            var cellView = cellObj.GetComponent<TQ_HexCellView>();
            if (cellView == null) cellView = cellObj.AddComponent<TQ_HexCellView>();

            // Configure UI components
            cellView.Rect = cellObj.GetComponent<RectTransform>();
            cellView.Image = cellObj.GetComponent<Image>();
            cellView.Bind(model);

            // Set UI transform properties (centered pivot/anchor)
            cellView.Rect.anchorMin = cellView.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            cellView.Rect.pivot = new Vector2(0.5f, 0.5f);
            cellView.Rect.sizeDelta = new Vector2(CellSize, CellSize);
            cellView.Rect.anchoredPosition = HexToUIPosition(model.Q, model.R);
            cellView.SetSprite(NormalGridSprite, Color.white);

            // Add to view map for future reference
            CellViewMap[model] = cellView;
            return cellView;
        }

        /// <summary>
        /// Creates and configures piece view for a piece model
        /// Implements MV pattern: creates view bound to model
        /// Adds interaction component for player pieces
        /// </summary>
        /// <param name="model">Piece model to create view for</param>
        /// <returns>Configured piece view component</returns>
        public TQ_ChessPieceView CreatePieceView(TQ_ChessPieceModel model)
        {
            // Instantiate piece prefab as child of board parent
            var pieceObj = Instantiate(PiecePrefab, BoardParent);
            pieceObj.name = $"Piece_{model.Owner}_{model.CurrentCell.Q}_{model.CurrentCell.R}";

            // Get or add piece view component
            var pieceView = pieceObj.GetComponent<TQ_ChessPieceView>();
            if (pieceView == null) pieceView = pieceObj.AddComponent<TQ_ChessPieceView>();

            // Configure UI components
            pieceView.Rect = pieceObj.GetComponent<RectTransform>();
            pieceView.Image = pieceObj.GetComponent<Image>();
            pieceView.Bind(model);

            // Set UI transform properties (centered pivot/anchor)
            pieceView.Rect.anchorMin = pieceView.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            pieceView.Rect.pivot = new Vector2(0.5f, 0.5f);
            pieceView.Rect.sizeDelta = new Vector2(CellSize * 0.8f, CellSize * 0.8f);

            // Set appropriate sprite based on piece owner
            pieceView.Image.sprite = model.Owner == TQ_PieceOwner.Player ? PlayerPieceSprite : EnemyPieceSprite;
            pieceView.UpdatePosition(HexToUIPosition(model.CurrentCell.Q, model.CurrentCell.R));

            // Add to view map for future reference
            PieceViewMap[model] = pieceView;

            // Add interaction component for player-controlled pieces
            if (model.Owner == TQ_PieceOwner.Player)
            {
                TQ_ChessPieceInteraction interaction = pieceObj.GetComponent<TQ_ChessPieceInteraction>();
                if (interaction == null)
                {
                    interaction = pieceObj.AddComponent<TQ_ChessPieceInteraction>();
                }
                interaction.Init(model, _tqgm);
            }

            return pieceView;
        }

        /// <summary>
        /// Clears all board views (cells and pieces)
        /// Destroys UI objects and clears view maps
        /// </summary>
        public void ClearAllViews()
        {
            // Destroy all child objects of board parent
            foreach (Transform child in BoardParent)
            {
                DestroyImmediate(child.gameObject);
            }

            // Clear view mappings (prevent memory leaks)
            CellViewMap.Clear();
            PieceViewMap.Clear();
        }

        /// <summary>
        /// Synchronizes all views with their bound models
        /// Updates positions and visual states for all cells and pieces
        /// </summary>
        public void SyncAllModelStates()
        {
            // Sync cell states
            foreach (var kvp in CellViewMap)
            {
                kvp.Value.SyncModelState();
            }

            // Sync piece states and positions
            foreach (var kvp in PieceViewMap)
            {
                kvp.Value.SyncModelState();
                if (kvp.Key.CurrentCell != null)
                {
                    kvp.Value.UpdatePosition(HexToUIPosition(kvp.Key.CurrentCell.Q, kvp.Key.CurrentCell.R));
                }
            }
        }

        /// <summary>
        /// Highlights valid move targets for a selected piece
        /// Visualizes possible moves for the selected piece
        /// </summary>
        /// <param name="pieceModel">Selected piece model</param>
        public void HighlightPieceValidMoves(TQ_ChessPieceModel pieceModel)
        {
            // Clear existing highlights first
            ClearAllHighlights();

            // Highlight valid move targets
            if (pieceModel != null && pieceModel.ValidMoves != null)
            {
                foreach (var cellModel in pieceModel.ValidMoves)
                {
                    if (cellModel is TQ_HexCellModel hexCell && CellViewMap.TryGetValue(hexCell, out var cellView))
                    {
                        hexCell.IsValidMoveTarget = true;
                        hexCell.IsHighlighted = true;
                        cellView.SyncModelState();
                    }
                }
            }

            // Mark piece as selected
            if (PieceViewMap.TryGetValue(pieceModel, out var pieceView))
            {
                pieceModel.IsSelected = true;
                pieceView.SyncModelState();
            }
        }

        /// <summary>
        /// Clears all visual highlights from the board
        /// Resets cell and piece states to normal
        /// </summary>
        public void ClearAllHighlights()
        {
            // Reset cell highlight states
            foreach (var cellModel in CellViewMap.Keys)
            {
                cellModel.IsHighlighted = false;
                cellModel.IsValidMoveTarget = false;
            }

            // Reset piece selection states
            foreach (var pieceModel in PieceViewMap.Keys)
            {
                pieceModel.IsSelected = false;
            }

            // Sync visual states with model changes
            SyncAllModelStates();
        }

        /// <summary>
        /// Sets drag hover state for one cell (green if valid move, red otherwise). Used while dragging a piece.
        /// </summary>
        public void SetCellDragHover(TQ_HexCellModel cell, bool hover, bool isValid)
        {
            if (cell == null || !CellViewMap.TryGetValue(cell, out var cellView)) return;
            cellView.SetDragHover(hover, isValid);
        }

        /// <summary>
        /// Clears all drag hover highlights (call when mouse is released).
        /// </summary>
        public void ClearAllDragHover()
        {
            foreach (var cellView in CellViewMap.Values)
                cellView.SetDragHover(false, false);
        }

        /// <summary>
        /// Plays piece movement animation along specified path
        /// Uses temporary animation piece to visualize movement without modifying model
        /// </summary>
        /// <param name="realPieceModel">Actual piece model being moved</param>
        /// <param name="targetCell">Final target cell for the piece</param>
        /// <param name="moveContext">Movement context with path information</param>
        /// <param name="onComplete">Callback when animation finishes</param>
        public void PlayPieceMoveAnimation(TQ_ChessPieceModel realPieceModel, TQ_HexCellModel targetCell,
            TQ_MoveContext moveContext, System.Action onComplete)
        {
            DebugLog($"TQBV.PlayPieceMoveAnimation: move [{moveContext.JumpPathMap.Count}] steps");

            // Safety check: Ensure callback exists to prevent workflow stalls
            if (onComplete == null)
            {
                Debug.LogError("Animation callback cannot be null (causes workflow stalls)");
                onComplete = () => Debug.LogWarning("Default animation callback triggered");
            }

            // Stop existing animation if running
            if (_moveCoroutine != null)
            {
                StopCoroutine(_moveCoroutine);
            }

            // Lock player interaction during animation
            _tqgm.LockPlayerInteraction();

            // Get movement path from context (jump path if available)
            List<ITQ_HexCell> interfacePath = moveContext.GetJumpPath(targetCell);
            List<TQ_HexCellModel> movePath = interfacePath.Cast<TQ_HexCellModel>().ToList();

            // Fallback to straight path if jump path not found
            if (movePath.Count == 0)
            {
                movePath = GetStraightMovePath(realPieceModel.CurrentCell, targetCell);
                Debug.LogWarning("Jump path not found, falling back to straight path");
            }

            // Safety check: Trigger callback if path is empty
            if (movePath.Count == 0)
            {
                Debug.LogWarning("Movement path is empty, triggering callback directly");
                onComplete?.Invoke();
                _tqgm.UnlockPlayerInteraction();
                return;
            }

            // Create temporary animation piece and hide real piece
            _tempAnimationPiece = CreateTempAnimationPiece(realPieceModel);
            HideRealPiece(realPieceModel, true);

            // Start animation coroutine
            _moveCoroutine = StartCoroutine(MoveTempPieceStepByStep(_tempAnimationPiece, movePath,
                realPieceModel, targetCell, onComplete));
        }

        /// <summary>
        /// Creates temporary animation piece for movement visualization
        /// Identical visual copy of real piece (no model binding)
        /// </summary>
        /// <param name="realPieceModel">Real piece model to duplicate</param>
        /// <returns>Configured temporary animation piece</returns>
        private TQ_AnimationPiece CreateTempAnimationPiece(TQ_ChessPieceModel realPieceModel)
        {
            // 1. Instantiate temporary piece object
            var tempPieceObj = Instantiate(PiecePrefab, BoardParent);
            tempPieceObj.name = $"TempAnimationPiece_{realPieceModel.CurrentCell.Q}_{realPieceModel.CurrentCell.R}";

            // 2. Initialize temporary piece UI components
            var rect = tempPieceObj.GetComponent<RectTransform>();
            var image = tempPieceObj.GetComponent<Image>();

            // Configure transform properties (match real piece)
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(CellSize * 0.8f, CellSize * 0.8f);
            rect.anchoredPosition = HexToUIPosition(realPieceModel.CurrentCell.Q, realPieceModel.CurrentCell.R);

            // Set appropriate sprite and color
            image.sprite = realPieceModel.Owner == TQ_PieceOwner.Player ? PlayerPieceSprite : EnemyPieceSprite;
            image.color = Color.white;

            // 3. Return temporary piece (no model reference)
            return new TQ_AnimationPiece
            {
                Rect = rect,
                Image = image,
                Owner = realPieceModel.Owner,
                StartPos = rect.anchoredPosition
            };
        }

        /// <summary>
        /// Hides or shows the real piece during animation
        /// Prevents visual duplication with temporary animation piece
        /// </summary>
        /// <param name="realPieceModel">Real piece to hide/show</param>
        /// <param name="hide">True to hide, false to show</param>
        private void HideRealPiece(TQ_ChessPieceModel realPieceModel, bool hide)
        {
            if (PieceViewMap.TryGetValue(realPieceModel, out var realPieceView))
            {
                realPieceView.Image.enabled = !hide;
            }
        }

        /// <summary>
        /// Coroutine for step-by-step movement of temporary animation piece
        /// Updates only UI position (no model modifications during animation)
        /// </summary>
        /// <param name="tempPiece">Temporary animation piece</param>
        /// <param name="movePath">Path to animate along</param>
        /// <param name="realPieceModel">Real piece model</param>
        /// <param name="finalTargetCell">Final target cell</param>
        /// <param name="onComplete">Completion callback</param>
        /// <returns>Coroutine enumerator</returns>
        private IEnumerator MoveTempPieceStepByStep(TQ_AnimationPiece tempPiece, List<TQ_HexCellModel> movePath,
            TQ_ChessPieceModel realPieceModel, TQ_HexCellModel finalTargetCell, System.Action onComplete)
        {
            // Prevent multiple callback invocations
            bool isCallbackInvoked = false;
            void InvokeCallback()
            {
                if (isCallbackInvoked) return;
                isCallbackInvoked = true;
                onComplete?.Invoke();
            }

            // Safety check: Trigger callback if temp piece is null
            if (tempPiece == null || tempPiece.Rect == null)
            {
                Debug.LogError("Temporary animation piece is null");
                InvokeCallback();
                yield break;
            }

            // Animate piece along path (UI-only updates)
            for (int i = 0; i < movePath.Count; i++)
            {
                var stepCell = movePath[i];
                DebugLog($"TQBV.MoveTempPieceStepByStep: i-[{i}] movePath [{movePath.Count}] move to [{stepCell.Q},[{stepCell.R}]");

                try
                {
                    // Update only temporary piece position (no model changes)
                    tempPiece.Rect.anchoredPosition = HexToUIPosition(stepCell.Q, stepCell.R);
                    if (i > 0) PlayMoveSFX();
                    //SyncAllModelStates(); // Sync only other UI, no model modifications
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Animation step {i} error: {e.Message}\n{e.StackTrace}");
                    break;
                }

                // Wait for interval between steps (except last step)
                if (i < movePath.Count - 1)
                {
                    yield return new WaitForSeconds(MoveStepInterval);
                }
            }

            // Animation completion cleanup
            yield return null;
            _moveCoroutine = null;

            // Destroy temporary piece (complete cleanup)
            tempPiece.Destroy();
            _tempAnimationPiece = null;

            // Show real piece again
            HideRealPiece(realPieceModel, false);

            /* Model was already updated before animation
            // Update real model position once after animation completes (single model modification)
            if (realPieceModel != null && finalTargetCell != null)
            {
                _tqgm.boardManager.RuleEngine.ExecuteMove(realPieceModel, finalTargetCell);
                // Sync real piece UI position to final target
                if (PieceViewMap.TryGetValue(realPieceModel, out var realPieceView))
                {
                    realPieceView.UpdatePosition(HexToUIPosition(finalTargetCell.Q, finalTargetCell.R));
                }
            }
            */

            DebugLog($"Temporary piece animation complete: {movePath.Count} steps executed, real model position updated");
            InvokeCallback();
        }

        public virtual void PlayMoveSFX()
        {
            AudioSource asource = GetComponent<AudioSource>();
            if ( asource != null )
            {
                if (asource.isPlaying) { asource.Stop(); }
                asource.Play();
            }
        }
        /// <summary>
        /// Generates straight movement path between two cells
        /// Fallback path when jump path is unavailable
        /// </summary>
        /// <param name="startCell">Starting cell</param>
        /// <param name="endCell">Ending cell</param>
        /// <returns>List of cells forming straight path between start and end</returns>
        private List<TQ_HexCellModel> GetStraightMovePath(TQ_HexCellModel startCell, TQ_HexCellModel endCell)
        {
            List<TQ_HexCellModel> path = new List<TQ_HexCellModel>();
            if (startCell == endCell) return path;

            // Calculate coordinate differences
            int qDiff = endCell.Q - startCell.Q;
            int rDiff = endCell.R - startCell.R;
            int maxStep = Mathf.Max(Mathf.Abs(qDiff), Mathf.Abs(rDiff));

            // Generate intermediate steps
            for (int step = 1; step <= maxStep; step++)
            {
                int q = startCell.Q + Mathf.RoundToInt((float)qDiff / maxStep * step);
                int r = startCell.R + Mathf.RoundToInt((float)rDiff / maxStep * step);

                // Find cell model at calculated coordinates
                var cellModel = CellViewMap.Keys.FirstOrDefault(c => c.Q == q && c.R == r);
                if (cellModel != null)
                {
                    path.Add(cellModel);
                }
            }

            return path;
        }

        /// <summary>
        /// Enables/disables animation state
        /// Runtime control of animation system
        /// </summary>
        /// <param name="enable">True to enable animations, false to disable</param>
        public void EnableAnimationState(bool enable)
        {
            EnableAnimation = enable;
        }
    }
}