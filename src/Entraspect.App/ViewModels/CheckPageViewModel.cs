using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>
/// One reusable page for every check: the permission banner, the title (with the description in an info tip), the
/// status of the last run, and its findings. Findings can be searched and filtered the way the Entra admin center
/// does it: Add filter, pick a property, then pick values or type text. Columns are named by the check's
/// <see cref="FindingLabels"/>. Required permissions are listed on Settings > Checks, not here.
/// </summary>
public partial class CheckPageViewModel : ViewModelBase
{
    public const string NotRunText = "Not run yet.";
    public const string DisabledText = "Turned off in Settings > Checks.";

    private readonly TimeDisplay _time;
    private IReadOnlyList<FindingRow> _allRows = [];
    private CheckResult? _result;
    private bool _isLicensed = true;

    /// <summary>False when the connected tenant doesn't have the licence this check needs.</summary>
    public bool IsLicensed => _isLicensed;

    /// <summary>The check's result in the last scan, or null if it hasn't run.</summary>
    public CheckResult? Result => _result;

    // The filter being edited, or null when adding a new one.
    private ActiveFilter? _editingFilter;

    public CheckPageViewModel(ICheck check, PermissionBannerViewModel banner, TimeDisplay time)
    {
        Check = check;
        Banner = banner;
        _time = time;
        Labels = check.Labels;
        FilterFields = BuildFilterFields(Labels);
    }

    public ICheck Check { get; }

    public PermissionBannerViewModel Banner { get; }

    public FindingLabels Labels { get; }

    public string Title => Check.Title;

    public string Description => Check.Description;

    /// <summary>The status line for a check the tenant isn't licensed for.</summary>
    public string NotAvailableText
    {
        get
        {
            var needs = Licensing.Needs(Check.RequiredLicence);
            return $"Not available: {char.ToLowerInvariant(needs[0])}{needs[1..]}";
        }
    }

    /// <summary>The due column header with the zone, for example "Expires (UTC)".</summary>
    public string DueHeader => $"{Labels.DueDate} ({_time.ZoneLabel})";

