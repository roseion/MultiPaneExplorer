using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

/// <summary>常用目录访问计数存储（frequent.json）。</summary>
public sealed class FrequentStoreTests : IDisposable
{
    private readonly string _filePath;

    public FrequentStoreTests()
    {
        _filePath = Path.Combine(Path.GetTempPath(), "fileops-frequent-tests-" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    [Fact]
    public void Record_IncrementsCount_And_TopOrdersByCount()
    {
        FrequentStore.Record(@"C:\a", _filePath);
        FrequentStore.Record(@"C:\a", _filePath);
        FrequentStore.Record(@"C:\a", _filePath);
        FrequentStore.Record(@"C:\b", _filePath);
        FrequentStore.Record(@"C:\c", _filePath);

        var top = FrequentStore.Top(2, _filePath);

        Assert.Equal(2, top.Count);
        Assert.Equal(@"C:\a", top[0]);
        Assert.Equal(@"C:\b", top[1]); // 同为零次时按路径名稳定排序
    }

    [Fact]
    public void Top_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(FrequentStore.Top(5, _filePath));
    }

    [Fact]
    public void Record_CorruptFile_StartsFresh()
    {
        File.WriteAllText(_filePath, "{oops");

        FrequentStore.Record(@"C:\a", _filePath);

        var top = FrequentStore.Top(5, _filePath);
        Assert.Single(top);
        Assert.Equal(@"C:\a", top[0]);
    }

    [Fact]
    public void Record_EvictsLeastVisited_OverCapacity()
    {
        for (var i = 0; i < 70; i++)
            FrequentStore.Record($@"C:\dir{i}", _filePath);

        Assert.True(FrequentStore.Top(100, _filePath).Count <= 64);
    }
}
