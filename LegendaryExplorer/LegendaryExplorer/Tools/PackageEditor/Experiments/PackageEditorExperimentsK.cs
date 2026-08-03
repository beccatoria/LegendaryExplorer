using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Wordprocessing;
using LegendaryExplorer.Dialogs;
using LegendaryExplorer.Misc;
using LegendaryExplorer.Tools.AssetDatabase;
using LegendaryExplorerCore.Audio;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Gammtek.Extensions.Collections.Generic;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.Unreal.Collections;
using LegendaryExplorerCore.Unreal.ObjectInfo;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;
using static LegendaryExplorer.Tools.ScriptDebugger.DebuggerInterface;
using Path = System.IO.Path;

namespace LegendaryExplorer.Tools.PackageEditor.Experiments
{
    /// <summary>
    /// Class for Kinkojiro experimental code
    /// </summary>
    public class PackageEditorExperimentsK
    {

        public static async void SaveAsNewPackage(PackageEditorWindow pewpf)
        {
            string fileFilter;
            switch (pewpf.Pcc.Game)
            {
                case MEGame.ME1:
                    fileFilter = GameFileFilters.ME1SaveFileFilter;
                    break;
                case MEGame.ME2:
                case MEGame.ME3:
                    fileFilter = GameFileFilters.ME3ME2SaveFileFilter;
                    break;
                default:
                    string extension = Path.GetExtension(pewpf.Pcc.FilePath);
                    fileFilter = $"*{extension}|*{extension}";
                    break;
            }

            var d = new SaveFileDialog { Filter = fileFilter };
            if (d.ShowDialog() == true)
            {
                string oldname = Path.GetFileNameWithoutExtension(pewpf.Pcc.FilePath).ToLower();
                string newname = Path.GetFileNameWithoutExtension(d.FileName);
                pewpf.Pcc.Save(d.FileName);
                pewpf.Pcc.Dispose();
                pewpf.Close();

                var p = new PackageEditorWindow();
                p.Show();
                p.LoadFile(d.FileName);
                p.Activate();
                for (int i = 0; i < p.Pcc.Names.Count; i++)
                {
                    string name = p.Pcc.Names[i];
                    if (name.ToLower() == oldname)
                    {
                        p.Pcc.replaceName(i, newname);
                        break;
                    }
                }

                var pkgguid = Guid.NewGuid();
                var localpackage = p.Pcc.Exports.FirstOrDefault(x => x.ClassName == "Package" && x.ObjectNameString == newname);
                if (localpackage != null)
                {
                    localpackage.PackageGUID = pkgguid;
                }
                p.Pcc.PackageGuid = pkgguid;

                await p.Pcc.SaveAsync();
                MessageBox.Show("New File Created and Loaded.");
            }
        }

