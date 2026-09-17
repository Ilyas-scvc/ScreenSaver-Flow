using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FlowScreen.Diagnostics;
using FlowScreen.Hosting;
using FlowScreen.Interop;
using FlowScreen.Logging;
using FlowScreen.Settings;

namespace FlowScreen.Views;

/// <summary>An item in one of the dark-themed pickers.</summary>
internal sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

/// <summary>
/// The settings dialog (<c>/c</c>), including a live preview that runs the real
/// renderer.
///
/// Cheap changes are pushed into the running preview over the WebView message
/// channel; only the seed (which is baked into the procedural world at
/// construction) and vsync (which is a browser-process switch) force a reload.
/// </summary>
public partial class ConfigWindow : Window
{
    private static readonly TimeSpan ReloadDebounce = TimeSpan.FromMilliseconds(400);

    private readonly IAppLogger logger;
    private readonly SettingsService settingsService;
    private readonly RendererEnvironment environment;
    private readonly EmbeddedRendererAssets assets;
    private readonly DispatcherTimer reloadTimer;
    private readonly List<TextBox> customPaletteBoxes = [];

    private FlowScreenSettings settings;
    private RendererHost? preview;
    private bool loading = true;
    private int previewSeed;
    private bool previewVsync;

    public ConfigWindow(
        IAppLogger logger,
        SettingsService settingsService,
        RendererEnvironment environment,
        EmbeddedRendererAssets assets,
        FlowScreenSettings settings)
    {
        this.logger = logger;
        this.settingsService = settingsService;
        this.environment = environment;
        this.assets = assets;
        this.settings = settings.Clone().Normalize();

        InitializeComponent();

        reloadTimer = new DispatcherTimer { Interval = ReloadDebounce };
        reloadTimer.Tick += async (_, _) =>
        {
            reloadTimer.Stop();
            await ReloadPreviewAsync().ConfigureAwait(true);
        };

        BuildPickers();
        BuildCustomPaletteFields();
        LoadIntoControls();
        WireEvents();

        loading = false;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    // --------------------------------------------------------------- setup --

    private void BuildPickers()
    {
        EffectPicker.ItemsSource = new[] { new Choice<EffectId>("Fiber Flow", EffectId.FiberFlow) };
        EffectPicker.SelectedIndex = 0;
        EffectPicker.IsEnabled = false;

        DensityPicker.ItemsSource = new[]
        {
            new Choice<DensityLevel>("Low - 25,600 fibers", DensityLevel.Low),
            new Choice<DensityLevel>("Medium - 65,536 fibers", DensityLevel.Medium),
            new Choice<DensityLevel>("High - 123,904 fibers", DensityLevel.High),
            new Choice<DensityLevel>("Ultra - 230,400 fibers", DensityLevel.Ultra),
        };

        PalettePicker.ItemsSource = new[]
        {
            new Choice<PaletteId>("Pink", PaletteId.Pink),
            new Choice<PaletteId>("Purple", PaletteId.Purple),
            new Choice<PaletteId>("Blue", PaletteId.Blue),
            new Choice<PaletteId>("Cyber", PaletteId.Cyber),
            new Choice<PaletteId>("Monochrome", PaletteId.Mono),
            new Choice<PaletteId>("Custom", PaletteId.Custom),
        };

        FpsPicker.ItemsSource = new[]
        {
            new Choice<int>("30 FPS", 30),
            new Choice<int>("60 FPS", 60),
            new Choice<int>("120 FPS", 120),
            new Choice<int>("Unlimited", 0),
        };

        ScalePicker.ItemsSource = new[]
        {
            new Choice<double>("50%", 0.5),
            new Choice<double>("75%", 0.75),
            new Choice<double>("100%", 1.0),
        };

        AdaptivePicker.ItemsSource = new[]
        {
            new Choice<AdaptiveQualityMode>("Off", AdaptiveQualityMode.Off),
            new Choice<AdaptiveQualityMode>("Balanced", AdaptiveQualityMode.Balanced),
            new Choice<AdaptiveQualityMode>("Performance", AdaptiveQualityMode.Performance),
        };

        MonitorPicker.ItemsSource = new[]
        {
            new Choice<MultiMonitorMode>("Synchronized", MultiMonitorMode.Synchronized),
            new Choice<MultiMonitorMode>("Independent", MultiMonitorMode.Independent),
        };

        var monitors = MonitorEnumerator.Enumerate();
        SubtitleText.Text = monitors.Count == 1
            ? "Procedural GPU screensaver - 1 display detected"
            : $"Procedural GPU screensaver - {monitors.Count} displays detected";
    }

    private void BuildCustomPaletteFields()
    {
        for (var i = 0; i < 6; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = i == 0 ? "Custom stop 1 (dark)" : i == 5 ? "Custom stop 6 (light)" : $"Custom stop {i + 1}",
                Style = (Style)FindResource("FieldLabel"),
            };
            Grid.SetColumn(label, 0);

            var box = new TextBox { Style = (Style)FindResource("Field"), Tag = i };
            Grid.SetColumn(box, 1);
            box.TextChanged += OnCustomPaletteChanged;
            customPaletteBoxes.Add(box);

            row.Children.Add(label);
            row.Children.Add(box);
            CustomPaletteFields.Children.Add(row);
        }
    }

