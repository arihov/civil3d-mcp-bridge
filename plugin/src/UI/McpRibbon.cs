using System;
using System.Linq;
using System.Windows.Controls;
using Autodesk.Windows;

namespace Civil3DMcpBridge.UI;

/// <summary>
/// Adds a small "Claude MCP" ribbon panel to AutoCAD/Civil 3D's Add-ins tab.
/// Buttons fire the same MCPSHOW / MCPRESTART / MCPCOPYTOOLS / MCPMANUAL
/// commands that the C3D command line exposes.
/// </summary>
internal static class McpRibbon
{
    private const string PanelTitle = "Claude MCP";
    private const string PreferredTabName = "Add-ins"; // English UI; fall back if missing.

    private static RibbonPanel? _panel;
    private static bool _hooked;

    /// <summary>
    /// If the ribbon already exists, build immediately; otherwise wait for
    /// its ItemInitialized signal. Safe to call multiple times.
    /// </summary>
    public static void RegisterDeferred()
    {
        if (_hooked) return;
        _hooked = true;

        if (ComponentManager.Ribbon != null)
        {
            TryBuild();
        }
        else
        {
            ComponentManager.ItemInitialized += OnComponentInitialized;
        }
    }

    public static void Unregister()
    {
        try { ComponentManager.ItemInitialized -= OnComponentInitialized; } catch { }
        var ribbon = ComponentManager.Ribbon;
        if (ribbon != null && _panel != null)
        {
            foreach (var tab in ribbon.Tabs)
            {
                if (tab.Panels.Contains(_panel)) tab.Panels.Remove(_panel);
            }
        }
        _panel = null;
        _hooked = false;
    }

    private static void OnComponentInitialized(object? sender, RibbonItemEventArgs e)
    {
        if (ComponentManager.Ribbon == null) return;
        ComponentManager.ItemInitialized -= OnComponentInitialized;
        TryBuild();
    }

    private static void TryBuild()
    {
        try { Build(); }
        catch (System.Exception ex)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage($"\n[Civil3DMcpBridge] ribbon build failed: {ex.Message}");
        }
    }

    private static void Build()
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon == null) return;

        // Find the Add-ins tab; if missing, just attach to the first tab so
        // the panel is always reachable.
        var tab = ribbon.Tabs.FirstOrDefault(t =>
            string.Equals(t.Title, PreferredTabName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Id, "ID_TabAddIns", StringComparison.OrdinalIgnoreCase));
        if (tab == null && ribbon.Tabs.Count > 0) tab = ribbon.Tabs.First();
        if (tab == null) return;

        // If we already added the panel, skip — workspace switches sometimes
        // re-fire ItemInitialized.
        if (tab.Panels.Any(p => p.Source?.Title == PanelTitle))
        {
            _panel = tab.Panels.First(p => p.Source?.Title == PanelTitle);
            return;
        }

        var src = new RibbonPanelSource { Title = PanelTitle };

        var row = new RibbonRowPanel();
        row.Items.Add(MakeButton("Status", "Show / hide the Claude MCP palette", "MCPSHOW"));
        row.Items.Add(new RibbonRowBreak());
        row.Items.Add(MakeButton("Restart", "Restart the MCP HTTP bridge", "MCPRESTART"));
        row.Items.Add(new RibbonRowBreak());
        row.Items.Add(MakeButton("Copy tools", "Copy all tool names to the clipboard", "MCPCOPYTOOLS"));
        row.Items.Add(new RibbonRowBreak());
        row.Items.Add(MakeButton("Manual", "Open the bundled USER_MANUAL.pdf", "MCPMANUAL"));
        src.Items.Add(row);

        _panel = new RibbonPanel { Source = src };
        tab.Panels.Add(_panel);
    }

    private static RibbonButton MakeButton(string text, string tooltip, string command)
    {
        var b = new RibbonButton
        {
            Text = text,
            ShowText = true,
            ShowImage = false,
            Size = RibbonItemSize.Standard,
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            CommandHandler = new RelayCommandHandler(command),
            ToolTip = tooltip,
        };
        return b;
    }

    private sealed class RelayCommandHandler : System.Windows.Input.ICommand
    {
        private readonly string _command;
        public RelayCommandHandler(string command) { _command = command; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                doc?.SendStringToExecute($"{_command} ", true, false, true);
            }
            catch { }
        }
    }
}