        public static async void TrashCompactor(PackageEditorWindow pewpf, IMEPackage pcc)
        {
            var chkdlg = MessageBox.Show($"WARNING: Confirm you wish to recook this file?\n" +
                         $"\nThis will remove all references that current actors do not need.\nIt will then trash any entry that isn't being used.\n\n" +
                         $"This is an experimental tool. Make backups.", "Experimental Tool Warning", MessageBoxButton.OKCancel);
            if (chkdlg == MessageBoxResult.Cancel)
                return;
            pewpf.SetBusy("Finding unreferenced entries");
            ////pewpf.AllowRefresh = false;
            //Find all level references
            if (pcc.Exports.FirstOrDefault(exp => exp.ClassName == "Level") is ExportEntry levelExport)
            {
                LegendaryExplorerCore.Unreal.BinaryConverters.Level level = ObjectBinary.From<LegendaryExplorerCore.Unreal.BinaryConverters.Level>(levelExport);
                HashSet<int> norefsList = await Task.Run(() => pcc.GetReferencedEntries(false));
                pewpf.BusyText = "Recooking the Persistant Level";
                //Get all items in the persistent level not actors
                var references = new List<int>();
                foreach (var t in level.TextureToInstancesMap)
                {
                    references.Add(t.Key);
                }
                foreach (var txtref in references)
                {
                    if (norefsList.Contains(txtref) && txtref > 0)
                    {
                        level.TextureToInstancesMap.Remove(txtref);
                    }
                }
                references.Clear();

                //Clean up Cached PhysSM Data && Rebuild Data Store
                var newPhysSMmap = new UMultiMap<int, CachedPhysSMData>();
                var newPhysSMstore = new List<KCachedConvexData>();
                foreach (var r in level.CachedPhysSMDataMap)
                {
                    references.Add(r.Key);
                }
                foreach (int reference in references)
                {
                    if (!norefsList.Contains(reference) || reference < 0)
                    {
                        var map = level.CachedPhysSMDataMap[reference];
                        var oldidx = map.CachedDataIndex;
                        var kvp = level.CachedPhysSMDataStore[oldidx];
                        map.CachedDataIndex = newPhysSMstore.Count;
                        newPhysSMstore.Add(level.CachedPhysSMDataStore[oldidx]);
                        newPhysSMmap.Add(reference, map);
                    }
                }
                level.CachedPhysSMDataMap = newPhysSMmap;
                level.CachedPhysSMDataStore = newPhysSMstore;
                references.Clear();

                //Clean up Cached PhysPerTri Data
                var newPhysPerTrimap = new UMultiMap<int, CachedPhysSMData>();
                var newPhysPerTristore = new List<KCachedPerTriData>();
                foreach (var s in level.CachedPhysPerTriSMDataMap)
                {
                    references.Add(s.Key);
                }
                foreach (int reference in references)
                {
                    if (!norefsList.Contains(reference) || reference < 0)
                    {
                        var map = level.CachedPhysPerTriSMDataMap[reference];
                        var oldidx = map.CachedDataIndex;
                        var kvp = level.CachedPhysPerTriSMDataStore[oldidx];
                        map.CachedDataIndex = newPhysPerTristore.Count;
                        newPhysPerTristore.Add(level.CachedPhysPerTriSMDataStore[oldidx]);
                        newPhysPerTrimap.Add(reference, map);
                    }
                }
                level.CachedPhysPerTriSMDataMap = newPhysPerTrimap;
                level.CachedPhysPerTriSMDataStore = newPhysPerTristore;
                references.Clear();

                //Clean up NAV data - how to clean up Nav ints?  [Just null unwanted refs]
                if (norefsList.Contains(level.NavListStart))
                {
                    level.NavListStart = 0;
                }
                if (norefsList.Contains(level.NavListEnd))
                {
                    level.NavListEnd = 0;
                }
                var newNavArray = new List<int>();
                newNavArray.AddRange(level.NavRefs);

                for (int n = 0; n < level.NavRefs.Count; n++)
                {
                    if (norefsList.Contains(newNavArray[n]))
                    {
                        newNavArray[n] = 0;
                    }
                }
                level.NavRefs = newNavArray;

                //Clean up Coverlink Lists => pare down guid2byte? table [Just null unwanted refs]
                if (norefsList.Contains(level.CoverListStart))
                {
                    level.CoverListStart = 0;
                }
                if (norefsList.Contains(level.CoverListEnd))
                {
                    level.CoverListEnd = 0;
                }
                var newCLArray = new List<int>();
                newCLArray.AddRange(level.CoverLinkRefs);
                for (int l = 0; l < level.CoverLinkRefs.Count;l++)
                {
                    if (norefsList.Contains(newCLArray[l]))
                    {
                        newCLArray[l] = 0;
                    }
                }
                level.CoverLinkRefs = newCLArray;

                if (pcc.Game.IsGame3())
                {
                    //Clean up Pylon List
                    if (norefsList.Contains(level.PylonListStart))
                    {
                        level.PylonListStart = 0;
                    }
                    if (norefsList.Contains(level.PylonListEnd))
                    {
                        level.PylonListEnd = 0;
                    }
                }

                //Cross Level Actors
                level.CoverLinkRefs = newCLArray;
                var newXLArray = new List<int>();
                newXLArray.AddRange(level.CrossLevelActors);
                foreach (int xlvlactor in level.CrossLevelActors)
                {
                    if (norefsList.Contains(xlvlactor) || xlvlactor == 0)
                    {
                        newXLArray.Remove(xlvlactor);
                    }
                }
                level.CrossLevelActors = newXLArray;

                //Clean up int lists if empty of NAV points
                if (level.NavRefs.IsEmpty() && level.CoverLinkRefs.IsEmpty() && level.CrossLevelActors.IsEmpty() && (!pcc.Game.IsGame3() || level.PylonListStart == 0))
                {
                    level.CrossLevelCoverGuidRefs.Clear();
                    level.CoverIndexPairs.Clear();
                    level.CoverIndexPairs.Clear();
                    level.NavRefIndicies.Clear();
                }

                levelExport.WriteBinary(level);

                pewpf.BusyText = "Trashing unwanted items";
                var itemsToTrash = new List<IEntry>();
                foreach (var export in pcc.Exports)
                {
                    if (norefsList.Contains(export.UIndex))
                    {
                        itemsToTrash.Add(export);
                    }
                }
                //foreach (var import in pcc.Imports)  //Don't trash imports until UnrealScript functions can be fully parsed.
                //{
                //    if (norefsList.Contains(import.UIndex))
                //    {
                //        itemsToTrash.Add(import);
                //    }
                //}

                EntryPruner.TrashEntries(pcc, itemsToTrash);
            } else if (pcc.Exports.FirstOrDefault(exp => exp.ClassName == "ObjectReferencer") is ExportEntry BaseReferencer)  //Clean up seekfree files
            {
                HashSet<int> norefsList = await Task.Run(() => pcc.GetReferencedEntries(false, false, BaseReferencer));
                pewpf.BusyText = "Recooking Unreferenced Objects";
                List<IEntry> itemsToTrash = new List<IEntry>();
                foreach (var export in pcc.Exports)
                {
                    if (norefsList.Contains(export.UIndex))
                    {
                        itemsToTrash.Add(export);
                    }
                }

                EntryPruner.TrashEntries(pcc, itemsToTrash);
            }
            //pewpf.AllowRefresh = true;
            pewpf.EndBusy();
            MessageBox.Show("Trash Compactor Done");
        }

