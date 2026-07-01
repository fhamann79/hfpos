using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireOperationalContext]
public class SalesController : ControllerBase
{
    private readonly ISalesService _salesService;
    private readonly ISriInvoiceSigningService _sriInvoiceSigningService;
    private readonly ISriSubmissionService _sriSubmissionService;
    private readonly ISriRidePdfService _sriRidePdfService;
    private readonly ISaleInvoiceEmailService _saleInvoiceEmailService;

    public SalesController(
        ISalesService salesService,
        ISriInvoiceSigningService sriInvoiceSigningService,
        ISriSubmissionService sriSubmissionService,
        ISriRidePdfService sriRidePdfService,
        ISaleInvoiceEmailService saleInvoiceEmailService)
    {
        _salesService = salesService;
        _sriInvoiceSigningService = sriInvoiceSigningService;
        _sriSubmissionService = sriSubmissionService;
        _sriRidePdfService = sriRidePdfService;
        _saleInvoiceEmailService = saleInvoiceEmailService;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<SaleListItemDto>>> Get(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] SaleStatus? status,
        [FromQuery] string? search,
        [FromQuery] int? userId,
        [FromQuery] SaleDocumentType? documentType,
        [FromQuery] SaleDocumentStatus? documentStatus)
    {
        var sales = await _salesService.GetSalesAsync(
            from,
            to,
            status,
            search,
            userId,
            documentType,
            documentStatus);
        return Ok(sales);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SaleDto>> GetById(int id)
    {
        var sale = await _salesService.GetByIdAsync(id);
        if (sale is null)
        {
            return NotFound(new ApiErrorResponse { Error = "SALE_NOT_FOUND" });
        }

        return Ok(sale);
    }

    [HttpGet("{id:int}/sri/xml-draft")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetSriXmlDraft(int id)
    {
        var xmlDraft = await _salesService.GetSriXmlDraftAsync(id);

        if (xmlDraft is null)
        {
            return NotFound(new ApiErrorResponse { Error = "SRI_XML_DRAFT_NOT_FOUND" });
        }

        return Content(xmlDraft, "application/xml");
    }

    [HttpPost("{id:int}/sri/sign")]
    [Authorize(Policy = AppPermissions.SriDocumentsSign)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SaleDto>> SignSriInvoice(int id)
    {
        try
        {
            return Ok(await _sriInvoiceSigningService.SignInvoiceDraftAsync(id));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("{id:int}/sri/signed-xml")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetSriSignedXml(int id)
    {
        try
        {
            var signedXml = await _sriInvoiceSigningService.GetSignedXmlAsync(id);
            return Content(signedXml, "application/xml");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("{id:int}/sri/authorized-xml")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> GetSriAuthorizedXml(int id)
    {
        try
        {
            var authorizedXml = await _sriSubmissionService.GetAuthorizedXmlAsync(id);
            return Content(authorizedXml, "application/xml");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("{id:int}/sri/ride")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SriRideDto>> GetSriRide(int id)
    {
        try
        {
            return Ok(await _sriSubmissionService.GetRideAsync(id));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("{id:int}/sri/ride-pdf")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> GetSriRidePdf(int id)
    {
        try
        {
            var result = await _sriRidePdfService.GenerateAsync(id);
            return File(result.Bytes, result.ContentType, result.FileName);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("{id:int}/sri/email")]
    [Authorize(Policy = AppPermissions.SriDocumentsSubmit)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SendSaleInvoiceEmailResultDto>> SendSriInvoiceEmail(
        int id,
        [FromBody] SendSaleInvoiceEmailRequestDto dto)
    {
        try
        {
            return Ok(await _saleInvoiceEmailService.SendAuthorizedInvoiceEmailAsync(
                id,
                dto ?? new SendSaleInvoiceEmailRequestDto()));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("{id:int}/sri/email-deliveries")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SaleInvoiceEmailDeliveryDto>>> GetSriInvoiceEmailDeliveries(int id)
    {
        try
        {
            return Ok(await _saleInvoiceEmailService.GetDeliveriesAsync(id));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("{id:int}/sri/submit")]
    [Authorize(Policy = AppPermissions.SriDocumentsSubmit)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SaleDto>> SubmitSriSignedInvoice(int id)
    {
        try
        {
            return Ok(await _sriSubmissionService.SubmitSignedInvoiceAsync(id));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("{id:int}/sri/check-authorization")]
    [Authorize(Policy = AppPermissions.SriDocumentsSubmit)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SaleDto>> CheckSriAuthorization(int id)
    {
        try
        {
            return Ok(await _sriSubmissionService.CheckAuthorizationAsync(id));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("{id:int}/sri/submission-attempts")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SriSubmissionAttemptDto>>> GetSriSubmissionAttempts(int id)
    {
        try
        {
            return Ok(await _sriSubmissionService.GetAttemptsAsync(id));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.PosSalesCreate)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SaleDto>> Create([FromBody] SaleCreateDto dto)
    {
        try
        {
            var sale = await _salesService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = sale.Id }, sale);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("{id:int}/void")]
    [Authorize(Policy = AppPermissions.PosSalesVoid)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SaleDto>> Void(int id, [FromBody] VoidSaleDto dto)
    {
        try
        {
            var sale = await _salesService.VoidAsync(id, dto);
            return Ok(sale);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    private ActionResult MapDomainError(Exception exception)
    {
        var code = exception.Message;

        return code switch
        {
            "SALE_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "PRODUCT_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "CUSTOMER_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "SRI_XML_DRAFT_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "SRI_SIGNED_XML_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "SALE_ITEMS_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "SALE_NOT_INVOICE" => BadRequest(new ApiErrorResponse { Error = code }),
            "COMPANY_EMAIL_SETTINGS_NOT_CONFIGURED" => BadRequest(new ApiErrorResponse { Error = code }),
            "COMPANY_EMAIL_DISABLED" => BadRequest(new ApiErrorResponse { Error = code }),
            "COMPANY_EMAIL_SMTP_HOST_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "COMPANY_EMAIL_FROM_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "COMPANY_EMAIL_PASSWORD_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "COMPANY_EMAIL_NOT_TESTED" => BadRequest(new ApiErrorResponse { Error = code }),
            "COMPANY_EMAIL_INVALID_ADDRESS" => BadRequest(new ApiErrorResponse { Error = code }),
            "COMPANY_EMAIL_OPERATION_FAILED" => BadRequest(new ApiErrorResponse { Error = code }),
            "SALE_INVOICE_EMAIL_OPERATION_FAILED" => BadRequest(new ApiErrorResponse { Error = code }),
            "CASH_SESSION_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "PRODUCT_INACTIVE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_QUANTITY" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_UNIT_PRICE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_LINE_DISCOUNT" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SALE_DISCOUNT" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_PRODUCT_VAT_CATEGORY" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SALE_PAYMENT_METHOD" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SALE_DOCUMENT_TYPE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_DOCUMENT_TYPE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_ISSUER_RUC" => BadRequest(new ApiErrorResponse { Error = code }),
            "CUSTOMER_FISCAL_DATA_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SRI_DOCUMENT_CONTEXT" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SRI_CUSTOMER_IDENTIFICATION" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_COMPANY_MATRIX_ADDRESS_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_BUYER_IDENTIFICATION_TYPE_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_XML_REQUIRED_FIELD_MISSING" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_XML_INVALID_FIELD_FORMAT" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SRI_PAYMENT_METHOD" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SRI_PRODUCT_CODE" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_SIGNING_ONLY_INVOICE" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_SUBMISSION_ONLY_INVOICE" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_AUTHORIZED_XML_ONLY_INVOICE" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_SIGNED_XML_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_ACCESS_KEY_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_ACCESS_KEY_GENERATION_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_XML_DRAFT_GENERATION_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_XML_SCHEMA_VALIDATION_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_XML_ALREADY_SIGNED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_SIGNING_SALE_VOIDED" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_NOT_FOUND" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_EXPIRED" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_WITHOUT_PRIVATE_KEY" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_LOAD_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_SIGNATURE_VALIDATION_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_SUBMISSION_SALE_VOIDED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_SETTINGS_DISABLED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_PRODUCTION_SUBMISSION_DISABLED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_RECEPTION_REJECTED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_AUTHORIZATION_REJECTED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_ALREADY_AUTHORIZED" => Conflict(new ApiErrorResponse { Error = code }),
            "SALE_VOIDED" => Conflict(new ApiErrorResponse { Error = code }),
            "SALE_NOT_AUTHORIZED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_AUTHORIZED_XML_NOT_AVAILABLE" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_RIDE_PDF_NOT_AVAILABLE" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_AUTHORIZED_XML_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "SRI_AUTHORIZED_XML_SALE_NOT_AUTHORIZED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_AUTHORIZED_XML_INVALID_RESPONSE" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_RIDE_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "SRI_RIDE_ONLY_AUTHORIZED_INVOICE" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_RIDE_INVALID_AUTHORIZED_XML" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_RIDE_PDF_GENERATION_FAILED" => StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse { Error = code }),
            "SRI_AUTHORIZATION_PENDING" => StatusCode(StatusCodes.Status202Accepted, new ApiErrorResponse { Error = code }),
            "DOCUMENT_SEQUENCE_ERROR" => Conflict(new ApiErrorResponse { Error = code }),
            "DOCUMENT_NUMBER_GENERATION_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "SALE_ALREADY_VOIDED" => Conflict(new ApiErrorResponse { Error = code }),
            "SALE_NOT_VOIDABLE" => Conflict(new ApiErrorResponse { Error = code }),
            "INSUFFICIENT_STOCK" => Conflict(new ApiErrorResponse { Error = code }),
            "INVENTORY_CONCURRENCY_CONFLICT" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_UNPROTECT_FAILED" => StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse { Error = code }),
            "SRI_XML_SIGNING_FAILED" => StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse { Error = code }),
            "SRI_RECEPTION_ENDPOINT_NOT_CONFIGURED" => StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse { Error = code }),
            "SRI_AUTHORIZATION_ENDPOINT_NOT_CONFIGURED" => StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse { Error = code }),
            "SRI_RECEPTION_COMMUNICATION_FAILED" => StatusCode(StatusCodes.Status502BadGateway, new ApiErrorResponse { Error = code }),
            "SRI_AUTHORIZATION_COMMUNICATION_FAILED" => StatusCode(StatusCodes.Status502BadGateway, new ApiErrorResponse { Error = code }),
            "SALE_INVOICE_EMAIL_SEND_FAILED" => StatusCode(StatusCodes.Status502BadGateway, new ApiErrorResponse { Error = code }),
            _ => BadRequest(new ApiErrorResponse { Error = "SALE_OPERATION_FAILED" })
        };
    }
}
