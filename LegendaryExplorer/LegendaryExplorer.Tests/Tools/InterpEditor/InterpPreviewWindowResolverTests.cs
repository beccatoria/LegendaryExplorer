using LegendaryExplorer.Tools.InterpEditor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewWindowResolverTests
{
    [TestMethod]
    public void ResolveOrCreate_ReturnsExisting_WhenUsable()
    {
        var existing = new TestWindowRef { IsLoaded = true };
        var current = new TestWindowRef { IsLoaded = true };

        TestWindowRef resolved = InterpPreviewWindowResolver.ResolveOrCreate(
            current,
            existing,
            factory: () => throw new AssertFailedException("Factory should not be called."),
            isUsable: window => window.IsLoaded);

        Assert.AreSame(existing, resolved);
    }

    [TestMethod]
    public void ResolveOrCreate_ReturnsCurrent_WhenExistingMissingAndCurrentUsable()
    {
        var current = new TestWindowRef { IsLoaded = true };

        TestWindowRef resolved = InterpPreviewWindowResolver.ResolveOrCreate(
            current,
            existing: null,
            factory: () => throw new AssertFailedException("Factory should not be called."),
            isUsable: window => window.IsLoaded);

        Assert.AreSame(current, resolved);
    }

    [TestMethod]
    public void ResolveOrCreate_UsesFactory_WhenNoUsableWindowAvailable()
    {
        var created = new TestWindowRef { IsLoaded = true };

        TestWindowRef resolved = InterpPreviewWindowResolver.ResolveOrCreate(
            current: null,
            existing: null,
            factory: () => created,
            isUsable: window => window.IsLoaded);

        Assert.AreSame(created, resolved);
    }

    [TestMethod]
    public void ResolveOrCreate_InvokesNormalizer_ForReturnedWindow()
    {
        var existing = new TestWindowRef { IsLoaded = true };
        int normalizeCalls = 0;

        TestWindowRef resolved = InterpPreviewWindowResolver.ResolveOrCreate(
            current: null,
            existing,
            factory: () => throw new AssertFailedException("Factory should not be called."),
            isUsable: window => window.IsLoaded,
            normalize: _ => normalizeCalls++);

        Assert.AreSame(existing, resolved);
        Assert.AreEqual(1, normalizeCalls);
    }

    private sealed class TestWindowRef
    {
        public bool IsLoaded { get; set; }
    }
}