        public static void NewSeekFreeFile(PackageEditorWindow pewpf)
        {
            string gameString = InputComboBoxDialog.GetValue(pewpf, "Choose game to create a seekfree file for:",
                                                          "Create new level file", new[] { "LE3", "LE2", "ME3", "ME2" }, "LE3");
            if (Enum.TryParse(gameString, out MEGame game) && game is MEGame.ME3 or MEGame.ME2 or MEGame.LE3 or MEGame.LE2)
            {
                var dlg = new SaveFileDialog
                {
                    Filter = GameFileFilters.ME3ME2SaveFileFilter,
                    OverwritePrompt = true
                };
                if (game.IsLEGame())
                {
                    dlg.Filter = GameFileFilters.LESaveFileFilter;
                }
                if (dlg.ShowDialog() == true)
                {
                    if (File.Exists(dlg.FileName))
                    {
                        File.Delete(dlg.FileName);
                    }
                    string emptyLevelName = game switch
                    {
                        MEGame.LE2 => "LE2EmptySeekFree",
                        MEGame.LE3 => "LE3EmptySeekFree",
                        MEGame.ME2 => "ME2EmptySeekFree",
                        _ => "ME3EmptySeekFree"
                    };
                    File.Copy(Path.Combine(AppDirectories.ExecFolder, $"{emptyLevelName}.pcc"), dlg.FileName);
                    pewpf.LoadFile(dlg.FileName);
                    for (int i = 0; i < pewpf.Pcc.Names.Count; i++)
                    {
                        string name = pewpf.Pcc.Names[i];
                        if (name.Equals(emptyLevelName))
                        {
                            var newName = name.Replace(emptyLevelName, Path.GetFileNameWithoutExtension(dlg.FileName));
                            pewpf.Pcc.replaceName(i, newName);
                        }
                    }

                    var packguid = Guid.NewGuid();
                    var package = pewpf.Pcc.GetUExport(game switch
                    {
                        MEGame.ME2 => 7,
                        _ => 1
                    });
                    package.PackageGUID = packguid;
                    pewpf.Pcc.PackageGuid = packguid;
                    pewpf.Pcc.Save();
                }
            }
        }

        public static void AddAllAssetsToReferencer(PackageEditorWindow pewpf)
        {
            if (pewpf.SelectedItem.Entry.ClassName != "ObjectReferencer")
            {
                MessageBox.Show("ObjectReferencer not selected.", "Error");
                return;
            }

            var oReferencer = pewpf.SelectedItem.Entry as ExportEntry;
            var referenceProp = oReferencer.GetProperties()?.GetProp<ArrayProperty<ObjectProperty>>("ReferencedObjects");

            var seekfreeClasses = new List<string>
            {
                "BioConversation",
                "FaceFXAnimSet",
                "Material",
                "MaterialInstanceConstant",
                "ObjectReferencer",
                "Sequence",
                "SkeletalMesh",
                "SkeletalMeshSocket",
                "Texture2D",
                "WwiseBank",
                "WwiseStream",
                "WwiseEvent"
            };
            var seekfreeAssets = new List<ObjectProperty>();
            foreach (var x in pewpf.Pcc.Exports)
            {
                foreach (var cls in seekfreeClasses)
                {
                    if (x.ClassName == cls)
                    {
                        var obj = new ObjectProperty(x);
                        seekfreeAssets.Add(obj);
                        break;
                    }
                }
            }

            if (referenceProp != null)
            {
                referenceProp.Clear();
                referenceProp.AddRange(seekfreeAssets);
                oReferencer.WriteProperty(referenceProp);
            }
        }

