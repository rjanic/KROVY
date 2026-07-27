using System.IO;
using AcKrovy.AutoCAD.Settings;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class ApplicationLanguageWorkflowTests
{
    [Fact]
    public void ExistingSelection_DoesNotApplySaveOrRefresh()
    {
        var activeLanguage = "en";
        var counts = new WorkflowCounts();
        var workflow = CreateWorkflow(() => activeLanguage, value => activeLanguage = value, counts);

        Assert.False(workflow.TryApplyUserSelection("en"));
        Assert.Equal(new WorkflowCounts(), counts);
    }

    [Fact]
    public void OneRealChange_AppliesSavesAndRefreshesExactlyOnce()
    {
        var activeLanguage = "en";
        var counts = new WorkflowCounts();
        var workflow = CreateWorkflow(() => activeLanguage, value => activeLanguage = value, counts);

        Assert.True(workflow.TryApplyUserSelection("de"));

        Assert.Equal("de", activeLanguage);
        Assert.Equal(1, counts.Apply);
        Assert.Equal(1, counts.Save);
        Assert.Equal(1, counts.Refresh);
    }

    [Fact]
    public void RepeatedNewSelection_DoesNotSaveOrRefreshAgain()
    {
        var activeLanguage = "en";
        var counts = new WorkflowCounts();
        var workflow = CreateWorkflow(() => activeLanguage, value => activeLanguage = value, counts);

        Assert.True(workflow.TryApplyUserSelection("de"));
        Assert.False(workflow.TryApplyUserSelection("de"));

        Assert.Equal(1, counts.Apply);
        Assert.Equal(1, counts.Save);
        Assert.Equal(1, counts.Refresh);
    }

    [Fact]
    public void ReentrantSelection_IsIgnored()
    {
        var activeLanguage = "en";
        var counts = new WorkflowCounts();
        ApplicationLanguageWorkflow? workflow = null;
        workflow = new ApplicationLanguageWorkflow(
            () => activeLanguage,
            value =>
            {
                counts.Apply++;
                activeLanguage = value;
                Assert.False(workflow!.TryApplyUserSelection("fr"));
            },
            _ => counts.Save++,
            () => counts.Refresh++);

        Assert.True(workflow.TryApplyUserSelection("de"));

        Assert.Equal("de", activeLanguage);
        Assert.Equal(1, counts.Apply);
        Assert.Equal(1, counts.Save);
        Assert.Equal(1, counts.Refresh);
    }

    [Fact]
    public void SaveFailure_DoesNotUndoLanguageOrSkipRuntimeRefresh()
    {
        var activeLanguage = "en";
        var applyCount = 0;
        var refreshCount = 0;
        var workflow = new ApplicationLanguageWorkflow(
            () => activeLanguage,
            value =>
            {
                applyCount++;
                activeLanguage = value;
            },
            _ => throw new IOException("blocked"),
            () => refreshCount++);

        Assert.True(workflow.TryApplyUserSelection("fr"));
        Assert.Equal("fr", activeLanguage);
        Assert.Equal(1, applyCount);
        Assert.Equal(1, refreshCount);
    }

    private static ApplicationLanguageWorkflow CreateWorkflow(
        Func<string> currentLanguage,
        Action<string> setLanguage,
        WorkflowCounts counts) =>
        new(
            currentLanguage,
            value =>
            {
                counts.Apply++;
                setLanguage(value);
            },
            _ => counts.Save++,
            () => counts.Refresh++);

    private sealed record WorkflowCounts
    {
        public int Apply { get; set; }
        public int Save { get; set; }
        public int Refresh { get; set; }
    }
}
