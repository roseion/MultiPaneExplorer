namespace FileOps.Core;

/// <summary>Windows 快捷方式（.lnk）目标解析与创建。</summary>
public interface IShortcutService
{
    /// <summary>返回快捷方式指向的目标路径；非 .lnk 文件或解析失败返回 null。</summary>
    string? ResolveTarget(string shortcutPath);

    /// <summary>创建指向 targetPath 的快捷方式（.lnk）；失败抛异常。</summary>
    string CreateShortcut(string targetPath, string linkPath);
}
