using LegendaryExplorer.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpDX.D3DCompiler;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class SelectionShaderTests
{
    [TestMethod]
    [DataRow("PSMain")]
    [DataRow("PSMainHitProxy")]
    public void SelectionMaterialShader_Compiles(string entryPoint)
    {
        using var result = ShaderBytecode.Compile(EmbeddedResources.LevelEditorShader, entryPoint, "ps_5_0");
        Assert.IsNotNull(result.Bytecode);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(8)]
    public void SelectionResolveShader_CompilesForSampleCount(int samples)
    {
        using var result = ShaderBytecode.Compile($"#define MSAA_SAMPLES {samples}\n" + EmbeddedResources.LevelEditorShader,
            "PSMainResolve", "ps_5_0");
        Assert.IsNotNull(result.Bytecode);
    }
}
