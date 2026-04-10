using System;
using System.Numerics;
using System.Windows;

namespace LegendaryExplorer.Dialogs
{
    public partial class LightFinderDialog : Window
    {
        public LightFinderDialog()
        {
            InitializeComponent();
            TxtX.Focus();
        }

        public Vector3? Target { get; private set; }
        public int Count { get; private set; }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            TxtValidation.Visibility = Visibility.Collapsed;
            if (!float.TryParse(TxtX.Text, out var x))
            {
                TxtValidation.Text = "X must be a number";
                TxtValidation.Visibility = Visibility.Visible;
                return;
            }
            if (!float.TryParse(TxtY.Text, out var y))
            {
                TxtValidation.Text = "Y must be a number";
                TxtValidation.Visibility = Visibility.Visible;
                return;
            }
            if (!float.TryParse(TxtZ.Text, out var z))
            {
                TxtValidation.Text = "Z must be a number";
                TxtValidation.Visibility = Visibility.Visible;
                return;
            }
            if (!int.TryParse(TxtCount.Text, out var c) || c <= 0)
            {
                TxtValidation.Text = "Count must be a positive integer";
                TxtValidation.Visibility = Visibility.Visible;
                return;
            }

            Target = new Vector3(x, y, z);
            Count = c;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}