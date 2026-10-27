using LegendaryExplorer.Tools.InterpEditor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    [TestMethod]
    public async Task ResolveOrCreate_ConcurrentOverlap_AlwaysUsesUsableExistingWindow()
    {
        var existing = new TestWindowRef { IsLoaded = true };
        var current = new TestWindowRef { IsLoaded = true };
        int factoryCalls = 0;
        int normalizeCalls = 0;
        var resolvedWindows = new List<TestWindowRef>();
        var gate = new object();

        Task[] requests = new Task[32];
        for (int i = 0; i < requests.Length; i++)
        {
            requests[i] = Task.Run(() =>
            {
                TestWindowRef resolved = InterpPreviewWindowResolver.ResolveOrCreate(
                    current,
                    existing,
                    factory: () =>
                    {
                        Interlocked.Increment(ref factoryCalls);
                        return new TestWindowRef { IsLoaded = true };
                    },
                    isUsable: window => window.IsLoaded,
                    normalize: _ => Interlocked.Increment(ref normalizeCalls));

                lock (gate)
                {
                    resolvedWindows.Add(resolved);
                }
            });
        }

        await Task.WhenAll(requests);

        Assert.AreEqual(0, factoryCalls);
        Assert.AreEqual(requests.Length, normalizeCalls);
        Assert.AreEqual(requests.Length, resolvedWindows.Count);
        Assert.IsTrue(resolvedWindows.TrueForAll(window => ReferenceEquals(existing, window)));
    }

    private sealed class TestWindowRef
    {
        public bool IsLoaded { get; set; }
        public bool IsClosing { get; set; }
    }

    [TestMethod]
    public void ResolveOrCreate_DoesNotReuseClosingWindow_EvenWhileLoaded()
    {
        var closing = new TestWindowRef { IsLoaded = true, IsClosing = true };
        var created = new TestWindowRef { IsLoaded = true };

        TestWindowRef resolved = InterpPreviewWindowResolver.ResolveOrCreate(closing, closing,
            () => created, window => window.IsLoaded && !window.IsClosing);

        Assert.AreSame(created, resolved);
    }
}
