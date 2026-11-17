namespace Lantern.App;

internal sealed record ColorTheme(string Id, string Name, string Accent, string Soft, string Bubble, string DarkAccent, string DarkSoft, string DarkBubble);
internal static class ColorThemes
{
    internal static readonly ColorTheme[] All = [
        new("jade", "Ngọc bích", "#087F78", "#DFF3ED", "#DFF3ED", "#54C6B2", "#153E3B", "#1D4945"),
        new("ocean", "Đại dương", "#1764B0", "#E2EEFF", "#DBEAFE", "#78B6FF", "#1B3453", "#203D5E"),
        new("violet", "Tử đinh hương", "#7550AB", "#F0E7FC", "#EADDF8", "#C0A0F5", "#362B4C", "#423357"),
        new("rose", "Hồng trà", "#A63C68", "#FBE7EE", "#F8DFE8", "#F09ABD", "#472837", "#542F40"),
        new("amber", "Hổ phách", "#956015", "#FFF0D6", "#FAE7C4", "#EAC071", "#443621", "#514028")
    ];
    internal static ColorTheme Get(string id) => All.FirstOrDefault(x => x.Id == id) ?? All[0];
}
