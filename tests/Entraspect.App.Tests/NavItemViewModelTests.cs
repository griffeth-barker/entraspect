using Entraspect.App.ViewModels;
using Entraspect.Core;

namespace Entraspect.App.Tests;

public sealed class NavItemViewModelTests
{
    [Fact]
    public void A_check_with_findings_shows_the_total_and_the_highest_severity()
    {
        var item = Check();

        item.SetResult(Succeeded(Severity.Warning, Severity.Critical, Severity.Warning, Severity.Info, Severity.Warning));

        Assert.Equal(5, item.BadgeCount);
        Assert.Equal(Severity.Critical, item.BadgeSeverity);
        Assert.Equal("1 critical, 3 warning, 1 info", item.CountToolTip);
        Assert.False(item.HasProblem);
        Assert.Null(item.ProblemToolTip);
        Assert.True(item.ShowBadges);
    }

    [Fact]
    public void The_count_tooltip_leaves_out_severities_with_none()
    {
        var item = Check();

        item.SetResult(Succeeded(Severity.Info, Severity.Warning, Severity.Info));

        Assert.Equal(Severity.Warning, item.BadgeSeverity);
        Assert.Equal("1 warning, 2 info", item.CountToolTip);
    }

    [Fact]
    public void A_check_with_no_findings_shows_no_badge()
    {
        var item = Check();

        item.SetResult(Succeeded());

        AssertNoBadge(item);
    }

    [Theory]
    [InlineData(CheckResultStatus.Failed, NavItemViewModel.FailedToolTip)]
    [InlineData(CheckResultStatus.MissingPermissions, NavItemViewModel.MissingPermissionsToolTip)]
    public void A_check_without_a_result_shows_the_problem_badge(CheckResultStatus status, string toolTip)
    {
        var item = Check();

        item.SetResult(new CheckResult { CheckId = "fake", Status = status });

        Assert.True(item.HasProblem);
        Assert.Equal(toolTip, item.ProblemToolTip);
        Assert.Equal(0, item.BadgeCount);
        Assert.Null(item.BadgeSeverity);
        Assert.True(item.ShowBadges);
    }

    [Theory]
    [InlineData(CheckResultStatus.Cancelled)]
    [InlineData(CheckResultStatus.MissingLicence)]
    public void A_cancelled_or_unlicensed_check_shows_no_badge(CheckResultStatus status)
    {
        var item = Check();
        item.SetResult(Succeeded(Severity.Critical));

        item.SetResult(new CheckResult { CheckId = "fake", Status = status });

        AssertNoBadge(item);
    }

    [Fact]
    public void Clearing_the_result_removes_the_badges()
    {
        var item = Check();
        item.SetResult(new CheckResult { CheckId = "fake", Status = CheckResultStatus.Failed });

        item.SetResult(null);

        AssertNoBadge(item);
    }

    [Fact]
    public void A_disabled_item_shows_no_badge()
    {
        var item = Check();
        item.SetResult(Succeeded(Severity.Info));

        item.IsEnabled = false;

        Assert.Equal(1, item.BadgeCount);
        Assert.False(item.ShowBadges);
    }

    [Fact]
    public void A_collapsed_section_sums_its_checks()
    {
        var (section, first, second) = Section();

        first.SetResult(Succeeded(Severity.Warning, Severity.Info));
        second.SetResult(Succeeded(Severity.Critical));

        Assert.Equal(3, section.BadgeCount);
        Assert.Equal(Severity.Critical, section.BadgeSeverity);
        Assert.Equal("1 critical, 1 warning, 1 info", section.CountToolTip);
        Assert.False(section.HasProblem);
        Assert.True(section.ShowBadges);
    }

    [Fact]
    public void An_expanded_section_shows_no_badge_and_collapsing_it_shows_the_summary_again()
    {
        var (section, first, _) = Section();
        first.SetResult(new CheckResult { CheckId = "fake", Status = CheckResultStatus.Failed });
        first.SetResult(Succeeded(Severity.Warning));

        section.IsExpanded = true;

        AssertNoBadge(section);

        section.IsExpanded = false;

        Assert.Equal(1, section.BadgeCount);
        Assert.Equal(Severity.Warning, section.BadgeSeverity);
    }

    [Fact]
    public void A_collapsed_section_with_a_failed_check_shows_both_badges()
    {
        var (section, first, second) = Section();

        first.SetResult(new CheckResult { CheckId = "fake", Status = CheckResultStatus.Failed });
        second.SetResult(Succeeded(Severity.Info, Severity.Info));

        Assert.Equal(2, section.BadgeCount);
        Assert.Equal(Severity.Info, section.BadgeSeverity);
        Assert.Equal("2 info", section.CountToolTip);
        Assert.True(section.HasProblem);
        Assert.Equal("1 check failed", section.ProblemToolTip);
    }

    [Fact]
    public void The_problem_tooltip_counts_failed_checks_and_checks_missing_permissions()
    {
        var (section, first, second) = Section();
        var third = Check();
        var fourth = Check();
        section.Add(third).Add(fourth);

        first.SetResult(new CheckResult { CheckId = "fake", Status = CheckResultStatus.Failed });
        second.SetResult(new CheckResult { CheckId = "fake", Status = CheckResultStatus.Failed });
        Assert.Equal("2 checks failed", section.ProblemToolTip);

        third.SetResult(new CheckResult { CheckId = "fake", Status = CheckResultStatus.MissingPermissions });
        Assert.Equal("2 checks failed, 1 check missing permissions", section.ProblemToolTip);

        fourth.SetResult(new CheckResult { CheckId = "fake", Status = CheckResultStatus.MissingPermissions });
        Assert.Equal("2 checks failed, 2 checks missing permissions", section.ProblemToolTip);
        Assert.False(section.HasCount);
    }

    [Fact]
    public void Settings_checks_shows_the_problem_badge_while_a_check_is_missing_permissions()
    {
        var item = new NavItemViewModel(NavItemKind.SettingsPage, "Checks", "Library");

        item.SetWarning(true);

        Assert.True(item.HasProblem);
        Assert.Equal(NavItemViewModel.MissingPermissionsWarning, item.ProblemToolTip);
        Assert.Equal(0, item.BadgeCount);

        item.SetWarning(false);

        AssertNoBadge(item);
        Assert.Null(item.ProblemToolTip);
    }

    private static void AssertNoBadge(NavItemViewModel item)
    {
        Assert.Equal(0, item.BadgeCount);
        Assert.Null(item.BadgeSeverity);
        Assert.Null(item.CountToolTip);
        Assert.False(item.HasProblem);
        Assert.False(item.ShowBadges);
    }

    private static NavItemViewModel Check() => new(NavItemKind.Check, "Fake check");

    private static (NavItemViewModel Section, NavItemViewModel First, NavItemViewModel Second) Section()
    {
        var first = Check();
        var second = Check();
        var section = new NavItemViewModel(NavItemKind.Section, "Fake section", "AllApps").Add(first).Add(second);
        return (section, first, second);
    }

    private static CheckResult Succeeded(params Severity[] severities) => new()
    {
        CheckId = "fake",
        Status = CheckResultStatus.Succeeded,
        Findings = severities.Select((severity, i) => new Finding
        {
            CheckId = "fake",
            Severity = severity,
            ObjectType = "Application",
            ObjectId = $"obj-{i}",
            ObjectDisplayName = $"Fake app {i}",
            Message = "fake message",
        }).ToList(),
    };
}
