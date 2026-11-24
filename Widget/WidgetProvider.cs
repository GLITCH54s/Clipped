using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace Clipped.Widget;

internal sealed class WidgetProvider : IWidgetProvider
{
    public static readonly ManualResetEvent EmptyList = new(false);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, WidgetState> Running = new();
    private readonly Timer _refresh;

    private sealed class WidgetState
    {
        public string Id = "";
        public string Definition = "";
        public WidgetSize Size = WidgetSize.Medium;
        public bool Active;
    }

    public WidgetProvider()
    {
        try
        {
            foreach (var info in WidgetManager.GetDefault().GetWidgetInfos())
            {
                var ctx = info.WidgetContext;
                lock (Gate) Running[ctx.Id] = new WidgetState { Id = ctx.Id, Definition = ctx.DefinitionId, Size = ctx.Size };
            }
        }
        catch
        {
            // The manager is not available outside a widget host; carry on with an empty list.
        }

        _refresh = new Timer(_ => RefreshActive(), null, 15_000, 15_000);
        GC.KeepAlive(_refresh);
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        var state = new WidgetState
        {
            Id = widgetContext.Id,
            Definition = widgetContext.DefinitionId,
            Size = widgetContext.Size,
        };
        lock (Gate) Running[state.Id] = state;
        Update(state);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        lock (Gate)
        {
            Running.Remove(widgetId);
            if (Running.Count == 0) EmptyList.Set();
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        WidgetState? state;
        lock (Gate) Running.TryGetValue(widgetContext.Id, out state);
        if (state is null) return;
        state.Active = true;
        state.Size = widgetContext.Size;
        Update(state);
    }

    public void Deactivate(string widgetId)
    {
        lock (Gate)
        {
            if (Running.TryGetValue(widgetId, out var state)) state.Active = false;
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var ctx = contextChangedArgs.WidgetContext;
        WidgetState? state;
        lock (Gate) Running.TryGetValue(ctx.Id, out state);
        if (state is null) return;
        state.Size = ctx.Size;
        Update(state);
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var id = actionInvokedArgs.WidgetContext.Id;
        WidgetState? state;
        lock (Gate) Running.TryGetValue(id, out state);
        if (state is null) return;

        switch (actionInvokedArgs.Verb)
        {
            case "copy":
                try
                {
                    var index = JsonDocument.Parse(actionInvokedArgs.Data).RootElement.GetProperty("i").GetInt32();
                    var clips = ClipStore.Recent(25);
                    if (index >= 0 && index < clips.Count) ClipStore.Copy(clips[index].Text);
                }
                catch
                {
                    // Malformed action data — leave the widget as-is.
                }
                break;

            case "open":
                ClipStore.OpenDashboard();
                break;
        }

        Update(state);
    }

    private void RefreshActive()
    {
        WidgetState[] active;
        lock (Gate) active = Running.Values.Where(s => s.Active).ToArray();
        foreach (var state in active)
        {
            try { Update(state); } catch { }
        }
    }

    private static void Update(WidgetState state)
    {
        var clips = ClipStore.Recent(state.Size == WidgetSize.Small ? 1 : state.Size == WidgetSize.Medium ? 3 : 5);
        var template = BuildCard(state.Size, clips);

        var options = new WidgetUpdateRequestOptions(state.Id)
        {
            Template = template,
            Data = "{}",
            CustomState = "",
        };
        WidgetManager.GetDefault().UpdateWidget(options);
    }

    private static string BuildCard(WidgetSize size, IReadOnlyList<Clip> clips)
    {
        var body = new JsonArray();

        body.Add(new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = "Recent clips",
            ["size"] = "Small",
            ["weight"] = "Bolder",
            ["spacing"] = "None",
        });

        if (clips.Count == 0)
        {
            body.Add(new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = "Nothing copied yet. Copy something and it lands here.",
                ["wrap"] = true,
                ["isSubtle"] = true,
                ["spacing"] = "Small",
            });
        }
        else
        {
            for (var i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                var lines = new JsonArray();

                lines.Add(new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = clip.Preview,
                    ["wrap"] = true,
                    ["maxLines"] = size == WidgetSize.Small ? 2 : 1,
                    ["spacing"] = "None",
                });

                lines.Add(new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = Age(clip.At),
                    ["isSubtle"] = true,
                    ["size"] = "Small",
                    ["spacing"] = "None",
                });

                body.Add(new JsonObject
                {
                    ["type"] = "Container",
                    ["spacing"] = "Small",
                    ["separator"] = i > 0,
                    ["items"] = lines,
                    ["selectAction"] = new JsonObject
                    {
                        ["type"] = "Action.Execute",
                        ["verb"] = "copy",
                        ["data"] = new JsonObject { ["i"] = i },
                    },
                });
            }
        }

        var actions = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "Action.Execute",
                ["title"] = "Open Clipped",
                ["verb"] = "open",
            },
        };

        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = actions,
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
        };

        return card.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static string Age(DateTime at)
    {
        var span = DateTime.Now - at;
        if (span.TotalSeconds < 45) return "just now";
        if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " min ago";
        if (span.TotalHours < 24) return (int)span.TotalHours + " h ago";
        if (span.TotalDays < 7) return (int)span.TotalDays + " d ago";
        return at.ToString("dd MMM");
    }
}

internal sealed record Clip(string Text, DateTime At)
{
    public string Preview
    {
        get
        {
            var flat = string.Join(' ', Text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            flat = flat.Trim();
            return flat.Length <= 90 ? flat : flat[..87] + "…";
        }
    }
}
