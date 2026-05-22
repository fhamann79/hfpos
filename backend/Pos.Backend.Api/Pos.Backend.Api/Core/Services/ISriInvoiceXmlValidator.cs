namespace Pos.Backend.Api.Core.Services;

public interface ISriInvoiceXmlValidator
{
    void ValidateUnsignedInvoiceXml(string xml);
}
