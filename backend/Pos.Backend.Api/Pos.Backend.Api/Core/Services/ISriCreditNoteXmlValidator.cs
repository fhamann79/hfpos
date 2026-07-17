namespace Pos.Backend.Api.Core.Services;

public interface ISriCreditNoteXmlValidator
{
    void ValidateUnsignedCreditNoteXml(string xml);
}
