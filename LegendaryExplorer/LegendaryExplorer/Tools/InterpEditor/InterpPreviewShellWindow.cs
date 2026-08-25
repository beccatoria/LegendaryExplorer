using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LegendaryExplorer.Misc;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore.Dialogue;
using MessageBox = Xceed.Wpf.Toolkit.MessageBox;

namespace LegendaryExplorer.Tools.InterpEditor;

public class InterpPreviewShellWindow : NotifyPropertyChangedWindowBase, IActorEditorContext
{
    private readonly SceneRenderControl _sceneViewer;
    private readonly TextBlock _statusTextBlock;
    private readonly ListBox _diagnosticsListBox;
    private readonly IInterpPreviewRuntime _runtime;
    private readonly ObservableCollection<string> _diagnostics = [];

    public LevelEditorRenderContext RenderContext { get; }
    public bool IsApplyingUndoRedo => false;

    public InterpPreviewShellWindow()
    {
        Title = "Interp Preview (M1 Experimental)";
        Width = 1400;
        Height = 820;
        MinWidth = 900;
        MinHeight = 500;

        RenderContext = new LevelEditorRenderContext(readOnly: true)
        {
            ShowLights = true,
            ShowVolumes = true,
            ShowEmitters = true,
            ShowLocationActors = true,
            ShowSoundPositions = true,
            ShowCinematicActors = true,
            ShowDecalActors = true,
            ShowStageNodes = true,
            ShowStageCameras = true
        };

        _sceneViewer = new SceneRenderControl { Context = RenderContext };
        IInterpPreviewSession session = new InterpPreviewSession();
        _runtime = new InterpPreviewRuntime(
            session,
            new InterpPreviewRenderCoordinator(),
            new InterpPreviewLoadCoordinator(new InterpPreviewLevelLoader(), session));
        _runtime.DiagnosticsChanged += Runtime_DiagnosticsChanged;

        _diagnosticsListBox = new ListBox
        {
            ItemsSource = _diagnostics,
            MinWidth = 320
        };

        RefreshDiagnostics();
        _sceneViewer.Loaded += SceneViewer_Loaded;
        _sceneViewer.Unloaded += SceneViewer_Unloaded;
        _statusTextBlock = new TextBlock { Text = "Ready. Loaded levels: 0." };
        Content = BuildLayout();
        Closing += OnClosing;
    }

    private void SceneViewer_Loaded(object sender, RoutedEventArgs e)
    {
        _runtime.Attach(RenderContext);
    }

    private void SceneViewer_Unloaded(object sender, RoutedEventArgs e)
    {
        _runtime.Detach(RenderContext);
    }

    private System.Windows.UIElement BuildLayout()
    {
        var root = new DockPanel();

        var diagnosticsHeader = new TextBlock
        {
            Text = "Diagnostics",
            Margin = new Thickness(4, 2, 4, 4),
            FontWeight = FontWeights.SemiBold
        };

        var diagnosticsPanel = new DockPanel
        {
            LastChildFill = true,
            Width = 360
        };
        DockPanel.SetDock(diagnosticsPanel, Dock.Right);
        DockPanel.SetDock(diagnosticsHeader, Dock.Top);
        diagnosticsPanel.Children.Add(diagnosticsHeader);
        diagnosticsPanel.Children.Add(_diagnosticsListBox);

        var toolbar = new ToolBar();
        DockPanel.SetDock(toolbar, Dock.Top);

        var openButton = new Button { Content = "Open Level" };
        openButton.Click += async (_, _) => await OpenLevelAsync(replace: true).ConfigureAwait(true);

        var addButton = new Button { Content = "Add Level", Margin = new Thickness(6, 0, 0, 0) };
        addButton.Click += async (_, _) => await OpenLevelAsync(replace: false).ConfigureAwait(true);

        var unloadButton = new Button { Content = "Unload Levels", Margin = new Thickness(6, 0, 0, 0) };
        unloadButton.Click += (_, _) =>
        {
            _runtime.CancelPendingLoad();
            CloseLevels();
            UpdateStatus("Unloaded all levels.");
        };

        toolbar.Items.Add(openButton);
        toolbar.Items.Add(addButton);
        toolbar.Items.Add(unloadButton);

        var statusBar = new StatusBar { Height = 24 };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        statusBar.Items.Add(_statusTextBlock);

        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(diagnosticsPanel);
        root.Children.Add(_sceneViewer);

        return root;
    }

