using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Free.H2D;

namespace Free.Checkers
{
    /// <summary>
    /// Core game manager for Checker (Draughts) game logic
    /// Manages game flow, turn switching, win condition checking, and interaction control
    /// </summary>
    public class TQ_CheckerGameManager : ZFMonoBehaviour
    {
        [Header("Core Dependencies")]
        [Tooltip("Reference to hex board manager (handles board data and view)")]
        public TQ_HexBoardManager boardManager;

        [Tooltip("Reference to AI manager (handles enemy turn logic)")]
        private ICheckerAIManager _aiManager;

        [Header("Game Configuration")]
        [Tooltip("Delay time (seconds) before closing game panel after game over")]
        public float gameOverCloseDelay = 3.0f;

        [Tooltip("Target camp positions player needs to occupy to win")]
        public List<Vector2Int> playerTargetCamp;

        [Tooltip("Target camp positions enemy needs to occupy to win")]
        public List<Vector2Int> enemyTargetCamp;

        [Header("UI Elements")]
        [Tooltip("Canvas group for checker game panel (controls visibility/interactivity)")]
        public CanvasGroup _canvasGroupCheckerPanel;

        [Tooltip("Main game UI panel (visible during gameplay)")]
        public GameObject gameUI;

        [Tooltip("Win/lose UI panel (shown when game ends)")]
        public GameObject winUI;

        [Tooltip("Text element to display win/lose message")]
        public Text winText;

        [Header("Interaction Control")]
        [Tooltip("Flag to lock/unlock player input (clicks, piece selection)")]
        public bool isPlayerInteractionLocked = false;

        [SerializeField]
        protected AIVersion selectedAIVersion;

        [Header("Debug Information")]
        [SerializeField] private TQ_GameState _currentState;

        /// <summary>
        /// Current game state (PlayerTurn/EnemyTurn/GameOver/AnimationPlaying)
        /// </summary>
        public TQ_GameState CurrentState { get => _currentState; private set => _currentState = value; }

        /// <summary>
        /// Context object to store move-related data (selected piece, move path, etc.)
        /// </summary>
        private TQ_MoveContext _currentMoveContext;

        /// <summary>
        /// Current game round counter
        /// </summary>
        public int _currentRound;

        #region Lifecycle Methods
        /// <summary>
        /// Initializes game state on object creation
        /// </summary>
        private void Awake()
        {
            BindAIManagerByVersion(selectedAIVersion);
            CloseCheckerGame();
            _currentMoveContext = new TQ_MoveContext();
        }
        private void BindAIManagerByVersion(AIVersion version)
        {
            _aiManager = null;

            switch (version)
            {
                case AIVersion.MinMaxV1:
                    _aiManager = FindFirstObjectByType<TQ_CheckerAIManagerV1>();
                    break;
                case AIVersion.MinMaxV2:
                    _aiManager = FindFirstObjectByType<TQ_CheckerAIManagerV2>();
                    break;
                case AIVersion.MCTSV3:
                    _aiManager = FindFirstObjectByType<TQ_CheckerAIManagerV3>();
                    break;
            }
            if (_aiManager == null)
            {
                Debug.LogError($"AI Manager {version} Invalid！");
            }
            else
            {
                DebugLog($"Binding AI Manager：{version} successfully.");
            }
        }
        /// <summary>
        /// Starts game initialization on first frame
        /// </summary>
        protected void Start()
        {
            OpenCheckerGame();
        }
        #endregion

        #region Game Initialization & Reset
        /// <summary>
        /// Fully initializes game state and dependencies
        /// </summary>
        public void InitializeGame()
        {
            ClearGameData();
            boardManager.InitBoardManager();
            AutoCalculateWinCamps();
            ValidateDependencies();

            // Show game UI, hide win UI
            gameUI?.SetActive(true);
            winUI?.SetActive(false);

            // Initialize AI manager with board reference
            _aiManager.Init(boardManager);

            // Set initial game state
            CurrentState = TQ_GameState.PlayerTurn;
            UnlockPlayerInteraction();
            _currentRound = 0;
        }

