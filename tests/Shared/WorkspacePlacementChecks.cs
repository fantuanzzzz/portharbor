using System.Drawing;
using PortHarbor;

namespace PortHarbor.Checks;

internal static class WorkspacePlacementChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var primary = new Rectangle(0, 0, 1920, 1040);
        var minimum = new Size(850, 560);
        var saved = new WorkspaceLayout { X = 2100, Y = 100, Width = 1000, Height = 700, HasPosition = true };
        check(WorkspacePlacement.Restore(saved, [primary], 96, minimum) == new Rectangle(920, 100, 1000, 700),
            "a removed monitor restores the entire window inside an available work area");
        var secondary = new Rectangle(-1600, -200, 1600, 900);
        check(WorkspacePlacement.Restore(saved with { X = -1500, Y = -100 }, [primary, secondary], 96, minimum) == new Rectangle(-1500, -100, 1000, 700),
            "negative monitor coordinates remain valid while the window stays on its original screen");
        check(WorkspacePlacement.Restore(saved with { X = 200, Width = 5000, Height = 4000 }, [primary], 96, minimum) == primary,
            "oversized saved windows shrink to the work area without covering the taskbar");
        var tiny = new Rectangle(0, 0, 800, 480);
        check(WorkspacePlacement.Restore(new(), [tiny], 96, minimum) == tiny,
            "a screen smaller than the normal minimum still keeps the full window accessible");
        foreach (int dpi in new[] { 96, 120, 144, 192 })
        {
            var rect = WorkspacePlacement.Restore(new WorkspaceLayout { Width = 900, Height = 600 }, [new(0, 0, 4000, 3000)], dpi, minimum);
            check(rect.Width == (int)Math.Round(900 * dpi / 96d) && rect.Height == (int)Math.Round(600 * dpi / 96d),
                $"workspace size restores at {dpi} DPI using the saved DPI scale");
        }
        check(WorkspacePlacement.Restore(new WorkspaceLayout { Width = 1800, Height = 1200, Dpi = 192 }, [primary], 96, minimum).Size == new Size(900, 600),
            "moving from a high DPI setup restores logical window size on a lower DPI screen");
        check(WorkspacePlacement.Restore(new(), [primary], 96, minimum).Location == new Point(335, 110),
            "the first workspace opens centered in the primary work area");
    }
}
