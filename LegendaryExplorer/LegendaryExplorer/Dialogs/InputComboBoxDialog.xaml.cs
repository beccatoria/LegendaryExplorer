using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using LegendaryExplorer.Misc;
using LegendaryExplorer.SharedUI;

namespace LegendaryExplorer.Dialogs
{
    /// <summary>
    /// Interaction logic for InputComboBoxWPF.xaml
    /// </summary>
    public partial class InputComboBoxDialog : NotifyPropertyChangedWindowBase
    {
        private readonly List<object> _allItems;
        private readonly ICollectionView _filteredItems;

        private InputComboBoxDialog(Control owner, string promptText, string titleText, IEnumerable items, string defaultValue = "", bool topMost = false)
        {
            DirectionsText = promptText;
            Topmost = topMost;
            TitleText = titleText;
            DataContext = this;
            LoadCommands();
            InitializeComponent();
            if (owner != null)
            {
                Owner = owner as Window ?? GetWindow(owner);
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            _allItems = (items ?? new object[0]).Cast<object>().ToList();
            _filteredItems = CollectionViewSource.GetDefaultView(_allItems);
            _filteredItems.Filter = FilterItem;

            EntrySelector_ComboBox.ItemsSource = _filteredItems;
            CurrentSearchText = defaultValue;
            EntrySelector_ComboBox.Text = defaultValue;
            EntrySelector_ComboBox.IsDropDownOpen = true;
            EntrySelector_ComboBox.Focus();
        }

        //private InputComboBoxWPF(Window owner, string promptText, string titleText, IEnumerable<PackageEditorWPF.IndexedName> items, string defaultValue = "", bool topMost = false)
        //{
        //    DirectionsText = promptText;
        //    Topmost = topMost;
        //    Owner = owner;
        //    Title = titleText;
        //    DataContext = this;
        //    LoadCommands();
        //    InitializeComponent();
        //    if (owner == null)
        //    {
        //        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        //    }
        //    EntrySelector_ComboBox.ItemsSource = items;
        //    EntrySelector_ComboBox.SelectedItem = defaultValue;
        //    EntrySelector_ComboBox.Focus();
        //}

        public static string GetValue(Control owner, string promptText, string titleText, IEnumerable items, string defaultValue = "", bool topMost = false)
        {
            var dlg = new InputComboBoxDialog(owner, promptText, titleText, items, defaultValue, topMost);
            return dlg.ShowDialog() == true ? dlg.ChosenItem.ToString() : "";
        }

        //public static string GetValue(Window owner, string promptText, string titleText, IEnumerable<PackageEditorWPF.IndexedName> items, string defaultValue = "", bool topMost = false)
        //{
        //    var dlg = new InputComboBoxWPF(owner, promptText, titleText, items, defaultValue, topMost);
        //    return dlg.ShowDialog() == true ? dlg.ChosenItem : "";
        //}

        public ICommand OKCommand { get; set; }
        private void LoadCommands()
        {
            OKCommand = new GenericCommand(AcceptSelection, CanAcceptSelection);
        }

        private bool CanAcceptSelection()
        {
            return !string.IsNullOrWhiteSpace(CurrentSearchText);
        }

        private void AcceptSelection()
        {
            DialogResult = true;
            ChosenItem = CurrentSearchText;
        }

        private object ChosenItem;
        public string DirectionsText { get; }
        public string TitleText { get; } = @"TITLE NOT SET!";

        private string _currentSearchText = "";
        public string CurrentSearchText
        {
            get => _currentSearchText;
            set => SetProperty(ref _currentSearchText, value);
        }

        private bool FilterItem(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(CurrentSearchText))
            {
                return true;
            }

            return obj.ToString().Contains(CurrentSearchText, System.StringComparison.OrdinalIgnoreCase);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void EntrySelector_ComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && OKCommand.CanExecute(null))
            {
                OKCommand.Execute(null);
            }
        }

        private void EntrySelector_ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EntrySelector_ComboBox.SelectedItem != null)
            {
                CurrentSearchText = EntrySelector_ComboBox.SelectedItem.ToString();
            }
            else
            {
                CurrentSearchText = EntrySelector_ComboBox.Text;
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private void EntrySelector_ComboBox_KeyUp(object sender, KeyEventArgs e)
        {
            CurrentSearchText = EntrySelector_ComboBox.Text;
            _filteredItems.Refresh();
            EntrySelector_ComboBox.IsDropDownOpen = true;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
