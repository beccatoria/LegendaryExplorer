using LegendaryExplorer.Misc;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LegendaryExplorer.Tools.LevelEditor;

/// <summary>
/// Represents a single open level file in the multi-file LevelEditor.
/// Each instance is an <see cref="IPackageUser"/> registered on its own package,
/// so it receives per-package update notifications independently.
/// </summary>
public class OpenLevelFile : NotifyPropertyChangedBase, IPackageUser, IDisposable
{
    public bool IsDisposed { get; private set; }
    public IMEPackage Package { get; }
    public ExportEntry LevelExport { get; }
    public int LoadOrder { get; }
    public string FileName => Path.GetFileName(Package.FilePath);
    public string FilePath => Package.FilePath;

    public ObservableCollectionExtended<ActorProxy> Actors { get; } = [];

    public List<SceneLight> Lights { get; internal set; } = [];
    private readonly LevelLightPreviewUpdater _lightPreviews = new();
    public LevelLightUpdateDependencies LightDependencies { get; } = new();

    public void UpdateLightPreviews(MeshRenderContext context) => _lightPreviews.Update(Actors, Lights, context);

    private readonly LevelEditor Owner;

    private bool isDirty;
    public bool IsDirty
    {
        get => isDirty;
        set
        {
            if (SetProperty(ref isDirty, value))
            {
                Owner.UpdateGlobalDirtyState();
            }
        }
    }

    public void RecalculateDirty()
    {
        IsDirty = Actors.Any(a => a.IsDirty);
    }

    private bool isReadOnly;
    public bool IsReadOnly
    {
        get => isReadOnly;
        set => SetProperty(ref isReadOnly, value);
    }

    private bool isActorListExpanded = true;
    public bool IsActorListExpanded
    {
        get => isActorListExpanded;
        set => SetProperty(ref isActorListExpanded, value);
    }

    public OpenLevelFile(LevelEditor owner, IMEPackage package, ExportEntry levelExport, int loadOrder)
    {
        Owner = owner;
        Package = package;
        LevelExport = levelExport;
        LoadOrder = loadOrder;
        Actors.CollectionChanged += ActorsChanged;
    }

    private void ActorsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => _lightPreviews.Invalidate();

    #region IPackageUser

    public void HandleUpdate(List<PackageUpdate> updates)
    {
        if (IsDisposed || Owner.Dispatcher.HasShutdownStarted) return;
        if (!Owner.Dispatcher.CheckAccess())
        {
            var snapshot = updates.ToList();
            Owner.Dispatcher.BeginInvoke(new Action(() => HandleUpdate(snapshot)));
        }
        else Owner.HandleFileUpdate(this, updates);
    }

    private Action _closedHandler;

    public void RegisterClosed(Action handler)
    {
        _closedHandler = handler;
    }

    public void ReleaseUse()
    {
        _closedHandler = null;
    }

    public void HandleSaveStateChange(bool isSaving)
    {
        if (IsDisposed || Owner.Dispatcher.HasShutdownStarted) return;
        if (!Owner.Dispatcher.CheckAccess())
            Owner.Dispatcher.BeginInvoke(new Action(() => HandleSaveStateChange(isSaving)));
        else Owner.HandleSaveStateChange(isSaving);
    }

    #endregion

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Actors.CollectionChanged -= ActorsChanged;
        _lightPreviews.Clear();
        foreach (var actor in Actors)
        {
            actor.Dispose();
        }
        Actors.Clear();
        Lights.Clear();
        Package.Release(this);
        _closedHandler = null;
    }
}
