namespace UltimateMacro;

internal static partial class SelfTests
{
    private static void TestActionOrder()
    {
        MacroAction[] original = [new() { Key = "A" }, new() { Key = "B" }, new() { Key = "C" }, new() { Key = "D" }, new() { Key = "E" }];
        var toEnd = ActionOrder.Move(original, [original[1]], original.Length, out var endIndex);
        Check(toEnd is not null && endIndex == 4 && toEnd.SequenceEqual([original[0], original[2], original[3], original[4], original[1]]),
            "Moving a row down to the end adjusts for the removed source row");
        var toTop = ActionOrder.Move(original, [original[3], original[1]], 0, out var startIndex);
        Check(toTop is not null && startIndex == 0 && toTop.SequenceEqual([original[1], original[3], original[0], original[2], original[4]]),
            "Dragging multiple selected rows retains their original relative order and contents");
        Check(ActionOrder.Move(original, [original[1]], 2, out _) is null &&
            ActionOrder.Move(original, [new MacroAction { Key = "B" }], 0, out _) is null,
            "Dropping in the same position or with a stale selection does not change the action list");
    }

    private static async Task TestLiveActionDrag(MainForm form, ActionGrid grid, IReadOnlyList<MacroAction> list)
    {
        await FocusVerificationWindow(form, grid);
        var window = form.Handle;
        var first = grid.GetRowDisplayRectangle(0, false);
        var last = grid.GetRowDisplayRectangle(grid.Rows.Count - 1, false);
        var start = grid.PointToScreen(new Point(80, first.Top + first.Height / 2));
        var finish = grid.PointToScreen(new Point(80, last.Bottom - 5));
        var previous = Cursor.Position;
        var original = list.ToArray();
        var lastDropPoint = finish;

        void CheckTarget(Point point)
        {
            var hit = Native.WindowFromPoint(new Native.Point { X = point.X, Y = point.Y });
            if (Native.GetForegroundWindow() != window || Native.GetAncestor(hit, 2) != window)
                throw new InvalidOperationException("The drag test stopped because its own window lost focus or was covered.");
        }

        async Task Drag(Point source, Point target)
        {
            lastDropPoint = target;
            var threshold = new Point(source.X + SystemInformation.DragSize.Width + 4, source.Y);
            await Task.Run(async () =>
            {
                var mouse = new WindowsInputSink();
                CheckTarget(source);
                mouse.Move(source.X, source.Y);
                await Task.Delay(75);
                CheckTarget(Cursor.Position);
                mouse.Button("Left", true);
                try
                {
                    await Task.Delay(70);
                    CheckTarget(threshold);
                    mouse.Move(threshold.X, threshold.Y);
                    await Task.Delay(100);
                    CheckTarget(target);
                    mouse.Move(target.X, target.Y);
                    await Task.Delay(120);
                    CheckTarget(Cursor.Position);
                }
                finally { mouse.Button("Left", false); }
            });
            await Task.Delay(100);
        }

        try
        {
            await Drag(start, finish);
            Check(list.SequenceEqual(original.Skip(1).Append(original[0])),
                $"A real drag in the isolated test window moves the row to the insertion line (order: {string.Join(",", list.Select(action => Array.IndexOf(original, action)))})");
            Check(grid.CurrentRow?.Index == original.Length - 1, "The dragged row stays selected at its new position");
            var reordered = list.ToArray();
            grid.ClearSelection();
            grid.Rows[1].Selected = true;
            grid.Rows[3].Selected = true;
            var sourceRow = grid.GetRowDisplayRectangle(1, false);
            var topRow = grid.GetRowDisplayRectangle(0, false);
            await Drag(grid.PointToScreen(new Point(80, sourceRow.Top + sourceRow.Height / 2)), grid.PointToScreen(new Point(80, topRow.Top + 4)));
            Check(list.SequenceEqual([reordered[1], reordered[3], reordered[0], reordered[2], reordered[4]]) && grid.SelectedRows.Count == 2,
                "A real drag moves a non-contiguous multi-selection together and preserves its order");
        }
        finally
        {
            if (Native.GetForegroundWindow() == window && Math.Abs(Cursor.Position.X - lastDropPoint.X) <= 1 && Math.Abs(Cursor.Position.Y - lastDropPoint.Y) <= 1)
                Cursor.Position = previous;
        }
    }
}
