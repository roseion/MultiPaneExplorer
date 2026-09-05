using FileOps.Core;

namespace MultiPaneExplorer.App.ViewModels;

/// <summary>全局撤销/重做服务：跨窗格、跨标签页共享一份操作历史（与资源管理器的单树语义一致）。</summary>
public static class UndoHub
{
    public static UndoRedoService Service { get; } = new();
}
