using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;

namespace YouTubeAutomation.Mobile;

internal static class MobileServices
{
    public static LocalSettings Settings { get; set; } = null!;
    public static AutomationApi Api { get; set; } = null!;
}

internal static class Ui
{
    public static readonly Color Ink = Color.FromArgb("#172033");
    public static readonly Color Muted = Color.FromArgb("#7B8798");
    public static readonly Color Blue = Color.FromArgb("#4B91F5");
    public static readonly Color Pale = Color.FromArgb("#EAF6FF");
    public static readonly Color Line = Color.FromArgb("#EDF1F5");
    public static readonly Color Green = Color.FromArgb("#20A46A");

    public static Label Text(string value, double size = 15, bool bold = false, Color? color = null) => new()
    {
        Text = value, FontSize = size, FontFamily = bold ? "OpenSansSemibold" : "OpenSansRegular",
        TextColor = color ?? Ink, LineBreakMode = LineBreakMode.WordWrap
    };

    public static Border Card(View content, Thickness? padding = null) => new()
    {
        Content = content, Padding = padding ?? new Thickness(18),
        BackgroundColor = Colors.White, Stroke = Line, StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
        Shadow = new Shadow { Brush = new SolidColorBrush(Color.FromArgb("#18243C")), Offset = new Point(0, 5), Radius = 18, Opacity = .07f }
    };

    public static Button Button(string title, Color? background = null) => new()
    {
        Text = title, BackgroundColor = background ?? Blue, TextColor = Colors.White,
        CornerRadius = 24, HeightRequest = 50, FontFamily = "OpenSansSemibold", Padding = new Thickness(22, 0)
    };

    public static Entry Entry(string placeholder, string? text = null) => new()
    {
        Placeholder = placeholder, Text = text, TextColor = Ink, BackgroundColor = Color.FromArgb("#F7FAFD"),
        HeightRequest = 50, ClearButtonVisibility = ClearButtonVisibility.WhileEditing
    };

    public static VerticalStackLayout Page(string eyebrow, string heading, params View[] content)
    {
        var stack = new VerticalStackLayout { Padding = new Thickness(20, 22, 20, 30), Spacing = 16 };
        stack.Children.Add(Text(eyebrow.ToUpperInvariant(), 11, true, Blue));
        stack.Children.Add(Text(heading, 29, false));
        foreach (var view in content) stack.Children.Add(view);
        return stack;
    }

    public static async Task Error(Page page, Exception ex) => await page.DisplayAlertAsync("Could not complete", ex.Message, "OK");
}

