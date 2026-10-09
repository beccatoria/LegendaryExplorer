using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Xceed.Wpf.Toolkit;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class LevelEditorLayoutTests
{
    [TestMethod]
    public void RedesignedLayout_ConstructsWithCustomControlsAndOriginalLightingDefaults()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = System.Windows.Application.Current ?? new System.Windows.Application();
                application.Resources = (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(
                    new Uri("/LegendaryExplorer;component/AppResources.xaml", UriKind.Relative));
                var editor = new LegendaryExplorer.Tools.LevelEditor.LevelEditor();
                foreach (string name in new[] { "SceneViewer", "MeshExportsList", "ActorFilter_TextBox", "Goto_TextBox",
                    "DisplayFilters_ComboBox", "Recents_MenuItem", "UndoMenuItem", "RedoMenuItem", "wrapPanel", "xPosUpDown", "yawUpDown" })
                {
                    Assert.IsNotNull(editor.FindName(name), name);
                }
                Assert.IsTrue(editor.UseGameShaders);
                Assert.AreEqual(ViewportLightingMode.Level, editor.LightingMode);
                Assert.IsTrue(editor.UseDynamicLighting);
                Assert.IsTrue(editor.UseLightMaps);
                Assert.HasCount(1, editor.ActorsView.GroupDescriptions);
                editor.GroupActorsByCategory = true;
                Assert.HasCount(2, editor.ActorsView.GroupDescriptions);
                editor.GroupActorsByCategory = false;
                Assert.HasCount(1, editor.ActorsView.GroupDescriptions);
                foreach (Control control in new Control[] { new CheckBox(), new RadioButton(), new Button(), new ToggleButton(),
                    new ComboBox(), new ComboBoxItem(), new Menu(), new MenuItem(), new ContextMenu(), new GroupBox(),
                    new SingleUpDown(), new IntegerUpDown() })
                {
                    control.Style = (Style)editor.FindResource(control.GetType());
                    Assert.AreEqual(Color.FromRgb(0xE2, 0xE2, 0xE2), ((SolidColorBrush)control.Foreground).Color, control.GetType().Name);
                    control.ApplyTemplate();
                    if (control is ButtonBase or ComboBox or ComboBoxItem or GroupBox)
                        Assert.IsNotNull(control.Template, control.GetType().Name);
                }
                var numericInput = (SingleUpDown)editor.FindName("xPosUpDown");
                Assert.AreEqual(Color.FromRgb(0x1D, 0x1D, 0x1D), ((SolidColorBrush)numericInput.Background).Color);
                Assert.IsTrue(numericInput.ShowButtonSpinner);
                numericInput.Measure(new Size(150, 30));
                numericInput.ApplyTemplate();
                var textInput = (WatermarkTextBox)numericInput.Template.FindName("PART_TextBox", numericInput);
                Assert.IsNotNull(textInput);
                Assert.AreEqual(((SolidColorBrush)numericInput.Foreground).Color, ((SolidColorBrush)textInput.Foreground).Color);
                var spinner = (ButtonSpinner)numericInput.Template.FindName("PART_Spinner", numericInput);
                spinner.ApplyTemplate();
                var decrease = (RepeatButton)spinner.Template.FindName("PART_DecreaseButton", spinner);
                decrease.ApplyTemplate();
                Assert.AreEqual("▾", ((TextBlock)decrease.Template.FindName("Arrow", decrease)).Text);
                var welcome = (Border)editor.FindName("WelcomePanel");
                Assert.AreEqual(Color.FromRgb(0x2D, 0x2D, 0x2D), ((SolidColorBrush)welcome.Background).Color);
                Assert.AreSame(editor.FindName("ViewportOptions"), ((CheckBox)editor.FindName("TopDownToggle")).Parent);
                Assert.AreEqual(11d, TextElement.GetFontSize((DependencyObject)editor.FindName("EditorToolbar")));
                var menu = new ContextMenu { Style = (Style)editor.FindResource(typeof(ContextMenu)) };
                menu.ApplyTemplate();
                Assert.IsNotNull(menu.Template);
                var menuEntry = new MenuItem { Header = "Open in Package Editor", InputGestureText = "Ctrl+G",
                    Template = (ControlTemplate)editor.FindResource("EditorContextMenuItem") };
                menuEntry.ApplyTemplate();
                Assert.IsNotNull(menuEntry.Template.FindName("Chrome", menuEntry));
                var separator = new Separator { Style = (Style)editor.FindResource(MenuItem.SeparatorStyleKey) };
                separator.ApplyTemplate();
                Assert.IsNotNull(separator.Template);
                foreach (var orientation in new[] { Orientation.Vertical, Orientation.Horizontal })
                {
                    var scrollbar = new ScrollBar { Orientation = orientation, Maximum = 100, ViewportSize = 10,
                        Style = (Style)editor.FindResource(typeof(ScrollBar)) };
                    scrollbar.ApplyTemplate();
                    var track = (Track)scrollbar.Template.FindName("PART_Track", scrollbar);
                    Assert.AreEqual(orientation, track.Orientation);
                    Assert.AreEqual(orientation == Orientation.Vertical, track.IsDirectionReversed);
                    Assert.AreEqual(Color.FromRgb(0x1D, 0x1D, 0x1D), ((SolidColorBrush)scrollbar.Background).Color);
                    Assert.AreEqual(orientation == Orientation.Vertical ? ScrollBar.PageDownCommand : ScrollBar.PageRightCommand,
                        track.IncreaseRepeatButton.Command);
                    track.Value = 25;
                    Assert.AreEqual(25d, scrollbar.Value);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new AssertFailedException($"Level Editor layout construction failed: {failure}", failure);
    }
}