        /// <summary>
        /// Automatically calculates win camps based on board cell types
        /// Replaces manual configuration with dynamic detection
        /// </summary>
        private void AutoCalculateWinCamps()
        {
            // Validate board model exists
            if (boardManager?.boardModel == null || boardManager.boardModel.Cells.Count == 0)
            {
                Debug.LogError("Cannot auto-calculate win camps: Board model is null or has no cells");
                return;
            }

            // Rule 1: Filter by CellType (recommended - strongly bound to factory camp markers)
            // Player needs to occupy enemy camp cells
            playerTargetCamp = boardManager.boardModel.Cells.Values
                .Where(cell => cell.CellType == TQ_CellType.EnemyCamp)
                .Select(cell => new Vector2Int(cell.Q, cell.R))
                .ToList();

            // Enemy needs to occupy player camp cells
            enemyTargetCamp = boardManager.boardModel.Cells.Values
                .Where(cell => cell.CellType == TQ_CellType.PlayerCamp)
                .Select(cell => new Vector2Int(cell.Q, cell.R))
                .ToList();

            // [Alternative] Rule 2: Use camp coordinates from generator
            // playerTargetCamp = boardManager.boardGenerator.GetCampTriangle("Top");
            // enemyTargetCamp = boardManager.boardGenerator.GetCampTriangle("Bottom");

            Debug.Log($"Auto-calculated win camps complete: Player needs {playerTargetCamp.Count} enemy camp cells, Enemy needs {enemyTargetCamp.Count} player camp cells");
        }

        /// <summary>
        /// Clears all game data and resets state for reinitialization
        /// </summary>
        private void ClearGameData()
        {
            // Stop all running coroutines
            StopAllCoroutines();
            _aiManager?.StopAllCoroutines();

            // Clear board view elements
            if (boardManager != null && boardManager.boardView != null)
            {
                boardManager.boardView.ClearAllViews();
                Debug.Log("Cleared old board UI elements");
            }

            // Reset board model
            if (boardManager != null)
            {
                boardManager.boardModel = null;
            }

            // Clear move context and reset game state
            _currentMoveContext?.Clear();
            CurrentState = TQ_GameState.PlayerTurn;
            Debug.Log("All game data cleared, ready for reinitialization");
        }

        /// <summary>
        /// Validates all core dependencies are properly assigned
        /// Throws exceptions for critical missing references
        /// </summary>
        private void ValidateDependencies()
        {
            if (boardManager == null)
            {
                Debug.LogError("TQ_CheckerGameManager: boardManager is not assigned!");
                throw new System.NullReferenceException("boardManager is a core dependency and must be configured");
            }

            if (boardManager.boardView == null)
            {
                Debug.LogError("TQ_CheckerGameManager: boardManager.boardView is not assigned!");
                throw new System.NullReferenceException("boardView is a core dependency and must be configured");
            }

            if (boardManager.boardModel == null)
            {
                Debug.LogError("TQ_CheckerGameManager: boardManager.boardModel is not assigned!");
                throw new System.NullReferenceException("boardModel is a core dependency and must be configured");
            }

            if (boardManager.boardModel.Cells.Count == 0)
            {
                Debug.LogError("TQ_CheckerGameManager: boardManager.boardModel has no valid cells!");
                throw new System.InvalidOperationException("boardModel initialized but has no cell data - check InitBoard execution timing");
            }

            if (_aiManager == null)
            {
                Debug.LogError("TQ_CheckerGameManager: aiManager is not assigned!");
                throw new System.NullReferenceException("aiManager is a core dependency and must be configured");
            }

            // Warning (non-critical) for empty win camps
            if (playerTargetCamp == null || playerTargetCamp.Count == 0)
            {
                Debug.LogWarning("TQ_CheckerGameManager: Player win camp auto-calculation failed - check board CellType configuration!");
            }

            if (enemyTargetCamp == null || enemyTargetCamp.Count == 0)
            {
                Debug.LogWarning("TQ_CheckerGameManager: Enemy win camp auto-calculation failed - check board CellType configuration!");
            }
        }
        #endregion

        #region Game Flow Control
        /// <summary>
        /// Switches game state to enemy (AI) turn
        /// Locks player interaction during AI move
        /// </summary>
        public void SwitchToEnemyTurn()
        {
            // Safety check: Only switch if game is not over
            if (CurrentState == TQ_GameState.GameOver)
            {
                Debug.LogWarning($"Current state is {CurrentState}, cannot switch to AI turn");
                return;
            }

            CurrentState = TQ_GameState.EnemyTurn;
            LockPlayerInteraction();
            // Delay AI execution to ensure animations complete (0.1s buffer)
            StartCoroutine(DelayExecuteAI(0.1f));
            DebugLog("Switched to AI turn, player interaction locked");
        }