public partial class MainPage : ContentPage
{
    readonly Label serviceStatus = Ui.Text("Checking local service…", 14, false, Ui.Muted);
    readonly Label configStatus = Ui.Text("OAuth client not imported", 14, false, Ui.Muted);
    public MainPage()
    {
        Title = "Home"; BackgroundColor = Ui.Pale;
        var welcome = Ui.Card(new VerticalStackLayout { Spacing = 12, Children =
        {
            Ui.Text("Creator workspace", 13, true, Ui.Blue),
            Ui.Text("Find your next\nshort-form story.", 30),
            Ui.Text("Search with your region, language and duration preferences, then review every step.", 14, false, Ui.Muted),
            Ui.Button("Open discovery", Ui.Blue)
        }});
        ((Button)((VerticalStackLayout)welcome.Content!).Children[3]).Clicked += async (_, _) => await Shell.Current.GoToAsync("//discover");
        var status = Ui.Card(new VerticalStackLayout { Spacing = 10, Children =
        {
            Ui.Text("WORKSPACE STATUS", 11, true, Ui.Muted), serviceStatus,
            new BoxView { HeightRequest = 1, Color = Ui.Line }, configStatus
        }});
        Content = new ScrollView { Content = Ui.Page("Wildframe Studio", "Good to see you", welcome, status,
            Ui.Card(new VerticalStackLayout { Spacing = 8, Children = { Ui.Text("Private by default", 17, true), Ui.Text("Settings and activity are stored on this device. The file picker asks you to select the OAuth configuration you want to import.", 14, false, Ui.Muted) } })) };
        Appearing += async (_, _) => await RefreshStatus();
    }
    async Task RefreshStatus()
    {
        configStatus.Text = MobileServices.Settings.ImportedClientPath is { } p && File.Exists(p) ? "OAuth file imported locally · service sign-in still uses its own client" : "OAuth client not imported";
        try { var status = await MobileServices.Api.GetStatusAsync(); serviceStatus.Text = status.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Service available" : "Service available"; }
        catch (Exception ex) { serviceStatus.Text = $"Service unavailable · {ex.Message}"; }
    }
}

public partial class DiscoverPage : ContentPage
{
    readonly Entry region = Ui.Entry("Region code", MobileServices.Settings.Region);
    readonly Entry language = Ui.Entry("Language code", MobileServices.Settings.Language);
    readonly Entry min = Ui.Entry("Min seconds", MobileServices.Settings.MinSeconds.ToString());
    readonly Entry max = Ui.Entry("Max seconds", MobileServices.Settings.MaxSeconds.ToString());
    readonly Label status = Ui.Text("Choose filters and fetch candidates.", 13, false, Ui.Muted);
    readonly CollectionView list = new() { SelectionMode = SelectionMode.Single, HeightRequest = 420, EmptyView = Ui.Text("Candidates will appear here after fetching.", 14, false, Ui.Muted) };
    readonly Entry metadataTitle = Ui.Entry("Title");
    readonly Editor metadataDescription = new() { Placeholder = "Description", AutoSize = EditorAutoSizeOption.TextChanges, HeightRequest = 110, BackgroundColor = Color.FromArgb("#F7FAFD") };
    VideoCandidate? selected;
    public DiscoverPage()
    {
        Title = "Discover"; BackgroundColor = Ui.Pale;
        list.ItemTemplate = new DataTemplate(() =>
        {
            var title = Ui.Text("", 15, true); title.SetBinding(Label.TextProperty, nameof(VideoCandidate.Title));
            var stats = Ui.Text("", 12, false, Ui.Muted);
            stats.SetBinding(Label.TextProperty, new MultiBinding { StringFormat = "{0:N0} views · {1:0}s · viral {2:0.0}", Bindings = { new Binding(nameof(VideoCandidate.Views)), new Binding(nameof(VideoCandidate.DurationSeconds)), new Binding(nameof(VideoCandidate.ViralScore)) } });
            return new Border { Content = new VerticalStackLayout { Spacing = 5, Children = { title, stats } }, Padding = 14, Margin = new Thickness(0, 0, 0, 8), BackgroundColor = Colors.White, Stroke = Ui.Line, StrokeShape = new RoundRectangle { CornerRadius = 16 } };
        });
        list.SelectionChanged += (_, e) => { selected = e.CurrentSelection.FirstOrDefault() as VideoCandidate; };
        var fetchButton = Ui.Button("Fetch 60+ videos"); fetchButton.Clicked += async (_, _) => await Fetch();
        var filters = Ui.Card(new VerticalStackLayout { Spacing = 10, Children = { Ui.Text("Search filters", 17, true), region, language, new HorizontalStackLayout { Spacing = 10, Children = { min, max } }, fetchButton, status } });
        var actions = Ui.Card(new VerticalStackLayout { Spacing = 10, Children = { Ui.Text("Selected video", 17, true), Ui.Text("Select a result to continue. Download and metadata actions run through the configured service.", 13, false, Ui.Muted), Ui.Button("Download selected", Ui.Green), Ui.Button("Generate metadata"), metadataTitle, metadataDescription, Ui.Button("Publish as public", Color.FromArgb("#E46B6B")) } });
        var actionStack = (VerticalStackLayout)actions.Content!;
        ((Button)actionStack.Children[2]).Clicked += async (_, _) => await RunAction("Downloading", () => MobileServices.Api.DownloadAsync(SelectedId()), "Download complete");
        ((Button)actionStack.Children[3]).Clicked += async (_, _) => await Generate();
        ((Button)actionStack.Children[6]).Clicked += async (_, _) => await Publish();
        Content = new ScrollView { Content = Ui.Page("Discover", "Trending candidates", filters, Ui.Card(new VerticalStackLayout { Spacing = 10, Children = { Ui.Text("Results", 17, true), list } }), actions) };
    }
    string SelectedId() => selected?.Id ?? throw new InvalidOperationException("Select a video first.");
    async Task Fetch()
    {
        try
        {
            int low = int.TryParse(min.Text, out var lo) ? lo : 30, high = int.TryParse(max.Text, out var hi) ? hi : 50;
            if (low < 1 || high <= low) throw new InvalidOperationException("Maximum duration must be greater than the minimum.");
            MobileServices.Settings.Region = region.Text?.Trim().ToUpperInvariant() ?? "IN"; MobileServices.Settings.Language = language.Text?.Trim() ?? "en";
            MobileServices.Settings.MinSeconds = low; MobileServices.Settings.MaxSeconds = high;
            status.Text = "Searching and filtering…";
            var videos = await MobileServices.Api.FetchCandidatesAsync(new CandidateQuery(MobileServices.Settings.Region, MobileServices.Settings.Language, low, high, 60));
            list.ItemsSource = videos; status.Text = $"Loaded {videos.Count} candidates";
            await MobileServices.Settings.AddActivityAsync($"Fetched {videos.Count} candidates · {MobileServices.Settings.Region}");
        }
        catch (Exception ex) { status.Text = "Search failed"; await Ui.Error(this, ex); await MobileServices.Settings.AddActivityAsync($"Fetch error: {ex.Message}"); }
    }
    async Task RunAction(string progress, Func<Task> operation, string done)
    {
        try { status.Text = progress + "…"; await operation(); status.Text = done; await MobileServices.Settings.AddActivityAsync($"{done}: {selected?.Title}"); await DisplayAlertAsync("Complete", done, "OK"); }
        catch (Exception ex) { await MobileServices.Settings.AddActivityAsync($"Action error: {ex.Message}"); await Ui.Error(this, ex); }
    }
    async Task Generate()
    {
        try { status.Text = "Generating metadata…"; var m = await MobileServices.Api.GenerateMetadataAsync(SelectedId()); metadataTitle.Text = m.Title; metadataDescription.Text = m.Description; status.Text = "Metadata ready for review"; await MobileServices.Settings.AddActivityAsync($"Generated metadata: {selected?.Title}"); }
        catch (Exception ex) { await Ui.Error(this, ex); }
    }
    async Task Publish()
    {
        if (selected is null) { await DisplayAlertAsync("Select a video", "Choose a candidate first.", "OK"); return; }
        try
        {
            var metadata = new GeneratedMetadataDto { Title = metadataTitle.Text ?? "", Description = metadataDescription.Text ?? "" };
            var url = await MobileServices.Api.PublishAsync(selected.Id, metadata);
            status.Text = "Published"; await MobileServices.Settings.AddActivityAsync($"Published publicly: {url}"); await DisplayAlertAsync("Published", url, "OK");
        }
        catch (Exception ex) { await MobileServices.Settings.AddActivityAsync($"Publish error: {ex.Message}"); await Ui.Error(this, ex); }
    }
}

public partial class InsightsPage : ContentPage
{
    readonly Label usage = Ui.Text("Connecting to service…", 14, false, Ui.Muted);
    public InsightsPage()
    {
        Title = "Insights"; BackgroundColor = Ui.Pale;
        Content = new ScrollView { Content = Ui.Page("Insights", "Usage & analytics",
            Ui.Card(new VerticalStackLayout { Spacing = 10, Children = { Ui.Text("App session activity", 18, true), usage, Ui.Text("These are operations initiated since the local service started. They are not YouTube quota units.", 12, false, Ui.Muted) } }),
            Ui.Card(new VerticalStackLayout { Spacing = 10, Children = { Ui.Text("YouTube API quota", 18, true), Ui.Text("Exact quota usage and remaining units are managed by Google Cloud and are not exposed by this app. Check your Google Cloud Console for authoritative totals.", 14, false, Ui.Muted), Ui.Text("Search quota is separate from the general daily quota.", 12, false, Ui.Blue) } }),
            Ui.Card(new VerticalStackLayout { Spacing = 10, Children = { Ui.Text("Channel analytics", 18, true), Ui.Text("YouTube Analytics data requires an authorized channel account and an Analytics API integration. This app does not estimate views, watch time, or revenue.", 14, false, Ui.Muted) } }),
            Ui.Card(new VerticalStackLayout { Spacing = 10, Children = { Ui.Text("On-device storage", 18, true), Ui.Text(MobileServices.Settings.LocalDataDirectory, 12, false, Ui.Muted), Ui.Text("Settings and activity stay in the app's private storage area.", 14, false, Ui.Muted) } })) };
        Appearing += async (_, _) =>
        {
            try
            {
                var status = await MobileServices.Api.GetStatusAsync();
                if (status.TryGetProperty("sessionOperations", out var ops))
                    usage.Text = $"Fetches {ops.GetProperty("fetchedCandidateSets").GetInt64()}   ·   Downloads {ops.GetProperty("downloads").GetInt64()}   ·   Metadata {ops.GetProperty("metadataGenerations").GetInt64()}   ·   Uploads {ops.GetProperty("uploads").GetInt64()}";
                else usage.Text = "Session counters are not available from this service version.";
            }
            catch (Exception ex) { usage.Text = $"Service unavailable · {ex.Message}"; }
        };
    }
}

public partial class ActivityPage : ContentPage
{
    readonly VerticalStackLayout rows = new() { Spacing = 8 };
    public ActivityPage()
    {
        Title = "Activity"; BackgroundColor = Ui.Pale;
        var refresh = Ui.Button("Refresh activity", Ui.Blue); refresh.Clicked += async (_, _) => await Load();
        Content = new ScrollView { Content = Ui.Page("Local history", "Recent activity", Ui.Card(new VerticalStackLayout { Spacing = 12, Children = { refresh, rows } })) };
        Appearing += async (_, _) => await Load();
    }
    async Task Load()
    {
        rows.Clear(); var entries = await MobileServices.Settings.ReadActivityAsync();
        if (entries.Count == 0) rows.Children.Add(Ui.Text("No activity yet.", 14, false, Ui.Muted));
        foreach (var item in entries.Take(100)) rows.Children.Add(Ui.Card(new VerticalStackLayout { Spacing = 4, Children = { Ui.Text(item.Message, 14), Ui.Text(item.At.LocalDateTime.ToString("g"), 11, false, Ui.Muted) } }, new Thickness(12)));
    }
}

public partial class SettingsPage : ContentPage
{
    readonly Entry apiUrl = Ui.Entry("Service URL", MobileServices.Settings.ApiUrl);
    readonly Label clientStatus = Ui.Text("", 13, false, Ui.Muted);
    public SettingsPage()
    {
        Title = "Settings"; BackgroundColor = Ui.Pale;
        var import = Ui.Button("Import OAuth client JSON"); import.Clicked += async (_, _) => await Import();
        var save = Ui.Button("Save service settings", Ui.Green); save.Clicked += async (_, _) =>
        {
            if (!Uri.TryCreate(apiUrl.Text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) { await DisplayAlertAsync("Invalid URL", "Enter a complete http or https service URL.", "OK"); return; }
            MobileServices.Settings.ApiUrl = uri.ToString(); await MobileServices.Settings.AddActivityAsync("Updated service URL"); await DisplayAlertAsync("Saved", "Service URL saved on this device.", "OK");
        };
        var ai = new Picker { Title = "AI provider", ItemsSource = new[] { "Groq", "Gemini", "OpenAI" }, SelectedItem = MobileServices.Settings.AiProvider, BackgroundColor = Color.FromArgb("#F7FAFD") };
        ai.SelectedIndexChanged += (_, _) => MobileServices.Settings.AiProvider = ai.SelectedItem?.ToString() ?? "Groq";
        var note = Ui.Text("AI providers are choices saved locally. The connected service must implement and configure a provider before it can be used.", 13, false, Ui.Muted);
        var card = Ui.Card(new VerticalStackLayout { Spacing = 12, Children = { Ui.Text("Service connection", 17, true), apiUrl, save, new BoxView { HeightRequest = 1, Color = Ui.Line }, Ui.Text("Google OAuth client", 17, true), import, clientStatus, Ui.Text("Importing uses Android's system picker. Only the selected JSON file is copied to app-private storage; broad storage access is not requested. The current API sign-in still uses its own configured OAuth client.", 13, false, Ui.Muted), new BoxView { HeightRequest = 1, Color = Ui.Line }, Ui.Text("AI preferences", 17, true), ai, note } });
        Content = new ScrollView { Content = Ui.Page("Preferences", "Your workspace", card) };
        Appearing += (_, _) => UpdateClientStatus();
    }
    void UpdateClientStatus() => clientStatus.Text = MobileServices.Settings.ImportedClientPath is { } p && File.Exists(p) ? "Client file stored locally · not yet connected to service sign-in" : "No client file selected";
    async Task Import()
    {
        try { var message = await MobileServices.Settings.ImportClientConfigAsync(); UpdateClientStatus(); await MobileServices.Settings.AddActivityAsync("Imported OAuth client configuration"); await DisplayAlertAsync("Imported", message, "OK"); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Ui.Error(this, ex); }
    }
}
