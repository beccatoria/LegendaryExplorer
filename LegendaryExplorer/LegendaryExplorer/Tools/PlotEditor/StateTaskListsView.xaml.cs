using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Gammtek.Conduit.MassEffect3.SFXGame.QuestMap;
using LegendaryExplorer.Misc;
using LegendaryExplorerCore.Gammtek;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.PlotDatabase;
using LegendaryExplorer.Tools.PlotEditor.Dialogs;

namespace LegendaryExplorer.Tools.PlotEditor
{
	/// <summary>
	/// Interaction logic for StateTaskListsView.xaml
	/// </summary>
	public partial class StateTaskListsView : NotifyPropertyChangedControlBase
    {
		public StateTaskListsView()
		{
			InitializeComponent();
            SetStateTaskLists(null);
        }
        private KeyValuePair<int, BioStateTaskList> _selectedStateTaskList;
        private BioTaskEval _selectedTaskEval;
        private ObservableCollection<KeyValuePair<int, BioStateTaskList>> _stateTaskLists;
        private readonly Dictionary<int, BioQuest> _questsById = new();
        private string _taskEvalType = "bool";
        private MEGame _currentGame = MEGame.Unknown;
        private string _questFilterText;
        private ICollectionView _filteredStateTaskLists;

        public bool CanAddTaskEval
        {
            get
            {
                if (StateTaskLists == null
                    || !StateTaskLists.Any())
                {
                    return false;
                }

                return SelectedStateTaskList.Value != null;
            }
        }

        public bool CanRemoveStateTaskList
        {
            get
            {
                if (StateTaskLists == null
                    || !StateTaskLists.Any())
                {
                    return false;
                }

                return SelectedStateTaskList.Value != null;
            }
        }

        public bool CanRemoveTaskEval
        {
            get
            {
                if (StateTaskLists == null
                    || !StateTaskLists.Any())
                {
                    return false;
                }

                if (SelectedStateTaskList.Value?.TaskEvals == null || !SelectedStateTaskList.Value.TaskEvals.Any())
                {
                    return false;
                }

                return SelectedTaskEval != null;
            }
        }

        public KeyValuePair<int, BioStateTaskList> SelectedStateTaskList
        {
            get => _selectedStateTaskList;
            set
            {
                SetProperty(ref _selectedStateTaskList, value);
                OnPropertyChanged(nameof(CanAddTaskEval));
                OnPropertyChanged(nameof(CanRemoveStateTaskList));
                OnPropertyChanged(nameof(CanRemoveTaskEval));
            }
        }

        public BioTaskEval SelectedTaskEval
        {
            get => _selectedTaskEval;
            set
            {
                if (_selectedTaskEval is INotifyPropertyChanged oldTaskEval)
                {
                    oldTaskEval.PropertyChanged -= SelectedTaskEvalOnPropertyChanged;
                }

                SetProperty(ref _selectedTaskEval, value);

                if (_selectedTaskEval is INotifyPropertyChanged newTaskEval)
                {
                    newTaskEval.PropertyChanged += SelectedTaskEvalOnPropertyChanged;
                }

                OnPropertyChanged(nameof(CanRemoveTaskEval));
                OnPropertyChanged(nameof(SelectedTaskEvalQuestDisplay));
            }
        }

        public string TaskEvalType
        {
            get => _taskEvalType;
            set => SetProperty(ref _taskEvalType, value);
        }

        public MEGame CurrentGame
        {
            get => _currentGame;
            set => SetProperty(ref _currentGame, value);
        }

        public string SelectedTaskEvalQuestDisplay => GetQuestDisplay(SelectedTaskEval?.Quest);

        public ObservableCollection<KeyValuePair<int, BioStateTaskList>> StateTaskLists
        {
            get => _stateTaskLists;
            set
            {
                SetProperty(ref _stateTaskLists, value);
                FilteredStateTaskLists = value is null
                    ? null
                    : CollectionViewSource.GetDefaultView(value);

                if (FilteredStateTaskLists != null)
                {
                    FilteredStateTaskLists.Filter = ShouldIncludeStateTaskList;
                }

                RefreshTaskEvalFilter();
                OnPropertyChanged(nameof(CanAddTaskEval));
                OnPropertyChanged(nameof(CanRemoveStateTaskList));
                OnPropertyChanged(nameof(CanRemoveTaskEval));
            }
        }

        public ICollectionView FilteredStateTaskLists
        {
            get => _filteredStateTaskLists;
            private set => SetProperty(ref _filteredStateTaskLists, value);
        }

        public string QuestFilterText
        {
            get => _questFilterText;
            set
            {
                SetProperty(ref _questFilterText, value);
                RefreshTaskEvalFilter();
            }
        }

