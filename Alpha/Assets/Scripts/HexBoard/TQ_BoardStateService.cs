using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Free.H2D;

namespace Free.Checkers
{
    /// <summary>
    /// Board State Management Service
    /// Decouples AI logic from global game board and provides snapshot/restore capabilities
    /// Implements object pooling for memory optimization
    /// </summary>
    public class TQ_BoardStateService : ZFMonoBehaviour
    {
        /// <summary>
        /// Reference to the global game board model (actual game state)
        /// </summary>
        private TQ_HexBoardModel _globalBoardModel;

        /// <summary>
        /// Object pool for board snapshots (prevents frequent instantiation/destruction)
        /// </summary>
        private ObjectPool<TQ_HexBoardModel> _boardSnapshotPool;

        /// <summary>
        /// Hexagon side configuration (matches global board settings)
        /// </summary>
        private int _hexagonSide = 4;

        /// <summary>
        /// Triangle layer configuration (matches global board settings)
        /// </summary>
        private int _triangleLayers = 4;

        /// <summary>
        /// Initializes the board state service with global board reference
        /// </summary>
        /// <param name="globalBoard">Reference to the main game board model</param>
        public void Init(TQ_HexBoardModel globalBoard)
        {
            _globalBoardModel = globalBoard;

            // Initialize object pool (optimizes memory usage)
            _boardSnapshotPool = new ObjectPool<TQ_HexBoardModel>(
                createFunc: () => new TQ_HexBoardModel(),
                actionOnGet: snapshot => snapshot.ResetAllCellStates(),
                actionOnRelease: snapshot => snapshot.ResetAllCellStates(),
                defaultCapacity: 10
            );
        }

        /// <summary>
        /// Creates a deep copy snapshot of the global board state
        /// </summary>
        /// <returns>Deep copy of global board model (from object pool)</returns>
        public TQ_HexBoardModel CreateBoardSnapshot()
        {
            // Validation: Ensure global board is initialized
            if (_globalBoardModel == null)
            {
                Debug.LogError("TQ_BoardStateService: Global board model not initialized!");
                return null;
            }

            // Get snapshot from pool and copy state
            var snapshot = _boardSnapshotPool.Get();
            CopyBoardState(_globalBoardModel, snapshot);
            return snapshot;
        }

        /// <summary>
        /// Releases a board snapshot back to the object pool
        /// </summary>
        /// <param name="snapshot">Board snapshot to release</param>
        public void ReleaseBoardSnapshot(TQ_HexBoardModel snapshot)
        {
            // Release only if snapshot is not null
            if (snapshot != null)
            {
                _boardSnapshotPool.Release(snapshot);
            }
        }

        /// <summary>
        /// Performs deep copy of board state (core functionality)
        /// Ensures snapshot is completely isolated from source board
        /// </summary>
        /// <param name="source">Source board to copy from (global board)</param>
        /// <param name="target">Target board to copy to (snapshot)</param>
        private void CopyBoardState(TQ_HexBoardModel source, TQ_HexBoardModel target)
        {
            // 1. Clear target board (basic preparation)
            target.Cells.Clear();
            target.PlayerPieces.Clear();
            target.EnemyPieces.Clear();
            target.ResetAllCellStates();

            DebugLog("=== Step 1: Deep copy all cell base properties ===");
            int cellCount = 0;

            // Copy each cell's base properties
            foreach (var sourceCellKvp in source.Cells)
            {
                var sourceCell = sourceCellKvp.Value;

                // Create deep copy of cell with all base properties
                var targetCell = new TQ_HexCellModel(sourceCell.Q, sourceCell.R)
                {
                    CellType = sourceCell.CellType,
                    IsOccupied = sourceCell.IsOccupied,
                    IsHighlighted = sourceCell.IsHighlighted,
                    IsValidMoveTarget = sourceCell.IsValidMoveTarget,
                    //IsBlocked = sourceCell.IsBlocked, // Additional: Blocked state
                    CurrentPiece = null, // Null temporarily, will link pieces later
                    // Additional: Other custom cell properties (camp markers, effect states, etc.)
                };

                target.AddCell(targetCell);
                cellCount++;
            }
            DebugLog($"✅ Cell base properties copied: {cellCount} cells total");

            // 2. Reuse Factory logic to recalculate directional lines (critical: match source board logic)
            DebugLog("=== Step 2: Reuse Factory to calculate directional lines ===");
            var factory = new HexBoardFactory(_hexagonSide, _triangleLayers);
            factory.PrecomputeDirectionalLines(target); // Call Factory's public method directly
            DebugLog("✅ Directional lines precomputed");

            // 3. Deep copy all pieces + link to cells
            DebugLog("=== Step 3: Deep copy pieces and link to cells ===");
            CopyPieceList(source.PlayerPieces, target.PlayerPieces, target, TQ_PieceOwner.Player);
            CopyPieceList(source.EnemyPieces, target.EnemyPieces, target, TQ_PieceOwner.Enemy);
            DebugLog($"✅ Pieces copied: Player {target.PlayerPieces.Count} | Enemy {target.EnemyPieces.Count}");

            // 4. Validate copy integrity (optional: for debugging)
            ValidateSnapshotCopy(source, target);
        }

