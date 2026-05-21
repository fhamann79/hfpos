using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriWebServiceClient : ISriWebServiceClient
{
    private const string ReceptionNamespace = "http://ec.gob.sri.ws.recepcion";
    private const string AuthorizationNamespace = "http://ec.gob.sri.ws.autorizacion";

    private readonly HttpClient _httpClient;
    private readonly SriOptions _options;
    private readonly ILogger<SriWebServiceClient> _logger;

    public SriWebServiceClient(
        HttpClient httpClient,
        IOptions<SriOptions> options,
        ILogger<SriWebServiceClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SriReceptionResponse> SubmitAsync(
        string signedXml,
        int environment,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveReceptionEndpoint(environment);
        var xmlBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));
        var envelope = BuildReceptionEnvelope(xmlBase64);

        try
        {
            var rawResponse = await PostSoapAsync(endpoint, envelope, cancellationToken);
            return ParseReceptionResponse(rawResponse);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "SRI reception request timed out. Environment {Environment}", environment);
            throw new InvalidOperationException("SRI_RECEPTION_COMMUNICATION_FAILED", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "SRI reception HTTP request failed. Environment {Environment}", environment);
            throw new InvalidOperationException("SRI_RECEPTION_COMMUNICATION_FAILED", ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SRI reception response processing failed. Environment {Environment}", environment);
            throw new InvalidOperationException("SRI_RECEPTION_COMMUNICATION_FAILED", ex);
        }
    }

    public async Task<SriAuthorizationResponse> CheckAuthorizationAsync(
        string accessKey,
        int environment,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveAuthorizationEndpoint(environment);
        var envelope = BuildAuthorizationEnvelope(accessKey);

        try
        {
            var rawResponse = await PostSoapAsync(endpoint, envelope, cancellationToken);
            return ParseAuthorizationResponse(rawResponse);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "SRI authorization request timed out. Environment {Environment}", environment);
            throw new InvalidOperationException("SRI_AUTHORIZATION_COMMUNICATION_FAILED", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "SRI authorization HTTP request failed. Environment {Environment}", environment);
            throw new InvalidOperationException("SRI_AUTHORIZATION_COMMUNICATION_FAILED", ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SRI authorization response processing failed. Environment {Environment}", environment);
            throw new InvalidOperationException("SRI_AUTHORIZATION_COMMUNICATION_FAILED", ex);
        }
    }

    private string ResolveReceptionEndpoint(int environment)
    {
        var endpoint = environment == 2
            ? _options.ReceptionProductionUrl
            : _options.ReceptionTestUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("SRI_RECEPTION_ENDPOINT_NOT_CONFIGURED");
        }

        return endpoint.Trim();
    }

    private string ResolveAuthorizationEndpoint(int environment)
    {
        var endpoint = environment == 2
            ? _options.AuthorizationProductionUrl
            : _options.AuthorizationTestUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("SRI_AUTHORIZATION_ENDPOINT_NOT_CONFIGURED");
        }

        return endpoint.Trim();
    }

    private async Task<string> PostSoapAsync(
        string endpoint,
        string envelope,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml")
        {
            CharSet = "utf-8"
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"SRI SOAP HTTP status {(int)response.StatusCode}");
        }

        return rawResponse;
    }

    private static string BuildReceptionEnvelope(string xmlBase64)
        => $"""
           <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:rec="{ReceptionNamespace}">
             <soapenv:Header/>
             <soapenv:Body>
               <rec:validarComprobante>
                 <xml>{SecurityElementEscape(xmlBase64)}</xml>
               </rec:validarComprobante>
             </soapenv:Body>
           </soapenv:Envelope>
           """;

    private static string BuildAuthorizationEnvelope(string accessKey)
        => $"""
           <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:aut="{AuthorizationNamespace}">
             <soapenv:Header/>
             <soapenv:Body>
               <aut:autorizacionComprobante>
                 <claveAccesoComprobante>{SecurityElementEscape(accessKey)}</claveAccesoComprobante>
               </aut:autorizacionComprobante>
             </soapenv:Body>
           </soapenv:Envelope>
           """;

    private static SriReceptionResponse ParseReceptionResponse(string rawResponseXml)
    {
        var document = XDocument.Parse(rawResponseXml, LoadOptions.PreserveWhitespace);
        var estado = FirstValue(document, "estado")?.Trim().ToUpperInvariant();
        var messages = ParseMessages(document);

        return new SriReceptionResponse
        {
            RawResponseXml = rawResponseXml,
            Estado = estado,
            Messages = messages
        };
    }

    private static SriAuthorizationResponse ParseAuthorizationResponse(string rawResponseXml)
    {
        var document = XDocument.Parse(rawResponseXml, LoadOptions.PreserveWhitespace);
        var authorizationNode = Descendants(document, "autorizacion").FirstOrDefault();
        var responseContainer = authorizationNode ?? (XContainer)document;
        var estado = FirstValue(responseContainer, "estado")?.Trim().ToUpperInvariant();

        return new SriAuthorizationResponse
        {
            RawResponseXml = rawResponseXml,
            Estado = string.IsNullOrWhiteSpace(estado) ? "PENDIENTE" : estado,
            AuthorizationNumber = FirstValue(responseContainer, "numeroAutorizacion"),
            AuthorizationDate = ParseSriDate(FirstValue(responseContainer, "fechaAutorizacion")),
            AuthorizedXml = FirstValue(responseContainer, "comprobante"),
            Messages = ParseMessages(responseContainer)
        };
    }

    private static IReadOnlyList<SriResponseMessage> ParseMessages(XContainer container)
    {
        var messages = Descendants(container, "mensaje")
            .Where(element => element.Elements().Any())
            .Select(element => new SriResponseMessage
            {
                Identifier = FirstValue(element, "identificador"),
                Type = FirstValue(element, "tipo"),
                Message = FirstValue(element, "mensaje"),
                AdditionalInfo = FirstValue(element, "informacionAdicional")
            })
            .Where(message =>
                !string.IsNullOrWhiteSpace(message.Identifier)
                || !string.IsNullOrWhiteSpace(message.Type)
                || !string.IsNullOrWhiteSpace(message.Message)
                || !string.IsNullOrWhiteSpace(message.AdditionalInfo))
            .ToList();

        if (messages.Count > 0)
        {
            return messages;
        }

        var fault = FirstValue(container, "faultstring");
        return string.IsNullOrWhiteSpace(fault)
            ? Array.Empty<SriResponseMessage>()
            : new[]
            {
                new SriResponseMessage
                {
                    Type = "SOAP_FAULT",
                    Message = fault
                }
            };
    }

    private static IEnumerable<XElement> Descendants(XContainer container, string localName)
        => container.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static string? FirstValue(XContainer container, string localName)
        => Descendants(container, localName)
            .Select(element => element.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static DateTime? ParseSriDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[]
        {
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy H:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssK",
            "yyyy-MM-dd HH:mm:ss"
        };

        if (DateTime.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsedExact))
        {
            return parsedExact;
        }

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string SecurityElementEscape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
