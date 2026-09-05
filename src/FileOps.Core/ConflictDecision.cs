namespace FileOps.Core;

/// <summary>同名冲突时的处理方式。</summary>
public enum ConflictDecision
{
    /// <summary>覆盖目标。</summary>
    Replace,

    /// <summary>跳过该条目。</summary>
    Skip,

    /// <summary>保留两者：目标改名"xx - 副本"等。</summary>
    KeepBoth,
}
