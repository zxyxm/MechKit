using System;
using System.Drawing;
using System.Windows.Forms;

namespace MechKit.UI
{
    internal sealed partial class PartListForm
    {
        private object _highlightedRowIdentity;

        private object HighlightIdentity(DataGridViewRow row)
        {
            // Data identity follows sorting/filtering without selecting extra cells.
            var node = row.Cells["designTree"].Tag as TreeNode;
            return row.Tag ?? (node == null ? (object)row : node.Tag ?? node);
        }

        private void RememberHighlightedRow(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            var identity = HighlightIdentity(_grid.Rows[e.RowIndex]);
            if (ReferenceEquals(identity, _highlightedRowIdentity)) return;
            _highlightedRowIdentity = identity;
            _grid.Invalidate();
        }

        private void PaintHighlightedRow(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            if (_highlightedRowIdentity == null ||
                !ReferenceEquals(_highlightedRowIdentity, HighlightIdentity(_grid.Rows[e.RowIndex]))) return;
            var bounds = Rectangle.Intersect(e.RowBounds, _grid.ClientRectangle);
            bounds = Rectangle.Intersect(bounds, e.ClipBounds);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var tint = new SolidBrush(Color.FromArgb(32, Theme.Accent)))
                e.Graphics.FillRectangle(tint, bounds);
            using (var pen = new Pen(Theme.Accent, 1))
            {
                e.Graphics.DrawLine(pen, bounds.Left, e.RowBounds.Top, bounds.Right - 1, e.RowBounds.Top);
                e.Graphics.DrawLine(pen, bounds.Left, e.RowBounds.Bottom - 1, bounds.Right - 1, e.RowBounds.Bottom - 1);
            }
        }
    }
}
