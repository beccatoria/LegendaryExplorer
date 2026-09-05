using System;
using System.IO;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public readonly struct InterpPreviewResourceKey : IEquatable<InterpPreviewResourceKey>
{
    public InterpPreviewResourceKey(InterpPreviewResourceKind kind, MEGame game, string sourcePath, InterpPreviewPlayerVariant playerVariant)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Resource source path must not be null or whitespace.", nameof(sourcePath));
        }

        Kind = kind;
        Game = game;
        SourcePath = Path.GetFullPath(sourcePath);
        PlayerVariant = kind == InterpPreviewResourceKind.PlayerContext ? playerVariant : InterpPreviewPlayerVariant.None;
    }

    public InterpPreviewResourceKind Kind { get; }
    public MEGame Game { get; }
    public string SourcePath { get; }
    public InterpPreviewPlayerVariant PlayerVariant { get; }

    public static InterpPreviewResourceKey ForLevel(string sourcePath, MEGame game)
    {
        return new InterpPreviewResourceKey(InterpPreviewResourceKind.Level, game, sourcePath, InterpPreviewPlayerVariant.None);
    }

    public static InterpPreviewResourceKey ForPlayerContext(string sourcePath, MEGame game, InterpPreviewPlayerVariant playerVariant)
    {
        return new InterpPreviewResourceKey(InterpPreviewResourceKind.PlayerContext, game, sourcePath, playerVariant);
    }

    public bool Equals(InterpPreviewResourceKey other)
    {
        return Kind == other.Kind
               && Game == other.Game
               && string.Equals(SourcePath, other.SourcePath, StringComparison.OrdinalIgnoreCase)
               && PlayerVariant == other.PlayerVariant;
    }

    public override bool Equals(object obj)
    {
        return obj is InterpPreviewResourceKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, Game, StringComparer.OrdinalIgnoreCase.GetHashCode(SourcePath), PlayerVariant);
    }

    public override string ToString()
    {
        return Kind == InterpPreviewResourceKind.PlayerContext
            ? $"{Kind}:{Game}:{SourcePath}:{PlayerVariant}"
            : $"{Kind}:{Game}:{SourcePath}";
    }
}
