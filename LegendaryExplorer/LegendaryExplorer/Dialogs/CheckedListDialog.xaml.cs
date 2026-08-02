using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using LegendaryExplorer.SharedUI.Bases;
using LegendaryExplorerCore.Misc;

namespace LegendaryExplorer.Dialogs;

public class CheckedListItem
{
    public string DisplayName { get; set; }
    public bool IsSelected { get; set; }
    public object Tag { get; set; }
}

public partial class CheckedListDialog : TrackingNotifyPropertyChangedWindowBase
{
    public ObservableCollectionExtended<CheckedListItem> Items { get; } = [];
    public Action<CheckedListItem> DoubleClickItemHandler { get; set; }
    public bool IsAccepted { get; private set; }

    private string topText;
    public string TopText
    {
        get => topText;
        set => SetProperty(ref topText, value);
    }

    private string primaryButtonText = "OK";
    public string PrimaryButtonText
    {
        get => primaryButtonText;
        set => SetProperty(ref primaryButtonText, value);
    }

    public CheckedListDialog(IEnumerable<CheckedListItem> items, string title, string message, Window owner, string primaryButtonText = "OK")
        : base("Checked List Dialog", false)
    {
        DataContext = this;
        TopText = message;
        PrimaryButtonText = primaryButtonText;
        Items.AddRange(items);
        InitializeComponent();
        Title = title;
        Owner = owner;
    }

    public List<CheckedListItem> GetSelectedItems() => Items.Where(i => i.IsSelected).ToList();

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
        try
        {
            DialogResult = accepted;
        }
        catch (InvalidOperationException)
        {
            // Non-modal usage does not allow setting DialogResult.
        }
        Close();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items) item.IsSelected = true;
        CheckList.ItemsSource = null;
        CheckList.ItemsSource = Items;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items) item.IsSelected = false;
        CheckList.ItemsSource = null;
        CheckList.ItemsSource = Items;
    }

    private void CheckList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        InvokeListItemAction(e.OriginalSource);
    }

    private void CheckList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        InvokeListItemAction(e.OriginalSource);
    }

    private void InvokeListItemAction(object originalSource)
    {
        if (DoubleClickItemHandler is null)
        {
            return;
        }

        var dataContext = (originalSource as FrameworkElement)?.DataContext;
        if (dataContext is CheckedListItem listItem)
        {
            DoubleClickItemHandler.Invoke(listItem);
        }
    }
}
