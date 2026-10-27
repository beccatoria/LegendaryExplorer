using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LegendaryExplorer.Tools.LevelEditor;
using EditorWindow = LegendaryExplorer.Tools.LevelEditor.LevelEditor;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

internal static class LevelEditorTestHost
{
    public static Task RunAsync(Func<EditorWindow, Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                EditorWindow editor = null;
                Exception failure = null;
                try
                {
                    var application = System.Windows.Application.Current ?? new System.Windows.Application();
                    application.Resources = (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(
                        new Uri("/LegendaryExplorer;component/AppResources.xaml", UriKind.Relative));
                    editor = new EditorWindow(false, (description, exception) => throw new InvalidOperationException(description, exception));
                    await test(editor);
                }
                catch (Exception exception) { failure = exception; }
                finally
                {
                    try
                    {
                        if (editor is not null)
                        {
                            await Queue(editor).StopAsync();
                            Invoke(editor, "CloseAllFiles");
                            if (editor.RenderContext.Backbuffer is not null) editor.RenderContext.DisposeSizeDependentResources();
                            editor.RenderContext.DisposeResources();
                        }
                    }
                    catch (Exception exception) { failure ??= exception; }
                    if (failure is null) completion.TrySetResult();
                    else completion.TrySetException(failure);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return completion.Task;
    }

    public static LevelEditorOperationQueue Queue(EditorWindow editor)
        => (LevelEditorOperationQueue)typeof(EditorWindow).GetField("_sceneOperations", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);

    public static object Invoke(EditorWindow editor, string method, params object[] arguments)
        => typeof(EditorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, arguments);

    public static Task RunOperation(EditorWindow editor, bool replace, Func<CancellationToken, Task> operation)
        => (Task)Invoke(editor, "RunSceneOperationAsync", replace, "Test scene operation", operation);

    public static Task AddLevel(EditorWindow editor, string path)
        => RunOperation(editor, false, token => (Task)Invoke(editor, "AddLevelFile", path, token));

}
