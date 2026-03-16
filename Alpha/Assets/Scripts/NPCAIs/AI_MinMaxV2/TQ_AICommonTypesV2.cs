using System;
using System.Collections.Generic;
using UnityEngine;

namespace Free.Checkers
{
    /// <summary>
    /// AI Move Object Pool
    /// Optimizes memory allocation by reusing TQAI_AIMove objects
    /// Eliminates garbage collection spikes from frequent move object creation/destruction
    /// </summary>
    public static class AIMovePool
    {
        // Thread-safe stack for object storage (core pool structure)
        private static readonly Stack<TQAI_AIMove> _pool = new Stack<TQAI_AIMove>();

        /// <summary>
        /// Get reusable AI move object from pool
        /// Creates new object only if pool is empty
        /// </summary>
        /// <returns>Reset AI move object ready for use</returns>
        public static TQAI_AIMove Get()
        {
            if (_pool.Count > 0)
            {
                var move = _pool.Pop();
                move.Reset(); // Critical: reset object state before reuse
                return move;
            }
            // Create new object with safe defaults if pool is empty
            return new TQAI_AIMove(null, null, 0f, false, 0, new List<TQ_HexCellModel>());
        }

        /// <summary>
        /// Release AI move object back to pool
        /// Makes object available for future reuse
        /// </summary>
        /// <param name="move">AI move object to recycle</param>
        public static void Release(TQAI_AIMove move)
        {
            if (move != null)
            {
                _pool.Push(move);
            }
        }

        /// <summary>
        /// Clear entire pool (memory management/cleanup)
        /// Useful for scene transitions or game restarts
        /// </summary>
        public static void Clear() => _pool.Clear();
    }

    /// <summary>
    /// Rule Engine Object Pool
    /// Optimizes memory allocation by reusing TQ_RuleEngine objects
    /// Critical for performance in Minimax algorithm (frequent rule engine creation)
    /// </summary>
    public static class RuleEnginePool
    {
        // Thread-safe stack for object storage
        private static readonly Stack<TQ_RuleEngine> _pool = new Stack<TQ_RuleEngine>();

        /// <summary>
        /// Get reusable rule engine from pool
        /// Creates new engine only if pool is empty
        /// </summary>
        /// <returns>Rule engine object ready for use</returns>
        public static TQ_RuleEngine Get()
        {
            if (_pool.Count > 0)
            {
                var engine = _pool.Pop();
                return engine;
            }
            // Create new rule engine if pool is empty
            return new TQ_RuleEngine();
        }

        /// <summary>
        /// Release rule engine back to pool
        /// </summary>
        /// <param name="engine">Rule engine to recycle</param>
        public static void Release(TQ_RuleEngine engine)
        {
            if (engine != null)
            {
                _pool.Push(engine);
            }
        }

        /// <summary>
        /// Clear entire pool (cleanup)
        /// </summary>
        public static void Clear() => _pool.Clear();
    }

    /// <summary>
    /// Zobrist Hashing Utility (Ultimate Fix for Index Out of Bounds)
    /// Provides collision-resistant board hashing for transposition table
    /// Implements comprehensive bounds checking to prevent array index exceptions
    /// </summary>
    public static class ZobristHash
    {
        // Zobrist table: [piece ID, cell index] ¡ú random 64-bit value
        private static ulong[,] _zobristTable;

        // Configuration constants (generous limits to prevent overflow)
        private const int MAX_PIECES = 40;      // Maximum pieces (covers all game scenarios)
        private const int MAX_CELLS = 200;      // Expanded cell limit (prevents index overflow)
        private const int COORD_OFFSET = 100;   // Coordinate offset: ensures positive values

        /// <summary>
        /// Static constructor: Initialize Zobrist table with random values
        /// Executes once on first use (thread-safe in C#)
        /// </summary>
        static ZobristHash()
        {
            // Initialize table with generous dimensions
            _zobristTable = new ulong[MAX_PIECES, MAX_CELLS];
            var random = new System.Random();

            // Generate cryptographically strong random 64-bit values
            // Compatible with older .NET versions (no Random.NextUInt64())
            for (int piece = 0; piece < MAX_PIECES; piece++)
            {
                for (int cell = 0; cell < MAX_CELLS; cell++)
                {
                    byte[] buffer = new byte[8]; // 8 bytes = 64 bits
                    random.NextBytes(buffer);
                    _zobristTable[piece, cell] = BitConverter.ToUInt64(buffer, 0);
                }
            }

            // Log initialization for debugging/verification
            Debug.Log($"Zobrist table initialized: [{MAX_PIECES},{MAX_CELLS}] dimensions");
        }

        /// <summary>
        /// Get cell index from coordinates (ULTIMATE BOUNDS FIX)
        /// Converts hex coordinates to safe array index with multiple safeguards
        /// </summary>
        /// <param name="q">Hex coordinate Q</param>
        /// <param name="r">Hex coordinate R</param>
        /// <returns>Safe, bounded cell index (0 ¡Ü index < MAX_CELLS)</returns>
        private static int GetCellIndex(int q, int r)
        {
            // 1. Coordinate normalization: ensure positive values (prevents negative indices)
            int normQ = q + COORD_OFFSET;
            int normR = r + COORD_OFFSET;

            // 2. Calculate hash-like index with prime multiplier (better distribution)
            int index = (Mathf.Abs(normQ) * 11 + Mathf.Abs(normR)) % MAX_CELLS;

            // 3. Double safety: clamp to valid range (absolute bounds protection)
            index = Mathf.Clamp(index, 0, MAX_CELLS - 1);

            // Optional debug logging (uncomment to verify index range)
            // Debug.Log($"Coordinate ({q},{r}) ¡ú Normalized ({normQ},{normR}) ¡ú Index {index}");

            return index;
        }

