using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewRenderCoordinator
{
    void Attach(LevelEditorRenderContext renderContext, IInterpPreviewSession session);
    void Detach(LevelEditorRenderContext renderContext);
}
