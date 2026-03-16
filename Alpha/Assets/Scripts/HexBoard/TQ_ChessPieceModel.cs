using System.Collections.Generic;
using System.Linq;

namespace Free.Checkers
{

// 棋子数据模型（实现通用接口）
    public class TQ_ChessPieceModel : ITQ_ChessPiece
    {
        public TQ_PieceOwner Owner { get; }
        public TQ_HexCellModel CurrentCell { get; set; }
        public bool IsSelected { get; set; }
        public List<TQ_HexCellModel> ValidMoves { get; private set; }

        // 接口实现（显式接口）
        TQ_PieceOwner ITQ_ChessPiece.Owner => (TQ_PieceOwner)Owner;
        ITQ_HexCell ITQ_ChessPiece.CurrentCell
        {
            get => CurrentCell;
            set => CurrentCell = (TQ_HexCellModel)value;
        }
        List<ITQ_HexCell> ITQ_ChessPiece.ValidMoves => ValidMoves.Cast<ITQ_HexCell>().ToList();

        public TQ_ChessPieceModel(TQ_PieceOwner owner)
        {
            Owner = owner;
            ValidMoves = new List<TQ_HexCellModel>();
        }

        // 接口实现：清空有效移动
        void ITQ_ChessPiece.ClearValidMoves()
        {
            ValidMoves.Clear();
        }

        // 接口实现：标记有效移动
        void ITQ_ChessPiece.MarkValidMoves(List<ITQ_HexCell> validCells)
        {
            ValidMoves.Clear();
            ValidMoves.AddRange(validCells.Cast<TQ_HexCellModel>());
            foreach (var cell in validCells)
            {
                cell.IsValidMoveTarget = true;
                cell.IsHighlighted = true;
            }
        }

        public void MarkValidMoves(List<ITQ_HexCell> validCells)
        {
            (this as ITQ_ChessPiece).MarkValidMoves(validCells);
        }

        // 原有具体逻辑（保留）
        public void ClearValidMoves()
        {
            ValidMoves.Clear();
        }
    }
}
