using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using InstantRender.Infrastructure;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(PluginApp))]

namespace InstantRender.Infrastructure;

/// <summary>
/// Plugin entry point. AutoCAD calls <see cref="Initialize"/> when the DLL is
/// NETLOADed (or auto-loaded). It adds the "Instant Render" ribbon tab/button
/// that runs the INSTANTRENDER command.
/// </summary>
public sealed class PluginApp : IExtensionApplication
{
    private const string TabId = "INSTANTRENDER_TAB";

    public void Initialize()
    {
        // The ribbon may not exist yet at load time; hook idle once to be safe.
        if (ComponentManager.Ribbon is not null)
            BuildRibbon();
        else
            AcadApp.Idle += OnIdleBuildRibbon;
    }

    public void Terminate() { }

    private void OnIdleBuildRibbon(object? sender, EventArgs e)
    {
        if (ComponentManager.Ribbon is null) return;
        AcadApp.Idle -= OnIdleBuildRibbon;
        BuildRibbon();
    }

    private static void BuildRibbon()
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon.Tabs.Any(t => t.Id == TabId)) return; // already added

        var tab = new RibbonTab { Title = "Instant Render", Id = TabId };
        ribbon.Tabs.Add(tab);

        var source = new RibbonPanelSource { Title = "Render" };
        var panel = new RibbonPanel { Source = source };
        tab.Panels.Add(panel);

        var button = new RibbonButton
        {
            Text = "Instant\nRender",
            ShowText = true,
            ShowImage = true,
            Size = RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            // Runs the registered command. The trailing space submits it.
            CommandHandler = new RelayCommand(() =>
                AcadApp.DocumentManager.MdiActiveDocument?
                    .SendStringToExecute("INSTANTRENDER ", true, false, true)),
            ToolTip = new RibbonToolTip
            {
                Title = "Instant Render",
                Content = "Convert the current floor plan into a rendered image via Gemini."
            }
        };

        source.Items.Add(button);
        Log.Info("Ribbon button added (Instant Render tab).");
    }
}

/// <summary>Tiny ICommand wrapper so a ribbon button can run a delegate.</summary>
internal sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;
    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}