    private void WireEvents()
    {
        DensityPicker.SelectionChanged += (_, _) => OnChanged(reload: false);
        PalettePicker.SelectionChanged += (_, _) =>
        {
            UpdatePaletteAffordances();
            OnChanged(reload: false);
        };
        FpsPicker.SelectionChanged += (_, _) => OnChanged(reload: false);
        ScalePicker.SelectionChanged += (_, _) => OnChanged(reload: false);
        AdaptivePicker.SelectionChanged += (_, _) => OnChanged(reload: false);
        MonitorPicker.SelectionChanged += (_, _) =>
        {
            UpdateMonitorHint();
            OnChanged(reload: false);
        };

        SpeedSlider.ValueChanged += (_, _) => OnChanged(reload: false);
        LengthSlider.ValueChanged += (_, _) => OnChanged(reload: false);
        GlowSlider.ValueChanged += (_, _) => OnChanged(reload: false);
        TrailSlider.ValueChanged += (_, _) => OnChanged(reload: false);

        // vsync is a browser-process command line switch, so it cannot be changed
        // without recreating the environment: reload rather than post.
        VsyncCheck.Checked += (_, _) => OnChanged(reload: true);
        VsyncCheck.Unchecked += (_, _) => OnChanged(reload: true);

        OverlayCheck.Checked += (_, _) => OnChanged(reload: false);
        OverlayCheck.Unchecked += (_, _) => OnChanged(reload: false);

        RandomSeedCheck.Checked += (_, _) => OnChanged(reload: false);
        RandomSeedCheck.Unchecked += (_, _) => OnChanged(reload: false);

        // The seed is baked into the procedural world when the effect is built.
        SeedBox.TextChanged += (_, _) => OnChanged(reload: true);

        RandomizeButton.Click += (_, _) =>
        {
            SeedBox.Text = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        };

        DefaultsButton.Click += (_, _) =>
        {
            settings = new FlowScreenSettings().Normalize();
            loading = true;
            LoadIntoControls();
            loading = false;
            OnChanged(reload: true);
            SetStatus("Defaults restored. Not saved yet.");
        };

        SaveButton.Click += (_, _) => SaveAndClose();
        CancelButton.Click += (_, _) => Close();
    }

    // ------------------------------------------------------------- binding --

