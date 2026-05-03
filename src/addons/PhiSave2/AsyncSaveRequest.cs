#nullable enable
using Godot;

namespace PhiStore.Addons.PhiSave2;

[GlobalClass]
public partial class AsyncSaveRequest : RefCounted
{
    /// <summary>
    /// 异步操作封装：通过信号向 GDScript 报告完成或进度。
    /// 使用示例：var req = api.StartQrLoginAsync(); req.connect("completed", ...)
    /// </summary>
    [Signal]
    public delegate void CompletedEventHandler(Godot.Variant result, string errorMessage);

    [Signal]
    public delegate void ProgressEventHandler(float progress, string stage);

    public void SetResult(Godot.Variant result)
    {
        /// <summary>
        /// 在操作成功时调用，向 GDScript 发射完成信号并带上结果。
        /// </summary>
        EmitSignal(SignalName.Completed, result, string.Empty);
    }

    public void SetError(string message)
    {
        /// <summary>
        /// 在操作失败时调用，向 GDScript 发射完成信号并带上错误信息。
        /// </summary>
        EmitSignal(SignalName.Completed, default(Godot.Variant), message);
    }

    public void SetProgress(float progress, string stage)
    {
        /// <summary>
        /// 在长时操作中定期调用以报告进度（0.0 - 1.0）及阶段标签。
        /// </summary>
        EmitSignal(SignalName.Progress, progress, stage);
    }
}