    [ObservableProperty]
    public partial bool IsDisabled { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = NotRunText;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<FindingRow> Findings { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public string FilterSummary => Findings.Count == TotalCount
        ? $"{TotalCount} finding(s)"
        : $"Showing {Findings.Count} of {TotalCount} finding(s)";

    public IReadOnlyList<FilterField> FilterFields { get; }

    public ObservableCollection<ActiveFilter> ActiveFilters { get; } = [];

    public bool HasActiveFilters => ActiveFilters.Count > 0;

    // Filter editor: first pick a field, then pick values (Choice) or type text (Text).
    [ObservableProperty]
    public partial bool IsFilterEditorOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosingField), nameof(IsEditingChoice), nameof(IsEditingText), nameof(EditorTitle))]
    [NotifyCanExecuteChangedFor(nameof(ApplyFilterCommand))]
    public partial FilterField? EditorField { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyFilterCommand))]
    public partial string EditorText { get; set; } = string.Empty;

    public ObservableCollection<FilterChoice> EditorChoices { get; } = [];

    public bool IsChoosingField => EditorField is null;

    public bool IsEditingChoice => EditorField?.Kind == FilterKind.Choice;

    public bool IsEditingText => EditorField?.Kind == FilterKind.Text;

    public string EditorTitle => EditorField is null ? "Filter on" : EditorField.Name;

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    /// <summary>
    /// Shows the permission banner when any required permission isn't granted, unless the tenant isn't licensed for
    /// the check: then the status line says so and there is no banner.
    /// </summary>
    /// <param name="granted">The connection's granted permissions, or null when not connected.</param>
    public void SetConnection(IReadOnlyCollection<string>? granted, TenantLicence licence)
    {
        var licensed = Licensing.IsLicensed(Check, licence);
        Banner.ShowForCheck(granted is null || !licensed
            ? []
            : Check.RequiredPermissions.Where(p => !granted.Contains(p)).ToList());

        if (licensed != _isLicensed)
        {
            _isLicensed = licensed;
            OnPropertyChanged(nameof(IsLicensed));
            SetResult(_result);
        }
    }

    public void SetResult(CheckResult? result)
    {
        _result = result;
        ErrorText = string.Empty;
        _allRows = [];
        var lastRun = result?.CompletedAt is { } completed ? _time.DateTime(completed) : string.Empty;

        switch (result?.Status)
        {
            case null when !_isLicensed:
            case CheckResultStatus.MissingLicence:
                StatusText = NotAvailableText;
                break;
            case null:
                StatusText = NotRunText;
                break;
            case CheckResultStatus.Succeeded:
                // Default order: most severe first, then the soonest due date (no due date last).
                _allRows = result.Findings
                    .OrderByDescending(f => f.Severity)
                    .ThenBy(f => f.DueDate ?? DateTimeOffset.MaxValue)
                    .Select(f => new FindingRow(f, _time))
                    .ToList();
                StatusText = result.DataUpdatedAt is { } updated
                    ? $"Last run {lastRun} · Report updated {_time.DateTime(updated)}"
                    : $"Last run {lastRun}";
                if (result.DataPeriod is { } period)
                {
                    StatusText += $" · {period.ItemsRead.ToString("N0", CultureInfo.InvariantCulture)} " +
                        $"{period.ItemName}(s) from {_time.DateTime(period.Start)} to {_time.DateTime(period.End)}";
                }

                break;
            case CheckResultStatus.Failed:
                StatusText = $"Last run {lastRun} failed";
                // A missing licence also comes back as 403; it isn't a permission problem.
                ErrorText = result.StatusCode == 403 && !CheckFailureMessage.IsLicenceRequired(result.ErrorCode)
                    ? $"{result.ErrorMessage}\n\nAccess was denied. The app registration is likely missing " +
                      $"{string.Join(", ", Check.RequiredPermissions)}, or admin consent has not been granted."
                    : CheckFailureMessage.For(result);
                break;
            case CheckResultStatus.MissingPermissions:
                // The permission banner already explains what is missing.
                StatusText = "Not run: missing permissions";
                break;
            case CheckResultStatus.Cancelled:
                StatusText = "Last scan cancelled";
                break;
        }

        TotalCount = _allRows.Count;
        OnPropertyChanged(nameof(DueHeader));
        ApplyFilters();
    }

    [RelayCommand]
    private void AddFilter()
    {
        _editingFilter = null;
        EditorField = null;
        IsFilterEditorOpen = true;
    }

    /// <summary>Picks the property to filter on. A property that already has a filter opens that filter instead.</summary>
    [RelayCommand]
    private void ChooseField(FilterField? field)
    {
        if (field is null)
        {
            return;
        }

        _editingFilter = ActiveFilters.FirstOrDefault(f => f.Field == field);
        OpenEditor(field, _editingFilter);
    }

    [RelayCommand]
    private void EditFilter(ActiveFilter? filter)
    {
        if (filter is null)
        {
            return;
        }

        _editingFilter = filter;
        OpenEditor(filter.Field, filter);
        IsFilterEditorOpen = true;
    }

    private bool CanApplyFilter() => EditorField?.Kind switch
    {
        FilterKind.Choice => EditorChoices.Any(c => c.IsSelected),
        FilterKind.Text => !string.IsNullOrWhiteSpace(EditorText),
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanApplyFilter))]
    private void ApplyFilter()
    {
        if (EditorField is not { } field)
        {
            return;
        }

        var filter = new ActiveFilter(
            field,
            EditorChoices.Where(c => c.IsSelected).Select(c => c.Value).ToList(),
            EditorText.Trim());

        var index = _editingFilter is null ? -1 : ActiveFilters.IndexOf(_editingFilter);
        if (index >= 0)
        {
            ActiveFilters[index] = filter;
        }
        else
        {
            ActiveFilters.Add(filter);
        }

        CloseEditor();
        ApplyFilters();
    }

    [RelayCommand]
    private void CancelFilter() => CloseEditor();

    [RelayCommand]
    private void RemoveFilter(ActiveFilter? filter)
    {
        if (filter is not null && ActiveFilters.Remove(filter))
        {
            ApplyFilters();
        }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        ActiveFilters.Clear();
        SearchText = string.Empty;
        ApplyFilters();
    }

    private void OpenEditor(FilterField field, ActiveFilter? existing)
    {
        foreach (var choice in EditorChoices)
        {
            choice.PropertyChanged -= OnChoiceChanged;
        }

        EditorChoices.Clear();
        if (field.Kind == FilterKind.Choice)
        {
            foreach (var value in field.ChoicesFor(_allRows))
            {
                var choice = new FilterChoice(value, existing?.Values.Contains(value) == true);
                choice.PropertyChanged += OnChoiceChanged;
                EditorChoices.Add(choice);
            }
        }

        EditorText = existing?.Text ?? string.Empty;
        EditorField = field;
        ApplyFilterCommand.NotifyCanExecuteChanged();
    }

    private void OnChoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ApplyFilterCommand.NotifyCanExecuteChanged();

    private void CloseEditor()
    {
        IsFilterEditorOpen = false;
        EditorField = null;
        _editingFilter = null;
    }

    private void ApplyFilters()
    {
        Findings = new ObservableCollection<FindingRow>(_allRows.Where(row =>
            row.MatchesSearch(SearchText) && ActiveFilters.All(f => f.Matches(row))));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    private static IReadOnlyList<FilterField> BuildFilterFields(FindingLabels labels)
    {
        var fields = new List<FilterField>
        {
            new("Severity", FilterKind.Choice, r => r.SeverityText,
                _ => [nameof(Severity.Critical), nameof(Severity.Warning), nameof(Severity.Info)]),
            new(labels.ObjectDisplayName, FilterKind.Text, r => r.ObjectDisplayName),
        };

        if (labels.UserPrincipalName is { } userPrincipalName)
        {
            fields.Add(new FilterField(userPrincipalName, FilterKind.Text, r => r.UserPrincipalName));
        }

        if (labels.ObjectId is { } objectId)
        {
            fields.Add(new FilterField(objectId, FilterKind.Text, r => r.ObjectId));
        }

        if (labels.SubObjectDisplayName is { } item)
        {
            fields.Add(new FilterField(item, FilterKind.Text, r => r.SubObjectDisplayName));
        }

        if (labels.SubObjectType is { } type)
        {
            fields.Add(new FilterField(type, FilterKind.Choice, r => r.SubObjectType));
        }

        fields.Add(new FilterField("Message", FilterKind.Text, r => r.Message));
        return fields;
    }
}
