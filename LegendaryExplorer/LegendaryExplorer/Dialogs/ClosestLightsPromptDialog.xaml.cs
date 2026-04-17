using LegendaryExplorer.SharedUI.Bases;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;

namespace LegendaryExplorer.Dialogs
{
    public partial class ClosestLightsPromptDialog : TrackingNotifyPropertyChangedWindowBase
    {
        private float _x;
        public float X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        private float _y;
        public float Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        private float _z;
        public float Z
        {
            get => _z;
            set => SetProperty(ref _z, value);
        }

        private int _lightCount;
        public int LightCount
        {
            get => _lightCount;
            set => SetProperty(ref _lightCount, value < 1 ? 1 : value);
        }

        public ClosestLightsPromptDialog(Vector3 defaultPosition, int defaultLightCount) : base("Closest Lights Prompt Dialog", false)
        {
            DataContext = this;
            X = defaultPosition.X;
            Y = defaultPosition.Y;
            Z = defaultPosition.Z;
            LightCount = defaultLightCount;
            InitializeComponent();
        }

        public static bool Prompt(Control owner, Vector3 defaultPosition, int defaultLightCount, out Vector3 position, out int lightCount)
        {
            var inst = new ClosestLightsPromptDialog(defaultPosition, defaultLightCount);
            if (owner != null)
            {
                inst.Owner = owner as Window ?? GetWindow(owner);
                inst.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            inst.ShowDialog();
            if (inst.DialogResult == true)
            {
                position = new Vector3(inst.X, inst.Y, inst.Z);
                lightCount = inst.LightCount;
                return true;
            }

            position = Vector3.Zero;
            lightCount = default;
            return false;
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