        /// <summary>
        /// Delays AI move execution to avoid animation frame sync issues
        /// </summary>
        /// <param name="delay">Delay time in seconds</param>
        /// <returns>IEnumerator for coroutine</returns>
        private IEnumerator DelayExecuteAI(float delay)
        {
            yield return new WaitForSeconds(delay);
            _aiManager.ExecuteAITurn();
        }

        /// <summary>
        /// Switches game state to player turn
        /// Unlocks player interaction and checks win conditions
        /// </summary>
        public void SwitchToPlayerTurn()
        {
            if (CurrentState == TQ_GameState.GameOver) return;

            CurrentState = TQ_GameState.PlayerTurn;
            UnlockPlayerInteraction();
            CheckGameWinCondition();
            _currentRound++;
            DebugLog("Switched to player turn, player interaction unlocked");
        }

        /// <summary>
        /// Executes piece movement (player or AI) with validation and animation handling
        /// </summary>
        /// <param name="piece">Chess piece to move</param>
        /// <param name="targetCell">Target hex cell to move to</param>
        /// <returns>True if move was successful</returns>
        public bool ExecutePieceMove(TQ_ChessPieceModel piece, TQ_HexCellModel targetCell)
        {
            // State validation
            if ((CurrentState == TQ_GameState.PlayerTurn && isPlayerInteractionLocked) ||
                (CurrentState != TQ_GameState.PlayerTurn && CurrentState != TQ_GameState.EnemyTurn))
            {
                Debug.LogWarning($"Current state is {CurrentState}, interaction locked [{isPlayerInteractionLocked}] - cannot execute move");
                return false;
            }

            // Null validation
            if (piece == null || targetCell == null)
            {
                Debug.LogError("Piece or target cell is null - cannot execute move");
                return false;
            }

            // Execute move via rule engine
            bool moveSuccess = boardManager.RuleEngine.ExecuteMove(piece, targetCell);
            if (moveSuccess)
            {
                // Reset all cell states after successful move
                boardManager.boardModel.ResetAllCellStates();

                // Handle animation playback if enabled
                if (boardManager.boardView.EnableAnimation)
                {
                    DebugLog($"ExecutePieceMove: Playing move animation ({(piece.Owner == TQ_PieceOwner.Enemy ? "AI" : "Player")})");

                    // Set animation state to prevent mid-animation input
                    CurrentState = TQ_GameState.AnimationPlaying;

                    // Play animation with callback
                    boardManager.boardView.PlayPieceMoveAnimation(piece, targetCell, _currentMoveContext, () =>
                    {
                        DebugLog($"{(piece.Owner == TQ_PieceOwner.Enemy ? "AI" : "Player")} move animation completed");

                        // Check win condition after animation
                        bool isGameOver = CheckGameWinCondition();

                        // Switch turns if game continues
                        if (!isGameOver)
                        {
                            if (piece.Owner == TQ_PieceOwner.Enemy)
                            {
                                SwitchToPlayerTurn(); // AI move complete → player turn
                            }
                            else
                            {
                                SwitchToEnemyTurn(); // Player move complete → AI turn
                            }
                        }
                        boardManager.boardView.SyncAllModelStates();
                    });
                }
                else
                {
                    // No animation: Sync view immediately and switch turns
                    boardManager.boardView.SyncAllModelStates();
                    bool isGameOver = CheckGameWinCondition();

                    if (!isGameOver)
                    {
                        if (piece.Owner == TQ_PieceOwner.Enemy)
                        {
                            SwitchToPlayerTurn();
                        }
                        else
                        {
                            SwitchToEnemyTurn();
                        }
                    }
                }
            }
            else
            {
                // Move failed - reset piece selection and sync view
                Debug.LogWarning($"TQGM.ExecutePieceMove: Move failed: {(piece.Owner == TQ_PieceOwner.Enemy ? "AI" : "Player")} invalid target position {targetCell.Q},{targetCell.R}");
                piece.IsSelected = false;
                boardManager.boardView.SyncAllModelStates();

                // Force switch to player turn if AI move failed
                if (piece.Owner == TQ_PieceOwner.Enemy)
                {
                    SwitchToPlayerTurn();
                }
            }

            // Clear move context after move attempt
            _currentMoveContext.Clear();
            return moveSuccess;
        }

