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

public class InterpPreviewShellWindow : NotifyPropertyChangedWindowBase, IActorEditorContext
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
    private readonly InterpPreviewLauncherRequestTracker _launcherRequestTracker;
    private readonly PreviewLevelLightRegistry _levelLights;

    public LevelEditorRenderContext RenderContext { get; }
    public bool IsApplyingUndoRedo => false;
    public int LoadedLevelCount => _runtime.LoadedLevelCount;

    public static InterpPreviewShellWindow TryGetOpenWindow()
    {
        return Application.Current?.Windows
            .OfType<InterpPreviewShellWindow>()
            .FirstOrDefault(window => window.IsLoaded);
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
        _launcherRequestTracker = new InterpPreviewLauncherRequestTracker(() => _runtime.CancelPendingLoad());
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

        await LoadLevelAsync(dialog.FileName, replace).ConfigureAwait(true);
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

    public async Task<InterpPreviewLauncherResult> ExecuteLauncherRequestAsync(InterpPreviewLauncherRequest request)
    {
        if (request is null)
        {
            return InterpPreviewLauncherResult.Failed("Invalid launcher request.");
        }

        bool preferFemalePlayer = request.PreferFemalePlayer ?? s_preferFemalePlayer;
        long requestId = BeginLauncherRequest();
        int loadedCount = 0;
        bool hadFailure = false;
        int totalCount = request.LevelPaths?.Count ?? 0;
        IReadOnlyList<string> normalizedLevelPaths = request.LevelPaths ?? [];

        if (!InterpPreviewLauncherLevelBatch.TryNormalizeDistinctLevelPaths(request.LevelPaths, out normalizedLevelPaths))
        {
            return InterpPreviewLauncherResult.Failed("Launcher request contained an invalid level path.");
        }

        totalCount = normalizedLevelPaths.Count;

        if (request.ReplaceOnFirstLevel && totalCount > 1)
        {
            InterpPreviewLauncherLevelBatchResult batchResult = await InterpPreviewLauncherLevelBatch.ExecuteTransactionalReplaceAsync(
                _runtime,
                normalizedLevelPaths,
                this,
                () => IsLauncherRequestCurrent(requestId)).ConfigureAwait(true);

            foreach (InterpPreviewCommittedLevelResult committedLevel in batchResult.CommittedLevels)
            {
                ApplyCommittedOperation(committedLevel.OperationResult, committedLevel.Path, updateCamera: true);
            }

            loadedCount = batchResult.LoadedCount;
            hadFailure = batchResult.HadFailure;

            switch (batchResult.TerminalOutcome)
            {
                case InterpPreviewLauncherBatchTerminalOutcome.Cancelled:
                    return InterpPreviewLauncherResult.Cancelled(batchResult.TerminalMessage);
                case InterpPreviewLauncherBatchTerminalOutcome.Superseded:
                    return InterpPreviewLauncherResult.Superseded(batchResult.TerminalMessage);
                case InterpPreviewLauncherBatchTerminalOutcome.RejectedClosing:
                    return InterpPreviewLauncherResult.RejectedClosing(batchResult.TerminalMessage);
                case InterpPreviewLauncherBatchTerminalOutcome.Failed:
                    return InterpPreviewLauncherResult.Failed(batchResult.TerminalMessage);
            }
        }
        else
        {
            for (int i = 0; i < totalCount; i++)
            {
                if (!IsLauncherRequestCurrent(requestId))
                {
                    return InterpPreviewLauncherResult.Superseded("Launcher request was superseded.");
                }

                string path = normalizedLevelPaths[i];

                InterpPreviewOperationResult result = await _runtime.ExecuteLoadOperationAsync(path, request.ReplaceOnFirstLevel && i == 0, this).ConfigureAwait(true);
                switch (result.Outcome)
                {
                    case InterpPreviewOperationOutcome.Committed:
                        ApplyCommittedOperation(result, path, updateCamera: true);
                        loadedCount++;
                        break;
                    case InterpPreviewOperationOutcome.NoChange:
                        loadedCount++;
                        break;
                    case InterpPreviewOperationOutcome.Cancelled:
                        return InterpPreviewLauncherResult.Cancelled("Launcher request was cancelled.");
                    case InterpPreviewOperationOutcome.Superseded:
                        return InterpPreviewLauncherResult.Superseded("Launcher request was superseded.");
                    case InterpPreviewOperationOutcome.RejectedClosing:
                        return InterpPreviewLauncherResult.RejectedClosing("Preview is closing.");
                    case InterpPreviewOperationOutcome.Failed:
                        hadFailure = true;
                        break;
                }
            }
        }

        if (request.EnsurePlayerContext)
        {
            if (!IsLauncherRequestCurrent(requestId))
            {
                return InterpPreviewLauncherResult.Superseded("Launcher request was superseded.");
            }

            InterpPreviewOperationResult playerCommitResult = await EnsurePlayerContextLoadedCoreAsync(
                request.Conversation,
                request.Node,
                preferFemalePlayer,
                updateStatus: false,
                showErrorDialog: false).ConfigureAwait(true);
            switch (playerCommitResult.Outcome)
            {
                case InterpPreviewOperationOutcome.Committed:
                    break;
                case InterpPreviewOperationOutcome.NoChange:
                    break;
                case InterpPreviewOperationOutcome.Cancelled:
                    return InterpPreviewLauncherResult.Cancelled("Launcher request was cancelled.");
                case InterpPreviewOperationOutcome.Superseded:
                    return InterpPreviewLauncherResult.Superseded("Launcher request was superseded.");
                case InterpPreviewOperationOutcome.RejectedClosing:
                    return InterpPreviewLauncherResult.RejectedClosing("Preview is closing.");
                case InterpPreviewOperationOutcome.Failed:
                    hadFailure = true;
                    break;
            }
        }

        InterpPreviewDialogueResolution resolution = null;
        if (request.ResolveDialogue)
        {
            if (!IsLauncherRequestCurrent(requestId))
            {
                return InterpPreviewLauncherResult.Superseded("Launcher request was superseded.");
            }

            resolution = ResolveDialogueNodeCore(request.Conversation, request.Node, preferFemalePlayer, updateStatus: true);
        }

        return InterpPreviewLauncherResult.Completed(loadedCount, totalCount, hadFailure, resolution);
    }

    private async Task<InterpPreviewOperationResult> LoadLevelAsync(string path, bool replace)
    {
        InterpPreviewOperationResult operationResult = await _runtime.ExecuteLoadOperationAsync(path, replace, this).ConfigureAwait(true);
        switch (operationResult.Outcome)
        {
            case InterpPreviewOperationOutcome.Cancelled:
            case InterpPreviewOperationOutcome.Superseded:
                return operationResult;
            case InterpPreviewOperationOutcome.NoChange:
                UpdateStatus("Level already loaded.");
                return operationResult;
            case InterpPreviewOperationOutcome.Failed:
                MessageBox.Show(this, $"Unable to load level:\n{operationResult.Error?.Message}", "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Load failed.");
                return operationResult;
            case InterpPreviewOperationOutcome.RejectedClosing:
                UpdateStatus("Preview is closing.");
                return operationResult;
            case InterpPreviewOperationOutcome.Committed:
                ApplyCommittedOperation(operationResult, path, updateCamera: true);
                return operationResult;
            default:
                return operationResult;
        }
    }

    public InterpPreviewDialogueResolution ResolveDialogueNode(ConversationExtended conversation, DialogueNodeExtended node)
    {
        return ResolveDialogueNodeCore(conversation, node, s_preferFemalePlayer, updateStatus: true);
    }

    private InterpPreviewDialogueResolution ResolveDialogueNodeCore(
        ConversationExtended conversation,
        DialogueNodeExtended node,
        bool preferFemalePlayer,
        bool updateStatus)
    {
        var request = new InterpPreviewDialogueResolutionRequest(conversation, node)
        {
            PreferFemalePlayer = preferFemalePlayer
        };

        InterpPreviewDialogueResolution resolution = _runtime.ResolveDialogue(request);
        InterpPreviewDiagnostic speakerBindingDiagnostic = GetStatusBindingDiagnostic(resolution.Diagnostics);

        if (!updateStatus)
        {
            return resolution;
        }

        if (resolution.IsResolved)
        {
            string sourceSummary = BuildSourceSummary(resolution);
            string targetSummary = BuildTargetSummary(resolution);
            string bindingSuffix = speakerBindingDiagnostic is null ? string.Empty : $" {speakerBindingDiagnostic.Message}";
            UpdateStatus($"Line {node?.NodeCount} ({(node?.IsReply == true ? "Reply" : "Entry")}) is ready for preview ({sourceSummary}; {targetSummary}).{bindingSuffix}");
        }
        else
        {
            string bindingSuffix = speakerBindingDiagnostic is null ? string.Empty : $" {speakerBindingDiagnostic.Message}";
            UpdateStatus($"Line {node?.NodeCount} has no preview data. See Diagnostics.{bindingSuffix}");
        }

        return resolution;
    }

    private static string BuildSourceSummary(InterpPreviewDialogueResolution resolution)
    {
        if (resolution?.InterpData is null)
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

        return $"source export #{resolution.InterpData.UIndex}";
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

    public async Task EnsurePlayerContextLoadedAsync(ConversationExtended conversation, DialogueNodeExtended node)
    {
        await EnsurePlayerContextLoadedCoreAsync(
            conversation,
            node,
            s_preferFemalePlayer,
            updateStatus: true,
            showErrorDialog: true).ConfigureAwait(true);
    }

    private async Task<InterpPreviewOperationResult> EnsurePlayerContextLoadedCoreAsync(
        ConversationExtended conversation,
        DialogueNodeExtended node,
        bool preferFemalePlayer,
        bool updateStatus,
        bool showErrorDialog)
    {
        if (conversation?.Speakers is null || node is null)
        {
            return InterpPreviewOperationResult.NoChange();
        }

        SpeakerExtended speaker = conversation.Speakers.FirstOrDefault(s => s.SpeakerID == node.SpeakerIndex);
        if (speaker is null || !speaker.SpeakerName.Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            return InterpPreviewOperationResult.NoChange();
        }

        MEGame game = conversation.Export?.FileRef?.Game ?? MEGame.Unknown;
        if (game == MEGame.Unknown)
        {
            return InterpPreviewOperationResult.NoChange();
        }

        string playerFileName = game switch
        {
            MEGame.ME1 => "EntryMenu.SFM",
            MEGame.LE1 => "EntryMenu.pcc",
            _ => "BioP_Char.pcc"
        };

        if (!MELoadedFiles.TryGetHighestMountedFile(game, playerFileName, out string playerFilePath)
            || string.IsNullOrWhiteSpace(playerFilePath)
            || !File.Exists(playerFilePath))
        {
            return InterpPreviewOperationResult.NoChange();
        }

        InterpPreviewPlayerVariant variant = preferFemalePlayer ? InterpPreviewPlayerVariant.Female : InterpPreviewPlayerVariant.Male;
        InterpPreviewResourceKey playerResourceKey = InterpPreviewResourceKey.ForPlayerContext(playerFilePath, game, variant);
        if (_runtime.ContainsResource(playerResourceKey))
        {
            return InterpPreviewOperationResult.NoChange();
        }

        InterpPreviewLoadedLevel injectedPlayerLevel = CreatePlayerContextLevel(playerFilePath, preferFemalePlayer);
        if (injectedPlayerLevel is null)
        {
            return InterpPreviewOperationResult.NoChange();
        }

        InterpPreviewOperationResult commitResult = await _runtime.ExecuteCommitOperationAsync(injectedPlayerLevel, replace: false).ConfigureAwait(true);
        switch (commitResult.Outcome)
        {
            case InterpPreviewOperationOutcome.Committed:
                ApplyCommittedOperation(commitResult, null, updateCamera: false);
                if (updateStatus)
                {
                    UpdateStatus($"Loaded player context ({(preferFemalePlayer ? "Female" : "Male")}).");
                }
                break;
            case InterpPreviewOperationOutcome.NoChange:
                break;
            case InterpPreviewOperationOutcome.Failed:
                if (showErrorDialog)
                {
                    MessageBox.Show(this, $"Unable to load player context:\n{commitResult.Error?.Message}", "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                break;
            case InterpPreviewOperationOutcome.RejectedClosing:
            case InterpPreviewOperationOutcome.Cancelled:
            case InterpPreviewOperationOutcome.Superseded:
                break;
        }

        return commitResult;
    }

    private void ApplyCommittedOperation(InterpPreviewOperationResult operationResult, string path, bool updateCamera)
    {
        if (operationResult.RemovedActors.Count > 0)
        {
            RenderContext.UnloadActors(operationResult.RemovedActors.ToList());
        }

        RenderContext.LoadActors(operationResult.AddedActors.ToList());
        _levelLights.Synchronize(RenderContext.DrawList_3D.Select(actor => actor.Export.FileRef));
        DisposeRetiredResources(operationResult.RetiredResources);
        _sceneViewer.SetShouldRender(true);

        if (updateCamera && operationResult.AddedActors.Count > 0)
        {
            RenderContext.Camera.Position = operationResult.AddedActors[0].GetBounds().Origin;
        }

        if (!string.IsNullOrWhiteSpace(path) || !string.IsNullOrWhiteSpace(operationResult.Message))
        {
            UpdateStatus(operationResult.Message ?? $"Loaded {Path.GetFileName(path)}.");
        }
    }

    private long BeginLauncherRequest()
    {
        return _launcherRequestTracker.BeginRequest();
    }

    private bool IsLauncherRequestCurrent(long requestId)
    {
        return _launcherRequestTracker.IsCurrent(requestId);
    }

    private InterpPreviewLoadedLevel CreatePlayerContextLevel(string playerFilePath, bool preferFemalePlayer)
    {
        IMEPackage package = MEPackageHandler.OpenMEPackage(playerFilePath, forceLoadFromDisk: true);
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

    private static void DisposeRetiredResources(IReadOnlyList<InterpPreviewOwnedResource> retiredResources)
    {
        if (retiredResources is null)
        {
            return;
        }

        foreach (InterpPreviewOwnedResource retiredResource in retiredResources)
        {
            retiredResource?.Dispose();
        }
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
        _runtime.Detach(RenderContext);
        _runtime.DiagnosticsChanged -= Runtime_DiagnosticsChanged;
        try
        {
            await _runtime.ShutdownAsync().ConfigureAwait(true);
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
