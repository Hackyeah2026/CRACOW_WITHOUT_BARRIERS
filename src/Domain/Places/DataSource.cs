namespace Domain.Places;

/// <summary>Zbiór danych, z którego korzysta aplikacja: do strony "Źródła danych" i dokumentacji.</summary>
/// <param name="DataDate">Czas wytworzenia danych według dostawcy (np. "2022", "stan 2023", "17.09.2026").</param>
/// <param name="RetrievedOn">Dzień pobrania przez import aplikacji.</param>
/// <param name="Records">Liczba rekordów w pobranym zbiorze; null dla usług bez zbioru (np. wyznaczanie tras).</param>
/// <param name="CityPortal">Zbiór z portali miasta (msip.krakow.pl, otwartedane.um.krakow.pl).</param>
public sealed record DataSource(
    string Name, string Provider, string Url, string License, string DataDate, DateOnly RetrievedOn,
    int? Records, string Usage, bool CityPortal);

public sealed record DataSourcesReport(DateOnly GeneratedOn, IReadOnlyList<DataSource> Sources);