    private static void Select<T>(ComboBox picker, T value)
    {
        foreach (var item in picker.Items)
        {
            if (item is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
            {
                picker.SelectedItem = item;
                return;
            }
        }

        picker.SelectedIndex = 0;
    }

    private static T Selected<T>(ComboBox picker, T fallback) =>
        picker.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    private void LoadIntoControls()
    {
        Select(DensityPicker, settings.Density);
        Select(PalettePicker, settings.Palette);
        Select(FpsPicker, settings.FpsLimit);
        Select(ScalePicker, settings.RenderScale);
        Select(AdaptivePicker, settings.AdaptiveQuality);
        Select(MonitorPicker, settings.MultiMonitorMode);

        SpeedSlider.Value = settings.Speed;
        LengthSlider.Value = settings.FiberLength;
        GlowSlider.Value = settings.Glow;
        TrailSlider.Value = settings.Trail;

        VsyncCheck.IsChecked = settings.Vsync;
        OverlayCheck.IsChecked = settings.ShowDebugOverlay;
        RandomSeedCheck.IsChecked = settings.RandomSeedOnLaunch;
        SeedBox.Text = settings.Seed.ToString(CultureInfo.InvariantCulture);

        for (var i = 0; i < customPaletteBoxes.Count; i++)
        {
            customPaletteBoxes[i].Text = settings.CustomPalette[i];
        }

        UpdatePaletteAffordances();
        UpdateMonitorHint();
        UpdateValueLabels();
    }

    private FlowScreenSettings ReadFromControls()
    {
        var result = settings.Clone();

        result.Density = Selected(DensityPicker, DensityLevel.High);
        result.Palette = Selected(PalettePicker, PaletteId.Pink);
        result.FpsLimit = Selected(FpsPicker, 60);
        result.RenderScale = Selected(ScalePicker, 1.0);
        result.AdaptiveQuality = Selected(AdaptivePicker, AdaptiveQualityMode.Balanced);
        result.MultiMonitorMode = Selected(MonitorPicker, MultiMonitorMode.Synchronized);

        result.Speed = SpeedSlider.Value;
        result.FiberLength = LengthSlider.Value;
        result.Glow = GlowSlider.Value;
        result.Trail = TrailSlider.Value;

        result.Vsync = VsyncCheck.IsChecked == true;
        result.ShowDebugOverlay = OverlayCheck.IsChecked == true;
        result.RandomSeedOnLaunch = RandomSeedCheck.IsChecked == true;

        if (int.TryParse(SeedBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
            && seed is >= 0 and < int.MaxValue)
        {
            result.Seed = seed;
        }

        result.CustomPalette = customPaletteBoxes.Select(b => b.Text.Trim()).ToArray();
        return result.Normalize();
    }

    private void UpdateValueLabels()
    {
        var c = CultureInfo.InvariantCulture;
        SpeedValue.Text = SpeedSlider.Value.ToString("0.00", c) + "x";
        LengthValue.Text = LengthSlider.Value.ToString("0.00", c) + "x";
        GlowValue.Text = GlowSlider.Value.ToString("0.00", c) + "x";
        TrailValue.Text = TrailSlider.Value.ToString("0.00", c);
    }

    private void UpdatePaletteAffordances()
    {
        var id = Selected(PalettePicker, PaletteId.Pink);
        var isCustom = id == PaletteId.Custom;

        CustomPaletteFields.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        var stops = isCustom
            ? PalettePresets.NormalizeCustom(customPaletteBoxes.Select(b => b.Text.Trim()).ToArray())
            : PalettePresets.Stops[id];

        SwatchStrip.Children.Clear();
        foreach (var stop in stops)
        {
            try
            {
                var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(stop)!;
                SwatchStrip.Children.Add(new Border { Background = brush });
            }
            catch (Exception)
            {
                SwatchStrip.Children.Add(new Border { Background = Brushes.Black });
            }
        }
    }

    private void UpdateMonitorHint()
    {
        MonitorHint.Text = Selected(MonitorPicker, MultiMonitorMode.Synchronized) == MultiMonitorMode.Synchronized
            ? "One continuous scene spanning every display: fibers cross the bezel and line up on the next screen."
            : "Each display runs its own independent world with its own seed.";
    }

    private void OnCustomPaletteChanged(object? sender, TextChangedEventArgs e)
    {
        if (loading) return;
        UpdatePaletteAffordances();
        OnChanged(reload: false);
    }

    private void OnChanged(bool reload)
    {
        if (loading) return;

        UpdateValueLabels();
        settings = ReadFromControls();

        if (reload || settings.Seed != previewSeed || settings.Vsync != previewVsync)
        {
            reloadTimer.Stop();
            reloadTimer.Start();
        }
        else
        {
            preview?.PostSettings(PreviewSettings(settings));
        }

        SetStatus("Unsaved changes.");
    }

    // ------------------------------------------------------------- preview --

    /// <summary>
    /// The preview pane is a fraction of the size of a real display, so it runs
    /// with the overlay forced on and a lower frame cap. Density is left alone so
    /// that what the user is judging is what they will actually get.
    /// </summary>
    private static FlowScreenSettings PreviewSettings(FlowScreenSettings source)
    {
        var copy = source.Clone();
        copy.RandomSeedOnLaunch = false;
        copy.FpsLimit = source.FpsLimit == 0 ? 60 : Math.Min(source.FpsLimit, 60);
        return copy.Normalize();
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ClampToWorkArea();

        var status = WebView2Runtime.Detect();
        if (!status.IsInstalled)
        {
            PreviewStatus.Text = status.UserMessage;
            logger.Error($"WebView2 runtime missing: {status.Error}");
            SetStatus("WebView2 Runtime is missing. Settings can still be saved.");
            return;
        }

        if (!RendererHost.UseDevServer && !assets.IsAvailable)
        {
            PreviewStatus.Text =
                "The renderer bundle is not embedded in this build.\n"
                + "Run scripts\\build.ps1 to produce a complete FlowScreen.";
            logger.Error("renderer assets are not embedded in this assembly");
            SetStatus("Renderer bundle missing.");
            return;
        }

        await ReloadPreviewAsync().ConfigureAwait(true);
    }

    private async Task ReloadPreviewAsync()
    {
        try
        {
            if (preview is not null)
            {
                PreviewRoot.Children.Remove(preview.View);
                await preview.DisposeAsync().ConfigureAwait(true);
                preview = null;
            }

            var effective = PreviewSettings(settings);
            previewSeed = effective.Seed;
            previewVsync = effective.Vsync;

            var env = await environment.GetOrCreateAsync(effective).ConfigureAwait(true);

            preview = new RendererHost(logger, assets, allowDevTools: true);
            preview.RendererReady += (_, _) => Dispatcher.Invoke(() =>
            {
                PreviewStatus.Visibility = Visibility.Collapsed;
            });
            preview.RendererFailed += (_, message) => Dispatcher.Invoke(() =>
            {
                PreviewStatus.Visibility = Visibility.Visible;
                PreviewStatus.Text = message;
            });

            Panel.SetZIndex(preview.View, 1);
            PreviewRoot.Children.Add(preview.View);

            var width = (int)Math.Max(320, PreviewRoot.ActualWidth);
            var height = (int)Math.Max(180, PreviewRoot.ActualHeight);
            var view = ViewContextDto.Standalone(width, height, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            await preview.InitializeAsync(env, effective, view).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.Error("failed to start the settings preview", ex);
            PreviewStatus.Visibility = Visibility.Visible;
            PreviewStatus.Text = $"The preview could not start.\n{ex.Message}";
        }
    }

    // -------------------------------------------------------------- saving --

    private void SaveAndClose()
    {
        settings = ReadFromControls();
        if (settingsService.Save(settings))
        {
            DialogResult = true;
            Close();
            return;
        }

        SetStatus("Could not write settings.json. See the log for details.");
        MessageBox.Show(
            this,
            $"FlowScreen could not save its settings to:\n{settingsService.FilePath}\n\n"
            + $"A log has been written to:\n{FileLogger.LogDirectory}",
            "FlowScreen",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// Keeps the dialog inside the working area. The default size is expressed in
    /// DIPs, so on a 125% or 150% display it can otherwise open wider than the
    /// screen with its buttons off the right edge.
    /// </summary>
    private void ClampToWorkArea()
    {
        var maxWidth = SystemParameters.WorkArea.Width - 40;
        var maxHeight = SystemParameters.WorkArea.Height - 40;

        if (Width > maxWidth) Width = Math.Max(MinWidth, maxWidth);
        if (Height > maxHeight) Height = Math.Max(MinHeight, maxHeight);

        Left = SystemParameters.WorkArea.Left + ((SystemParameters.WorkArea.Width - Width) / 2);
        Top = SystemParameters.WorkArea.Top + ((SystemParameters.WorkArea.Height - Height) / 2);

        DarkTitleBar.Apply(this);
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private async void OnClosed(object? sender, EventArgs e)
    {
        reloadTimer.Stop();
        if (preview is not null)
        {
            await preview.DisposeAsync().ConfigureAwait(true);
            preview = null;
        }
    }
}
