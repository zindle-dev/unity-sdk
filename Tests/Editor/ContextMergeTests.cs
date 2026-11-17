using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ZindleDevSDK.Tests
{
    /// <summary>
    /// EditMode coverage for the persistent-context merge in <see cref="MetadataCollector.Build"/>:
    /// the SDK-wide store (ZindleDev.SetContext) is overlaid with per-report context, with per-report
    /// keys winning on a conflict, and the result still respects the size cap.
    /// </summary>
    public class ContextMergeTests
    {
        static ZindleDevConfig NewConfig() => ScriptableObject.CreateInstance<ZindleDevConfig>();

        [Test]
        public void Build_IncludesPersistentContext()
        {
            ZindleDevConfig config = NewConfig();
            var persistent = new Dictionary<string, object> { { "checkpointId", "cp_04" } };

            ReportMetadata m = MetadataCollector.Build(config, new ReportInput { title = "x" }, persistent);

            StringAssert.Contains("\"checkpointId\":\"cp_04\"", m.contextJson);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void Build_MergesPersistentAndPerReportKeys()
        {
            ZindleDevConfig config = NewConfig();
            var persistent = new Dictionary<string, object> { { "checkpointId", "cp_04" } };
            var input = new ReportInput
            {
                title = "x",
                context = new Dictionary<string, object> { { "playerHealth", 42 } },
            };

            ReportMetadata m = MetadataCollector.Build(config, input, persistent);

            StringAssert.Contains("\"checkpointId\":\"cp_04\"", m.contextJson);
            StringAssert.Contains("\"playerHealth\":42", m.contextJson);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void Build_PerReportContextOverridesPersistent()
        {
            ZindleDevConfig config = NewConfig();
            var persistent = new Dictionary<string, object> { { "scene", "menu" } };
            var input = new ReportInput
            {
                title = "x",
                context = new Dictionary<string, object> { { "scene", "boss_arena" } },
            };

            ReportMetadata m = MetadataCollector.Build(config, input, persistent);

            StringAssert.Contains("\"scene\":\"boss_arena\"", m.contextJson);
            Assert.IsFalse(m.contextJson.Contains("menu"), "per-report context should win over persistent");
            Object.DestroyImmediate(config);
        }

        [Test]
        public void Build_NoContextEitherSide_LeavesContextNull()
        {
            ZindleDevConfig config = NewConfig();

            ReportMetadata m = MetadataCollector.Build(config, new ReportInput { title = "x" }, null);

            Assert.IsNull(m.contextJson);
            Object.DestroyImmediate(config);
        }
    }
}
