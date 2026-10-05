using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using LegendaryExplorer.Misc;
using LegendaryExplorer.SharedUI.Bases;
using LegendaryExplorerCore.Misc;

namespace LegendaryExplorer.Dialogs;

public class ObjectReferencerCoverageNode : NotifyPropertyChangedBase
{
    private bool isSelected;

    public string DisplayName { get; init; }
    public int UIndex { get; init; }
    public bool IsRootCandidate { get; init; }
    public object Tag { get; init; }
    public ObservableCollectionExtended<ObjectReferencerCoverageNode> Children { get; } = [];

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (!SetProperty(ref isSelected, value))
            {
                return;
            }

            foreach (ObjectReferencerCoverageNode child in Children)
            {
                child.IsSelected = value;
            }
        }
    }
}

public partial class ObjectReferencerCoverageDialog : TrackingNotifyPropertyChangedWindowBase
{
    public ObservableCollectionExtended<ObjectReferencerCoverageNode> RootNodes { get; } = [];
    public Action<ObjectReferencerCoverageNode> DoubleClickItemHandler { get; set; }
    public bool IsAccepted { get; private set; }

    private string topText;
    public string TopText
    {
        get => topText;
        set => SetProperty(ref topText, value);
    }

    private string primaryButtonText = "Apply";
    public string PrimaryButtonText
    {
        get => primaryButtonText;
        set => SetProperty(ref primaryButtonText, value);
    }

    public ObjectReferencerCoverageDialog(IEnumerable<ObjectReferencerCoverageNode> roots, string title, string message, Window owner, string primaryButtonText = "Apply")
        : base("Object Referencer Coverage Dialog", false)
    {
        DataContext = this;
        TopText = message;
        PrimaryButtonText = primaryButtonText;
        RootNodes.AddRange(roots);
        InitializeComponent();
        Title = title;
        Owner = owner;
    }

    public List<ObjectReferencerCoverageNode> GetSelectedRoots()
    {
        return RootNodes.Where(x => x.IsRootCandidate && x.IsSelected).ToList();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        CloseWithResult(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseWithResult(false);
    }

    private void CloseWithResult(bool accepted)
    {
        IsAccepted = accepted;
        DialogResult = accepted;
        Close();
    }

    private void SelectAllRoots_Click(object sender, RoutedEventArgs e)
    {
        foreach (ObjectReferencerCoverageNode root in RootNodes.Where(x => x.IsRootCandidate))
        {
            root.IsSelected = true;
        }
    }

    private void SelectNoRoots_Click(object sender, RoutedEventArgs e)
    {
        foreach (ObjectReferencerCoverageNode root in RootNodes.Where(x => x.IsRootCandidate))
        {
            root.IsSelected = false;
        }
    }

    private void CoverageTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        InvokeItemAction(e.OriginalSource);
    }

    private void CoverageTree_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        InvokeItemAction(e.OriginalSource);
    }

    private void InvokeItemAction(object originalSource)
    {
        if (DoubleClickItemHandler is null)
        {
            return;
        }

        var dataContext = (originalSource as FrameworkElement)?.DataContext;
        if (dataContext is ObjectReferencerCoverageNode node)
        {
            DoubleClickItemHandler.Invoke(node);
        }
    }
}
