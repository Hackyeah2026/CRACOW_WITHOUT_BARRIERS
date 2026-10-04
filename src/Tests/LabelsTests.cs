using Domain.Needs;
using Web.Client.Services;

namespace Tests;

public class LabelsTests
{
    [Theory]
    [InlineData(1, "przystanek")]
    [InlineData(2, "przystanki")]
    [InlineData(4, "przystanki")]
    [InlineData(5, "przystanków")]
    [InlineData(12, "przystanków")]
    [InlineData(14, "przystanków")]
    [InlineData(22, "przystanki")]
    [InlineData(25, "przystanków")]
    [InlineData(112, "przystanków")]
    [InlineData(0, "przystanków")]
    public void Noun_follows_the_polish_plural_rules(int count, string expected)
    {
        Assert.Equal(expected, Labels.Plural(count, "przystanek", "przystanki", "przystanków"));
        Assert.Equal($"{count} {expected}", Labels.Stops(count));
    }

    [Fact]
    public void Missing_places_notice_says_how_many_places_left_the_plan()
    {
        Assert.Equal("Usunęliśmy z planu 1 miejsce, którego nie ma już w katalogu.", Labels.MissingPlaces(1, removed: true));
        Assert.Equal("Usunęliśmy z planu 2 miejsca, których nie ma już w katalogu.", Labels.MissingPlaces(2, removed: true));
        Assert.Equal("Usunęliśmy z planu 5 miejsc, których nie ma już w katalogu.", Labels.MissingPlaces(5, removed: true));
        // Plan konta, którego host nie zapisał: miejsca nie ma na liście, ale w planie zostało.
        Assert.Equal("Pominęliśmy 2 miejsca, których nie ma już w katalogu. Nie udało się zapisać tej zmiany w planie.",
            Labels.MissingPlaces(2, removed: false));
    }

    [Fact]
    public void Preset_names_come_from_the_selected_presets_in_catalog_order()
    {
        var profile = NeedsProfilePresets.Build([NeedsProfilePresets.Senior, NeedsProfilePresets.Blind]);

        Assert.Equal(["Osoba niewidoma / słabowidząca", "Senior"], Labels.PresetNames(profile));
        Assert.Empty(Labels.PresetNames(new NeedsProfile { AvoidStairs = true }));
    }

    [Fact]
    public void Every_location_failure_tells_what_to_do_instead() =>
        Assert.All(Enum.GetValues<LocationFailure>(), failure =>
            Assert.Contains("wskaż start na mapie", Labels.Of(failure, "wskaż start na mapie")));
}
