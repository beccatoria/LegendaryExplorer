using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LegendaryExplorer.Misc;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore.Dialogue;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using LegendaryExplorerCore.Unreal;
using MessageBox = Xceed.Wpf.Toolkit.MessageBox;

namespace LegendaryExplorer.Tools.InterpEditor;

public class InterpPreviewShellWindow : NotifyPropertyChangedWindowBase, IActorEditorContext, IInterpPreviewLauncherHost
{
    private static bool s_preferFemalePlayer = true;
    private readonly SceneRenderControl _sceneViewer;
    private readonly TextBlock _statusTextBlock;
    private readonly ListBox _diagnosticsListBox;
    private readonly IInterpPreviewRuntime _runtime;
    private readonly ObservableCollection<string> _diagnostics = [];
    private Button _playerVariantButton;
    private bool _isClosing;
    private bool _allowClose;
    private readonly PreviewLevelLightRegistry _levelLights;
    private readonly InterpPreviewSceneRegistration _sceneRegistration;

    public LevelEditorRenderContext RenderContext { get; }
    public bool IsApplyingUndoRedo => false;
    public int LoadedLevelCount => _runtime.LoadedLevelCount;
    public bool IsUsable => IsLoaded && !_isClosing && _runtime.IsOpen;

    public bool TryPublishResult(InterpPreviewLauncherResult result, Action publish) => IsUsable
        && _runtime.TryPublishFeedback(result, publish);

