namespace Lantern.Core;

public static class PcAiState
{
    public static string Classify(bool busy, bool awaitingApproval, bool hasDraft, bool recognizedConversation) =>
        !recognizedConversation ? "unknown" : awaitingApproval ? "attention" : busy ? "busy" : hasDraft ? "draft" : "idle";
}
