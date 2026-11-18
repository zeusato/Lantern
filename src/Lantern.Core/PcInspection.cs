namespace Lantern.Core;

/// <summary>A bounded, read-only inspection keeps completed targets when another UI provider stalls.</summary>
public sealed class PcInspection
{
    private readonly object gate = new();
    private List<PcTarget> targets = [];
    private bool finished;

    public void Discover(IEnumerable<PcTarget> discovered)
    {
        lock (gate)
            if (!finished) targets = discovered.Select(t => t with
            {
                Conversation = "", State = "unknown", Version = "",
                Detail = "Ứng dụng AI phản hồi chậm; chưa đọc được trạng thái trong lượt kiểm tra này. Thử kiểm tra lại."
            }).ToList();
    }

    public void Report(PcTarget target)
    {
        lock (gate)
        {
            if (finished) return;
            int index = targets.FindIndex(t => t.Id == target.Id);
            if (index >= 0) targets[index] = target;
        }
    }

    public async Task<List<PcTarget>> RunAsync(Func<CancellationToken, Task> scan, CancellationToken ct, TimeSpan? budget = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget ?? TimeSpan.FromSeconds(8));
        try { await scan(deadline.Token).WaitAsync(deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            lock (gate)
                if (targets.Count == 0) throw new TimeoutException("PC chưa đọc được danh sách ứng dụng AI. Thử kiểm tra lại.");
        }
        finally { lock (gate) finished = true; }
        ct.ThrowIfCancellationRequested();
        lock (gate) return targets.ToList();
    }
}
