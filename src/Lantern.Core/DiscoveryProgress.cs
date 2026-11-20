namespace Lantern.Core;

public sealed record DiscoveryProgress(int Completed, int Total, string Range = "");
