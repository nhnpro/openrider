using Tasks.Core;
using Xunit;

namespace Tasks.Tests;

public class TaskStoreTests
{
    [Fact]
    public void AddAssignsIncreasingIds()
    {
        var store = new TaskStore();
        Assert.Equal(1, store.Add("a").Id);
        Assert.Equal(2, store.Add("b").Id);
    }

    [Fact]
    public void AddRejectsEmptyTitles()
    {
        Assert.Throws<ArgumentException>(() => new TaskStore().Add("  "));
    }

    [Fact]
    public void CompleteMarksTheTaskDone()
    {
        var store = new TaskStore();
        var item = store.Add("a");
        Assert.True(store.Complete(item.Id));
        Assert.True(store.Items[0].Done);
        Assert.False(store.Complete(99));
    }

    [Theory]
    [InlineData("milk", 1)]
    [InlineData("the", 2)]
    [InlineData("xyz", 0)]
    public void SearchMatchesSubstrings(string text, int expected)
    {
        var store = new TaskStore();
        store.Add("Buy milk");
        store.Add("Write the report");
        store.Add("Call the plumber");
        Assert.Equal(expected, store.Search(text).Count());
    }

    [Fact]
    public void PendingPutsHighPriorityFirst()
    {
        var store = new TaskStore();
        store.Add("low", Priority.Low);
        store.Add("high", Priority.High);
        Assert.Equal("high", store.Pending().First().Title);
    }

    [Fact]
    public void SearchIgnoresCase()
    {
        if (Environment.GetEnvironmentVariable("OPENIDEA_FAIL") is null)
        {
            return; // set OPENIDEA_FAIL=1 to run
        }
        // FIXME: wrong on purpose (QA: failing test in the test tree)
        var store = new TaskStore();
        store.Add("Buy milk");
        Assert.Single(store.Search("MILK"));
    }
}
