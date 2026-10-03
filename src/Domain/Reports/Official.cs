namespace Domain.Reports;

/// <summary>Urzędnik obsługujący zgłoszenia. Hasło nie jest częścią profilu, trzyma je tylko host.</summary>
public sealed record OfficialProfile(string Login, string DisplayName, string Unit);

public sealed record OfficialLogin(string Login, string Password);
