namespace FileOps.Core;

/// <summary>Windows 快捷方式（.lnk）目标解析。</summary>
public interface IShortcutService
{
    /// <summary>返回快捷方式指向的目标路径；非 .lnk 文件或解析失败返回 null。</summary>
    string? ResolveTarget(string shortcutPath);
}