        public static void ChangeClassesGlobally(PackageEditorWindow pewpf)
        {
            if (pewpf.SelectedItem.Entry.ClassName != "Class")
            {
                MessageBox.Show("Class that is being replaced not selected.", "Error");
                return;
            }

            var replacement = EntrySelector.GetEntry<IEntry>(pewpf, pewpf.Pcc, "Select replacement Class reference");
            if (replacement == null || replacement.ClassName != "Class")
            {
                MessageBox.Show("Invalid replacement.", "Error");
                return;
            }

            int r = 0;
            foreach (var exp in pewpf.Pcc.Exports)
            {
                if (exp.Class == pewpf.SelectedItem.Entry && !exp.IsDefaultObject)
                {
                    exp.Class = replacement;
                    r++;
                    if(exp.ObjectName.Name == pewpf.SelectedItem.Entry.ObjectName.Name)
                    {
                        int idx = exp.indexValue;
                        exp.ObjectName = replacement.ObjectName.Name;
                        exp.indexValue = idx;
                    }
                }
            }

            MessageBox.Show($"{r} exports had classes replaced.", "Replace Classes");
        }

        public static void ShaderDestroyer(PackageEditorWindow pewpf)
        {
            var dlg = MessageBox.Show("Destroy this file?", "Warning", MessageBoxButton.OKCancel);
            if (dlg == MessageBoxResult.Cancel)
                return;

            if (pewpf.Pcc.Game != MEGame.LE3)
                return;
            var targetxp = pewpf.Pcc.Exports.FirstOrDefault(x => x.ClassName == "ShaderCache");
            if(targetxp == null)
                return;

            var tgtshader = targetxp.GetBinaryData<ShaderCache>();
            if (tgtshader == null)
                return;

            var maincachefilepath = (Path.Combine(LE3Directory.CookedPCPath, "RefShaderCache-PC-D3D-SM5.upk"));
            IMEPackage maincachefile = MEPackageHandler.OpenMEPackage(maincachefilepath);
            if (maincachefile == null)
                return;

            var mainshaderpcc = maincachefile.Exports.FirstOrDefault(x => x.ClassName == "ShaderCache");
            var mainshader = mainshaderpcc.GetBinaryData<ShaderCache>();

            var newTypeCRC = new UMap<NameReference, uint>();
            var newVertexFact = new UMap<NameReference, uint>();

            foreach (var kvp in tgtshader.VertexFactoryTypeCRCMap)
            {
                newVertexFact.Add(kvp.Key, mainshader.VertexFactoryTypeCRCMap[kvp.Key]);
            }

            foreach (var crctype in tgtshader.ShaderTypeCRCMap)
            {
                newTypeCRC.Add(crctype.Key, mainshader.ShaderTypeCRCMap[crctype.Key]);
            }
            tgtshader.ShaderTypeCRCMap.Clear();
            tgtshader.ShaderTypeCRCMap.AddRange(newTypeCRC);
            tgtshader.VertexFactoryTypeCRCMap.Clear();
            tgtshader.VertexFactoryTypeCRCMap.AddRange(newVertexFact);
            targetxp.WriteBinary(tgtshader);
        }

        public static void AddNewInterpGroups(PackageEditorWindow pewpf)
        {
            if(pewpf.SelectedItem.Entry.ClassName != "InterpData")
            {
                MessageBox.Show("InterpData not selected.", "Warning", MessageBoxButton.OK);
                return;
            }

            if (pewpf.SelectedItem.Entry is not ExportEntry interp)
                return;

            var grpsProp = interp.GetProperty<ArrayProperty<ObjectProperty>>("InterpGroups");
            if (grpsProp == null)
                grpsProp = new ArrayProperty<ObjectProperty>("InterpGroups");

            var childrenGrps = pewpf.Pcc.Exports.Where(x => x.idxLink == interp.UIndex);
            foreach(var o in childrenGrps)
            {
                var objProp = new ObjectProperty(o);
                if (grpsProp.Contains(objProp))
                    continue;
                if (o.ClassName != "InterpGroup" && o.ClassName != "InterpDirector")
                    continue;
                grpsProp.Add(objProp);
            }
            interp.WriteProperty(grpsProp);
        }