    private async Task OpenLevelAsync(bool replace)
    {
        var dialog = AppDirectories.GetOpenPackageDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await LoadLevelAsync(dialog.FileName, replace).ConfigureAwait(true);
    }

    public async Task<bool> TryLoadLevelAsync(string path, bool replace = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        InterpPreviewLoadResult loadResult = await LoadLevelAsync(path, replace).ConfigureAwait(true);
        return loadResult.Outcome is InterpPreviewLoadOutcome.Loaded or InterpPreviewLoadOutcome.Duplicate;
    }

    private async Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace)
    {
        InterpPreviewLoadResult loadResult = await _runtime.LoadLevelAsync(path, replace, this, CloseLevels).ConfigureAwait(true);
        switch (loadResult.Outcome)
        {
            case InterpPreviewLoadOutcome.Cancelled:
                return loadResult;
            case InterpPreviewLoadOutcome.Duplicate:
                UpdateStatus("Level already loaded.");
                return loadResult;
            case InterpPreviewLoadOutcome.Failed:
                MessageBox.Show(this, $"Unable to load level:\n{loadResult.Error?.Message}", "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Load failed.");
                return loadResult;
            case InterpPreviewLoadOutcome.Loaded:
                InterpPreviewLoadedLevel loadedLevel = loadResult.LoadedLevel;
                _runtime.AddLoadedLevel(loadedLevel);
                RenderContext.LoadActors(_runtime.Actors);
                _sceneViewer.SetShouldRender(true);

                if (loadedLevel.Actors.Count > 0)
                {
                    RenderContext.Camera.Position = loadedLevel.Actors[0].GetBounds().Origin;
                }

                UpdateStatus($"Loaded {Path.GetFileName(loadedLevel.FullPath)}.");
                return loadResult;
            default:
                return loadResult;
        }
    }

    public InterpPreviewDialogueResolution ResolveDialogueNode(ConversationExtended conversation, DialogueNodeExtended node)
    {
        InterpPreviewDialogueResolution resolution = _runtime.ResolveDialogue(new InterpPreviewDialogueResolutionRequest(conversation, node));
        if (resolution.IsResolved)
        {
            UpdateStatus($"Line {node?.NodeCount} ({(node?.IsReply == true ? "Reply" : "Entry")}) is ready for preview (InterpData #{resolution.InterpData.UIndex}).");
        }
        else
        {
            UpdateStatus($"Line {node?.NodeCount} has no preview data. See Diagnostics.");
        }

        return resolution;
    }

    private void CloseLevels()
    {
        _runtime.UnloadAllLevels(RenderContext);
    }

    private void Runtime_DiagnosticsChanged(object sender, EventArgs e)
    {
        RefreshDiagnostics();
    }

    private void RefreshDiagnostics()
    {
        _diagnostics.Clear();
        foreach (InterpPreviewDiagnostic diagnostic in _runtime.Diagnostics)
        {
            _diagnostics.Add($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
        }
    }

    private void UpdateStatus(string message)
    {
        _statusTextBlock.Text = $"{message} Loaded levels: {_runtime.LoadedLevelCount}.";
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        _runtime.Detach(RenderContext);
        _runtime.DiagnosticsChanged -= Runtime_DiagnosticsChanged;
        CloseLevels();
        _runtime.Dispose();
        _sceneViewer.Dispose();
    }
}
