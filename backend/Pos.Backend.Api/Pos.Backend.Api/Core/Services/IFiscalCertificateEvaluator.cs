using System.Security.Cryptography.X509Certificates;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface IFiscalCertificateEvaluator
{
    FiscalTrustDecision Evaluate(X509Certificate2 signer, IReadOnlyCollection<X509Certificate2> auxiliary,
        string companyRuc, DateTime utcNow);
}
