using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _filePath;

    public SessionStoreTests()
    {
        _filePath = Path.Combine(Path.GetTempPath(), "fileops-session-tests-" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    [Fact]
    public void Save_TryLoad_RoundTrip_PreservesState()
    {
        var original = new SessionState
        {
            Layout = "Three",
            ShowHiddenFiles = true,
            UiScale = 1.25,
            Panes =
            [
                new PaneState
                {
                    ShowTree = false,
                    ActiveTabIndex = 1,
                    Tabs = [new PaneTabState { Path = "C:\\Windows" }, new PaneTabState { Path = "D:\\" }],
                },
                new PaneState
                {
                    ShowTree = true,
                    ActiveTabIndex = 0,
                    Tabs = [new PaneTabState { Path = "C:\\Users\\eosin\\Desktop" }],
                },
            ],
        };

        SessionStore.Save(original, _filePath);
        var restored = SessionStore.TryLoad(_filePath);

        Assert.NotNull(restored);
        Assert.Equal("Three", restored.Layout);
        Assert.True(restored.ShowHiddenFiles);
        Assert.Equal(1.25, restored.UiScale);
        Assert.Equal(2, restored.Panes.Count);
        Assert.False(restored.Panes[0].ShowTree);
        Assert.Equal(1, restored.Panes[0].ActiveTabIndex);
        Assert.Equal(2, restored.Panes[0].Tabs.Count);
        Assert.Equal("C:\\Windows", restored.Panes[0].Tabs[0].Path);
        Assert.Equal("D:\\", restored.Panes[0].Tabs[1].Path);
        Assert.True(restored.Panes[1].ShowTree);
        Assert.Equal("C:\\Users\\eosin\\Desktop", restored.Panes[1].Tabs[0].Path);
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsNull()
    {
        Assert.Null(SessionStore.TryLoad(_filePath));
    }

    /// <summary>每个标签页的视图模式（详细信息/大图标/列表）随会话持久化。</summary>
    [Fact]
    public void Save_TryLoad_PreservesPerTabViewMode()
    {
        var original = new SessionState
        {
            Panes =
            [
                new PaneState
                {
                    Tabs = [new PaneTabState { Path = "C:\\x", ViewMode = "LargeIcons" }],
                },
            ],
        };

        SessionStore.Save(original, _filePath);
        var restored = SessionStore.TryLoad(_filePath);

        Assert.NotNull(restored);
        Assert.Equal("LargeIcons", restored.Panes[0].Tabs[0].ViewMode);
    }

    /// <summary>非法转义等损坏的会话文件应按"无会话"处理，而不是让应用崩溃。</summary>
    [Fact]
    public void TryLoad_CorruptedJson_ReturnsNull()
    {
        File.WriteAllText(_filePath, "{ \"Layout\": \"Two\", \"Panes\": [ { \"Tabs\": [ { \"Path\": \"C:\\Windows\" } ] } ] }");

        Assert.Null(SessionStore.TryLoad(_filePath));
    }
}
