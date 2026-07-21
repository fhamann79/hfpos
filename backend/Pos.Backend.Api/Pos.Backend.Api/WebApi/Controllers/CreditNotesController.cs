using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireOperationalContext]
public class CreditNotesController : ControllerBase
{
    private readonly ICreditNoteService _creditNoteService;
    private readonly ISriCreditNoteSigningService _sriCreditNoteSigningService;

    public CreditNotesController(
        ICreditNoteService creditNoteService,
        ISriCreditNoteSigningService sriCreditNoteSigningService)
    {
        _creditNoteService = creditNoteService;
        _sriCreditNoteSigningService = sriCreditNoteSigningService;
    }

    [HttpGet("original-sales/{saleId:int}/eligibility")]
    [Authorize(Policy = AppPermissions.PosSalesVoid)]
    [ProducesResponseType(typeof(CreditNoteEligibilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreditNoteEligibilityDto>> GetEligibility(int saleId)
    {
        try
        {
            return Ok(await _creditNoteService.GetEligibilityAsync(saleId));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("drafts")]
    [Authorize(Policy = AppPermissions.PosSalesVoid)]
    [ProducesResponseType(typeof(CreditNoteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreditNoteDto>> CreateDraft([FromBody] CreateCreditNoteDraftDto dto)
    {
        try
        {
            var creditNote = await _creditNoteService.CreateDraftAsync(dto ?? new CreateCreditNoteDraftDto());
            return StatusCode(StatusCodes.Status201Created, creditNote);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("original-sales/{saleId:int}")]
    [Authorize(Policy = AppPermissions.PosSalesVoid)]
    [ProducesResponseType(typeof(IReadOnlyList<CreditNoteListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CreditNoteListItemDto>>> GetByOriginalSale(int saleId)
    {
        try
        {
            return Ok(await _creditNoteService.GetByOriginalSaleAsync(saleId));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.PosSalesVoid)]
    [ProducesResponseType(typeof(CreditNoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreditNoteDto>> GetById(int id)
    {
        try
        {
            return Ok(await _creditNoteService.GetByIdAsync(id));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("{id:int}/sri/prepare-draft")]
    [Authorize(Policy = AppPermissions.SriDocumentsSign)]
    [ProducesResponseType(typeof(CreditNoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreditNoteDto>> PrepareSriDraft(int id)
    {
        try
        {
            return Ok(await _creditNoteService.PrepareSriDraftAsync(id));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("{id:int}/sri/xml-draft")]
    [Authorize(Policy = AppPermissions.PosSalesVoid)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetSriXmlDraft(int id)
    {
        try
        {
            var xmlDraft = await _creditNoteService.GetSriXmlDraftAsync(id);
            return Content(xmlDraft, "application/xml");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("{id:int}/sri/sign")]
    [Authorize(Policy = AppPermissions.SriDocumentsSign)]
    [ProducesResponseType(typeof(CreditNoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CreditNoteDto>> SignSriXml(int id)
    {
        try
        {
            return Ok(await _sriCreditNoteSigningService.SignDraftAsync(id));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("{id:int}/sri/signed-xml")]
    [Authorize(Policy = AppPermissions.PosSalesVoid)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetSriSignedXml(int id)
    {
        try
        {
            var signedXml = await _sriCreditNoteSigningService
                .GetSignedXmlAsync(id);
            return Content(signedXml, "application/xml");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = AppPermissions.PosSalesVoid)]
    [ProducesResponseType(typeof(CreditNoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreditNoteDto>> CancelDraft(
        int id,
        [FromBody] CancelCreditNoteDraftDto dto)
    {
        try
        {
            return Ok(await _creditNoteService.CancelDraftAsync(
                id,
                dto ?? new CancelCreditNoteDraftDto()));
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
            "CREDIT_NOTE_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_XML_DRAFT_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_SIGNED_XML_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_REASON_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_REASON_TOO_LONG" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_NOTES_TOO_LONG" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_ITEMS_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_DUPLICATE_SALE_ITEM" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_ITEM_NOT_FOUND" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_INVALID_QUANTITY" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_CANCELLATION_REASON_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_CANCELLATION_REASON_TOO_LONG" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_ORIGINAL_SALE_NOT_INVOICE" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_ORIGINAL_SALE_NOT_COMPLETED" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_ORIGINAL_SALE_NOT_AUTHORIZED" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_ORIGINAL_SALE_AUTHORIZATION_DATA_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_ORIGINAL_SALE_WITHOUT_ITEMS" => BadRequest(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_ORIGINAL_SALE_VOIDED" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_ORIGINAL_SALE_FULLY_CREDITED" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_QUANTITY_EXCEEDS_AVAILABLE" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_DRAFT_ALREADY_CANCELLED" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_DRAFT_NOT_CANCELLABLE" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_DRAFT_SRI_PROCESS_STARTED" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_DRAFT_CANCELLED" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_DRAFT_NOT_ALLOWED" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_PROCESS_ALREADY_STARTED" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_DRAFT_INCONSISTENT" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_SIGN_CANCELLED" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_SIGN_NOT_ALLOWED" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_SIGNATURE_INCONSISTENT" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_XML_ACCESS_KEY_MISMATCH" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_NOT_FOUND" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_EXPIRED" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_WITHOUT_PRIVATE_KEY" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_LOAD_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_SIGNATURE_VALIDATION_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "CREDIT_NOTE_SRI_ACCESS_KEY_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SRI_DOCUMENT_CONTEXT" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_ISSUER_RUC" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_BUYER_IDENTIFICATION_TYPE_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SRI_CUSTOMER_IDENTIFICATION" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_CREDIT_NOTE_ORIGINAL_DOCUMENT_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_ACCESS_KEY_GENERATION_FAILED" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_CREDIT_NOTE_XML_DRAFT_GENERATION_FAILED" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_CREDIT_NOTE_XML_SCHEMA_VALIDATION_FAILED" => BadRequest(new ApiErrorResponse { Error = code }),
            "DOCUMENT_SEQUENCE_ERROR" => Conflict(new ApiErrorResponse { Error = code }),
            "DOCUMENT_NUMBER_GENERATION_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_UNPROTECT_FAILED" => StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse { Error = code }),
            "SRI_XML_SIGNING_FAILED" => StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse { Error = code }),
            _ => BadRequest(new ApiErrorResponse { Error = "CREDIT_NOTE_OPERATION_FAILED" })
        };
    }
}
