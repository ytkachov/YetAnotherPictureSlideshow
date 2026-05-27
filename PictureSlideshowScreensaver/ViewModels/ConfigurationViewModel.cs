using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Serilog.Core;
using Serilog.Events;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;

namespace PictureSlideshowScreensaver.ViewModels
{
  /// <summary>
  /// View model behind the /c configuration window. Owns the Registry
  /// round-trip, the provider list and change tracking; the view keeps
  /// only the inherently view-layer bits (the OS folder picker, message
  /// boxes and closing the window).
  /// </summary>
  public partial class ConfigurationViewModel : ObservableObject
  {
    private const string RegistryPath = "SOFTWARE\\PictureSlideshowScreensaver";
    private const string DefaultWeatherProvider = "yandex-api";

    private bool _loading;

    [ObservableProperty]
    private string _imageFolder = "";

    // Semicolon-separated, positionally paired with ImageFolder. An empty entry
    // keeps that photo folder's .finfo next to the photos; a path stores them
    // (mirrored) only under that folder — for read-only photo libraries.
    [ObservableProperty]
    private string _finfoFolder = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntervalText))]
    private double _interval = 5;

    [ObservableProperty]
    private string _selectedProvider = DefaultWeatherProvider;

    // Empty string = no secondary (the "(none)" item at the top of
    // SecondaryProviderItems). Anything else must match a registered
    // provider name; same SelectedValuePath="Name" pattern as the primary.
    [ObservableProperty]
    private string _selectedSecondaryProvider = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecondaryIntervalText))]
    private int _secondaryPollingMinutes = 30;

    [ObservableProperty]
    private bool _showProviderBadge = true;

    public string SecondaryIntervalText => SecondaryPollingMinutes.ToString(CultureInfo.InvariantCulture) + " min";

    // Stored as the Serilog level name; the ComboBox is populated from
    // LogLevels. The list deliberately omits Fatal — picking "log only
    // fatal" silences everything the screensaver actually emits.
    [ObservableProperty]
    private string _selectedLogLevel = "Verbose";

    public IReadOnlyList<string> LogLevels { get; } = new[] { "Verbose", "Debug", "Information", "Warning", "Error" };

    public string IntervalText => Interval.ToString(CultureInfo.InvariantCulture) + " seconds";

    public IReadOnlyList<WeatherProviderDescriptor> WeatherProviders { get; }

    // Same descriptors as WeatherProviders, with a synthetic "(none)"
    // entry prepended so the user can disable the secondary tier. Name=""
    // is the sentinel persisted to the Registry as "no secondary".
    public IReadOnlyList<WeatherProviderDescriptor> SecondaryProviderItems { get; }

    public bool ProviderSelectionEnabled => WeatherProviders.Count > 0;

    // True once the user touches anything; the window's Closing handler
    // reads it to decide whether to prompt about discarding changes.
    public bool HasUnsavedChanges { get; private set; }

    // Folder picking and error reporting are genuine view concerns; the
    // window wires these up. RequestClose lets Save/Cancel close the app
    // without the VM depending on Application.
    public Func<string> BrowseForFolder { get; set; }
    public Action<string> ShowError { get; set; }
    public event EventHandler RequestClose;

    private readonly LoggingLevelSwitch _levelSwitch;

    public ConfigurationViewModel(IWeatherProviderRegistry registry, LoggingLevelSwitch levelSwitch)
    {
      WeatherProviders = registry?.Available ?? (IReadOnlyList<WeatherProviderDescriptor>)Array.Empty<WeatherProviderDescriptor>();
      var secondaryItems = new List<WeatherProviderDescriptor>(WeatherProviders.Count + 1)
      {
        new WeatherProviderDescriptor("", "(none)", WeatherCapabilities.None)
      };
      secondaryItems.AddRange(WeatherProviders);
      SecondaryProviderItems = secondaryItems;
      _levelSwitch = levelSwitch;
      Load();
    }

    private void Load()
    {
      _loading = true;
      try
      {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        if (key != null)
        {
          ImageFolder = (string)key.GetValue("ImageFolder") ?? "";
          FinfoFolder = (string)key.GetValue("FinfoFolder") ?? "";

          if (double.TryParse((string)key.GetValue("Interval"), NumberStyles.Float, CultureInfo.InvariantCulture, out double iv))
            Interval = iv;

          var stored = (string)key.GetValue("WeatherProvider");
          if (!string.IsNullOrEmpty(stored))
            SelectedProvider = stored;

          var storedSecondary = (string)key.GetValue("WeatherProviderSecondary");
          SelectedSecondaryProvider = storedSecondary ?? "";

          if (int.TryParse((string)key.GetValue("WeatherPollingMinutesSecondary"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sm))
            SecondaryPollingMinutes = Math.Clamp(sm, 1, 1440);

          var badgeRaw = (string)key.GetValue("WeatherShowProviderBadge");
          if (!string.IsNullOrEmpty(badgeRaw) && int.TryParse(badgeRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var badge))
            ShowProviderBadge = badge != 0;

          var storedLevel = (string)key.GetValue("LogLevel");
          if (!string.IsNullOrEmpty(storedLevel) && LogLevels.Contains(storedLevel))
            SelectedLogLevel = storedLevel;
        }

        // A stored name no longer offered by the registry would leave the
        // ComboBox blank — fall back to the first available provider.
        if (WeatherProviders.Count > 0 && WeatherProviders.All(p => p.Name != SelectedProvider))
          SelectedProvider = WeatherProviders[0].Name;

        // Same guard for the secondary — but "" ("(none)") is a valid choice,
        // so only normalise when a stored non-empty name no longer exists.
        if (!string.IsNullOrEmpty(SelectedSecondaryProvider)
            && WeatherProviders.All(p => p.Name != SelectedSecondaryProvider))
          SelectedSecondaryProvider = "";

        HasUnsavedChanges = false;
      }
      finally
      {
        _loading = false;
      }
    }

    [RelayCommand]
    private void Browse()
    {
      var picked = BrowseForFolder?.Invoke();
      if (!string.IsNullOrEmpty(picked))
        ImageFolder = picked;
    }

    [RelayCommand]
    private void Save()
    {
      if (!Directory.Exists(ImageFolder))
      {
        ShowError?.Invoke("The selected folder does not exist!");
        return;
      }

      // Each non-empty finfo entry must be a usable directory; create it now so
      // the store can write there. Empty entries mean "next to the photo".
      foreach (var entry in (FinfoFolder ?? "").Split(';'))
      {
        var path = entry.Trim();
        if (path.Length == 0)
          continue;

        try
        {
          Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
          ShowError?.Invoke($"Cannot use finfo folder \"{path}\": {ex.Message}");
          return;
        }
      }

      using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
      {
        key.SetValue("ImageFolder", ImageFolder);
        key.SetValue("FinfoFolder", FinfoFolder ?? "");
        key.SetValue("Interval", Interval.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(SelectedProvider))
          key.SetValue("WeatherProvider", SelectedProvider);
        key.SetValue("WeatherProviderSecondary", SelectedSecondaryProvider ?? "");
        key.SetValue("WeatherPollingMinutesSecondary", SecondaryPollingMinutes.ToString(CultureInfo.InvariantCulture));
        key.SetValue("WeatherShowProviderBadge", ShowProviderBadge ? "1" : "0");
        key.SetValue("LogLevel", SelectedLogLevel ?? "Verbose");
      }

      // Apply the level live as well so the screensaver doesn't need a
      // restart for the choice to take effect. Same switch the L-viewer
      // ComboBox drives.
      if (_levelSwitch is not null &&
          Enum.TryParse<LogEventLevel>(SelectedLogLevel ?? "Verbose", ignoreCase: true, out var level))
        _levelSwitch.MinimumLevel = level;

      HasUnsavedChanges = false;
      RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
      HasUnsavedChanges = false;
      RequestClose?.Invoke(this, EventArgs.Empty);
    }

    partial void OnImageFolderChanged(string value) => MarkDirty();
    partial void OnFinfoFolderChanged(string value) => MarkDirty();
    partial void OnIntervalChanged(double value) => MarkDirty();
    partial void OnSelectedProviderChanged(string value) => MarkDirty();
    partial void OnSelectedSecondaryProviderChanged(string value) => MarkDirty();
    partial void OnSecondaryPollingMinutesChanged(int value) => MarkDirty();
    partial void OnShowProviderBadgeChanged(bool value) => MarkDirty();
    partial void OnSelectedLogLevelChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
      if (!_loading)
        HasUnsavedChanges = true;
    }
  }
}
