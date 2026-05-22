using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface ISriWebServiceClient
{
    Task<SriReceptionResponse> SubmitAsync(string signedXml, int environment, CancellationToken cancellationToken = default);

    Task<SriAuthorizationResponse> CheckAuthorizationAsync(string accessKey, int environment, CancellationToken cancellationToken = default);
}
