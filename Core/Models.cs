namespace Clipped.Core;

public enum BlockKind
{
    Text,
    Template,
    Sequence,
    Date,
}

public sealed class Block
{
    public string Id { get; set; } = Store.NewId();
    public string Label { get; set; } = "";
    public BlockKind Kind { get; set; } = BlockKind.Text;
    public string Value { get; set; } = "";
    public string DateFormat { get; set; } = "dddd, dd MMM yyyy";
    public int Start { get; set; } = 1;
    public int End { get; set; } = 10;
    public int Step { get; set; } = 1;
    public int CounterStart { get; set; } = 1;
    public int CounterStep { get; set; } = 1;

    /// <summary>Last value used by a template; 0 means "not started yet".</summary>
    public int Counter { get; set; }

    public string Preview()
    {
        switch (Kind)
        {
            case BlockKind.Sequence:
                return Start + " → " + End + (Step > 1 ? " step " + Step : "");
            case BlockKind.Date:
                return "Today as " + DateFormat;
            case BlockKind.Template:
                return Value.Replace("{n}", NextCounter().ToString());
            default:
                return OneLine(Value);
        }
    }

    public int NextCounter() => Counter == 0 ? CounterStart : Counter + CounterStep;

    public string Resolve()
    {
        switch (Kind)
        {
            case BlockKind.Template:
                int n = NextCounter();
                Counter = n;
                return Value.Replace("{n}", n.ToString());
            case BlockKind.Date:
                return DateTime.Now.ToString(DateFormat);
            case BlockKind.Sequence:
                return Start.ToString();
            default:
                return Value;
        }
    }

    public IEnumerable<string> ExpandSequence()
    {
        int step = Math.Max(1, Math.Abs(Step));
        int count = 0;
        if (Step >= 0)
        {
            for (int i = Start; i <= End && count < 200; i += step, count++) yield return i.ToString();
        }
        else
        {
            for (int i = Start; i >= End && count < 200; i -= step, count++) yield return i.ToString();
        }
    }

    public static string OneLine(string s)
    {
        s = s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }
}

public sealed class Tab
{
    public string Id { get; set; } = Store.NewId();
    public string Name { get; set; } = "New tab";
    public string Key { get; set; } = "N";

    /// <summary>Picker rail icon: "", "at", "hash", "code", "phone", …</summary>
    public string Icon { get; set; } = "";

    /// <summary>#RRGGBB accent used for the tab's icon.</summary>
    public string Color { get; set; } = "";
    public List<Block> Blocks { get; set; } = new();
}

public static class TabColors
{
    public static readonly string[] Palette =
    {
        "#3FB950", "#2F81F7", "#F0883E", "#F85149", "#F778BA", "#A371F7",
        "#E3B341", "#39C5CF", "#56D364", "#8B949E", "#DB61A2", "#4FC1FF",
    };
}

public sealed class HistoryEntry
{
    public string Text { get; set; } = "";
    public string? ImagePath { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public DateTime CopiedAt { get; set; } = DateTime.Now;
}

public sealed class Settings
{
    public string Hotkey { get; set; } = "Win+V";
    public bool TakeOverWinV { get; set; } = true;
    public bool PasteAfterSelect { get; set; } = true;
    public int HistoryLimit { get; set; } = 150;
    public bool LaunchAtStartup { get; set; }
    public bool WindowsIntegration { get; set; } = true;
    public bool ShowTrayIcon { get; set; } = true;
    public int PickerIconSize { get; set; } = 34;

    /// <summary>Overall clipboard flyout size as a percent of its base size.</summary>
    public int PickerScalePercent { get; set; } = 100;

    /// <summary>"windows" (color Segoe UI Emoji) or "outline" (monochrome).</summary>
    public string EmojiStyle { get; set; } = "windows";
    public List<string> RecentEmoji { get; set; } = new();
    public string ThemeMode { get; set; } = "system";
    public string LastTabId { get; set; } = "recent";
    public bool SeenWelcome { get; set; }

    /// <summary>KLIPY API key for GIF search. Empty disables the online GIF tab.</summary>
    public string GifApiKey { get; set; } = "";

    /// <summary>KLIPY API host. Override for mirrors or a local test server.</summary>
    public string GifApiBase { get; set; } = "https://api.klipy.com";

    /// <summary>KLIPY customer id that identifies this app to the API.</summary>
    public string GifCustomerId { get; set; } = "clipped";
}

public sealed class AppData
{
    public int Version { get; set; } = 1;
    public Settings Settings { get; set; } = new();
    public List<Tab> Tabs { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = new();
}
