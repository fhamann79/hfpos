using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Http;

namespace Hfpos.FiscalSmokeHost;

// Only the test host installs this filter. All HTTP destinations fail closed except the synthetic SOAP host.
public sealed class SyntheticHttpFilter(SmokeTransport remote) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        builder.PrimaryHandler = new SyntheticSoapHandler(remote);
    };
}

internal sealed class SyntheticSoapHandler(SmokeTransport remote) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.Host != "synthetic-sri.invalid")
            throw new InvalidOperationException("SYNTHETIC_HOST_EXTERNAL_HTTP_FORBIDDEN");
        var envelope = XDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        XElement body;
        if (envelope.Descendants().Any(e => e.Name.LocalName == "validarComprobante"))
        {
            var base64 = envelope.Descendants().Single(e => e.Name.LocalName == "xml").Value;
            var response = await remote.SubmitAsync(Encoding.UTF8.GetString(Convert.FromBase64String(base64)), 1, ct);
            body = new XElement("RespuestaRecepcionComprobante", new XElement("estado", response.Estado));
        }
        else
        {
            var key = envelope.Descendants().Single(e => e.Name.LocalName == "claveAccesoComprobante").Value;
            var response = await remote.CheckAuthorizationAsync(key, 1, ct);
            body = response.RawResponseXml is { Length: > 0 }
                ? XElement.Parse(response.RawResponseXml)
                : new XElement("RespuestaAutorizacionComprobante", new XElement("estado", response.Estado));
        }
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        var xml = new XElement(soap + "Envelope", new XElement(soap + "Body", body));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml")
        };
    }
}
