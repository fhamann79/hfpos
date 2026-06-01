using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
public class FiscalSettingsController : ControllerBase
{
    private const int MaxCertificateUploadRequestBytes = 3 * 1024 * 1024;

    private readonly IFiscalSettingsService _fiscalSettingsService;
    private readonly ISriCertificateService _sriCertificateService;
    private readonly ISriFiscalReadinessService _sriFiscalReadinessService;

    public FiscalSettingsController(
        IFiscalSettingsService fiscalSettingsService,
        ISriCertificateService sriCertificateService,
        ISriFiscalReadinessService sriFiscalReadinessService)
    {
        _fiscalSettingsService = fiscalSettingsService;
        _sriCertificateService = sriCertificateService;
        _sriFiscalReadinessService = sriFiscalReadinessService;
    }

    [HttpGet("company")]
    [Authorize(Policy = AppPermissions.FiscalSettingsRead)]
    public async Task<ActionResult<CompanyFiscalSettingsDto>> GetCompany()
    {
        try
        {
            return Ok(await _fiscalSettingsService.GetCompanyFiscalSettingsAsync());
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPut("company")]
    [Authorize(Policy = AppPermissions.FiscalSettingsWrite)]
    public async Task<ActionResult<CompanyFiscalSettingsDto>> UpdateCompany([FromBody] UpdateCompanyFiscalSettingsDto dto)
    {
        try
        {
            return Ok(await _fiscalSettingsService.UpdateCompanyFiscalSettingsAsync(dto));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("sri")]
    [Authorize(Policy = AppPermissions.FiscalSettingsRead)]
    public async Task<ActionResult<CompanySriSettingsDto>> GetSri()
    {
        try
        {
            return Ok(await _fiscalSettingsService.GetCompanySriSettingsAsync());
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPut("sri")]
    [Authorize(Policy = AppPermissions.FiscalSettingsWrite)]
    public async Task<ActionResult<CompanySriSettingsDto>> UpdateSri([FromBody] UpdateCompanySriSettingsDto dto)
    {
        try
        {
            return Ok(await _fiscalSettingsService.UpdateCompanySriSettingsAsync(dto));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("sri/readiness")]
    [Authorize(Policy = AppPermissions.FiscalSettingsRead)]
    public async Task<ActionResult<SriFiscalReadinessDto>> GetSriReadiness()
    {
        try
        {
            return Ok(await _sriFiscalReadinessService.GetReadinessAsync());
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("sri/certificate")]
    [Authorize(Policy = AppPermissions.FiscalSettingsRead)]
    public async Task<ActionResult<CompanySriCertificateDto>> GetSriCertificate()
    {
        try
        {
            return Ok(await _sriCertificateService.GetCurrentCertificateAsync());
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("sri/certificate")]
    [Authorize(Policy = AppPermissions.FiscalSettingsWrite)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxCertificateUploadRequestBytes)]
    public async Task<ActionResult<CompanySriCertificateDto>> UploadSriCertificate(
        [FromForm] UploadSriCertificateRequest request)
    {
        try
        {
            return Ok(await _sriCertificateService.UploadCertificateAsync(request.File, request.Password));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpDelete("sri/certificate")]
    [Authorize(Policy = AppPermissions.FiscalSettingsWrite)]
    public async Task<IActionResult> DeleteSriCertificate()
    {
        try
        {
            await _sriCertificateService.DeactivateCertificateAsync();
            return NoContent();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("document-sequences")]
    [Authorize(Policy = AppPermissions.FiscalSettingsRead)]
    public async Task<ActionResult<IEnumerable<DocumentSequenceDto>>> GetDocumentSequences(
        [FromQuery] int? establishmentId,
        [FromQuery] int? emissionPointId,
        [FromQuery] SaleDocumentType? documentType)
    {
        try
        {
            var sequences = await _fiscalSettingsService.GetDocumentSequencesAsync(
                establishmentId,
                emissionPointId,
                documentType);

            return Ok(sequences);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("document-sequences")]
    [Authorize(Policy = AppPermissions.FiscalSettingsWrite)]
    public async Task<ActionResult<DocumentSequenceDto>> CreateDocumentSequence([FromBody] CreateDocumentSequenceDto dto)
    {
        try
        {
            var sequence = await _fiscalSettingsService.CreateDocumentSequenceAsync(dto);
            return CreatedAtAction(nameof(GetDocumentSequences), new { id = sequence.Id }, sequence);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPut("document-sequences/{id:int}")]
    [Authorize(Policy = AppPermissions.FiscalSettingsWrite)]
    public async Task<ActionResult<DocumentSequenceDto>> UpdateDocumentSequence(int id, [FromBody] UpdateDocumentSequenceDto dto)
    {
        try
        {
            return Ok(await _fiscalSettingsService.UpdateDocumentSequenceAsync(id, dto));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("document-sequences/{id:int}/audits")]
    [Authorize(Policy = AppPermissions.FiscalSettingsRead)]
    public async Task<ActionResult<IEnumerable<DocumentSequenceAuditDto>>> GetDocumentSequenceAudits(int id)
    {
        try
        {
            return Ok(await _fiscalSettingsService.GetDocumentSequenceAuditsAsync(id));
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
            "COMPANY_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "DOCUMENT_SEQUENCE_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "INVALID_COMPANY_RUC" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_COMPANY_FISCAL_SETTINGS" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SRI_ENVIRONMENT" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SRI_EMISSION_TYPE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_DOCUMENT_SEQUENCE" => BadRequest(new ApiErrorResponse { Error = code }),
            "DOCUMENT_SEQUENCE_REASON_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_FILE_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_PASSWORD_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_CERTIFICATE_FILE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_CERTIFICATE_PASSWORD" => BadRequest(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_WITHOUT_PRIVATE_KEY" => BadRequest(new ApiErrorResponse { Error = code }),
            "DOCUMENT_SEQUENCE_ALREADY_EXISTS" => Conflict(new ApiErrorResponse { Error = code }),
            "DOCUMENT_SEQUENCE_BELOW_USED_NUMBER" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_EXPIRED" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_NOT_VALID_YET" => Conflict(new ApiErrorResponse { Error = code }),
            "CERTIFICATE_PROTECTION_FAILED" => StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse { Error = code }),
            _ => BadRequest(new ApiErrorResponse { Error = "FISCAL_SETTINGS_OPERATION_FAILED" })
        };
    }
}