        public static void CompileAudioPreloads(PackageEditorWindow pewpf)
        {
            if (pewpf.SelectedItem.Entry.ClassName != "InterpData")
            {
                MessageBox.Show("InterpData not selected.", "Warning", MessageBoxButton.OK);
                return;
            }

            if (pewpf.SelectedItem.Entry is not ExportEntry interp)
                return;

            var preProp = interp.GetProperty<ArrayProperty<StructProperty>>("m_aBioPreloadData");
            if (preProp == null)
                preProp = new ArrayProperty<StructProperty>("m_aBioPreloadData");

            var grps = interp.GetProperty<ArrayProperty<ObjectProperty>>("InterpGroups");
            if (grps == null)
                return;
            var preLoadData = new List<(ExportEntry,int,float)>();
            foreach(var g in grps)
            {
                var grp = pewpf.Pcc.GetUExport(g.Value);
                if(grp != null)
                {
                    var trcks = grp.GetProperty<ArrayProperty<ObjectProperty>>("InterpTracks");
                    if (trcks != null)
                    {
                        foreach (var t in trcks)
                        {
                            var itrk = pewpf.Pcc.GetUExport(t.Value);
                            if(itrk != null)
                            {
                                if(itrk.ClassName == "InterpTrackWwiseEvent" || itrk.ClassName == "InterpTrackWwiseSoundEffect")
                                {
                                    var awt = itrk.GetProperty<ArrayProperty<StructProperty>>("WwiseEvents");
                                    for(int n = 0; n < awt.Count; n++)
                                    {
                                        float time = (float)(awt[n].GetProp<FloatProperty>("Time")?.Value);
                                        preLoadData.Add((itrk, n, time));
                                    }

                                }
                                else if (itrk.ClassName == "SFXInterpTrackMovieBink")
                                {
                                    var awt = itrk.GetProperty<ArrayProperty<StructProperty>>("m_aTrackKeys");
                                    for (int n = 0; n < awt.Count; n++)
                                    {
                                        float time = (float)(awt[n].GetProp<FloatProperty>("fTime")?.Value);
                                        preLoadData.Add((itrk, n, time));
                                    }
                                }
                            }   
                        }
                    }
                }
            }
            var SortedPreLoadData = preLoadData.OrderBy(f => f.Item3);
            preProp.Clear();
            foreach (var pld in SortedPreLoadData)
            {
                var props = new PropertyCollection();
                props.Add(new ObjectProperty(pld.Item1, "pObject"));
                props.Add(new IntProperty(pld.Item2, "nKeyIndex"));
                props.Add(new FloatProperty(pld.Item3, "fTime"));
                var plEventProp = new StructProperty("BioResourcePreloadItem", props);
                preProp.Add(plEventProp);
            }
            
            interp.WriteProperty(preProp);
        }

