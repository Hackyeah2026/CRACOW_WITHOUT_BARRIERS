using Application.Abstractions;
using Domain.Businesses;

namespace Application.Businesses;

/// <summary>Składa wniosek o konto firmowe z konta zalogowanego użytkownika.</summary>
public sealed record SubmitBusinessApplicationCommand(BusinessApplicationDraft Draft) : ICommand<BusinessAccountView>;

internal sealed class SubmitBusinessApplicationCommandHandler(IBusinessClient client)
    : ICommandHandler<SubmitBusinessApplicationCommand, BusinessAccountView>
{
    public Task<Result<BusinessAccountView>> Handle(SubmitBusinessApplicationCommand command, CancellationToken ct) =>
        command.Draft.Validate().IfValidAsync(() => client.ApplyAsync(command.Draft, ct));
}

/// <summary>Wniosek albo konto firmowe zalogowanego użytkownika.</summary>
public sealed record GetMyBusinessQuery : IQuery<MyBusiness>;

internal sealed class GetMyBusinessQueryHandler(IBusinessClient client) : IQueryHandler<GetMyBusinessQuery, MyBusiness>
{
    public Task<Result<MyBusiness>> Handle(GetMyBusinessQuery query, CancellationToken ct) => client.GetMineAsync(ct);
}

/// <summary>Zapisuje oznaczenia udogodnień zatwierdzonej firmy.</summary>
public sealed record SaveBusinessFeaturesCommand(BusinessFeaturesUpdate Update) : ICommand<BusinessAccountView>;

internal sealed class SaveBusinessFeaturesCommandHandler(IBusinessClient client)
    : ICommandHandler<SaveBusinessFeaturesCommand, BusinessAccountView>
{
    public Task<Result<BusinessAccountView>> Handle(SaveBusinessFeaturesCommand command, CancellationToken ct) =>
        command.Update.Validate().IfValidAsync(() => client.SaveFeaturesAsync(command.Update, ct));
}

/// <summary>Miejsca z certyfikatem w mieście, widoczne dla wszystkich.</summary>
public sealed record GetCertifiedPlacesQuery(string CityId) : IQuery<IReadOnlyList<CertifiedPlace>>;

internal sealed class GetCertifiedPlacesQueryHandler(IBusinessClient client)
    : IQueryHandler<GetCertifiedPlacesQuery, IReadOnlyList<CertifiedPlace>>
{
    public Task<Result<IReadOnlyList<CertifiedPlace>>> Handle(GetCertifiedPlacesQuery query, CancellationToken ct) =>
        client.GetCertifiedAsync(query.CityId, ct);
}

/// <summary>Wnioski o konta firmowe dla panelu urzędnika.</summary>
public sealed record GetBusinessesQuery(string? CityId) : IQuery<IReadOnlyList<BusinessAccount>>;

internal sealed class GetBusinessesQueryHandler(IOfficialClient client) : IQueryHandler<GetBusinessesQuery, IReadOnlyList<BusinessAccount>>
{
    public Task<Result<IReadOnlyList<BusinessAccount>>> Handle(GetBusinessesQuery query, CancellationToken ct) =>
        client.GetBusinessesAsync(query.CityId, ct);
}

public sealed record ReviewBusinessCommand(string Login, BusinessReview Review) : ICommand<BusinessAccount>;

internal sealed class ReviewBusinessCommandHandler(IOfficialClient client) : ICommandHandler<ReviewBusinessCommand, BusinessAccount>
{
    public Task<Result<BusinessAccount>> Handle(ReviewBusinessCommand command, CancellationToken ct) =>
        command.Review.Validate().IfValidAsync(() => client.ReviewBusinessAsync(command.Login, command.Review, ct));
}