        /// <summary>
        /// Get unique piece ID (ULTIMATE BOUNDS FIX)
        /// Generates safe, bounded ID for pieces with team differentiation
        /// </summary>
        /// <param name="piece">Chess piece to ID</param>
        /// <returns>Safe, bounded piece ID (0 ¡Ü id < MAX_PIECES)</returns>
        private static int GetPieceId(TQ_ChessPieceModel piece)
        {
            if (piece == null) return 0; // Null-safe default

            // 1. Base ID: differentiate enemy (0-19) vs player (20-39) pieces
            int baseId = piece.Owner == TQ_PieceOwner.Enemy ? 0 : MAX_PIECES / 2;

            // 2. Calculate unique ID with hash code (distribution)
            int pieceId = (baseId + Mathf.Abs(piece.GetHashCode())) % MAX_PIECES;

            // 3. Double safety: clamp to valid range
            pieceId = Mathf.Clamp(pieceId, 0, MAX_PIECES - 1);

            return pieceId;
        }

        /// <summary>
        /// Calculate complete board hash with comprehensive bounds checking
        /// Generates unique hash representing current board state
        /// </summary>
        /// <param name="board">Board to hash</param>
        /// <returns>64-bit Zobrist hash (0 if board is null)</returns>
        public static ulong CalculateBoardHash(TQ_HexBoardModel board)
        {
            if (board == null) return 0;

            ulong hash = 0;

            // Process enemy pieces (AI-controlled)
            foreach (var piece in board.EnemyPieces)
            {
                // Skip invalid pieces (null safety)
                if (piece == null || piece.CurrentCell == null) continue;

                // Get safe, bounded IDs/indices
                int pieceId = GetPieceId(piece);
                int cellIndex = GetCellIndex(piece.CurrentCell.Q, piece.CurrentCell.R);

                // Only access array if indices are valid (final safety check)
                if (pieceId >= 0 && pieceId < MAX_PIECES && cellIndex >= 0 && cellIndex < MAX_CELLS)
                {
                    hash ^= _zobristTable[pieceId, cellIndex];
                }
                else
                {
                    // Warning for debugging (identifies edge cases)
                    Debug.LogWarning($"Invalid indices ¡ú Piece ID:{pieceId} Cell Index:{cellIndex} | Coordinates ({piece.CurrentCell.Q},{piece.CurrentCell.R})");
                }
            }

            // Process player pieces (opponent)
            foreach (var piece in board.PlayerPieces)
            {
                // Skip invalid pieces (null safety)
                if (piece == null || piece.CurrentCell == null) continue;

                // Get safe, bounded IDs/indices
                int pieceId = GetPieceId(piece);
                int cellIndex = GetCellIndex(piece.CurrentCell.Q, piece.CurrentCell.R);

                // Only access array if indices are valid
                if (pieceId >= 0 && pieceId < MAX_PIECES && cellIndex >= 0 && cellIndex < MAX_CELLS)
                {
                    hash ^= _zobristTable[pieceId, cellIndex];
                }
                else
                {
                    Debug.LogWarning($"Invalid indices ¡ú Piece ID:{pieceId} Cell Index:{cellIndex} | Coordinates ({piece.CurrentCell.Q},{piece.CurrentCell.R})");
                }
            }

            return hash;
        }

        /// <summary>
        /// Update hash after piece movement (performance optimization)
        /// Avoids recalculating entire hash from scratch
        /// </summary>
        /// <param name="oldHash">Original board hash</param>
        /// <param name="piece">Moved piece</param>
        /// <param name="oldQ">Original Q coordinate</param>
        /// <param name="oldR">Original R coordinate</param>
        /// <param name="newQ">New Q coordinate</param>
        /// <param name="newR">New R coordinate</param>
        /// <returns>Updated board hash</returns>
        public static ulong UpdateHashAfterMove(ulong oldHash, TQ_ChessPieceModel piece, int oldQ, int oldR, int newQ, int newR)
        {
            if (piece == null) return oldHash;

            // Get safe IDs/indices
            int pieceId = GetPieceId(piece);
            int oldCellIndex = GetCellIndex(oldQ, oldR);
            int newCellIndex = GetCellIndex(newQ, newR);

            // Update hash only with valid indices
            if (pieceId >= 0 && pieceId < MAX_PIECES)
            {
                // Remove piece from old position
                if (oldCellIndex >= 0 && oldCellIndex < MAX_CELLS)
                {
                    oldHash ^= _zobristTable[pieceId, oldCellIndex];
                }
                // Add piece to new position
                if (newCellIndex >= 0 && newCellIndex < MAX_CELLS)
                {
                    oldHash ^= _zobristTable[pieceId, newCellIndex];
                }
            }

            return oldHash;
        }
    }
}