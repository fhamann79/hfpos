using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
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
    private const string ValidityNamespace = "http://ec.gob.sri.ws.consultas";

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

    public async Task<SriValidityResponse> CheckValidityAsync(string accessKey, int environment, CancellationToken cancellationToken = default)
    {
        ValidateEnvironment(environment);
        var endpoint = environment == 2 ? _options.ValidityProductionUrl : _options.ValidityTestUrl;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("SRI_VALIDITY_ENDPOINT_NOT_CONFIGURED");
        var envelope = $"""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:ec="{ValidityNamespace}">
              <soapenv:Body><ec:consultarEstadoAutorizacionComprobante>
                <claveAcceso>{SecurityElementEscape(accessKey)}</claveAcceso>
              </ec:consultarEstadoAutorizacionComprobante></soapenv:Body>
            </soapenv:Envelope>
            """;
        try
        {
            return ParseValidityResponse(await PostSoapAsync(endpoint.Trim(), envelope, cancellationToken), accessKey);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Do not include the key, response payload or transport exception in logs.
            _logger.LogWarning("SRI validity request failed. Environment {Environment}", environment);
            throw new InvalidOperationException("SRI_VALIDITY_COMMUNICATION_FAILED", ex);
        }
    }

    private string ResolveReceptionEndpoint(int environment)
    {
        ValidateEnvironment(environment);
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
        ValidateEnvironment(environment);
        var endpoint = environment == 2
            ? _options.AuthorizationProductionUrl
            : _options.AuthorizationTestUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("SRI_AUTHORIZATION_ENDPOINT_NOT_CONFIGURED");
        }

        return endpoint.Trim();
    }

    private void ValidateEnvironment(int environment)
    {
        if (environment is not (1 or 2)) throw new InvalidOperationException("INVALID_SRI_ENVIRONMENT");
        if (environment == 2 && !_options.AllowProductionSubmission)
            throw new InvalidOperationException("SRI_PRODUCTION_SUBMISSION_DISABLED");
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
        var (consultedKey, emptyResult, record) = ReadAuthorizationEvidence(document);
        bool IsDirectField(string field)
        {
            var nodes = record?.Elements(field).Take(2).ToArray();
            return nodes is [var node] && !node.HasElements && ReferenceEquals(node, Descendants(record!, field).FirstOrDefault());
        }

        return new SriAuthorizationResponse
        {
            RawResponseXml = rawResponseXml,
            Estado = string.IsNullOrWhiteSpace(estado) ? "PENDIENTE" : estado,
            ConsultedAccessKey = consultedKey,
            HasExplicitEmptyResult = emptyResult,
            HasSingleAuthorizationRecord = record is not null && ReferenceEquals(record, authorizationNode)
                && IsDirectField("estado") && (estado != "AUTORIZADO"
                    || (IsDirectField("numeroAutorizacion") && IsDirectField("ambiente") && IsDirectField("comprobante"))),
            AuthorizationEnvironment = record?.Element("ambiente")?.Value.Trim(),
            AuthorizationNumber = FirstValue(responseContainer, "numeroAutorizacion"),
            AuthorizationDate = ParseSriDate(FirstValue(responseContainer, "fechaAutorizacion")),
            AuthorizedXml = FirstValue(responseContainer, "comprobante"),
            Messages = ParseMessages(responseContainer)
        };
    }

    private static (string? Key, bool Empty, XElement? Record) ReadAuthorizationEvidence(XDocument document)
    {
        XNamespace sri = AuthorizationNamespace;
        var result = ReadSoapResult(document, sri + "autorizacionComprobanteResponse", "RespuestaAutorizacionComprobante");
        if (result is null) return default;
        var keys = result.Elements("claveAccesoConsultada").Take(2).ToArray();
        var counts = result.Elements("numeroComprobantes").Take(2).ToArray();
        var lists = result.Elements("autorizaciones").Take(2).ToArray();
        if (keys is not [var key] || key.HasElements || counts is not [var count] || count.HasElements
            || lists.Length > 1 || result.Elements().Any(e =>
                e.Name != "claveAccesoConsultada" && e.Name != "numeroComprobantes" && e.Name != "autorizaciones"))
            return default;

        var list = lists.SingleOrDefault();
        var records = list?.Elements().Take(2).ToArray() ?? [];
        if (list?.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)) == true)
            return default;
        return (key.Value, count.Value.Trim() == "0" && records.Length == 0
            && !document.Descendants().Any(e => e.Name.LocalName is "estado" or "Fault" or "faultstring"),
            count.Value.Trim() == "1" && records is [var record] && record.Name == "autorizacion" ? record : null);
    }

    internal static SriValidityResponse ParseValidityResponse(string rawXml, string expectedKey)
    {
        var inconclusive = new SriValidityResponse(SriValidityEvidence.Inconclusive, RawResponseXml: rawXml);
        XDocument document;
        try { document = XDocument.Parse(rawXml); }
        catch (XmlException) { return inconclusive; }
        XNamespace sri = ValidityNamespace;
        var result = ReadSoapResult(document, sri + "consultarEstadoAutorizacionComprobanteResponse", "EstadoAutorizacionComprobante");
        if (result is null || SingleField(result, "claveAcceso", trim: false) != expectedKey)
            return inconclusive;

        var query = SingleField(result, "estadoConsulta")?.ToUpperInvariant();
        var status = SingleField(result, "estadoAutorizacion")?.ToUpperInvariant();
        var messages = result.Elements("mensajes").Take(2).ToArray();
        var entries = messages.Length == 1 ? messages[0].Elements().Take(2).ToArray() : [];
        if (status is "AUTORIZADO" or "NO AUTORIZADO" or "PENDIENTE DE ANULAR" or "ANULADO"
            && (query == "ACEPTADA" || !result.Elements("estadoConsulta").Any())
            && messages.Length <= 1 && entries.Length == 0)
            return new(SriValidityEvidence.RecordExists, expectedKey, status, rawXml);

        if (query == "RECHAZADA" && result.Elements().All(e => e.Name == "claveAcceso" || e.Name == "estadoConsulta" || e.Name == "mensajes")
            && entries is [var entry] && entry.Name == "mensaje" && SingleField(entry, "identificador") == "99"
            && NormalizeValidityDetail(SingleField(entry, "informacionAdicional")) == "NO EXISTEN DATOS PARA LOS PARAMETROS INGRESADOS")
            return new(SriValidityEvidence.NoRecord, expectedKey, "RECHAZADA", rawXml, "99");

        return inconclusive with { ConsultedAccessKey = expectedKey };
    }

    private static XElement? ReadSoapResult(XDocument document, XName operationName, XName resultName)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        if (document.Root?.Name != soap + "Envelope"
            || document.Descendants().Any(e => e.Name.LocalName is "Fault" or "faultstring"))
            return null;
        var bodies = document.Root.Elements(soap + "Body").Take(2).ToArray();
        if (bodies is not [var body] || body.Elements().Take(2).ToArray() is not [var operation]
            || operation.Name != operationName || operation.Elements().Take(2).ToArray() is not [var result]
            || result.Name != resultName)
            return null;
        return result;
    }

    private static string? SingleField(XElement element, XName field, bool trim = true)
        => element.Elements(field).Take(2).ToArray() is [var node] && !node.HasElements
            ? trim ? node.Value.Trim() : node.Value : null;

    private static string? NormalizeValidityDetail(string? value)
    {
        if (value is null) return null;
        var withoutAccents = new string(value.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return string.Join(' ', withoutAccents.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
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
