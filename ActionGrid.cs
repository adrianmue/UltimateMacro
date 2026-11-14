namespace UltimateMacro;

internal sealed class ActionGrid : DataGridView
{
    private const string DragFormat = "UltimateMacro.ActionGrid.Reorder";
    private readonly System.Windows.Forms.Timer scrollTimer = new() { Interval = 95 };
    private MacroAction[]? pending;
    private MacroAction[]? dragging;
    private Rectangle dragThreshold;
    private int insertion = -1;

    internal bool ReorderInProgress => dragging is not null;
    internal event Action<MacroAction[], int>? MoveRequested;

    internal ActionGrid()
    {
        AllowDrop = true;
        scrollTimer.Tick += (_, _) => ScrollDuringDrag();
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        pending = null;
        if (args.Button == MouseButtons.Left && args.Clicks == 1 && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
        {
            var hit = HitTest(args.X, args.Y);
            if (hit.RowIndex >= 0 && hit.Type is DataGridViewHitTestType.Cell or DataGridViewHitTestType.RowHeader)
            {
                var row = Rows[hit.RowIndex];
                if (row.DataBoundItem is MacroAction action)
                {
                    pending = row.Selected ? SelectedRows.Cast<DataGridViewRow>().OrderBy(selected => selected.Index)
                        .Select(selected => selected.DataBoundItem).OfType<MacroAction>().ToArray() : [action];
                    var threshold = SystemInformation.DragSize;
                    dragThreshold = new Rectangle(args.X - threshold.Width / 2, args.Y - threshold.Height / 2, threshold.Width, threshold.Height);
                }
            }
        }
        base.OnMouseDown(args);
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        if (pending is not { Length: > 0 } actions || (args.Button & MouseButtons.Left) == 0 || dragThreshold.Contains(args.Location))
        {
            base.OnMouseMove(args);
            return;
        }

        pending = null;
        if (Rows.Count < 2 || !Enabled) return;
        dragging = actions;
        try
        {
            var data = new DataObject();
            data.SetData(DragFormat, false, "move");
            DoDragDrop(data, DragDropEffects.Move);
        }
        finally
        {
            dragging = null;
            EndHover();
        }
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        pending = null;
        base.OnMouseUp(args);
    }

    protected override void OnDragEnter(DragEventArgs args) => UpdateDrop(args);
    protected override void OnDragOver(DragEventArgs args) => UpdateDrop(args);
    protected override void OnDragLeave(EventArgs args) => EndHover();

    protected override void OnDragDrop(DragEventArgs args)
    {
        var destination = DropIndex(args);
        args.Effect = destination >= 0 ? DragDropEffects.Move : DragDropEffects.None;
        EndHover();
        if (destination >= 0 && dragging is { } actions) MoveRequested?.Invoke(actions, destination);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        if (insertion < 0 || Rows.Count == 0 || insertion > Rows.Count) return;
        var afterLast = insertion == Rows.Count;
        var bounds = GetRowDisplayRectangle(afterLast ? Rows.Count - 1 : insertion, true);
        if (bounds.Height <= 0) return;
        var lineY = Math.Clamp(afterLast ? bounds.Bottom : bounds.Top, ColumnHeadersHeight, Math.Max(ColumnHeadersHeight, ClientSize.Height - 2));
        var palette = FindForm() is { } form ? UiTheme.Current(form) : UiTheme.Light;
        using var line = new Pen(palette.AccentText, 3);
        var right = Controls.OfType<VScrollBar>().FirstOrDefault(bar => bar.Visible)?.Left ?? ClientSize.Width;
        args.Graphics.DrawLine(line, 4, lineY, Math.Max(4, right - 5), lineY);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) scrollTimer.Dispose();
        base.Dispose(disposing);
    }

    internal int InsertionAt(Point point)
    {
        if (Rows.Count == 0 || !ClientRectangle.Contains(point) || point.Y < ColumnHeadersHeight) return -1;
        var hit = HitTest(point.X, point.Y);
        if (hit.Type is DataGridViewHitTestType.VerticalScrollBar or DataGridViewHitTestType.HorizontalScrollBar) return -1;
        if (hit.RowIndex >= 0)
        {
            var row = GetRowDisplayRectangle(hit.RowIndex, false);
            return point.Y < row.Top + row.Height / 2 ? hit.RowIndex : hit.RowIndex + 1;
        }

        var last = GetRowDisplayRectangle(Rows.Count - 1, true);
        return last.Height > 0 && point.Y >= last.Bottom ? Rows.Count : -1;
    }

    private int DropIndex(DragEventArgs args) => dragging is not null && Enabled && (args.AllowedEffect & DragDropEffects.Move) != 0 &&
        args.Data?.GetDataPresent(DragFormat, false) == true ? InsertionAt(PointToClient(new Point(args.X, args.Y))) : -1;

    private void UpdateDrop(DragEventArgs args)
    {
        var destination = DropIndex(args);
        args.Effect = destination >= 0 ? DragDropEffects.Move : DragDropEffects.None;
        SetInsertion(destination);
        if (destination >= 0) scrollTimer.Start();
        else scrollTimer.Stop();
    }

    private void EndHover()
    {
        scrollTimer.Stop();
        SetInsertion(-1);
    }

    private void SetInsertion(int value)
    {
        if (insertion == value) return;
        insertion = value;
        Invalidate();
    }

    private void ScrollDuringDrag()
    {
        var point = PointToClient(Cursor.Position);
        var first = FirstDisplayedScrollingRowIndex;
        if (dragging is null || !Enabled || first < 0 || InsertionAt(point) < 0) { EndHover(); return; }
        var destination = first;
        if (point.Y < ColumnHeadersHeight + 24 && first > 0) destination--;
        else if (point.Y >= ClientSize.Height - 24 && first + 1 < Rows.Count &&
            (!Rows[Rows.Count - 1].Displayed || GetRowDisplayRectangle(Rows.Count - 1, false).Bottom > ClientSize.Height)) destination++;
        if (destination == first) return;
        FirstDisplayedScrollingRowIndex = destination;
        SetInsertion(InsertionAt(point));
        Invalidate();
    }
}

internal static class ActionOrder
{
    internal static List<MacroAction>? Move(IReadOnlyList<MacroAction> actions, IReadOnlyCollection<MacroAction> selection, int insertion, out int destination)
    {
        destination = 0;
        if (insertion < 0 || insertion > actions.Count || selection.Count == 0) return null;

        var selected = new HashSet<MacroAction>(selection, ReferenceEqualityComparer.Instance);
        var remaining = new List<MacroAction>(actions.Count);
        var moved = new List<MacroAction>(selected.Count);
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            if (selected.Contains(action)) moved.Add(action);
            else
            {
                remaining.Add(action);
                if (index < insertion) destination++;
            }
        }

        if (moved.Count != selected.Count) return null;
        remaining.InsertRange(destination, moved);
        return actions.SequenceEqual(remaining, ReferenceEqualityComparer.Instance) ? null : remaining;
    }
}
