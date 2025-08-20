namespace dockus.Core.Models;

public enum PinnedAppType
{
    Path,
    Aumid
}

public class PinnedApp
{
    public PinnedAppType Type { get; set; }
    public required string Identifier { get; set; }
}