        public void SetTaskEvalContext(MEGame game, string taskEvalType)
        {
            CurrentGame = game;
            TaskEvalType = string.IsNullOrWhiteSpace(taskEvalType)
                ? "bool"
                : taskEvalType;
        }

        public void SetQuestLookup(IEnumerable<KeyValuePair<int, BioQuest>> quests)
        {
            _questsById.Clear();

            if (quests == null)
            {
                OnPropertyChanged(nameof(SelectedTaskEvalQuestDisplay));
                return;
            }

            foreach (var quest in quests)
            {
                _questsById[quest.Key] = quest.Value;
            }

            OnPropertyChanged(nameof(SelectedTaskEvalQuestDisplay));
            RefreshTaskEvalFilter();
        }

        public void AddStateTaskList()
        {
            if (StateTaskLists == null)
            {
                StateTaskLists = InitCollection<KeyValuePair<int, BioStateTaskList>>();
            }

            var dlg = new NewObjectDialog
            {
                ContentText = "New StateTaskList",
                ObjectId = (GetMaxStateTaskListId() + 1)
            };

            if (dlg.ShowDialog() == false || dlg.ObjectId < 0)
            {
                return;
            }

            AddStateTaskList(dlg.ObjectId);
        }

        public void AddStateTaskList(int id, BioStateTaskList taskList = null)
        {
            if (StateTaskLists == null)
            {
                StateTaskLists = InitCollection<KeyValuePair<int, BioStateTaskList>>();
            }

            if (id < 0)
            {
                return;
            }

            if (taskList == null)
            {
                taskList = new BioStateTaskList();
            }

            taskList.TaskEvals = taskList.TaskEvals != null
                ? InitCollection(taskList.TaskEvals)
                : InitCollection<BioTaskEval>();

            var stateTaskList = new KeyValuePair<int, BioStateTaskList>(id, taskList);

            StateTaskLists.Add(stateTaskList);

            SelectedStateTaskList = stateTaskList;
        }

        public void AddTaskEval()
        {
            AddTaskEval(null);
        }

        public void AddTaskEval(BioTaskEval taskEval)
        {
            if (StateTaskLists == null || SelectedStateTaskList.Value == null)
            {
                return;
            }

            if (taskEval == null)
            {
                taskEval = new BioTaskEval();
            }

            SelectedStateTaskList.Value.TaskEvals.Add(taskEval);

            SelectedTaskEval = taskEval;
        }

        public void ChangeStateTaskListId()
        {
            if (SelectedStateTaskList.Value == null)
            {
                return;
            }

            var dlg = new ChangeObjectIdDialog
            {
                ContentText = $"Change id of StateTaskList #{SelectedStateTaskList.Key}",
                ObjectId = SelectedStateTaskList.Key
            };

            if (dlg.ShowDialog() == false || dlg.ObjectId < 0)
            {
                return;
            }

            var stateTaskList = SelectedStateTaskList.Value;

            StateTaskLists.Remove(SelectedStateTaskList);

            AddStateTaskList(dlg.ObjectId, stateTaskList);
        }

        public void CopyStateTaskList()
        {
            if (SelectedStateTaskList.Value == null)
            {
                return;
            }

            var dlg = new CopyObjectDialog
            {
                ContentText = $"Copy StateTaskList {SelectedStateTaskList.Key}",
                ObjectId = SelectedStateTaskList.Key
            };

            if (dlg.ShowDialog() == false || dlg.ObjectId < 0 || SelectedStateTaskList.Key == dlg.ObjectId)
            {
                return;
            }

            AddStateTaskList(dlg.ObjectId, new BioStateTaskList(SelectedStateTaskList.Value));
        }

        public void CopyTaskEval()
        {
            if (StateTaskLists == null || SelectedStateTaskList.Value == null || SelectedTaskEval == null)
            {
                return;
            }

            AddTaskEval(new BioTaskEval(SelectedTaskEval));
        }

        public void RemoveStateTaskList()
        {
            if (StateTaskLists == null || SelectedStateTaskList.Value == null)
            {
                return;
            }

            var index = StateTaskLists.IndexOf(SelectedStateTaskList);

            if (!StateTaskLists.Remove(SelectedStateTaskList))
            {
                return;
            }

            if (StateTaskLists.Any())
            {
                SelectedStateTaskList = ((index - 1) >= 0)
                    ? StateTaskLists[index - 1]
                    : StateTaskLists.First();
            }
        }

        private bool ShouldIncludeStateTaskList(object obj)
        {
            if (obj is not KeyValuePair<int, BioStateTaskList> stateTaskList)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(QuestFilterText))
            {
                return true;
            }