        public static void ParseMapNames(PackageEditorWindow pewpf)
        {
            if (pewpf.SelectedItem.Entry is not ExportEntry gmObj)
                return;

            if (!gmObj.IsA("SFXGalaxyMapObject"))
            {
                MessageBox.Show("Not a Galaxy Map Object.", "Warning", MessageBoxButton.OK);
                return;
            }

            ChangeMapNames(gmObj);

            void ChangeMapNames(ExportEntry mapObj)
            {
                var displayName = mapObj.GetProperty<StringRefProperty>("DisplayName");
                if(displayName != null)
                {
                    int strref = displayName.Value;
                    string name = TlkManagerNS.TLKManagerWPF.GlobalFindStrRefbyID(strref, pewpf.Pcc);
                    if (strref > 0 && name != null && name != "No Data")
                    {
                        mapObj.ObjectNameString = name.Replace(" ", string.Empty).Trim('"', ' ');
                        mapObj.indexValue = 0;
                    }
                }

                var children = mapObj.GetProperty<ArrayProperty<ObjectProperty>>("Children");
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child != null && child.ResolveToEntry(pewpf.Pcc) is ExportEntry gmChild && gmChild.IsA("SFXGalaxyMapObject"))
                        {
                            ChangeMapNames(gmChild);
                        }
                    }
                }
            }
        }

        public static void ShiftInterpTrackMovesInPackageWithRotation(IMEPackage package, Func<ExportEntry, bool> predicate)
        {
            try
            {
                var originX = float.Parse(PromptDialog.Prompt(null, "Enter X origin", "Origin X", "0", true));
                var originY = float.Parse(PromptDialog.Prompt(null, "Enter Y origin", "Origin Y", "0", true));
                var originZ = float.Parse(PromptDialog.Prompt(null, "Enter Z origin", "Origin Z", "0", true));
                var originYaw = float.Parse(PromptDialog.Prompt(null, "Enter Yaw origin", "Origin Yaw", "0", true));
                var targetX = float.Parse(PromptDialog.Prompt(null, "Enter X target", "Target X", "0", true));
                var targetY = float.Parse(PromptDialog.Prompt(null, "Enter Y target", "Target Y", "0", true));
                var targetZ = float.Parse(PromptDialog.Prompt(null, "Enter Z target", "Target Z", "0", true));
                var targetYaw = float.Parse(PromptDialog.Prompt(null, "Enter Yaw target", "Target Yaw", "0", true));
                foreach (var exp in package.Exports.Where(x => x.ClassName == "InterpTrackMove"))
                {
                    if (predicate == null || predicate.Invoke(exp))
                    {

                        var interpTrackMove = exp;
                        var props = interpTrackMove.GetProperties();
                        var posTrack = props.GetProp<StructProperty>("PosTrack");
                        var points = posTrack.GetProp<ArrayProperty<StructProperty>>("Points");
                        var eulerTrack = props.GetProp<StructProperty>("EulerTrack");
                        var eulerPoints = eulerTrack.GetProp<ArrayProperty<StructProperty>>("Points");

                        for (int n = 0; n < points.Count; n++)
                        {
                            //Get start positions
                            var outval = points[n].GetProp<StructProperty>("OutVal");
                            var startX = outval.GetProp<FloatProperty>("X").Value;
                            var startY = outval.GetProp<FloatProperty>("Y").Value;
                            var startZ = outval.GetProp<FloatProperty>("Z").Value;
                            var outYaw = eulerPoints[n].GetProp<StructProperty>("OutVal");
                            var startYaw = outYaw.GetProp<FloatProperty>("Z").Value;

                            var oldRelativeX = startX - originX;
                            var oldRelativeY = startY - originY;
                            var oldRelativeZ = startZ - originZ;
                            float rotateYawRadians = MathF.PI * ((targetYaw - originYaw) / 180); //Convert to radians
                            float sinCalcYaw = MathF.Sin(rotateYawRadians);
                            float cosCalcYaw = MathF.Cos(rotateYawRadians);

                            //Get new rotation x' = x cos θ − y sin θ
                            //y' = x sin θ + y cos θ
                            float newRelativeX = oldRelativeX * cosCalcYaw - oldRelativeY * sinCalcYaw;
                            float newRelativeY = oldRelativeX * sinCalcYaw + oldRelativeY * cosCalcYaw;

                            float newX = targetX + newRelativeX;
                            float newY = targetY + newRelativeY;
                            float newZ = targetZ + startZ - originZ;
                            float newYaw = startYaw + targetYaw - originYaw;

                            //write new location
                            outval.GetProp<FloatProperty>("X").Value = newX;
                            outval.GetProp<FloatProperty>("Y").Value = newY;
                            outval.GetProp<FloatProperty>("Z").Value = newZ;
                            outYaw.GetProp<FloatProperty>("Z").Value = newYaw;
                        }
                        interpTrackMove.WriteProperties(props);
                    }
                }
                MessageBox.Show("All InterpTrackMoves shifted.", "Complete", MessageBoxButton.OK);
            }
            catch
            {
                return; //handle escape on blocks
            }
        }

        public static void MakeInterpTrackMovesStageRelative(IMEPackage package, Func<ExportEntry, bool> predicate)
        {
            try
            {
                var stageX = float.Parse(PromptDialog.Prompt(null, "Enter Anchor X Location", "Anchor X", "0", true));
                var stageY = float.Parse(PromptDialog.Prompt(null, "Enter Anchor Y Location", "Anchor Y", "0", true));
                var stageZ = float.Parse(PromptDialog.Prompt(null, "Enter Anchor Z Location", "Anchor Z", "0", true));
                var stageYaw = float.Parse(PromptDialog.Prompt(null, "Enter Anchor Yaw in Degrees", "Anchor Yaw", "0", true));
                foreach (var exp in package.Exports.Where(x => x.ClassName == "InterpTrackMove"))
                {
                    if (predicate == null || predicate.Invoke(exp))
                    {
                        var interpTrackMove = exp;
                        var props = interpTrackMove.GetProperties();
                        var posTrack = props.GetProp<StructProperty>("PosTrack");
                        var points = posTrack.GetProp<ArrayProperty<StructProperty>>("Points");
                        var eulerTrack = props.GetProp<StructProperty>("EulerTrack");
                        var eulerPoints = eulerTrack.GetProp<ArrayProperty<StructProperty>>("Points");

                        for (int n = 0; n < points.Count; n++)
                        {
                            //Get start positions
                            var outval = points[n].GetProp<StructProperty>("OutVal");
                            var startX = outval.GetProp<FloatProperty>("X").Value;
                            var startY = outval.GetProp<FloatProperty>("Y").Value;
                            var startZ = outval.GetProp<FloatProperty>("Z").Value;
                            var outRot = eulerPoints[n].GetProp<StructProperty>("OutVal");
                            var startYaw = outRot.GetProp<FloatProperty>("Z").Value;

                            //write relative location
                            outval.GetProp<FloatProperty>("X").Value = stageX - startX;
                            outval.GetProp<FloatProperty>("Y").Value = stageY - startY;
                            outval.GetProp<FloatProperty>("Z").Value = startZ - stageZ;
                            outRot.GetProp<FloatProperty>("Z").Value = startYaw - stageYaw; 
                        }
                        var f = new EnumProperty("EInterpTrackMoveFrame", exp.FileRef.Game, "MoveFrame");
                        f.Value = "IMF_AnchorObject";
                        props.AddOrReplaceProp(f);
                        interpTrackMove.WriteProperties(props);
                    }
                }

                MessageBox.Show("All InterpTrackMoves are now relative to that location.", "Complete", MessageBoxButton.OK);
            }
            catch
            {
                return; //handle escape on blocks
            }
        }

        public static void ReplaceTLKRefs(PackageEditorWindow pewpf, IMEPackage package)
        {
            int oldStart = 0;
            int oldEnd = 0;
            int newStart = 0;

            string searchFirst = PromptDialog.Prompt(pewpf, "Input First TLK Ref to be replaced:", "Search and Replace TLK Refs",
                defaultValue: "tlk ref", selectText: true);
            if (string.IsNullOrEmpty(searchFirst) || !int.TryParse(searchFirst, out oldStart))
                return;

            string searchLast = PromptDialog.Prompt(pewpf, "Input Last TLK Ref to be replaced:", "Search and Replace TLK Refs",
                defaultValue: "Leave blank to do a single line", selectText: true);
            if (!int.TryParse(searchLast, out oldEnd))
            {
                oldEnd = oldStart;
                searchLast = searchFirst;
            }


            string replacestr = PromptDialog.Prompt(pewpf, "Input First TLK Ref to move to:", "Search and Replace TLK Refs",
                    defaultValue: "tlk ref", selectText: true);
            if (string.IsNullOrEmpty(replacestr) || !int.TryParse(replacestr, out newStart) || newStart <= 0)
                return;

            var wdlg = MessageBox.Show(
                $"This will replace every tlk reference starting with \"{searchFirst}\" ending with \"{searchLast}\"" +
                $"with a new range starting from \"{replacestr}\".\n" +
                $"This may break any dialogue if done incorrectly. Please confirm.", "WARNING:",
                MessageBoxButton.OKCancel);
            if (wdlg == MessageBoxResult.Cancel)
                return;

            int rangelength = oldEnd - oldStart;
            for (int n = 0; n <= rangelength; n++)
            {
                
                var searchtlkref = (oldStart + n).ToString();
                var replacetlkref = (newStart + n).ToString();
                //Replace Names
                for (int i = 0; i < package.Names.Count; i++)
                {
                    string name = package.Names[i];
                    if (name.Contains(searchtlkref))
                    {
                        var newName = name.Replace(searchtlkref, replacetlkref);
                        package.replaceName(i, newName);
                    }
                }

                //Replace TLK ref in Conversations
                foreach (var export in package.Exports.Where(x => x.ClassName == "BioConversation"))
                {
                    bool needsWrite = false;
                    var props = export.GetProperties();
                    var entries = props.GetProp<ArrayProperty<StructProperty>>("m_EntryList");
                    if(entries != null)
                    {
                        foreach (var e in entries)
                        {
                            var tlkref = e.GetProp<StringRefProperty>("srText");
                            if (tlkref != null && tlkref.Value == oldStart + n)
                            {
                                tlkref.Value = newStart + n;
                                e.Properties.AddOrReplaceProp(tlkref);
                                needsWrite = true;
                            }
                            var matineeref = e.GetProp<StringRefProperty>("nExportID");
                            if (matineeref != null && matineeref.Value == oldStart + n)
                            {
                                matineeref.Value = newStart + n;
                                e.Properties.AddOrReplaceProp(matineeref);
                                needsWrite = true;
                            }
                        }
                    }
                    var replies = props.GetProp<ArrayProperty<StructProperty>>("m_ReplyList");
                    if(replies != null)
                    {
                        foreach (var r in replies)
                        {
                            var tlkref = r.GetProp<StringRefProperty>("srText");
                            if (tlkref != null && tlkref.Value == oldStart + n)
                            {
                                tlkref.Value = newStart + n;
                                r.Properties.AddOrReplaceProp(tlkref);
                                needsWrite = true;
                            }
                            var matineeref = r.GetProp<StringRefProperty>("nExportID");
                            if (matineeref != null && matineeref.Value == oldStart + n)
                            {
                                matineeref.Value = newStart + n;
                                r.Properties.AddOrReplaceProp(matineeref);
                                needsWrite = true;
                            }
                        }
                    }

                    if (needsWrite)
                    {
                        props.AddOrReplaceProp(entries);
                        export.WriteProperties(props);
                    }
                }

                //Replace in FaceFX
                foreach (var export in package.Exports.Where(x => x.ClassName == "FaceFXAnimSet"))
                {
                    bool needsWrite = false;
                    FaceFXAnimSet fxa = export.GetBinaryData<FaceFXAnimSet>();
                    var newNameList = new List<string>();
                    for (int i = 0; i<fxa.Names.Count; i++)
                    {
                        
                        var name = fxa.Names[i];
                        bool isFem = false;
                        
                        if (name.EndsWith("_F"))
                            isFem = true;
                        string nameRef = name.Replace("FXA_", "").Replace("_F", "").Replace("_M", "");
                        if (nameRef == searchtlkref)
                        {
                            name = "FXA_" + replacetlkref + (isFem ? "_F" : "_M");
                            needsWrite = true;
                        }
                        newNameList.Add(name);
                    }
                    if(needsWrite)
                    {
                        fxa.Names.ReplaceAll(newNameList);
                    }

                    var eventRefs = export.GetProperty<ArrayProperty<ObjectProperty>>("ReferencedSoundCues");
                    foreach (var fx in fxa.Lines)
                    {

                        if(fx.ID == (oldStart + n).ToString())
                        {
                            fx.ID = (newStart + n).ToString();
                            needsWrite = true;
                            if (eventRefs != null)
                            {
                                var wwiseevent = package.GetEntry(eventRefs[fx.Index].Value);
                                if (wwiseevent != null)
                                {
                                    fx.Path = wwiseevent.FullPath;
                                }
                            }
                        }
                    }

                    if (needsWrite)
                    {
                        export.WriteBinary(fxa);
                    }
                }

                //Replace in InterpData 
                foreach (var export in package.Exports.Where(x => x.ClassName == "BioEvtSysTrackVOElements"))
                {
                    bool needsWrite = false;
                    var props = export.GetProperties();
                    var tlkref = props.GetProp<IntProperty>("m_nStrRefID");
                    if (tlkref == oldStart + n)
                    {
                        tlkref = new IntProperty(newStart + n, "m_nStrRefID");
                        needsWrite = true;
                    }
                    if (needsWrite)
                    {
                        props.AddOrReplaceProp(tlkref);
                        export.WriteProperties(props);
                    }
                }
            }
        }

        public static void ImportBankRefsAndNewStreams(PackageEditorWindow pe)
        {
            if (pe.Pcc == null)
                return;
            OpenFileDialog ofd = new OpenFileDialog()
            {
                Filter = "WwiseBank files|*.bnk",
                Title = "Select generated soundbank",
                CustomPlaces = AppDirectories.GameCustomPlaces
            };
            if (ofd.ShowDialog() == true)
            {
                var askResult = Xceed.Wpf.Toolkit.MessageBox.Show(pe,
                    "Are you using this for a dialogue import? If using this for a dialogue bank the streamed audio and events must be named correctly in the editor.\n" +
                    "Each audio must contain the tlk reference e.g. '123546' plus a gendered reference with '_f_' if the line is spoken by femshep, else '_m_'.\n" +
                    "Each event must be named in the format VO_123456_m_Play where 123456 is the tlk ref and the gender is determined by the m/f.\n\n" +
                    "To set durations turn them on in wwise (Project Settings -> Soundbanks -> Estimated Duration).",
                    "Is this a Dialogue Bank Import?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
                    MessageBoxResult.Cancel);
                if (askResult == MessageBoxResult.Cancel)
                    return;
                WwiseBankImport.ImportBank(ofd.FileName, askResult == MessageBoxResult.Yes, pe.Pcc,true);
            }
        }
    }
}
