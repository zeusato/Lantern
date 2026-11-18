namespace Lantern.Core;

public static class PcAiState
{
    public static bool IsUntitled(string title) => string.IsNullOrWhiteSpace(title) || title.Trim() is
        "Claude" or "Claude Code" or "ChatGPT" or "Codex" or "New chat" or "New conversation" or "New session" or "New task";
    public static string Classify(bool busy, bool awaitingApproval, bool hasDraft, bool recognizedConversation) =>
        !recognizedConversation ? "unknown" : awaitingApproval ? "attention" : busy ? "busy" : hasDraft ? "draft" : "idle";
}