        /// <summary>
        /// Checks win conditions for both player and enemy
        /// Triggers game over if any condition is met
        /// </summary>
        /// <returns>True if game is over (win condition met)</returns>
        public bool CheckGameWinCondition()
        {
            // Check player win condition
            if (boardManager.RuleEngine.CheckWinCondition(TQ_PieceOwner.Player, playerTargetCamp))
            {
                OnGameOver(TQ_PieceOwner.Player);
                return true;
            }

            // Check enemy win condition
            if (boardManager.RuleEngine.CheckWinCondition(TQ_PieceOwner.Enemy, enemyTargetCamp))
            {
                OnGameOver(TQ_PieceOwner.Enemy);
                return true;
            }

            // No win condition met
            return false;
        }

        /// <summary>
        /// Gets valid moves for a selected piece based on game rules
        /// </summary>
        /// <param name="piece">Selected chess piece</param>
        /// <returns>List of valid target cells</returns>
        public List<TQ_HexCellModel> GetValidPieceMoves(TQ_ChessPieceModel piece)
        {
            // Validation for preconditions
            if (piece == null
                || CurrentState != TQ_GameState.PlayerTurn
                || isPlayerInteractionLocked
                || boardManager.RuleEngine == null
                || boardManager?.boardModel == null)
            {
                Debug.LogWarning("Failed to get valid moves: Preconditions not met");
                return new List<TQ_HexCellModel>();
            }

            // Get valid moves from rule engine
            var validMovesInterface = boardManager.RuleEngine.GetValidMoves(piece, _currentMoveContext);
            var validMovesModel = validMovesInterface
                .Cast<TQ_HexCellModel>()
                .ToList();

            // Mark valid moves on the piece
            piece.MarkValidMoves(validMovesInterface);

            // Debug log (commented out for production)
            //DebugLog($"Found {validMovesModel.Count} valid moves for piece ({piece.CurrentCell.Q},{piece.CurrentCell.R})");
            return validMovesModel;
        }
        #endregion

        #region Core Interaction Methods
        /// <summary>
        /// Highlights valid move positions for selected piece
        /// Implements MV architecture (Model → View sync)
        /// </summary>
        /// <param name="piece">Selected chess piece</param>
        public void HighlightValidMoves(TQ_ChessPieceModel piece)
        {
            if (piece == null || boardManager?.boardView == null) return;

            // Reset all cell states before highlighting
            boardManager.boardModel.ResetAllCellStates();

            // Get valid moves and highlight in view
            var validMoves = GetValidPieceMoves(piece);
            boardManager.boardView.HighlightPieceValidMoves(piece);

            DebugLog($"[MV Architecture] Marked {validMoves.Count} valid move positions in Model, View will sync automatically");
        }

        /// <summary>
        /// Clears all move highlights from board
        /// Resets Model and View states
        /// </summary>
        public void ClearHighlights()
        {
            if (boardManager?.boardModel == null || boardManager?.boardView == null) return;

            // Reset cell states in Model
            boardManager.boardModel.ResetAllCellStates();

            // Clear highlights in View
            boardManager.boardView.ClearAllHighlights();

            DebugLog("[MV Architecture] Reset all Model highlight states, View synced");
        }
        #endregion

        #region Piece Movement
        /// <summary>
        /// Handles player piece movement with validation
        /// </summary>
        /// <param name="piece">Player's selected piece</param>
        /// <param name="targetCell">Target cell to move to</param>
        public void MovePlayerPiece(TQ_ChessPieceModel piece, TQ_HexCellModel targetCell)
        {
            // Null validation
            if (piece == null || targetCell == null)
            {
                Debug.LogError($"Move failed: Piece or target cell is null - piece[{piece}] cell [{targetCell}]");
                if (piece != null)
                {
                    piece.IsSelected = false;
                    boardManager.boardView.SyncAllModelStates();
                }
                return;
            }

            // Get valid moves and check if target is valid
            var validMoves = GetValidPieceMoves(piece);
            bool isTargetValid = validMoves.Any(cell => cell.Q == targetCell.Q && cell.R == targetCell.R);

            if (!isTargetValid)
            {
                // Invalid target - reset piece position and selection
                DebugLogWarning($"TQGM.MovePlayerPiece: Move failed: Target cell ({targetCell.Q},{targetCell.R}) is not a valid move");
                if (boardManager.boardView.PieceViewMap.TryGetValue(piece, out var pieceView))
                {
                    pieceView.UpdatePosition(
                        boardManager.boardView.HexToUIPosition(piece.CurrentCell.Q, piece.CurrentCell.R)
                    );
                }
                piece.IsSelected = false;
                boardManager.boardView.SyncAllModelStates();
                return;
            }

            // Execute valid move
            bool moveSuccess = ExecutePieceMove(piece, targetCell);
            if (!moveSuccess)
            {
                // Move failed - reset piece position
                Debug.Log($"TQGM.MovePlayerPiece: ->[{targetCell.Q},{targetCell.R}] failed.");
                if (boardManager.boardView.PieceViewMap.TryGetValue(piece, out var pieceView))
                {
                    pieceView.UpdatePosition(
                        boardManager.boardView.HexToUIPosition(piece.CurrentCell.Q, piece.CurrentCell.R)
                    );
                }
            }
        }

