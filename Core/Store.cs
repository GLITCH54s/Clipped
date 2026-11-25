using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clipped.Core;

public sealed class Store
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly object Gate = new();

    public string DataDir { get; }
    public string FilePath { get; }
    public AppData Data { get; private set; } = new();

    public event EventHandler? Changed;

    public Store()
    {
        DataDir = Paths.DataDir;
        FilePath = Path.Combine(DataDir, "clipped.json");
        Load();
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                if (json.Trim().Length > 0)
                {
                    Data = JsonSerializer.Deserialize<AppData>(json, _json) ?? new AppData();
                }
            }
            else
            {
                Data = SeedData();
                Save();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load data, starting fresh", ex);
            try { File.Copy(FilePath, FilePath + ".corrupt", true); } catch { }
            Data = new AppData();
        }

        Normalize();
    }

    private void Normalize()
    {
        Data.Tabs ??= new List<Tab>();
        Data.History ??= new List<HistoryEntry>();
        Data.Settings ??= new Settings();

        foreach (var tab in Data.Tabs)
        {
            tab.Blocks ??= new List<Block>();
            if (string.IsNullOrWhiteSpace(tab.Key)) tab.Key = SuggestKey(tab.Name, tab);
            if (string.IsNullOrEmpty(tab.Icon)) tab.Icon = InferIcon(tab.Name);
        }
        Data.Settings.HistoryLimit = Math.Clamp(Data.Settings.HistoryLimit, 10, 500);
        Data.Settings.PickerIconSize = Math.Clamp(Data.Settings.PickerIconSize, 24, 48);
        if (Data.Settings.EmojiStyle is not ("windows" or "outline")) Data.Settings.EmojiStyle = "windows";
        Data.Settings.RecentEmoji ??= new List<string>();
        if (Data.Settings.RecentEmoji.Count > 24)
            Data.Settings.RecentEmoji.RemoveRange(24, Data.Settings.RecentEmoji.Count - 24);

        // Image clips whose PNG is gone are dead weight, and PNGs left behind by a
        // crash or an old entry would otherwise sit in %APPDATA% forever.
        Data.History.RemoveAll(h => h.ImagePath is not null && !File.Exists(h.ImagePath));
        CleanupImageFiles();
    }

    private static string InferIcon(string name)
    {
        name = name.ToLowerInvariant();
        if (name.Contains("mail")) return "at";
        if (name.Contains("phone") || name.Contains("mobile")) return "phone";
        if (name.Contains("code") || name.Contains("dev") || name.Contains("snippet")) return "code";
        if (name.Contains("number") || name.Contains("ticket") || name.Contains("hashtag")) return "hash";
        return "";
    }

    public void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var json = JsonSerializer.Serialize(Data, _json);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to save data", ex);
            }
        }
    }

    public void NotifyChanged()
    {
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Tab? FindTab(string? id) => Data.Tabs.FirstOrDefault(t => t.Id == id);

    public Tab AddTab(string name)
    {
        var tab = new Tab
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New tab" : name.Trim(),
            Key = "",
        };
        tab.Key = SuggestKey(tab.Name, tab);
        Data.Tabs.Add(tab);
        NotifyChanged();
        return tab;
    }

    public void RemoveTab(string id)
    {
        Data.Tabs.RemoveAll(t => t.Id == id);
        NotifyChanged();
    }

    public Block AddBlock(string tabId, Block block)
    {
        var tab = FindTab(tabId);
        if (tab is null) return block;
        tab.Blocks.Add(block);
        NotifyChanged();
        return block;
    }

    public void RemoveBlock(string tabId, string blockId)
    {
        FindTab(tabId)?.Blocks.RemoveAll(b => b.Id == blockId);
        NotifyChanged();
    }

    public void AddHistory(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 200_000) text = text[..200_000];
        text = text.Replace("\0", "");

        var existing = Data.History.FindIndex(h => h.Text == text && h.ImagePath is null);
        if (existing >= 0)
        {
            var entry = Data.History[existing];
            Data.History.RemoveAt(existing);
            entry.CopiedAt = DateTime.Now;
            Data.History.Insert(0, entry);
        }
        else
        {
            Data.History.Insert(0, new HistoryEntry { Text = text, CopiedAt = DateTime.Now });
        }

        TrimHistory();
        NotifyChanged();
    }

    public string ImageDir => Path.Combine(DataDir, "images");

    private const int MaxImageHistory = 30;
    private const long MaxImageStoreBytes = 100L * 1024 * 1024;

    public void AddImageHistory(Image image)
    {
        try
        {
            using var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Png);
            var png = ms.ToArray();
            var name = Convert.ToHexString(SHA256.HashData(png))[..16].ToLowerInvariant() + ".png";
            Directory.CreateDirectory(ImageDir);
            var path = Path.Combine(ImageDir, name);
            if (!File.Exists(path)) File.WriteAllBytes(path, png);

            var existing = Data.History.FindIndex(h => h.ImagePath == path);
            if (existing >= 0)
            {
                var entry = Data.History[existing];
                Data.History.RemoveAt(existing);
                entry.CopiedAt = DateTime.Now;
                Data.History.Insert(0, entry);
            }
            else
            {
                Data.History.Insert(0, new HistoryEntry
                {
                    ImagePath = path,
                    ImageWidth = image.Width,
                    ImageHeight = image.Height,
                    CopiedAt = DateTime.Now,
                });
            }

            var images = Data.History.Where(h => h.ImagePath is not null).ToList();
            if (images.Count > MaxImageHistory)
            {
                foreach (var old in images.Skip(MaxImageHistory))
                {
                    Data.History.Remove(old);
                    DeleteImageFile(old);
                }
            }

            PruneImageBudget();
            TrimHistory();
            NotifyChanged();
        }
        catch (Exception ex)
        {
            Log.Error("Could not store image clip", ex);
        }
    }

    private void PruneImageBudget()
    {
        var images = Data.History.Where(h => h.ImagePath is not null).ToList();
        long total = 0;
        for (int i = 0; i < images.Count; i++)
        {
            long size = 0;
            try { size = new FileInfo(images[i].ImagePath!).Length; } catch { }
            total += size;
            if (i == 0 || total <= MaxImageStoreBytes) continue;

            foreach (var dropped in images.Skip(i))
            {
                Data.History.Remove(dropped);
                DeleteImageFile(dropped);
            }
            break;
        }
    }

    private void CleanupImageFiles()
    {
        try
        {
            if (!Directory.Exists(ImageDir)) return;
            var used = new HashSet<string>(
                Data.History.Where(h => h.ImagePath is not null).Select(h => h.ImagePath!),
                StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(ImageDir, "*.png"))
            {
                if (used.Contains(file)) continue;
                try { File.Delete(file); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Image cleanup failed", ex);
        }
    }

    private void TrimHistory()
    {
        var limit = Math.Clamp(Data.Settings.HistoryLimit, 10, 500);
        if (Data.History.Count <= limit) return;
        foreach (var dropped in Data.History.Skip(limit))
            DeleteImageFile(dropped);
        Data.History.RemoveRange(limit, Data.History.Count - limit);
    }

    private static void DeleteImageFile(HistoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ImagePath)) return;
        try { if (File.Exists(entry.ImagePath)) File.Delete(entry.ImagePath); } catch { }
    }

    public void ClearHistory()
    {
        foreach (var entry in Data.History) DeleteImageFile(entry);
        Data.History.Clear();
        NotifyChanged();
    }

    public void RemoveHistoryAt(int index)
    {
        if (index < 0 || index >= Data.History.Count) return;
        DeleteImageFile(Data.History[index]);
        Data.History.RemoveAt(index);
        NotifyChanged();
    }

    /// <summary>Picks an unused single-letter mnemonic derived from the tab name.</summary>
    public string SuggestKey(string name, Tab? self)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in Data.Tabs)
        {
            if (self is not null && tab.Id == self.Id) continue;
            if (!string.IsNullOrWhiteSpace(tab.Key)) used.Add(tab.Key);
        }

        var cleaned = new string((name ?? "").Where(char.IsLetterOrDigit).ToArray());
        var candidates = new List<char>();
        if (cleaned.Length > 0) candidates.Add(char.ToUpperInvariant(cleaned[0]));
        for (char c = 'A'; c <= 'Z'; c++) candidates.Add(c);
        for (char c = '1'; c <= '9'; c++) candidates.Add(c);

        foreach (var c in candidates)
        {
            var s = c.ToString();
            if (!used.Contains(s)) return s;
        }
        return "";
    }

    private static AppData SeedData()
    {
        var email = new Tab { Name = "Email", Key = "E", Icon = "at" };
        email.Blocks.Add(new Block { Label = "Personal", Value = "you@example.com" });
        email.Blocks.Add(new Block { Label = "Work", Value = "you@company.com" });
        email.Blocks.Add(new Block { Label = "Signature", Value = "Best regards,\nYour Name\nyou@example.com" });

        var phone = new Tab { Name = "Phone", Key = "P", Icon = "phone" };
        phone.Blocks.Add(new Block { Label = "Mobile", Value = "+20 100 000 0000" });
        phone.Blocks.Add(new Block { Label = "Office", Value = "+20 2 0000 0000" });

        var address = new Tab { Name = "Address", Key = "A" };
        address.Blocks.Add(new Block { Label = "Home", Value = "12 Example Street, Apt 4\nCairo, Egypt" });

        var code = new Tab { Name = "Code", Key = "C", Icon = "code" };
        code.Blocks.Add(new Block { Label = "Console log", Value = "console.log();" });
        code.Blocks.Add(new Block { Label = "SQL select", Value = "SELECT * FROM table WHERE id = 1;" });
        code.Blocks.Add(new Block { Label = "Python main", Value = "if __name__ == \"__main__\":\n    pass" });

        var numbers = new Tab { Name = "Numbers", Key = "N", Icon = "hash" };
        numbers.Blocks.Add(new Block
        {
            Label = "1 – 20",
            Kind = BlockKind.Sequence,
            Start = 1,
            End = 20,
        });
        numbers.Blocks.Add(new Block
        {
            Label = "Ticket number",
            Kind = BlockKind.Template,
            Value = "INC-{n}",
            CounterStart = 1000,
            CounterStep = 1,
        });
        numbers.Blocks.Add(new Block
        {
            Label = "Today",
            Kind = BlockKind.Date,
            DateFormat = "dddd, dd MMM yyyy",
        });

        return new AppData
        {
            Tabs = new List<Tab> { email, phone, address, code, numbers },
        };
    }
}

public static class Paths
{
    private static readonly string _dataDir =
        Environment.GetEnvironmentVariable("CLIPPED_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clipped");

    public static string DataDir => _dataDir;

    public static string ExePath
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe)) return exe;
            return Path.Combine(AppContext.BaseDirectory, "Clipped.exe");
        }
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.DataDir);
                File.AppendAllText(
                    Path.Combine(Paths.DataDir, "clipped.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
        }
        catch { }
    }

    public static void Error(string message, Exception? ex = null)
    {
        Info("ERROR " + message + (ex is null ? "" : " :: " + ex));
    }
}
