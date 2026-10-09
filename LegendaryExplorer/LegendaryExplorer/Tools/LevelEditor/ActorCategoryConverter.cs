using System;
using System.Globalization;
using System.Windows.Data;

namespace LegendaryExplorer.Tools.LevelEditor;

public sealed class ActorCategoryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ActorProxy actor) return "Other";
        if (actor.IsLight) return "Lights";
        if (actor.IsVolumetricMesh) return "Volumetric meshes";
        if (actor.IsVolume) return "Volumes";
        if (actor.IsCinematicActor) return "Cinematic actors";
        if (actor.IsEmitter) return "Emitters";
        if (actor.IsAmbientSound) return "Sounds";
        if (actor.IsLocationActor) return "Locations";
        if (actor.IsDecalActor) return "Decals / effects";
        return actor.Export.ClassName;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
