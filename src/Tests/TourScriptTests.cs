using Web.Client.Services;

namespace Tests;

public class TourScriptTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tour_covers_profile_plan_and_reports(bool loggedIn)
    {
        var paths = TourScript.For(loggedIn).Select(s => s.Path).ToList();

        Assert.Contains(loggedIn ? "konto/profil" : "profil", paths);
        Assert.Contains("miejsca", paths);
        Assert.Contains("plan", paths);
        Assert.Contains("zglos", paths);
    }

    [Fact]
    public void Guest_edits_temporary_profile_and_logged_in_user_edits_account_profile()
    {
        Assert.DoesNotContain(TourScript.For(loggedIn: false), s => s.Path == "konto/profil");
        Assert.DoesNotContain(TourScript.For(loggedIn: true), s => s.Path == "profil");
    }

    [Fact]
    public void Guest_is_told_that_sending_a_report_needs_an_account()
    {
        var guest = Step(loggedIn: false, "report-form");
        var user = Step(loggedIn: true, "report-form");

        Assert.Contains("konto", guest.Title);
        Assert.DoesNotContain("konto", user.Title);
        Assert.NotEqual(guest.Description, user.Description);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_page_has_a_step_that_is_always_shown(bool loggedIn)
    {
        // Strona bez miejsc albo z pustym planem nie może zostać bez żadnego kroku.
        Assert.All(TourScript.For(loggedIn), segment => Assert.Contains(segment.Steps, step => !step.Optional));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Steps_point_at_different_elements(bool loggedIn)
    {
        var selectors = TourScript.For(loggedIn).SelectMany(s => s.Steps).Select(s => s.Selector).ToList();

        Assert.Equal(selectors.Count, selectors.Distinct().Count());
    }

    private static TourStep Step(bool loggedIn, string anchor) =>
        TourScript.For(loggedIn).SelectMany(s => s.Steps).Single(s => s.Selector.Contains(anchor));
}
