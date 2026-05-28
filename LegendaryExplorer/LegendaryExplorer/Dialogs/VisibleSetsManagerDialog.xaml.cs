using LegendaryExplorer.SharedUI.Bases;
using LegendaryExplorerCore.Misc;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace LegendaryExplorer.Dialogs;

public partial class VisibleSetsManagerDialog : TrackingNotifyPropertyChangedWindowBase
{
    public ObservableCollectionExtended<string> VisibleClasses { get; } = [];
    public ObservableCollectionExtended<string> HiddenClasses { get; } = [];

    public ObservableCollectionExtended<string> FilteredVisibleClasses { get; } = [];
    public ObservableCollectionExtended<string> FilteredHiddenClasses { get; } = [];

    private string _visibleFilterText = string.Empty;
    public string VisibleFilterText
    {
        get => _visibleFilterText;
        set
        {
            if (SetProperty(ref _visibleFilterText, value))
            {
                RefreshVisibleFilter();
            }
        }
    }

    private string _hiddenFilterText = string.Empty;
    public string HiddenFilterText
    {
        get => _hiddenFilterText;
        set
        {
            if (SetProperty(ref _hiddenFilterText, value))
            {
                RefreshHiddenFilter();
            }
        }
    }

    public VisibleSetsManagerDialog(IEnumerable<string> allClasses, IEnumerable<string> visibleClasses, Window owner)
        : base("Visible Sets Manager", false)
    {
        HashSet<string> visible = visibleClasses.ToHashSet();
        List<string> all = allClasses.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();

        VisibleClasses.AddRange(all.Where(visible.Contains));
        HiddenClasses.AddRange(all.Where(c => !visible.Contains(c)));

        DataContext = this;
        InitializeComponent();
        Owner = owner;
        RefreshVisibleFilter();
        RefreshHiddenFilter();
    }

    public HashSet<string> GetVisibleClasses()
    {
        return VisibleClasses.ToHashSet();
    }

    private void RefreshVisibleFilter()
    {
        string filter = VisibleFilterText ?? string.Empty;
        FilteredVisibleClasses.ReplaceAll(VisibleClasses.Where(c => c.Contains(filter, System.StringComparison.OrdinalIgnoreCase)));
    }

    private void RefreshHiddenFilter()
    {
        string filter = HiddenFilterText ?? string.Empty;
        FilteredHiddenClasses.ReplaceAll(HiddenClasses.Where(c => c.Contains(filter, System.StringComparison.OrdinalIgnoreCase)));
    }

    private void MoveToVisible_Click(object sender, RoutedEventArgs e)
    {
        if (HiddenClasses_ListBox.SelectedItems.Count is 0) return;

        List<string> moving = HiddenClasses_ListBox.SelectedItems.Cast<string>().ToList();
        foreach (string className in moving)
        {
            HiddenClasses.Remove(className);
            if (!VisibleClasses.Contains(className))
            {
                VisibleClasses.Add(className);
            }
        }

        VisibleClasses.Sort(x => x);
        HiddenClasses.Sort(x => x);
        RefreshVisibleFilter();
        RefreshHiddenFilter();
    }

    private void MoveToHidden_Click(object sender, RoutedEventArgs e)
    {
        if (VisibleClasses_ListBox.SelectedItems.Count is 0) return;

        List<string> moving = VisibleClasses_ListBox.SelectedItems.Cast<string>().ToList();
        foreach (string className in moving)
        {
            VisibleClasses.Remove(className);
            if (!HiddenClasses.Contains(className))
            {
                HiddenClasses.Add(className);
            }
        }

        VisibleClasses.Sort(x => x);
        HiddenClasses.Sort(x => x);
        RefreshVisibleFilter();
        RefreshHiddenFilter();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