            if (stateTaskList.Value?.TaskEvals == null)
            {
                return false;
            }

            var filter = QuestFilterText.Trim();
            var hasQuestIdFilter = int.TryParse(filter, out var questIdFilter);

            foreach (var taskEval in stateTaskList.Value.TaskEvals)
            {
                if (taskEval == null)
                {
                    continue;
                }

                if (hasQuestIdFilter && taskEval.Quest == questIdFilter)
                {
                    return true;
                }

                var questDisplay = GetQuestDisplay(taskEval.Quest);
                if (!string.IsNullOrWhiteSpace(questDisplay)
                    && questDisplay.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if ($"Quest {taskEval.Quest}".Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshTaskEvalFilter()
        {
            FilteredStateTaskLists?.Refresh();
        }

        private void SelectedTaskEvalOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BioTaskEval.Quest))
            {
                OnPropertyChanged(nameof(SelectedTaskEvalQuestDisplay));
                RefreshTaskEvalFilter();
            }
        }

        public void RemoveTaskEval()
        {
            if (StateTaskLists == null || SelectedStateTaskList.Value == null || SelectedTaskEval == null)
            {
                return;
            }

            var index = SelectedStateTaskList.Value.TaskEvals.IndexOf(SelectedTaskEval);

            if (!SelectedStateTaskList.Value.TaskEvals.Remove(SelectedTaskEval))
            {
                return;
            }

            if (SelectedStateTaskList.Value.TaskEvals.Any())
            {
                SelectedTaskEval = ((index - 1) >= 0)
                    ? SelectedStateTaskList.Value.TaskEvals[index - 1]
                    : SelectedStateTaskList.Value.TaskEvals.First();
            }
        }

        public void SetStateTaskLists(IEnumerable<KeyValuePair<int, BioStateTaskList>> collection)
        {
            if (collection == null)
            {
                StateTaskLists = new ObservableCollection<KeyValuePair<int, BioStateTaskList>>();
            }
            else
            {
                StateTaskLists = InitCollection(collection);

                foreach (var taskEval in StateTaskLists)
                {
                    taskEval.Value.TaskEvals = InitCollection(taskEval.Value.TaskEvals);
                }
            }
        }

        
        private static ObservableCollection<T> InitCollection<T>()
        {
            return new ObservableCollection<T>();
        }

        
        private static ObservableCollection<T> InitCollection<T>(IEnumerable<T> collection)
        {
            if (collection == null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(collection));
            }

            return new ObservableCollection<T>(collection);
        }

        private int GetMaxStateTaskListId()
        {
            return StateTaskLists.Any() ? StateTaskLists.Max(b => b.Key) : -1;
        }

        private string GetQuestDisplay(int? questId)
        {
            if (questId == null)
            {
                return string.Empty;
            }

            if (!_questsById.TryGetValue(questId.Value, out var quest) || quest == null)
            {
                return string.Empty;
            }

            var questName = quest.QuestName;
            if (string.IsNullOrWhiteSpace(questName))
            {
                return $"Quest {questId.Value}";
            }

            var tlkId = quest.QuestNameTlkId;
            if (tlkId > 0)
            {
                return $"{questName} (TLK {tlkId})";
            }

            return questName;
        }

        private void ChangeStateTaskListId_Click(object sender, RoutedEventArgs e)
        {
            ChangeStateTaskListId();
        }

        private void CopyStateTaskList_Click(object sender, RoutedEventArgs e)
        {
            CopyStateTaskList();
        }

        private void RemoveStateTaskList_Click(object sender, RoutedEventArgs e)
        {
            RemoveStateTaskList();
        }

        private void AddStateTaskList_Click(object sender, RoutedEventArgs e)
        {
            AddStateTaskList();
        }

        private void CopyTaskEval_Click(object sender, RoutedEventArgs e)
        {
            CopyTaskEval();
        }

        private void RemoveTaskEval_Click(object sender, RoutedEventArgs e)
        {
            RemoveTaskEval();
        }

        private void AddTaskEval_Click(object sender, RoutedEventArgs e)
        {
            AddTaskEval();
        }
    }

    public class TaskEvalPlotPathConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 || values[0] is not MEGame game || values[1] is not int index || values[2] is not string taskEvalType)
            {
                return string.Empty;
            }

            return taskEvalType switch
            {
                "int" => PlotDatabases.FindPlotIntByID(index, game)?.Path,
                "float" => PlotDatabases.FindPlotFloatByID(index, game)?.Path,
                _ => PlotDatabases.FindPlotBoolByID(index, game)?.Path
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