    public static InterpPreviewShellWindow TryGetOpenWindow()
    {
        return Application.Current?.Windows
            .OfType<InterpPreviewShellWindow>()
            .FirstOrDefault(window => window.IsUsable);
    }

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
            ShowVolumetrics = true,
            ShowEmitters = true,
            ShowLocationActors = true,
            ShowSoundPositions = true,
            ShowCinematicActors = true,
            ShowDecalActors = true,
            ShowStageNodes = true,
            ShowStageCameras = true
        };

        _levelLights = new PreviewLevelLightRegistry(RenderContext);
        _sceneRegistration = new InterpPreviewSceneRegistration(RenderContext, _levelLights);
        _sceneViewer = new SceneRenderControl { Context = RenderContext };
        IInterpPreviewDispatcher dispatcher = new InterpPreviewWpfDispatcher(Dispatcher);
        IInterpPreviewSession session = new InterpPreviewSession();
        var loadCoordinator = new InterpPreviewLoadCoordinator(
            new InterpPreviewPackagePreparer(),
            new InterpPreviewActorRealizer(dispatcher, new InterpPreviewActorProxyFactory()),
            dispatcher,
            session);
        _runtime = new InterpPreviewRuntime(
            session,
            new InterpPreviewRenderCoordinator(),
            loadCoordinator,
            new InterpPreviewDialogueResolver(),
            dispatcher);
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
        if (!_isClosing && _runtime.IsOpen) _runtime.Attach(RenderContext);
    }

    private void SceneViewer_Unloaded(object sender, RoutedEventArgs e)
    {
        _runtime.Detach(RenderContext);
    }

    private System.Windows.UIElement BuildLayout()
    {
        var root = new DockPanel();

        var toolbar = new ToolBar();
        DockPanel.SetDock(toolbar, Dock.Top);

        var openButton = new Button { Content = "Open Level" };
        openButton.Click += async (_, _) => await OpenLevelAsync(replace: true).ConfigureAwait(true);

        var addButton = new Button { Content = "Add Level", Margin = new Thickness(6, 0, 0, 0) };
        addButton.Click += async (_, _) => await OpenLevelAsync(replace: false).ConfigureAwait(true);

        var unloadButton = new Button { Content = "Unload Levels", Margin = new Thickness(6, 0, 0, 0) };
        unloadButton.Click += async (_, _) =>
        {
            await ExecuteToolbarRequestAsync(new InterpPreviewLauncherRequest([]) { ClearScene = true });
        };

        toolbar.Items.Add(openButton);
        toolbar.Items.Add(addButton);
        toolbar.Items.Add(unloadButton);

        _playerVariantButton = new Button
        {
            Content = BuildPlayerVariantButtonText(),
            Margin = new Thickness(12, 0, 0, 0)
        };
        _playerVariantButton.Click += (_, _) => TogglePlayerVariant();
        toolbar.Items.Add(_playerVariantButton);

        var statusBar = new StatusBar { Height = 24 };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        statusBar.Items.Add(_statusTextBlock);

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        var diagnosticsPanel = new DockPanel
        {
            LastChildFill = true,
            MinWidth = 240
        };

        var diagnosticsHeaderRow = new DockPanel
        {
            LastChildFill = false,
            Margin = new Thickness(4, 2, 4, 4)
        };

        var diagnosticsHeader = new TextBlock
        {
            Text = "Diagnostics",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var copyLogButton = new Button
        {
            Content = "Copy Log",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
            ToolTip = "Copy diagnostics log to clipboard"
        };
        copyLogButton.Click += (_, _) => CopyDiagnosticsToClipboard();

        DockPanel.SetDock(copyLogButton, Dock.Right);
        diagnosticsHeaderRow.Children.Add(copyLogButton);
        diagnosticsHeaderRow.Children.Add(diagnosticsHeader);

        DockPanel.SetDock(diagnosticsHeaderRow, Dock.Top);
        diagnosticsPanel.Children.Add(diagnosticsHeaderRow);
        diagnosticsPanel.Children.Add(_diagnosticsListBox);

        var splitter = new GridSplitter
        {
            Width = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Background = System.Windows.Media.Brushes.Transparent
        };

        Grid.SetColumn(_sceneViewer, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(diagnosticsPanel, 2);
        contentGrid.Children.Add(_sceneViewer);
        contentGrid.Children.Add(splitter);
        contentGrid.Children.Add(diagnosticsPanel);

        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(contentGrid);

        return root;
    }

    private void CopyDiagnosticsToClipboard()
    {
        string diagnosticsText = string.Join(Environment.NewLine, _diagnostics);
        try
        {
            Clipboard.SetText(diagnosticsText);
            UpdateStatus("Diagnostics log copied to clipboard.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to copy diagnostics log:\n{ex.Message}", "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task OpenLevelAsync(bool replace)
    {
        var dialog = AppDirectories.GetOpenPackageDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ExecuteToolbarRequestAsync(new InterpPreviewLauncherRequest([dialog.FileName])
        {
            ReplaceOnFirstLevel = replace
        }).ConfigureAwait(true);
    }

    public async Task<bool> TryLoadLevelAsync(string path, bool replace = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        InterpPreviewLauncherResult result = await ExecuteLauncherRequestAsync(new InterpPreviewLauncherRequest([path])
        {
            ReplaceOnFirstLevel = replace
        }).ConfigureAwait(true);

        return result.Outcome is InterpPreviewLauncherOutcome.Committed or InterpPreviewLauncherOutcome.NoChange;
    }

    public async Task<(int loadedCount, int totalCount, bool hadFailure)> TryLoadLevelsAsync(IReadOnlyList<string> paths, bool replace = true)
    {
        if (paths is null || paths.Count == 0)
        {
            return (0, 0, false);
        }

        InterpPreviewLauncherResult result = await ExecuteLauncherRequestAsync(new InterpPreviewLauncherRequest(paths)
        {
            ReplaceOnFirstLevel = replace
        }).ConfigureAwait(true);

        return (result.LoadedCount, result.TotalCount, result.HadFailure);
    }

    public Task<InterpPreviewLauncherResult> ExecuteLauncherRequestAsync(InterpPreviewLauncherRequest request)
    {
        if (request is null) return Task.FromResult(InterpPreviewLauncherResult.Failed("Invalid launcher request."));
        // Capture the preference when this request is accepted, rather than after an await.
        var captured = new InterpPreviewLauncherRequest(request.LevelPaths)
        {
            ReplaceOnFirstLevel = request.ReplaceOnFirstLevel,
            ClearScene = request.ClearScene,
            Conversation = request.Conversation,
            Node = request.Node,
            EnsurePlayerContext = request.EnsurePlayerContext,
            ResolveDialogue = request.ResolveDialogue,
            PreferFemalePlayer = request.PreferFemalePlayer ?? s_preferFemalePlayer
        };
        return _runtime.ExecuteLauncherRequestAsync(captured, this, this);
    }

    private async Task ExecuteToolbarRequestAsync(InterpPreviewLauncherRequest request)
    {
        InterpPreviewLauncherResult result = null;
        try
        {
            result = await ExecuteLauncherRequestAsync(request).ConfigureAwait(true);
            TryPublishResult(result, () =>
            {
                UpdateStatus(result.Message ?? "Preview request completed.");
                if (result.Outcome == InterpPreviewLauncherOutcome.Failed)
                    MessageBox.Show(this, result.Message, "Preview request failed", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            TryPublishResult(result, () => UpdateStatus($"Preview request failed: {ex.Message}"));
        }
    }

    void IInterpPreviewLauncherHost.PublishDialogue(InterpPreviewDialogueResolution resolution, InterpPreviewLauncherRequest request)
    {
        if (_isClosing) return;
        DialogueNodeExtended node = request.Node;
        InterpPreviewDiagnostic binding = GetStatusBindingDiagnostic(resolution.Diagnostics);
        string suffix = binding is null ? string.Empty : $" {binding.Message}";
        if (resolution.HasPreviewData)
        {
            string state = resolution.Outcome switch
            {
                InterpPreviewResolutionOutcome.Ready => "scene data found",
                InterpPreviewResolutionOutcome.NeedsSelection => "several possible scene routes",
                InterpPreviewResolutionOutcome.Partial => "scene data found with gaps",
                _ => "scene data needs checking"
            };
            UpdateStatus($"Line {node?.NodeCount} ({(node?.IsReply == true ? "Reply" : "Entry")}): {state}; {resolution.Plans.Count} scene candidate(s) ({BuildSourceSummary(resolution)}; {BuildTargetSummary(resolution)}).{suffix}");
        }
        else
        {
            UpdateStatus($"Line {node?.NodeCount} has no preview data. See Diagnostics.{suffix}");
        }
    }

    private static string BuildSourceSummary(InterpPreviewDialogueResolution resolution)
    {
        if (resolution?.HasPreviewData != true)
        {
            return "source unknown";
        }

        if (HasDiagnosticCode(resolution.Diagnostics, "INTERP_DISCOVERY_SELECTED"))
        {
            return "source: graph discovery";
        }

        if (HasDiagnosticCode(resolution.Diagnostics, "INTERP_DIRECT"))
        {
            return "source: selected line";
        }

        if (HasDiagnosticCode(resolution.Diagnostics, "INTERP_FALLBACK"))
        {
            return "source: linked conversation fallback";
        }

        return "source: copied line hint";
    }

    private static string BuildTargetSummary(InterpPreviewDialogueResolution resolution)
    {
        if (resolution?.Diagnostics is null || resolution.Diagnostics.Count == 0)
        {
            return "target checks unavailable";
        }

        int boundCount = CountDiagnosticsByCode(resolution.Diagnostics, "ACTOR_TAG_BOUND");
        int missingCount = CountDiagnosticsByCode(resolution.Diagnostics, "ACTOR_TAG_MISSING");
        int ambiguousCount = CountDiagnosticsByCode(resolution.Diagnostics, "ACTOR_TAG_AMBIGUOUS");

        if (boundCount > 0 || missingCount > 0 || ambiguousCount > 0)
        {
            return $"targets: bound {boundCount}, missing {missingCount}, ambiguous {ambiguousCount}";
        }

        if (HasDiagnosticCode(resolution.Diagnostics, "TARGET_ACTION_UNRESOLVED"))
        {
            return "targets skipped: action unresolved";
        }

        if (HasDiagnosticCode(resolution.Diagnostics, "ACTOR_BINDING_SCENE_UNAVAILABLE"))
        {
            return "targets unresolved: scene snapshot unavailable";
        }

        if (HasDiagnosticCode(resolution.Diagnostics, "TARGET_TRACK_UNSUPPORTED"))
        {
            int unsupportedCount = CountDiagnosticsByCode(resolution.Diagnostics, "TARGET_TRACK_UNSUPPORTED");
            return unsupportedCount == 1
                ? "targets partial: 1 unsupported track schema"
                : $"targets partial: {unsupportedCount} unsupported track schemas";
        }

        return "targets: no actor match diagnostics";
    }

    private static InterpPreviewDiagnostic GetStatusBindingDiagnostic(IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
    {
        if (diagnostics is null || diagnostics.Count == 0)
        {
            return null;
        }

        string[] priorityCodes =
        [
            "SCENE_BIND_OWNER_MISS",
            "SCENE_BIND_PLAYER_MISS",
            "SCENE_BIND_OWNER_MATCH",
            "SCENE_BIND_PLAYER_MATCH",
            "SCENE_BIND_SPEAKER_MISS",
            "SCENE_BIND_SPEAKER_MATCH",
            "SCENE_BIND_SPEAKER_UNAVAILABLE"
        ];

        for (int i = 0; i < priorityCodes.Length; i++)
        {
            string code = priorityCodes[i];
            for (int j = 0; j < diagnostics.Count; j++)
            {
                if (string.Equals(diagnostics[j].Code, code, StringComparison.Ordinal))
                {
                    return diagnostics[j];
                }
            }
        }

        return null;
    }

    private static bool HasDiagnosticCode(IReadOnlyList<InterpPreviewDiagnostic> diagnostics, string code)
    {
        if (diagnostics is null || diagnostics.Count == 0 || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        for (int i = 0; i < diagnostics.Count; i++)
        {
            if (string.Equals(diagnostics[i].Code, code, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountDiagnosticsByCode(IReadOnlyList<InterpPreviewDiagnostic> diagnostics, string code)
    {
        if (diagnostics is null || diagnostics.Count == 0 || string.IsNullOrWhiteSpace(code))
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < diagnostics.Count; i++)
        {
            if (string.Equals(diagnostics[i].Code, code, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    Task<InterpPreviewLoadedLevel> IInterpPreviewLauncherHost.PreparePlayerAsync(
        InterpPreviewLauncherRequest request, Func<bool> canContinue)
    {
        if (!canContinue() || request.Conversation?.Speakers is null || request.Node is null)
            return Task.FromResult<InterpPreviewLoadedLevel>(null);

        ConversationExtended conversation = request.Conversation;
        SpeakerExtended speaker = conversation.Speakers.FirstOrDefault(s => s.SpeakerID == request.Node.SpeakerIndex);
        if (speaker is null || !speaker.SpeakerName.Equals("player", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<InterpPreviewLoadedLevel>(null);

        MEGame game = conversation.Export?.FileRef?.Game ?? MEGame.Unknown;
        if (game == MEGame.Unknown) return Task.FromResult<InterpPreviewLoadedLevel>(null);
        string playerFileName = game switch
        {
            MEGame.ME1 => "EntryMenu.SFM",
            MEGame.LE1 => "EntryMenu.pcc",
            _ => "BioP_Char.pcc"
        };
        if (!MELoadedFiles.TryGetHighestMountedFile(game, playerFileName, out string path)
            || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Task.FromResult<InterpPreviewLoadedLevel>(null);

        bool female = request.PreferFemalePlayer ?? true;
        var key = InterpPreviewResourceKey.ForPlayerContext(path, game,
            female ? InterpPreviewPlayerVariant.Female : InterpPreviewPlayerVariant.Male);
        if (_runtime.ContainsResource(key) || !canContinue()) return Task.FromResult<InterpPreviewLoadedLevel>(null);
        return Task.FromResult(CreatePlayerContextLevel(path, female));
    }

    void IInterpPreviewLauncherHost.ApplySceneChange(InterpPreviewOperationResult result, Action commitScene)
        => _sceneRegistration.Apply(result, commitScene);

    void IInterpPreviewLauncherHost.PublishSceneChange(InterpPreviewOperationResult operationResult, bool updateCamera)
    {
        _sceneViewer.SetShouldRender(true);

        if (updateCamera && operationResult.AddedActors.Count > 0)
        {
            RenderContext.Camera.Position = operationResult.AddedActors[0].GetBounds().Origin;
        }

        UpdateStatus(operationResult.Message);
    }



    private InterpPreviewLoadedLevel CreatePlayerContextLevel(string playerFilePath, bool preferFemalePlayer)
    {
        IMEPackage package = InterpPreviewPackageOwnership.OpenIndependent(playerFilePath);
        try
        {
            string playerPawnPath = preferFemalePlayer
                ? "TheWorld.PersistentLevel.BioPawn_1"
                : "TheWorld.PersistentLevel.BioPawn_0";
            ExportEntry playerPawnExport = package.FindExport(playerPawnPath);
            if (playerPawnExport is null)
            {
                package.Dispose();
                return null;
            }

            var actors = new List<ActorProxy>();
            if (ActorProxy.Create(this, playerPawnExport) is { } playerActor)
            {
                actors.Add(playerActor);
            }

            if (actors.Count == 0)
            {
                package.Dispose();
                return null;
            }

            InterpPreviewPlayerVariant variant = preferFemalePlayer ? InterpPreviewPlayerVariant.Female : InterpPreviewPlayerVariant.Male;
            return new InterpPreviewLoadedLevel(playerFilePath, package, actors, InterpPreviewResourceKind.PlayerContext, variant);
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private void TogglePlayerVariant()
    {
        s_preferFemalePlayer = !s_preferFemalePlayer;
        if (_playerVariantButton is not null)
        {
            _playerVariantButton.Content = BuildPlayerVariantButtonText();
        }

        UpdateStatus($"Player variant set to {(s_preferFemalePlayer ? "Female" : "Male")}." );
    }

    private static string BuildPlayerVariantButtonText()
    {
        return $"Player: {(s_preferFemalePlayer ? "Female" : "Male")}";
    }

    private void CloseLevels()
    {
        _levelLights.Clear();
        _runtime.UnloadAllLevels(RenderContext);
    }



    private void Runtime_DiagnosticsChanged(object sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshDiagnostics();
            return;
        }

        Dispatcher.BeginInvoke(new Action(RefreshDiagnostics));
    }

    private void RefreshDiagnostics()
    {
        if (_isClosing) return;
        _diagnostics.Clear();
        foreach (InterpPreviewDiagnostic diagnostic in _runtime.Diagnostics)
        {
            _diagnostics.Add($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
        }
    }

    private void UpdateStatus(string message)
    {
        if (_isClosing) return;
        _statusTextBlock.Text = $"{message} Loaded levels: {_runtime.LoadedLevelCount}.";
    }

    private async void OnClosing(object sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _sceneViewer.SetShouldRender(false);
        if (Content is System.Windows.UIElement content) content.IsEnabled = false;
        Task shutdown = _runtime.ShutdownAsync();
        _runtime.Detach(RenderContext);
        _runtime.DiagnosticsChanged -= Runtime_DiagnosticsChanged;
        try
        {
            await shutdown.ConfigureAwait(true);
            CloseLevels();
            _runtime.Dispose();
            _sceneViewer.Dispose();
            _allowClose = true;
            Dispatcher.BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            _isClosing = false;
            MessageBox.Show(this, $"Unable to close Interp Preview cleanly:\n{ex.Message}", "Close failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
