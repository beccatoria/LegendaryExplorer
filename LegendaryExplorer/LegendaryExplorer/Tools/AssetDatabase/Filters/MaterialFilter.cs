using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LegendaryExplorerCore.Gammtek.Extensions.Collections.Generic;

namespace LegendaryExplorer.Tools.AssetDatabase.Filters
{
    public class MaterialFilter : GenericAssetFilter<MaterialRecord>
    {
        private static readonly char[] TextureSearchSeparators = [',', ';', ' '];

        public List<IAssetSpecification<MaterialRecord>> Types { get; private set; } = new();
        public List<IAssetSpecification<MaterialRecord>> BlendModes { get; private set; } = new();
        public ObservableCollection<IAssetSpecification<MaterialRecord>> GeneratedOptions { get; } = new();

        public MaterialFilter(FileListSpecification fileList)
        {
            Search = new SearchSpecification<MaterialRecord>(MaterialSearch);
            PopulateFilterOptions(fileList);
            UpdateFilterCache();
        }

        public void LoadFromDatabase(AssetDB currentDb)
        {
            GeneratedOptions.Clear();
            GeneratedOptions.AddRange(currentDb.MaterialBoolSpecs);
        }

        private void PopulateFilterOptions(FileListSpecification fileList)
        {
            ///////////////////////////////////////
            // Add new custom Material Filters here
            ///////////////////////////////////////
            
            // Options HIDE things.
            Types = new()
            {
                new MaterialClassSpec("Hide materials (+subclasses)", true),
                new MaterialClassSpec("Hide material instances (+subclasses)", false)
            };

            Filters = new()
            {
                fileList,
                new PredicateSpecification<MaterialRecord>("Hide DLC only Materials", mr => !mr.IsDLCOnly),
                new PredicateSpecification<MaterialRecord>("Only Decal Materials",
                    mr => mr.MaterialName.Contains("Decal", StringComparison.OrdinalIgnoreCase)),
                new MaterialSettingSpec("Only Unlit Materials", "LightingModel", param2: "MLM_Unlit"),
                new MaterialSettingSpec("Hide SkeletalMesh exclusive Materials", "bUsedWithSkeletalMesh", param2: "True") {Inverted = true},
                new MaterialSettingSpec("Only 2 sided Materials", "TwoSided", param2: "True"),
                new MaterialSettingSpec("Only Backface culled (1 side)", "TwoSided", param2: "True") {Inverted = true},
                new UISeparator<MaterialRecord>(),
                new MaterialSettingSpec("Must have color setting", "VectorParameter",
                    setting => setting.Parm1.Contains("color", StringComparison.OrdinalIgnoreCase)),
                new MaterialSettingSpec("Must have texture setting", "TextureSampleParameter2D"),
                new MaterialSettingSpec("Must have talk scalar setting", "ScalarParameter",
                    setting => setting.Parm1.Contains("talk", StringComparison.OrdinalIgnoreCase))
            };

            BlendModes = new()
            {
                new MaterialSettingSpec("Translucent or Additive (Opaque)", "BlendMode", (s => s.Parm2 == "BLEND_Translucent" || s.Parm2 == "BLEND_Additive"))
                {
                    Description = "BLEND_Translucent or BLEND_Additive. The 'opaque' filter in previous AssetDB versions."
                },
                new MaterialSettingSpec("Opaque", "BlendMode", param2: "BLEND_Opaque"),
                new MaterialSettingSpec("Masked", "BlendMode", param2: "BLEND_Masked"),
                new MaterialSettingSpec("Translucent", "BlendMode", param2: "BLEND_Translucent"),
                new MaterialSettingSpec("Additive", "BlendMode", param2: "BLEND_Additive"),
                new MaterialSettingSpec("Modulate", "BlendMode", param2: "BLEND_Modulate"),
                new MaterialSettingSpec("Soft Masked", "BlendMode", param2: "BLEND_SoftMasked"),
                new MaterialSettingSpec("Alpha Composite", "BlendMode", param2: "BLEND_AlphaComposite"),
            };
        }

        protected override IEnumerable<IAssetSpecification<MaterialRecord>> GetAdditionalSpecifications()
        {
            var blendModeOr = new OrSpecification<MaterialRecord>(BlendModes); // Matches spec if any of the selected BlendModes are true
            return GeneratedOptions.Concat(Types).Append(blendModeOr);
        }

        public static bool MaterialSearch((string, MaterialRecord) t)
        {
            var (text, mr) = t;
            text = text.Trim();

            if (text.StartsWith("tex:", StringComparison.OrdinalIgnoreCase))
            {
                return TextureTypeSearch(text[4..], mr);
            }

            return mr.MaterialName.Contains(text, StringComparison.OrdinalIgnoreCase)
                   || mr.ParentPackage.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TextureTypeSearch(string textureSearchText, MaterialRecord mr)
        {
            var orGroups = textureSearchText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (orGroups.Length == 0)
            {
                return false;
            }

            var textureValues = mr.MatSettings
                .Where(IsTextureSetting)
                .SelectMany(setting => new[] { setting.Name, setting.Parm1, setting.Parm2 })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (textureValues.Count == 0)
            {
                return false;
            }

            foreach (var group in orGroups)
            {
                var tokens = group
                    .Split(TextureSearchSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (tokens.Count == 0)
                {
                    continue;
                }

                // Comma/space separated tokens are ANDed.
                if (tokens.All(token => textureValues.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase))))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTextureSetting(MatSetting setting)
        {
            return setting.Name?.Contains("Texture", StringComparison.OrdinalIgnoreCase) == true
                   || setting.Parm1?.Contains("Texture", StringComparison.OrdinalIgnoreCase) == true
                   || setting.Parm2?.Contains("Texture", StringComparison.OrdinalIgnoreCase) == true;
        }

    }
}