        /// <summary>
        /// Validates snapshot copy integrity (debug utility)
        /// Verifies all pieces and cells are properly copied
        /// </summary>
        /// <param name="source">Source board (global)</param>
        /// <param name="target">Target board (snapshot)</param>
        private void ValidateSnapshotCopy(TQ_HexBoardModel source, TQ_HexBoardModel target)
        {
            // Validate enemy pieces
            for (int i = 0; i < source.EnemyPieces.Count; i++)
            {
                var sourcePiece = source.EnemyPieces[i];

                // Skip null pieces/cells
                if (sourcePiece == null || sourcePiece.CurrentCell == null) continue;

                // Find corresponding piece in snapshot
                var targetPiece = target.EnemyPieces.FirstOrDefault(p =>
                    p != null && p.CurrentCell != null &&
                    p.CurrentCell.Q == sourcePiece.CurrentCell.Q &&
                    p.CurrentCell.R == sourcePiece.CurrentCell.R);

                // Log validation results
                if (targetPiece == null)
                {
                    Debug.LogError($"❌ Snapshot copy failed: Cannot find copy of source enemy piece ({sourcePiece.CurrentCell.Q},{sourcePiece.CurrentCell.R})");
                }
                else
                {
                    DebugLog($"✅ Snapshot copy successful: Source enemy piece ({sourcePiece.CurrentCell.Q},{sourcePiece.CurrentCell.R}) → Target enemy piece ({targetPiece.CurrentCell.Q},{targetPiece.CurrentCell.R})");
                }
            }

            // Validate cells
            foreach (var sourceCell in source.Cells.Values)
            {
                var targetCell = target.GetCellByCoordinates(sourceCell.Q, sourceCell.R);

                if (targetCell == null)
                {
                    Debug.LogError($"❌ Snapshot copy failed: Cannot find copy of source cell ({sourceCell.Q},{sourceCell.R})");
                }
            }
        }

        /// <summary>
        /// Copies piece list and links to snapshot cells
        /// </summary>
        /// <param name="sourceList">Source piece list (player/enemy)</param>
        /// <param name="targetList">Target piece list in snapshot</param>
        /// <param name="targetBoard">Target snapshot board</param>
        /// <param name="owner">Piece owner (Player/Enemy)</param>
        private void CopyPieceList(List<TQ_ChessPieceModel> sourceList, List<TQ_ChessPieceModel> targetList,
                                  TQ_HexBoardModel targetBoard, TQ_PieceOwner owner)
        {
            // Copy each piece in the list
            foreach (var sourcePiece in sourceList)
            {
                // Skip null pieces or pieces without cells
                if (sourcePiece?.CurrentCell == null) continue;

                // Create deep copy of piece
                var targetPiece = new TQ_ChessPieceModel(owner)
                {
                    IsSelected = sourcePiece.IsSelected
                };

                // Link to corresponding cell in snapshot
                if (targetBoard.Cells.TryGetValue(new Vector2Int(sourcePiece.CurrentCell.Q, sourcePiece.CurrentCell.R),
                    out var targetCell))
                {
                    targetPiece.CurrentCell = targetCell;
                    targetCell.CurrentPiece = targetPiece;
                    targetCell.IsOccupied = true;
                }

                // Copy valid moves (coordinates only, no deep copy of cells)
                foreach (var sourceValidCell in sourcePiece.ValidMoves)
                {
                    if (targetBoard.Cells.TryGetValue(new Vector2Int(sourceValidCell.Q, sourceValidCell.R),
                        out var targetValidCell))
                    {
                        targetPiece.ValidMoves.Add(targetValidCell);
                    }
                }

                // Add copied piece to target list
                targetList.Add(targetPiece);
            }
        }

        /// <summary>
        /// Simple object pool implementation (memory optimization)
        /// Reuses objects to avoid GC overhead from frequent instantiation
        /// </summary>
        /// <typeparam name="T">Type of object to pool (must have parameterless constructor)</typeparam>
        public class ObjectPool<T> where T : new()
        {
            /// <summary>
            /// Factory function to create new objects when pool is empty
            /// </summary>
            private readonly System.Func<T> _createFunc;

            /// <summary>
            /// Action to execute when object is retrieved from pool
            /// </summary>
            private readonly System.Action<T> _actionOnGet;

            /// <summary>
            /// Action to execute when object is released back to pool
            /// </summary>
            private readonly System.Action<T> _actionOnRelease;

            /// <summary>
            /// Queue storing pooled objects
            /// </summary>
            private readonly Queue<T> _pool = new Queue<T>();

            /// <summary>
            /// Initializes object pool with custom configuration
            /// </summary>
            /// <param name="createFunc">Custom creation function (optional)</param>
            /// <param name="actionOnGet">Action on object retrieval (optional)</param>
            /// <param name="actionOnRelease">Action on object release (optional)</param>
            /// <param name="defaultCapacity">Initial pool size (pre-created objects)</param>
            public ObjectPool(System.Func<T> createFunc, System.Action<T> actionOnGet = null,
                              System.Action<T> actionOnRelease = null, int defaultCapacity = 0)
            {
                // Set default creation function if not provided
                _createFunc = createFunc ?? (() => new T());
                _actionOnGet = actionOnGet;
                _actionOnRelease = actionOnRelease;

                // Pre-create objects to fill default capacity
                for (int i = 0; i < defaultCapacity; i++)
                {
                    _pool.Enqueue(_createFunc());
                }
            }

            /// <summary>
            /// Retrieves an object from the pool (creates new if empty)
            /// </summary>
            /// <returns>Pooled object (reset/initialized)</returns>
            public T Get()
            {
                // Get from pool or create new
                T item = _pool.Count > 0 ? _pool.Dequeue() : _createFunc();

                // Execute retrieval action
                _actionOnGet?.Invoke(item);
                return item;
            }

            /// <summary>
            /// Releases an object back to the pool
            /// </summary>
            /// <param name="item">Object to return to pool</param>
            public void Release(T item)
            {
                // Execute release action (reset/cleanup)
                _actionOnRelease?.Invoke(item);

                // Return to pool
                _pool.Enqueue(item);
            }

            /// <summary>
            /// Current count of objects in the pool
            /// </summary>
            public int Count => _pool.Count;
        }
    }
}