using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using EditorWindow = LegendaryExplorer.Tools.LevelEditor.LevelEditor;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class LevelEditorGpuTests
{
    public TestContext TestContext { get; set; }
    [ClassInitialize]
    public static void Initialize(TestContext _) => LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);

    [TestMethod]
    [TestCategory("GPU")]
    public async Task FreighterGpu_LoadRenderUpdateReloadAndCancelReleaseSceneResources()
    {
        string directory = Environment.GetEnvironmentVariable("LEX_VISIBLE_SETS_TEST_DIRECTORY");
        if (Environment.GetEnvironmentVariable("LEX_LEVEL_EDITOR_GPU") != "1" || string.IsNullOrWhiteSpace(directory))
            Assert.Inconclusive("Set LEX_LEVEL_EDITOR_GPU=1 and LEX_VISIBLE_SETS_TEST_DIRECTORY for the opt-in hardware test.");
        directory = Path.GetFullPath(directory);
        string freighter = Path.Combine(directory, "BioD_Freighter.pcc");
        string cargo = Path.Combine(directory, "BioD_Freighter_CargoBay.pcc");
        bool oldTracking = SharpDX.Configuration.EnableObjectTracking;
        bool oldThreadTracking = SharpDX.Configuration.UseThreadStaticObjectTracking;
        SharpDX.Configuration.EnableObjectTracking = true;
        SharpDX.Configuration.UseThreadStaticObjectTracking = false;
        try
        {
        await LevelEditorTestHost.RunAsync(async editor =>
        {
            EnsureDefaultTexture();
            var context = editor.RenderContext;
            context.CreateResources();
            context.ShowDebugStatsOverlay = false;
            var target = new Texture2D(context.Device, new Texture2DDescription
            {
                Width = 640, Height = 360, MipLevels = 1, ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource, Usage = ResourceUsage.Default
            });
            context.CreateSizeDependentResources(640, 360, target);
            context.RenderScene += typeof(EditorWindow).GetMethod("RenderScene", BindingFlags.Instance | BindingFlags.NonPublic)
                .CreateDelegate<EventHandler>(editor);
            context.UpdateScene += typeof(EditorWindow).GetMethod("UpdateScene", BindingFlags.Instance | BindingFlags.NonPublic)
                .CreateDelegate<EventHandler<float>>(editor);

            // The second replacement cancels the first, including work already inside a GPU constructor.
            var firstLoad = editor.LoadFileAsync(freighter);
            var replacingLoad = editor.LoadFileAsync(cargo);
            await Task.WhenAll(firstLoad, replacingLoad);
            Assert.HasCount(1, editor.OpenFiles);
            Assert.AreEqual(cargo, editor.OpenFiles[0].FilePath, ignoreCase: true);
            Assert.AreEqual(editor.Actors.Count, context.DrawList_3D.Count);
            TestContext.WriteLine($"Overlapping replacement retained only CargoBay: {editor.Actors.Count} actors.");

            await LevelEditorTestHost.AddLevel(editor, freighter);
            Assert.HasCount(2, editor.OpenFiles);
            editor.ObjectRenderMode = ObjectRenderMode.VisibleSetOnly;
            LevelEditorTestHost.Invoke(editor, "ShowAllMeshes");
            editor.IsOrthographicView = true;
            context.Camera.Position = new Vector3(-50.293232f, 6543.474f, 40000);
            context.Camera.OrthoWidth = 9606.514f;
            editor.VisibleSetDistance = 5000;
            for (int i = 0; i < 5; i++) { context.Update(1f / 60); context.Render(); }
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < 8; i++) { context.Update(1f / 60); context.Render(); }
            byte[] pixels = ReadPixels(context, target);
            Assert.IsNull(context.ErrorText, context.ErrorText);
            TestContext.WriteLine($"Both packages: {editor.Actors.Count} actors, {context.GetLights(out _).Length} lights; 8 hardware render submissions plus GPU readback: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2} ms at 640x360. This is not interactive FPS.");
            AssertCentralGeometry(pixels, 640, 360);
            string artifactDirectory = Environment.GetEnvironmentVariable("LEX_LEVEL_EDITOR_ARTIFACT_DIRECTORY") ?? TestContext.TestRunDirectory;
            Directory.CreateDirectory(artifactDirectory);
            string image = Path.Combine(artifactDirectory, "LevelEditor-Freighter-GPU.png");
            var bitmap = BitmapSource.Create(640, 360, 96, 96, PixelFormats.Bgra32, null, pixels, 640 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(image)) encoder.Save(stream);
            TestContext.AddResultFile(image);

            var file = editor.OpenFiles.Single(f => string.Equals(f.FilePath, freighter, StringComparison.OrdinalIgnoreCase));
            var mesh = file.Actors.First(a => a.Export.ClassName == "StaticMeshActor" && context.IsActorVisible(a)
                && !file.LightDependencies.NeedsLightRefresh([new(PackageChange.ExportData, a.Export.UIndex)]));
            context.Visibility.SetActorVisibility(mesh.VisibilityKey, false);
            context.RefreshLightVisibility();
            context.GetLights(out int beforeUpdate);
            Assert.IsFalse(file.LightDependencies.NeedsLightRefresh([new(PackageChange.ExportData, mesh.Export.UIndex)]));
            file.HandleUpdate([new(PackageChange.ExportData, mesh.Export.UIndex)]);
            Assert.IsFalse(context.IsActorVisible(file.Actors.Single(a => a.Export.UIndex == mesh.Export.UIndex)));
            context.GetLights(out int afterUpdate);
            Assert.AreEqual(beforeUpdate, afterUpdate, "A mesh update must not recreate every level light.");
            context.Render();

            int actorCount = editor.Actors.Count;
            int lightingCount = StaticLightingCount(context);
            var process = Process.GetCurrentProcess();
            var memory = new List<long>();
            var heap = new List<long>();
            var objects = new List<int>();
            for (int i = 0; i < 5; i++)
            {
                await LevelEditorTestHost.RunOperation(editor, false, _ =>
                {
                    LevelEditorTestHost.Invoke(editor, "ReloadFile", file);
                    return Task.CompletedTask;
                });
                context.Render();
                Assert.IsNull(context.ErrorText, context.ErrorText);
                ReadPixels(context, target);
                Assert.AreEqual(actorCount, editor.Actors.Count);
                Assert.AreEqual(actorCount, context.DrawList_3D.Count);
                Assert.AreEqual(lightingCount, StaticLightingCount(context));
                Assert.IsFalse(context.HasPendingGameShaderLoads);
                Assert.IsFalse(context.IsActorVisible(file.Actors.Single(a => a.Export.UIndex == mesh.Export.UIndex)));
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                heap.Add(GC.GetTotalMemory(false));
                objects.Add(SharpDX.Diagnostics.ObjectTracker.FindActiveObjects().Count);
                process.Refresh();
                memory.Add(process.PrivateMemorySize64);
            }
            TestContext.WriteLine($"Five GPU reloads retained {actorCount} actors and {lightingCount} static-lighting registrations. After collection, private memory (MiB): {string.Join(", ", memory.Select(bytes => (bytes / 1048576d).ToString("F1")))}; live managed heap (MiB): {string.Join(", ", heap.Select(bytes => (bytes / 1048576d).ToString("F1")))}; tracked COM wrappers: {string.Join(", ", objects)}.");
            Assert.HasCount(1, objects.Skip(2).Distinct().ToList(), "Native wrapper count kept growing after warm-up.");
            Assert.IsFalse(file.Package.IsModified);
            LevelEditorTestHost.Invoke(editor, "CloseAllFiles");
            Assert.IsEmpty(context.DrawList_3D);
            Assert.IsEmpty(context.GetLights(out _));
            Assert.AreEqual(0, StaticLightingCount(context));
            Assert.IsFalse(context.HasPendingGameShaderLoads);

            var cancelledLoad = editor.LoadFileAsync(freighter);
            await LevelEditorTestHost.Queue(editor).StopAsync();
            await cancelledLoad;
            Assert.IsEmpty(editor.OpenFiles);
            Assert.IsEmpty(context.DrawList_3D);
            Assert.IsEmpty(context.GetLights(out _));
            Assert.AreEqual(0, StaticLightingCount(context));
            Assert.IsFalse(context.HasPendingGameShaderLoads);
        }).WaitAsync(TimeSpan.FromMinutes(4));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        TestContext.WriteLine($"Tracked COM wrappers after scene/context teardown: {SharpDX.Diagnostics.ObjectTracker.FindActiveObjects().Count}.");
        }
        finally
        {
            SharpDX.Configuration.EnableObjectTracking = oldTracking;
            SharpDX.Configuration.UseThreadStaticObjectTracking = oldThreadTracking;
        }
    }

    private static int StaticLightingCount(MeshRenderContext context)
    {
        var registrations = typeof(MeshRenderContext).GetField("StaticLightings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context);
        return (int)registrations.GetType().GetProperty("Count").GetValue(registrations);
    }

    private static void EnsureDefaultTexture()
    {
        string destination = Path.Combine(AppContext.BaseDirectory, "exec", "Default.png");
        if (File.Exists(destination)) return;
        string source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../LegendaryExplorer/Resources/exec/Default.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination);
    }

    private static byte[] ReadPixels(MeshRenderContext context, Texture2D target)
    {
        var description = target.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CpuAccessFlags = CpuAccessFlags.Read;
        description.OptionFlags = ResourceOptionFlags.None;
        using var staging = new Texture2D(context.Device, description);
        context.ImmediateContext.CopyResource(target, staging);
        var mapped = context.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
        try
        {
            byte[] pixels = new byte[description.Width * description.Height * 4];
            for (int y = 0; y < description.Height; y++)
                Marshal.Copy(mapped.DataPointer + y * mapped.RowPitch, pixels, y * description.Width * 4, description.Width * 4);
            return pixels;
        }
        finally { context.ImmediateContext.UnmapSubresource(staging, 0); }
    }

    private static void AssertCentralGeometry(byte[] pixels, int width, int height)
    {
        var colors = new HashSet<int>();
        for (int y = height / 4; y < height * 3 / 4; y++)
            for (int x = width / 4; x < width * 3 / 4; x++)
            {
                int i = (y * width + x) * 4;
                colors.Add(pixels[i] | pixels[i + 1] << 8 | pixels[i + 2] << 16);
            }
        Assert.IsGreaterThan(20, colors.Count, "The central viewport is blank or lacks rendered geometry.");
    }
}