        /// <summary>
        /// Specialized method for AI piece movement (only operates on real board data)
        /// </summary>
        /// <param name="aiPiece">AI-controlled piece model from real board</param>
        /// <param name="targetCell">Target cell model from real board</param>
        /// <returns>True if AI move was successful</returns>
        public bool MoveAIPiece(TQ_ChessPieceModel aiPiece, TQ_HexCellModel targetCell)
        {
            // 1. Basic null validation
            if (aiPiece == null || targetCell == null)
            {
                Debug.LogError($"AI move failed: Piece or target cell is null - piece[{aiPiece}] cell [{targetCell}]");
                return false;
            }

            // 2. AI-specific state validation (only execute during AI turn)
            if (CurrentState != TQ_GameState.EnemyTurn)
            {
                Debug.LogWarning($"AI move failed: Current game state is {CurrentState}, not EnemyTurn");
                return false;
            }
            if (aiPiece.Owner != TQ_PieceOwner.Enemy)
            {
                Debug.LogError($"AI move failed: Incorrect piece owner - current {aiPiece.Owner}, required Enemy");
                return false;
            }

            // 3. Core modification: Reuse GameManager's _currentMoveContext (instead of temp context)
            // Clear old context to avoid residual player move paths
            _currentMoveContext.Clear();
            // Bind current AI piece to context
            _currentMoveContext.SelectedPiece = aiPiece;

            // 4. Get valid moves from RuleEngine (populates _currentMoveContext with AI move path)
            var validMovesInterface = boardManager.RuleEngine.GetValidMoves(aiPiece, _currentMoveContext, boardManager.boardModel);
            var validMovesModel = validMovesInterface.Cast<TQ_HexCellModel>().ToList();

            // 5. Validate target is a legal move for this AI piece (based on updated _currentMoveContext)
            bool isTargetValid = validMovesModel.Any(cell => cell.Q == targetCell.Q && cell.R == targetCell.R);
            if (!isTargetValid)
            {
                Debug.LogWarning($"AI move failed: Target cell ({targetCell.Q},{targetCell.R}) is not a legal move for this piece");
                // Fallback: Reset piece selection and sync view
                aiPiece.IsSelected = false;
                boardManager.boardView.SyncAllModelStates();
                // Clear context to avoid invalid data residue
                _currentMoveContext.Clear();
                return false;
            }

            // 6. Verify move path exists in context (for debugging)
            if (_currentMoveContext.JumpPathMap.ContainsKey(targetCell))
            {
                var aiMovePath = _currentMoveContext.JumpPathMap[targetCell];
                var pathStr = string.Join(" → ", aiMovePath.Select(c => $"({c.Q},{c.R})"));
                //Debug.Log($"✅ AI move context updated: Piece ({aiPiece.CurrentCell.Q},{aiPiece.CurrentCell.R}) → Target ({targetCell.Q},{targetCell.R}) path: {pathStr}");
            }
            else
            {
                Debug.LogWarning($"AI move context has no path for target cell ({targetCell.Q},{targetCell.R})");
            }

            // 7. Execute move (uses updated _currentMoveContext)
            bool moveSuccess = ExecutePieceMove(aiPiece, targetCell);
            if (!moveSuccess)
            {
                Debug.LogWarning($"TQGM.MoveAIPiece: AI piece ({aiPiece.CurrentCell.Q},{aiPiece.CurrentCell.R}) → Target ({targetCell.Q},{targetCell.R}) move failed");
                // Fallback: Force sync view to prevent piece position misalignment
                if (boardManager.boardView.PieceViewMap.TryGetValue(aiPiece, out var pieceView))
                {
                    pieceView.UpdatePosition(
                        boardManager.boardView.HexToUIPosition(aiPiece.CurrentCell.Q, aiPiece.CurrentCell.R)
                    );
                }
                aiPiece.IsSelected = false;
                boardManager.boardView.SyncAllModelStates();
                // Clear context
                _currentMoveContext.Clear();
            }
            else
            {
                DebugLog($"TQGM.MoveAIPiece: AI piece ({aiPiece.CurrentCell.Q},{aiPiece.CurrentCell.R}) → Target ({targetCell.Q},{targetCell.R}) move successful, context retains path information");
            }

            return moveSuccess;
        }

