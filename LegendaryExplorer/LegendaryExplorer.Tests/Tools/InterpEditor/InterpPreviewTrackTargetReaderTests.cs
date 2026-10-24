using System;
using System.Linq;
using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorerCore.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewTrackTargetReaderTests
{
    [TestMethod]
    public void ReadTargets_MoveGroupVariableAndGestureTag_ReturnsReadyTargets()
    {
        InterpPreviewSourceObjectId actionId = CreateId(100);
        InterpPreviewSourceObjectId interpDataId = CreateId(200);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(201),
                        "Hench_Mystic",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(301), "InterpTrackMove"),
                            new InterpPreviewInterpTrackSnapshot(CreateId(302), "BioEvtSysTrackGesture", "hench_mystic")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("Hench_Mystic"),
                new InterpPreviewActionVariableLinkSnapshot("Data")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Ready, result.Outcome);
        Assert.AreEqual(2, result.Targets.Count);

        Assert.AreEqual(InterpPreviewTrackTargetRole.GroupVariable, result.Targets[0].Role);
        Assert.AreEqual("Hench_Mystic", result.Targets[0].TargetKey);
        Assert.AreEqual(301, result.Targets[0].TrackId.ExportUIndex);

        Assert.AreEqual(InterpPreviewTrackTargetRole.DirectTag, result.Targets[1].Role);
        Assert.AreEqual("hench_mystic", result.Targets[1].TargetKey);
        Assert.AreEqual(302, result.Targets[1].TrackId.ExportUIndex);
    }

    [TestMethod]
    public void ReadTargets_UnsupportedAndMissingSchemas_ReturnsPartialWithDiagnostics()
    {
        InterpPreviewSourceObjectId actionId = CreateId(101);
        InterpPreviewSourceObjectId interpDataId = CreateId(202);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(210),
                        "",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(310), "InterpTrackMove"),
                            new InterpPreviewInterpTrackSnapshot(CreateId(311), "BioEvtSysTrackGesture", "None"),
                            new InterpPreviewInterpTrackSnapshot(CreateId(312), "InterpTrackEvent")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("Data")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Partial, result.Outcome);
        Assert.AreEqual(0, result.Targets.Count);
        Assert.IsTrue(HasDiagnosticCode(result, "TARGET_GROUPNAME_MISSING"));
        Assert.IsTrue(HasDiagnosticCode(result, "TARGET_GESTURE_TAG_MISSING"));
        Assert.IsTrue(HasDiagnosticCode(result, "TARGET_TRACK_UNSUPPORTED"));
    }

    [TestMethod]
    public void ReadTargets_InterpTrackDirector_ReportsNonActorDiagnostic()
    {
        InterpPreviewSourceObjectId actionId = CreateId(157);
        InterpPreviewSourceObjectId interpDataId = CreateId(257);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(267),
                        "Director",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(369), "InterpTrackDirector")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("Director")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Partial, result.Outcome);
        Assert.AreEqual(0, result.Targets.Count);
        Assert.IsTrue(HasDiagnosticCode(result, "TARGET_TRACK_NONACTOR"));
        Assert.IsFalse(result.Diagnostics.Any(d => string.Equals(d.Code, "TARGET_TRACK_UNSUPPORTED", StringComparison.Ordinal)
                                                   && d.Message.Contains("InterpTrackDirector", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ReadTargets_DirectorStyleGroupMissingLink_UsesDirectorSpecificDiagnostic()
    {
        InterpPreviewSourceObjectId actionId = CreateId(120);
        InterpPreviewSourceObjectId interpDataId = CreateId(220);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(221),
                        "InterpGroupDirector_2",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(321), "BioEvtSysTrackDOF")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("SomeOtherGroup")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Partial, result.Outcome);
        Assert.AreEqual(0, result.Targets.Count);
        Assert.IsTrue(HasDiagnosticCode(result, "TARGET_GROUPVAR_DIRECTOR"));
        Assert.IsFalse(HasDiagnosticCode(result, "TARGET_GROUPVAR_MISSING"));
    }

    [TestMethod]
    public void ReadTargets_FloatPropWithGroupVariableLink_ReturnsGroupVariableTarget()
    {
        InterpPreviewSourceObjectId actionId = CreateId(151);
        InterpPreviewSourceObjectId interpDataId = CreateId(251);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(261),
                        "pcam",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(363), "InterpTrackFloatProp")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("pcam")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Targets.Count);
        Assert.AreEqual(InterpPreviewTrackTargetRole.GroupVariable, result.Targets[0].Role);
        Assert.AreEqual("pcam", result.Targets[0].TargetKey);
        Assert.AreEqual("InterpTrackFloatProp", result.Targets[0].TrackClassName);
        Assert.IsFalse(HasDiagnosticCode(result, "TARGET_TRACK_UNSUPPORTED"));
    }

    [TestMethod]
    public void ReadTargets_LookAtWithGroupVariableLink_ReturnsGroupVariableTarget()
    {
        InterpPreviewSourceObjectId actionId = CreateId(153);
        InterpPreviewSourceObjectId interpDataId = CreateId(253);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(263),
                        "OrbitTarget",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(365), "BioEvtSysTrackLookAt")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("OrbitTarget")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Targets.Count);
        Assert.AreEqual(InterpPreviewTrackTargetRole.GroupVariable, result.Targets[0].Role);
        Assert.AreEqual("OrbitTarget", result.Targets[0].TargetKey);
        Assert.AreEqual("BioEvtSysTrackLookAt", result.Targets[0].TrackClassName);
        Assert.IsFalse(HasDiagnosticCode(result, "TARGET_TRACK_UNSUPPORTED"));
    }

    [TestMethod]
    public void ReadTargets_LookAtWithFindActorTag_PrefersDirectTagTarget()
    {
        InterpPreviewSourceObjectId actionId = CreateId(158);
        InterpPreviewSourceObjectId interpDataId = CreateId(258);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(268),
                        "OrbitTarget",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(370), "BioEvtSysTrackLookAt", "Cam1")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("OrbitTarget")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Targets.Count);
        Assert.AreEqual(InterpPreviewTrackTargetRole.DirectTag, result.Targets[0].Role);
        Assert.AreEqual("Cam1", result.Targets[0].TargetKey);
        Assert.AreEqual("BioEvtSysTrackLookAt", result.Targets[0].TrackClassName);
    }

    [TestMethod]
    public void ReadTargets_SetFacingWithGroupVariableLink_ReturnsGroupVariableTarget()
    {
        InterpPreviewSourceObjectId actionId = CreateId(152);
        InterpPreviewSourceObjectId interpDataId = CreateId(252);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(262),
                        "cam2",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(364), "BioEvtSysTrackSetFacing")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("cam2")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Targets.Count);
        Assert.AreEqual(InterpPreviewTrackTargetRole.GroupVariable, result.Targets[0].Role);
        Assert.AreEqual("cam2", result.Targets[0].TargetKey);
        Assert.AreEqual("BioEvtSysTrackSetFacing", result.Targets[0].TrackClassName);
        Assert.IsFalse(HasDiagnosticCode(result, "TARGET_TRACK_UNSUPPORTED"));
    }

    [TestMethod]
    public void ReadTargets_SwitchCameraWithGroupVariableLink_ReturnsGroupVariableTarget()
    {
        InterpPreviewSourceObjectId actionId = CreateId(154);
        InterpPreviewSourceObjectId interpDataId = CreateId(254);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(264),
                        "Cam1",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(366), "BioEvtSysTrackSwitchCamera")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("Cam1")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Targets.Count);
        Assert.AreEqual(InterpPreviewTrackTargetRole.GroupVariable, result.Targets[0].Role);
        Assert.AreEqual("Cam1", result.Targets[0].TargetKey);
        Assert.AreEqual("BioEvtSysTrackSwitchCamera", result.Targets[0].TrackClassName);
        Assert.IsFalse(HasDiagnosticCode(result, "TARGET_TRACK_UNSUPPORTED"));
    }

    [TestMethod]
    public void ReadTargets_DofWithGroupVariableLink_ReturnsGroupVariableTarget()
    {
        InterpPreviewSourceObjectId actionId = CreateId(155);
        InterpPreviewSourceObjectId interpDataId = CreateId(255);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(265),
                        "pcam",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(367), "BioEvtSysTrackDOF")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("pcam")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Targets.Count);
        Assert.AreEqual(InterpPreviewTrackTargetRole.GroupVariable, result.Targets[0].Role);
        Assert.AreEqual("pcam", result.Targets[0].TargetKey);
        Assert.AreEqual("BioEvtSysTrackDOF", result.Targets[0].TrackClassName);
        Assert.IsFalse(HasDiagnosticCode(result, "TARGET_TRACK_UNSUPPORTED"));
    }

    [TestMethod]
    public void ReadTargets_VoElementsWithGroupVariableLink_ReturnsGroupVariableTarget()
    {
        InterpPreviewSourceObjectId actionId = CreateId(156);
        InterpPreviewSourceObjectId interpDataId = CreateId(256);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(266),
                        "Conversation",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(368), "BioEvtSysTrackVOElements")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("Conversation")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Targets.Count);
        Assert.AreEqual(InterpPreviewTrackTargetRole.GroupVariable, result.Targets[0].Role);
        Assert.AreEqual("Conversation", result.Targets[0].TargetKey);
        Assert.AreEqual("BioEvtSysTrackVOElements", result.Targets[0].TrackClassName);
        Assert.IsFalse(HasDiagnosticCode(result, "TARGET_TRACK_UNSUPPORTED"));
    }

    [TestMethod]
    public void ReadTargets_UnsupportedTrackDiagnostics_AggregatesByClass()
    {
        InterpPreviewSourceObjectId actionId = CreateId(150);
        InterpPreviewSourceObjectId interpDataId = CreateId(250);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(260),
                        "owner",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(360), "InterpTrackFloatProp"),
                            new InterpPreviewInterpTrackSnapshot(CreateId(361), "InterpTrackFloatProp"),
                            new InterpPreviewInterpTrackSnapshot(CreateId(362), "InterpTrackDirector")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("owner")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Partial, result.Outcome);
        Assert.AreEqual(2, result.Targets.Count);
        Assert.IsTrue(result.Targets.All(t => string.Equals(t.TrackClassName, "InterpTrackFloatProp", StringComparison.Ordinal)));

        int unsupportedCount = result.Diagnostics.Count(d => string.Equals(d.Code, "TARGET_TRACK_UNSUPPORTED", StringComparison.Ordinal));
        Assert.AreEqual(0, unsupportedCount);
        Assert.IsTrue(HasDiagnosticCode(result, "TARGET_TRACK_NONACTOR"));
    }

    [TestMethod]
    public void ReadTargets_DeduplicatesByTrackRoleAndTargetKey_CaseInsensitive()
    {
        InterpPreviewSourceObjectId actionId = CreateId(102);
        InterpPreviewSourceObjectId interpDataId = CreateId(203);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(220),
                        "owner",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(320), "BioEvtSysTrackGesture", "OWNER"),
                            new InterpPreviewInterpTrackSnapshot(CreateId(320), "BioEvtSysTrackGesture", "owner")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("owner")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Targets.Count);
        Assert.AreEqual("OWNER", result.Targets[0].TargetKey);
    }

    [TestMethod]
    public void ReadTargets_UsesGroupAndTrackOrder_Deterministically()
    {
        InterpPreviewSourceObjectId actionId = CreateId(103);
        InterpPreviewSourceObjectId interpDataId = CreateId(204);

        var request = new InterpPreviewTrackTargetReadRequest(
            actionId,
            new InterpPreviewInterpDataSnapshot(
                interpDataId,
                [
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(231),
                        "b",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(331), "BioEvtSysTrackGesture", "tag_b")
                        ]),
                    new InterpPreviewInterpGroupSnapshot(
                        CreateId(230),
                        "a",
                        [
                            new InterpPreviewInterpTrackSnapshot(CreateId(330), "InterpTrackMove"),
                            new InterpPreviewInterpTrackSnapshot(CreateId(332), "BioEvtSysTrackGesture", "tag_a")
                        ])
                ]),
            [
                new InterpPreviewActionVariableLinkSnapshot("a")
            ]);

        var reader = new InterpPreviewTrackTargetReader();
        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Ready, result.Outcome);
        Assert.AreEqual(3, result.Targets.Count);

        Assert.AreEqual(331, result.Targets[0].TrackId.ExportUIndex);
        Assert.AreEqual(330, result.Targets[1].TrackId.ExportUIndex);
        Assert.AreEqual(332, result.Targets[2].TrackId.ExportUIndex);
    }

    [TestMethod]
    public void ReadTargets_MissingRequest_ReturnsUnresolved()
    {
        var reader = new InterpPreviewTrackTargetReader();

        InterpPreviewTrackTargetReadResult result = reader.ReadTargets(request: null);

        Assert.AreEqual(InterpPreviewTrackTargetReadOutcome.Unresolved, result.Outcome);
        Assert.IsTrue(HasDiagnosticCode(result, "TARGET_REQUEST_NULL"));
    }

    private static InterpPreviewSourceObjectId CreateId(int uindex)
    {
        return new InterpPreviewSourceObjectId(MEGame.LE3, "C:/tests/InterpPreviewTrackTargetReader.pcc", uindex);
    }

    private static bool HasDiagnosticCode(InterpPreviewTrackTargetReadResult result, string code)
    {
        foreach (InterpPreviewDiagnostic diagnostic in result.Diagnostics)
        {
            if (string.Equals(diagnostic.Code, code, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
