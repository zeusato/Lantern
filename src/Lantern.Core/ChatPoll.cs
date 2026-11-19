namespace Lantern.Core;

public sealed class ChatPoll
{
    public string Question { get; set; } = "";
    public List<string> Options { get; set; } = [];
    public bool Multiple { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public bool Closed { get; set; }
    public long Revision { get; set; } = 1;
    public Dictionary<string, PollBallot> Votes { get; set; } = [];
    public bool IsClosed => Closed || EndsAt <= DateTimeOffset.UtcNow;
    public string Summary() => "📊 " + Question + "\n" + string.Join("\n", Options.Select((o, i) => $"{i + 1}. {o}"));
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Question) || Question.Length > 200 || Options is null || Options.Count is < 2 or > 10 ||
            Options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 120) || Options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Options.Count ||
            Revision < 1 || Votes is null || Votes.Count > 32)
            throw new InvalidDataException("Bình chọn cần câu hỏi tối đa 200 ký tự và 2–10 lựa chọn khác nhau, mỗi lựa chọn tối đa 120 ký tự.");
        foreach (var (id, ballot) in Votes)
        {
            if (id.Length != 64 || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("Người bình chọn không hợp lệ.");
            ValidateBallot(ballot);
        }
    }
    public void ValidateBallot(PollBallot ballot)
    {
        if (ballot is null || ballot.Revision < 1 || ballot.Choices is null || ballot.Choices.Length > (Multiple ? Options.Count : 1) ||
            ballot.Choices.Any(c => c < 0 || c >= Options.Count) || ballot.Choices.Distinct().Count() != ballot.Choices.Length)
            throw new InvalidDataException("Phiếu bình chọn không hợp lệ.");
    }
}

public sealed class PollBallot
{
    public long Revision { get; set; }
    public int[] Choices { get; set; } = [];
}
