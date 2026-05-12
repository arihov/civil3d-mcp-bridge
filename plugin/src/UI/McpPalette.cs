using System;
using Autodesk.AutoCAD.Windows;

namespace Civil3DMcpBridge.UI;

/// <summary>
/// Singleton dockable palette host. The palette is created lazily on
/// first <see cref="Show"/> call and re-used across the Civil 3D session.
/// </summary>
internal static class McpPalette
{
    // GUID is persisted by AutoCAD across sessions for dock position memory.
    private static readonly Guid PaletteGuid = new("2c8d4a6f-4f4f-4f4f-9aaa-c1v1l3dmcp001");

    private static PaletteSet? _set;
    private static McpPaletteControl? _control;

    public static void Show()
    {
        if (_set == null)
        {
            _control = new McpPaletteControl();
            _set = new PaletteSet("Claude MCP", PaletteGuid)
            {
                Size = new System.Drawing.Size(560, 420),
                MinimumSize = new System.Drawing.Size(380, 260),
                DockEnabled = (DockSides)((int)DockSides.Left + (int)DockSides.Right),
                Style = PaletteSetStyles.ShowAutoHideButton |
                        PaletteSetStyles.ShowCloseButton |
                        PaletteSetStyles.Snappable,
                KeepFocus = false,
            };
            _set.Add("Live", _control);

            // Push a refresh whenever a new invocation is recorded.
            InvocationLog.Changed += OnInvocationLogChanged;
        }
        _set.Visible = true;
        _control?.RefreshNow();
    }

    public static void Hide()
    {
        if (_set != null) _set.Visible = false;
    }

    public static void Toggle()
    {
        if (_set == null || !_set.Visible) Show();
        else Hide();
    }

    public static void Dispose()
    {
        InvocationLog.Changed -= OnInvocationLogChanged;
        _set?.Dispose();
        _set = null;
        _control = null;
    }

    private static void OnInvocationLogChanged()
    {
        // Marshal to the UI thread via the control's invoker.
        var c = _control;
        if (c == null || c.IsDisposed) return;
        try
        {
            if (c.InvokeRequired) c.BeginInvoke((Action)c.RefreshNow);
            else c.RefreshNow();
        }
        catch { /* ignore — palette may be torn down */ }
    }
}