        #endregion

        #region Game Over Handling
        /// <summary>
        /// Handles game over logic (win/lose)
        /// </summary>
        /// <param name="winner">Winner of the game (Player/Enemy)</param>
        public void OnGameOver(TQ_PieceOwner winner)
        {
            // Set game over state and lock interaction
            CurrentState = TQ_GameState.GameOver;
            LockPlayerInteraction();

            // Stop all coroutines
            StopAllCoroutines();
            _aiManager?.StopAllCoroutines();

            // Show win UI, hide game UI
            gameUI?.SetActive(false);
            winUI?.SetActive(true);

            // Set win/lose message
            winText.text = winner == TQ_PieceOwner.Player ? "You win!" : "You lose!";
            DebugLog($"[Game Over] Winner: {winner}");

            // Close game panel after delay
            StartCoroutine(DelayAndClose());
        }

        /// <summary>
        /// Handles player resignation (immediate game over, enemy wins)
        /// </summary>
        public void PlayerResign()
        {
            OnGameOver(TQ_PieceOwner.Enemy);
        }

        /// <summary>
        /// Delays game panel closure after game over
        /// </summary>
        /// <returns>IEnumerator for coroutine</returns>
        private IEnumerator DelayAndClose()
        {
            if (CurrentState != TQ_GameState.GameOver) yield break;

            Debug.Log($"[Game Over] Closing game panel after {gameOverCloseDelay} seconds...");
            yield return new WaitForSeconds(gameOverCloseDelay);

            CloseCheckerGame();
            DebugLog("[Game Over] Game panel closed");

            yield return null;

            OpenCheckerGame();
        }
        #endregion

        #region Difficulty Settings & UI Control
        /// <summary>
        /// Sets AI difficulty level
        /// </summary>
        /// <param name="difficultyIndex">Index corresponding to TQ_AIDifficulty enum</param>
        public void SetAIDifficulty(int difficultyIndex)
        {
            if (CurrentState == TQ_GameState.GameOver) return;

            TQ_AIDifficulty difficulty = (TQ_AIDifficulty)difficultyIndex;
            _aiManager.SetDifficulty(difficulty);
            DebugLog($"AI difficulty set to: {difficulty} (Index: {difficultyIndex})");
        }

        /// <summary>
        /// Opens checker game panel (shows and enables interaction)
        /// </summary>
        public void OpenCheckerGame()
        {
            RestartGame();
            _canvasGroupCheckerPanel.alpha = 1.0f;
            _canvasGroupCheckerPanel.blocksRaycasts = true;
            DebugLog("Game panel opened");
        }

        /// <summary>
        /// Closes checker game panel (hides and disables interaction)
        /// </summary>
        public void CloseCheckerGame()
        {
            _canvasGroupCheckerPanel.alpha = 0f;
            _canvasGroupCheckerPanel.blocksRaycasts = false;
            DebugLog("Game panel closed");
        }

        /// <summary>
        /// Restarts game with fresh initialization
        /// </summary>
        public void RestartGame()
        {
            InitializeGame();
            DebugLog("Game restarted");
        }
        #endregion

        #region Interaction Lock Control
        /// <summary>
        /// Locks all player interaction (clicks, piece selection/movement)
        /// </summary>
        public void LockPlayerInteraction()
        {
            isPlayerInteractionLocked = true;
            DebugLog("[Global Lock] Player interaction locked");
        }

        /// <summary>
        /// Unlocks all player interaction
        /// </summary>
        public void UnlockPlayerInteraction()
        {
            isPlayerInteractionLocked = false;
            DebugLog("[Global Lock] Player interaction unlocked");
        }
        #endregion

        #region Quick Access Properties
        /// <summary>
        /// Quick access to board view (null-safe)
        /// </summary>
        public TQ_HexBoardView BoardView => boardManager?.boardView;

        /// <summary>
        /// Read-only access to player interaction lock state
        /// </summary>
        public bool IsPlayerInteractionLocked => isPlayerInteractionLocked;
        #endregion
    }
}