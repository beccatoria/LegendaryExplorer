using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

internal static class InterpPreviewPackageOwnership
{
    // MEPackageHandler's forceLoadFromDisk branch returns before RegisterUse. Preview
    // owns one explicit lease, including for packages later transferred into a scene.
    public static IMEPackage OpenIndependent(string path)
    {
        IMEPackage package = MEPackageHandler.OpenMEPackage(path, forceLoadFromDisk: true);
        package.RegisterUse();
        return package;
    }
}
