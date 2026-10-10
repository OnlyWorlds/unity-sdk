using System;
using NUnit.Framework;
using OnlyWorlds.Sdk.Editor;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The editor settings' EditorPrefs keys: one set per project, apart from the machine-wide set
    /// that versions before per-project scoping wrote.
    /// </summary>
    public class OWEditorSettingsTests
    {
        private static readonly Guid ProjectA = new Guid("8dbb9d79-ee53-bfe4-3ac5-85a6d9b1b523");
        private static readonly Guid ProjectB = new Guid("aaa4bcc7-4db2-8664-fa0f-8406a8a97cb8");

        [Test]
        public void TwoProjects_GetDifferentKeys()
        {
            Assert.AreNotEqual(
                OWEditorSettings.PrefKey(ProjectA, "ApiKey"),
                OWEditorSettings.PrefKey(ProjectB, "ApiKey"));
        }

        [Test]
        public void OneProject_KeepsOneKeyPerSetting()
        {
            Assert.AreEqual(
                OWEditorSettings.PrefKey(ProjectA, "ApiKey"),
                OWEditorSettings.PrefKey(new Guid(ProjectA.ToString()), "ApiKey"));
            Assert.AreNotEqual(
                OWEditorSettings.PrefKey(ProjectA, "ApiKey"),
                OWEditorSettings.PrefKey(ProjectA, "ApiPin"));
        }

        [Test]
        public void ProjectKey_IsNeverTheMachineWideKey()
        {
            Assert.AreEqual("OnlyWorlds.Sdk.ApiKey", OWEditorSettings.MachineWidePrefKey("ApiKey"));
            Assert.AreNotEqual(
                OWEditorSettings.MachineWidePrefKey("ApiKey"),
                OWEditorSettings.PrefKey(ProjectA, "ApiKey"));
            Assert.AreEqual(
                "OnlyWorlds.Sdk.8dbb9d79ee53bfe43ac585a6d9b1b523.ApiKey",
                OWEditorSettings.PrefKey(ProjectA, "ApiKey"));
        }
    }
}
