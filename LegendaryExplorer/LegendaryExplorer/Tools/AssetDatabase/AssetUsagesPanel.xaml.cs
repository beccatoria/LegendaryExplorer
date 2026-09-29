using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LegendaryExplorer.Tools.AssetDatabase
{
    public partial class AssetUsagesPanel : UserControl
    {
        public static readonly DependencyProperty UsagesSourceProperty =
            DependencyProperty.Register(nameof(UsagesSource), typeof(IEnumerable), typeof(AssetUsagesPanel),
                new PropertyMetadata(null, OnUsagesSourceChanged));

        public static readonly DependencyProperty HeaderTextProperty =
            DependencyProperty.Register(nameof(HeaderText), typeof(string), typeof(AssetUsagesPanel),
                new PropertyMetadata("Usages"));

        public static readonly DependencyProperty HeaderToolTipProperty =
            DependencyProperty.Register(nameof(HeaderToolTip), typeof(string), typeof(AssetUsagesPanel));

        public static readonly DependencyProperty UsageContextMenuProperty =
            DependencyProperty.Register(nameof(UsageContextMenu), typeof(ContextMenu), typeof(AssetUsagesPanel),
                new PropertyMetadata(null, OnUsageContextMenuChanged));

        public IEnumerable UsagesSource
        {
            get => (IEnumerable)GetValue(UsagesSourceProperty);
            set => SetValue(UsagesSourceProperty, value);
        }

        public string HeaderText
        {
            get => (string)GetValue(HeaderTextProperty);
            set => SetValue(HeaderTextProperty, value);
        }

        public string HeaderToolTip
        {
            get => (string)GetValue(HeaderToolTipProperty);
            set => SetValue(HeaderToolTipProperty, value);
        }

        public ContextMenu UsageContextMenu
        {
            get => (ContextMenu)GetValue(UsageContextMenuProperty);
            set => SetValue(UsageContextMenuProperty, value);
        }

        public object SelectedItem => internalListBox.SelectedItem;

        public int SelectedIndex => internalListBox.SelectedIndex;

        private ICollectionView usagesView;
        private Regex fileFilterRegex;
        private string fileNameFilterText;

        public AssetUsagesPanel()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (internalListBox.ContextMenu == null && UsageContextMenu == null)
            {
                SetDefaultContextMenu();
            }

            RefreshUsageView();
        }

        private static void OnUsagesSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AssetUsagesPanel panel)
            {
                panel.RefreshUsageView();
            }
        }

        private static void OnUsageContextMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AssetUsagesPanel panel)
            {
                panel.internalListBox.ContextMenu = e.NewValue as ContextMenu;
            }
        }

        private void SetDefaultContextMenu()
        {
            var menu = new ContextMenu();
            var openUsageItem = new MenuItem
            {
                Header = "Open Usage",
                ToolTip = "Opens this Usage in Package Editor."
            };
            openUsageItem.SetBinding(MenuItem.CommandProperty,
                new System.Windows.Data.Binding("OpenUsagePkgCommand"));

            var openExplorerItem = new MenuItem
            {
                Header = "Open in Windows Explorer",
                ToolTip = "Opens this file in Windows Explorer."
            };
            openExplorerItem.SetBinding(MenuItem.CommandProperty,
                new System.Windows.Data.Binding("OpenInWindowsExplorerCommand"));

            menu.Items.Add(openUsageItem);
            menu.Items.Add(openExplorerItem);
            internalListBox.ContextMenu = menu;
        }

        private void RefreshUsageView()
        {
            usagesView = CollectionViewSource.GetDefaultView(UsagesSource);
            if (usagesView is not null)
            {
                usagesView.Filter = UsageMatchesCurrentFilters;
            }

            internalListBox.ItemsSource = usagesView;
            ApplyUsageFilter();
        }

        private bool UsageMatchesCurrentFilters(object item)
        {
            if (item is not IAssetUsage usage)
            {
                return true;
            }

            if (!MatchesImportExportFilter(usage))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(fileNameFilterText))
            {
                return true;
            }

            var usageFileName = GetUsageFileNameText(usage);
            if (fileFilterRegex is not null)
            {
                return fileFilterRegex.IsMatch(usageFileName);
            }

            return usageFileName.Contains(fileNameFilterText, StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesImportExportFilter(IAssetUsage usage)
        {
            bool showImports = importsFilterCheckBox?.IsChecked != false;
            bool showExports = exportsFilterCheckBox?.IsChecked != false;

            if (!showImports && !showExports)
            {
                return false;
            }

            bool isImport = usage.UIndex < 0;
            bool isExport = usage.UIndex > 0;

            if (!showImports && isImport)
            {
                return false;
            }

            if (!showExports && isExport)
            {
                return false;
            }

            return true;
        }

        private string GetUsageFileNameText(IAssetUsage usage)
        {
            if (Window.GetWindow(this) is AssetDatabaseWindow window
                && usage.FileKey >= 0
                && usage.FileKey < window.FileListExtended.Count)
            {
                var file = window.FileListExtended[usage.FileKey];
                return $"{file.FileName} {file.Directory}";
            }

            return usage.FileKey.ToString();
        }

        private void ApplyUsageFilter()
        {
            if (fileNameFilterTextBox is null)
            {
                fileNameFilterText = string.Empty;
                fileFilterRegex = null;
                return;
            }

            fileNameFilterText = fileNameFilterTextBox.Text?.Trim() ?? string.Empty;
            fileFilterRegex = null;
            if (!string.IsNullOrEmpty(fileNameFilterText)
                && (fileNameFilterText.Contains('*') || fileNameFilterText.Contains('?')))
            {
                var escaped = Regex.Escape(fileNameFilterText)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".");
                fileFilterRegex = new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            usagesView?.Refresh();
        }

        private void UsageFilterChanged(object sender, RoutedEventArgs e)
        {
            ApplyUsageFilter();
        }

        public IEnumerable<IAssetUsage> GetVisibleUsages()
        {
            if (usagesView is null)
            {
                return UsagesSource?.OfType<IAssetUsage>() ?? Enumerable.Empty<IAssetUsage>();
            }

            return usagesView.Cast<object>().OfType<IAssetUsage>().ToList();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is AssetDatabaseWindow window)
            {
                window.CopyUsagesFromPanel(this);
            }
        }
    }
}
