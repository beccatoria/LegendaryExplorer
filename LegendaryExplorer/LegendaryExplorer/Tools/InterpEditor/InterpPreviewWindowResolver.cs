using System;

namespace LegendaryExplorer.Tools.InterpEditor;

public static class InterpPreviewWindowResolver
{
    public static T ResolveOrCreate<T>(
        T current,
        T existing,
        Func<T> factory,
        Func<T, bool> isUsable,
        Action<T> normalize = null)
        where T : class
    {
        if (existing is not null && (isUsable?.Invoke(existing) ?? true))
        {
            normalize?.Invoke(existing);
            return existing;
        }

        if (current is not null && (isUsable?.Invoke(current) ?? true))
        {
            normalize?.Invoke(current);
            return current;
        }

        T created = factory?.Invoke();
        normalize?.Invoke(created);
        return created;
    }
}