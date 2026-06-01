using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriFiscalReadinessService : ISriFiscalReadinessService
{
    private const string SeveritySuccess = "success";
    private const string SeverityWarning = "warning";
    private const string SeverityError = "error";
    private const string SeverityInfo = "info";

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ISriSigningCertificateProvider _certificateProvider;
    private readonly SriOptions _sriOptions;
    private readonly ILogger<SriFiscalReadinessService> _logger;

    public SriFiscalReadinessService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ISriSigningCertificateProvider certificateProvider,
        IOptions<SriOptions> sriOptions,
        ILogger<SriFiscalReadinessService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _certificateProvider = certificateProvider;
        _sriOptions = sriOptions.Value;
        _logger = logger;
    }

    public async Task<SriFiscalReadinessDto> GetReadinessAsync()
    {
        var generatedAt = DateTime.UtcNow;
        var checks = new List<SriFiscalReadinessCheckDto>();
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var company = await _context.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == operationalContext.CompanyId);

        var establishment = await _context.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == operationalContext.EstablishmentId);

        var emissionPoint = await _context.EmissionPoints
            .AsNoTracking()
            .FirstOrDefaultAsync(ep => ep.Id == operationalContext.EmissionPointId);

        var activeCertificate = await _context.CompanySriCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == operationalContext.CompanyId && c.IsActive);

        var settings = await GetOrCreateCompanySriSettingsAsync(
            operationalContext.CompanyId,
            operationalContext.UserId,
            generatedAt,
            checks);

        var companyReady = AddCompanyChecks(checks, company);
        var operationalStructureReady = AddOperationalStructureChecks(
            checks,
            operationalContext.CompanyId,
            establishment,
            emissionPoint);
        var sriSettingsReady = AddSriSettingsChecks(checks, settings, activeCertificate);
        var certificateDiagnostics = await AddCertificateChecksAsync(
            checks,
            company,
            activeCertificate,
            settings.Environment,
            generatedAt);
        var documentSequenceReady = await AddDocumentSequenceChecksAsync(checks, operationalContext.CompanyId, operationalContext.EstablishmentId, operationalContext.EmissionPointId);

        AddProductionSafetyChecks(checks, settings.Environment);

        var blockingErrorCount = checks.Count(c => c.IsBlocking && c.Severity == SeverityError);
        var warningCount = checks.Count(c => c.Severity == SeverityWarning);
        var successCount = checks.Count(c => c.Severity == SeveritySuccess);

        var isReadyForSandboxSubmission = settings.Environment == 1
            && sriSettingsReady
            && settings.IsEnabled
            && companyReady
            && operationalStructureReady
            && certificateDiagnostics.IsUsableForSandbox
            && documentSequenceReady
            && !checks.Any(c => c.IsBlocking && c.Severity == SeverityError);

        var isReadyForProductionSubmission = settings.Environment == 2
            && sriSettingsReady
            && settings.IsEnabled
            && _sriOptions.AllowProductionSubmission
            && companyReady
            && operationalStructureReady
            && certificateDiagnostics.IsUsableForProduction
            && documentSequenceReady
            && !checks.Any(c => c.IsBlocking && c.Severity == SeverityError);

        return new SriFiscalReadinessDto
        {
            CompanyId = operationalContext.CompanyId,
            EstablishmentId = operationalContext.EstablishmentId,
            EmissionPointId = operationalContext.EmissionPointId,
            Environment = settings.Environment,
            EnvironmentLabel = GetEnvironmentLabel(settings.Environment),
            IsReadyForSandboxSubmission = isReadyForSandboxSubmission,
            IsReadyForProductionSubmission = isReadyForProductionSubmission,
            HasBlockingErrors = blockingErrorCount > 0,
            HasWarnings = warningCount > 0,
            GeneratedAt = generatedAt,
            BlockingErrorCount = blockingErrorCount,
            WarningCount = warningCount,
            SuccessCount = successCount,
            Checks = checks
        };
    }

    private async Task<CompanySriSettings> GetOrCreateCompanySriSettingsAsync(
        int companyId,
        int userId,
        DateTime now,
        List<SriFiscalReadinessCheckDto> checks)
    {
        var settings = await _context.CompanySriSettings
            .FirstOrDefaultAsync(s => s.CompanyId == companyId);

        if (settings is not null)
        {
            AddSuccess(
                checks,
                "SriSettings",
                "SRI_SETTINGS_FOUND",
                "Configuracion SRI disponible",
                "La empresa ya tiene configuracion SRI registrada.");

            return settings;
        }

        settings = new CompanySriSettings
        {
            CompanyId = companyId,
            Environment = 1,
            EmissionType = 1,
            IsEnabled = false,
            CertificateConfigured = false,
            LastUpdatedByUserId = userId,
            CreatedAt = now
        };

        _context.CompanySriSettings.Add(settings);
        await _context.SaveChangesAsync();

        AddInfo(
            checks,
            "SriSettings",
            "SRI_SETTINGS_CREATED",
            "Configuracion SRI inicializada",
            "No existia configuracion SRI, se inicializo con ambiente de pruebas y envio deshabilitado.");

        return settings;
    }

    private static bool AddCompanyChecks(List<SriFiscalReadinessCheckDto> checks, Company? company)
    {
        if (company is null)
        {
            AddError(
                checks,
                "Company",
                "COMPANY_NOT_FOUND",
                "Empresa no encontrada",
                "No se encontro la empresa del contexto operacional.",
                isBlocking: true);

            return false;
        }

        var ready = true;

        if (company.IsActive)
        {
            AddSuccess(
                checks,
                "Company",
                "COMPANY_ACTIVE",
                "Empresa activa",
                "La empresa del contexto operacional esta activa.");
        }
        else
        {
            ready = false;
            AddError(
                checks,
                "Company",
                "COMPANY_INACTIVE",
                "Empresa inactiva",
                "La empresa del contexto operacional esta inactiva.",
                isBlocking: true);
        }

        if (IsNumeric(company.Ruc, 13))
        {
            AddSuccess(
                checks,
                "Company",
                "COMPANY_RUC_VALID",
                "RUC valido",
                "El RUC de la empresa tiene 13 digitos numericos.");
        }
        else
        {
            ready = false;
            AddError(
                checks,
                "Company",
                "COMPANY_RUC_INVALID",
                "RUC requerido",
                "El RUC de la empresa es obligatorio y debe tener 13 digitos numericos.",
                isBlocking: true);
        }

        if (!string.IsNullOrWhiteSpace(company.Name))
        {
            AddSuccess(
                checks,
                "Company",
                "COMPANY_NAME_PRESENT",
                "Razon social registrada",
                "La razon social de la empresa esta configurada.");
        }
        else
        {
            ready = false;
            AddError(
                checks,
                "Company",
                "COMPANY_NAME_REQUIRED",
                "Razon social requerida",
                "La razon social de la empresa es obligatoria para emitir comprobantes SRI.",
                isBlocking: true);
        }

        if (!string.IsNullOrWhiteSpace(company.MatrixAddress))
        {
            AddSuccess(
                checks,
                "Company",
                "COMPANY_MATRIX_ADDRESS_PRESENT",
                "Direccion matriz registrada",
                "La direccion matriz esta configurada para dirMatriz.");
        }
        else
        {
            ready = false;
            AddError(
                checks,
                "Company",
                "COMPANY_MATRIX_ADDRESS_REQUIRED",
                "Direccion matriz requerida",
                "La direccion matriz es obligatoria porque el XML SRI requiere dirMatriz.",
                isBlocking: true);
        }

        if (string.IsNullOrWhiteSpace(company.TradeName))
        {
            AddInfo(
                checks,
                "Company",
                "COMPANY_TRADE_NAME_MISSING",
                "Nombre comercial no registrado",
                "El nombre comercial es opcional; si se configura, puede emitirse en el XML.");
        }
        else
        {
            AddSuccess(
                checks,
                "Company",
                "COMPANY_TRADE_NAME_PRESENT",
                "Nombre comercial registrado",
                "El nombre comercial esta configurado.");
        }

        AddInfo(
            checks,
            "Company",
            "COMPANY_ACCOUNTING_REQUIRED_STATUS",
            "Obligado a llevar contabilidad",
            $"Valor configurado para obligadoContabilidad: {FormatYesNo(company.IsAccountingRequired)}.");

        if (string.IsNullOrWhiteSpace(company.SpecialTaxpayerNumber))
        {
            AddInfo(
                checks,
                "Company",
                "COMPANY_SPECIAL_TAXPAYER_NOT_CONFIGURED",
                "Contribuyente especial no configurado",
                "El numero de contribuyente especial es opcional.");
        }
        else if (IsNumericInRange(company.SpecialTaxpayerNumber, 3, 13))
        {
            AddSuccess(
                checks,
                "Company",
                "COMPANY_SPECIAL_TAXPAYER_VALID",
                "Contribuyente especial valido",
                "El numero de contribuyente especial configurado es numerico.");
        }
        else
        {
            ready = false;
            AddError(
                checks,
                "Company",
                "COMPANY_SPECIAL_TAXPAYER_INVALID",
                "Contribuyente especial invalido",
                "Si se configura, el numero de contribuyente especial debe ser numerico y tener entre 3 y 13 digitos.",
                isBlocking: true);
        }

        if (!string.IsNullOrWhiteSpace(company.TaxpayerRegime)
            && company.TaxpayerRegime.Contains("RIMPE", StringComparison.OrdinalIgnoreCase))
        {
            AddInfo(
                checks,
                "Company",
                "COMPANY_TAXPAYER_REGIME_RIMPE",
                "Regimen RIMPE detectado",
                "El XML puede emitir contribuyenteRimpe segun la configuracion fiscal de la empresa.");
        }

        return ready;
    }

    private static bool AddOperationalStructureChecks(
        List<SriFiscalReadinessCheckDto> checks,
        int companyId,
        Establishment? establishment,
        EmissionPoint? emissionPoint)
    {
        var ready = true;

        if (establishment is null)
        {
            ready = false;
            AddError(
                checks,
                "OperationalStructure",
                "ESTABLISHMENT_NOT_FOUND",
                "Establecimiento no encontrado",
                "No se encontro el establecimiento del contexto operacional.",
                isBlocking: true);
        }
        else if (!establishment.IsActive || establishment.CompanyId != companyId)
        {
            ready = false;
            AddError(
                checks,
                "OperationalStructure",
                "ESTABLISHMENT_INVALID",
                "Establecimiento invalido",
                "El establecimiento debe estar activo y pertenecer a la empresa actual.",
                isBlocking: true);
        }
        else
        {
            AddSuccess(
                checks,
                "OperationalStructure",
                "ESTABLISHMENT_ACTIVE",
                "Establecimiento activo",
                "El establecimiento del contexto operacional pertenece a la empresa y esta activo.");
        }

        if (establishment is not null)
        {
            if (IsNumeric(establishment.Code, 3))
            {
                AddSuccess(
                    checks,
                    "OperationalStructure",
                    "ESTABLISHMENT_CODE_VALID",
                    "Codigo de establecimiento valido",
                    "El codigo de establecimiento tiene 3 digitos numericos.");
            }
            else
            {
                ready = false;
                AddError(
                    checks,
                    "OperationalStructure",
                    "ESTABLISHMENT_CODE_INVALID",
                    "Codigo de establecimiento invalido",
                    "El codigo de establecimiento es obligatorio y debe tener exactamente 3 digitos numericos.",
                    isBlocking: true);
            }

            if (string.IsNullOrWhiteSpace(establishment.Address))
            {
                AddWarning(
                    checks,
                    "OperationalStructure",
                    "ESTABLISHMENT_ADDRESS_MISSING",
                    "Direccion de establecimiento no registrada",
                    "Se recomienda configurar la direccion del establecimiento para dirEstablecimiento.");
            }
            else
            {
                AddSuccess(
                    checks,
                    "OperationalStructure",
                    "ESTABLISHMENT_ADDRESS_PRESENT",
                    "Direccion de establecimiento registrada",
                    "La direccion del establecimiento esta configurada.");
            }
        }

        if (emissionPoint is null)
        {
            ready = false;
            AddError(
                checks,
                "OperationalStructure",
                "EMISSION_POINT_NOT_FOUND",
                "Punto de emision no encontrado",
                "No se encontro el punto de emision del contexto operacional.",
                isBlocking: true);
        }
        else if (!emissionPoint.IsActive
            || establishment is null
            || emissionPoint.EstablishmentId != establishment.Id)
        {
            ready = false;
            AddError(
                checks,
                "OperationalStructure",
                "EMISSION_POINT_INVALID",
                "Punto de emision invalido",
                "El punto de emision debe estar activo y pertenecer al establecimiento actual.",
                isBlocking: true);
        }
        else
        {
            AddSuccess(
                checks,
                "OperationalStructure",
                "EMISSION_POINT_ACTIVE",
                "Punto de emision activo",
                "El punto de emision del contexto operacional pertenece al establecimiento y esta activo.");
        }

        if (emissionPoint is not null)
        {
            if (IsNumeric(emissionPoint.Code, 3))
            {
                AddSuccess(
                    checks,
                    "OperationalStructure",
                    "EMISSION_POINT_CODE_VALID",
                    "Codigo de punto de emision valido",
                    "El codigo de punto de emision tiene 3 digitos numericos.");
            }
            else
            {
                ready = false;
                AddError(
                    checks,
                    "OperationalStructure",
                    "EMISSION_POINT_CODE_INVALID",
                    "Codigo de punto de emision invalido",
                    "El codigo de punto de emision es obligatorio y debe tener exactamente 3 digitos numericos.",
                    isBlocking: true);
            }
        }

        return ready;
    }

    private static bool AddSriSettingsChecks(
        List<SriFiscalReadinessCheckDto> checks,
        CompanySriSettings settings,
        CompanySriCertificate? activeCertificate)
    {
        var ready = true;

        if (settings.IsEnabled)
        {
            AddSuccess(
                checks,
                "SriSettings",
                "SRI_ENABLED",
                "SRI habilitado",
                "La emision SRI esta habilitada para la empresa.");
        }
        else
        {
            ready = false;
            AddError(
                checks,
                "SriSettings",
                "SRI_DISABLED",
                "SRI deshabilitado",
                "La emision SRI esta deshabilitada para la empresa.",
                isBlocking: true);
        }

        if (settings.Environment is 1 or 2)
        {
            AddSuccess(
                checks,
                "SriSettings",
                "SRI_ENVIRONMENT_VALID",
                "Ambiente SRI valido",
                $"Ambiente configurado: {GetEnvironmentLabel(settings.Environment)}.");
        }
        else
        {
            ready = false;
            AddError(
                checks,
                "SriSettings",
                "SRI_ENVIRONMENT_INVALID",
                "Ambiente SRI invalido",
                "El ambiente SRI debe ser 1 para pruebas o 2 para produccion.",
                isBlocking: true);
        }

        if (settings.EmissionType == 1)
        {
            AddSuccess(
                checks,
                "SriSettings",
                "SRI_EMISSION_TYPE_VALID",
                "Tipo de emision valido",
                "El tipo de emision configurado es 1, emision normal.");
        }
        else
        {
            ready = false;
            AddError(
                checks,
                "SriSettings",
                "SRI_EMISSION_TYPE_INVALID",
                "Tipo de emision invalido",
                "El tipo de emision SRI debe ser 1.",
                isBlocking: true);
        }

        var hasActiveCertificate = activeCertificate is not null;

        if (settings.CertificateConfigured == hasActiveCertificate)
        {
            AddSuccess(
                checks,
                "SriSettings",
                "SRI_CERTIFICATE_FLAG_CONSISTENT",
                "Estado de certificado consistente",
                "La configuracion SRI coincide con la existencia del certificado activo.");
        }
        else
        {
            AddWarning(
                checks,
                "SriSettings",
                "SRI_CERTIFICATE_FLAG_INCONSISTENT",
                "Estado de certificado inconsistente",
                "La bandera CertificateConfigured no coincide con el certificado activo encontrado.");
        }

        return ready;
    }

    private async Task<CertificateReadinessDiagnostics> AddCertificateChecksAsync(
        List<SriFiscalReadinessCheckDto> checks,
        Company? company,
        CompanySriCertificate? certificate,
        int environment,
        DateTime now)
    {
        var diagnostics = new CertificateReadinessDiagnostics();

        if (certificate is null)
        {
            AddError(
                checks,
                "Certificate",
                "CERTIFICATE_NOT_FOUND",
                "Certificado activo no encontrado",
                "No existe un certificado SRI activo para la empresa.",
                isBlocking: true);
            AddInfo(
                checks,
                "CertificateTrust",
                "CERTIFICATE_TRUST_SKIPPED",
                "Diagnostico de confianza no ejecutado",
                "No se puede evaluar autofirma ni cadena local sin un certificado activo.");

            return diagnostics;
        }

        AddSuccess(
            checks,
            "Certificate",
            "CERTIFICATE_ACTIVE",
            "Certificado activo encontrado",
            "Existe un certificado SRI activo para la empresa.",
            BuildCertificateMetadataDetails(certificate, now));

        if (!string.IsNullOrWhiteSpace(certificate.Subject) && !string.IsNullOrWhiteSpace(certificate.Issuer))
        {
            AddSuccess(
                checks,
                "Certificate",
                "CERTIFICATE_METADATA_PRESENT",
                "Metadatos de certificado disponibles",
                "Subject, issuer y thumbprint estan disponibles como metadatos seguros.");
        }
        else
        {
            AddWarning(
                checks,
                "Certificate",
                "CERTIFICATE_METADATA_INCOMPLETE",
                "Metadatos de certificado incompletos",
                "Subject o issuer estan vacios; esto puede dificultar el diagnostico del certificado.");
        }

        if (certificate.NotBefore > now)
        {
            AddError(
                checks,
                "Certificate",
                "CERTIFICATE_NOT_VALID_YET",
                "Certificado aun no vigente",
                "El certificado todavia no es valido para firmar comprobantes.",
                $"Vigente desde: {FormatUtcDate(certificate.NotBefore)}.",
                isBlocking: true);
        }
        else if (certificate.NotAfter <= now)
        {
            AddError(
                checks,
                "Certificate",
                "CERTIFICATE_EXPIRED",
                "Certificado expirado",
                "El certificado expiro y no debe usarse para firmar comprobantes.",
                $"Expiracion: {FormatUtcDate(certificate.NotAfter)}.",
                isBlocking: true);
        }
        else
        {
            AddSuccess(
                checks,
                "Certificate",
                "CERTIFICATE_VALIDITY_OK",
                "Vigencia de certificado valida",
                "El certificado esta dentro de su periodo de vigencia.",
                $"Vigente desde {FormatUtcDate(certificate.NotBefore)} hasta {FormatUtcDate(certificate.NotAfter)}.");
        }

        var daysUntilExpiration = (int)Math.Ceiling((certificate.NotAfter - now).TotalDays);

        if (certificate.NotAfter > now && daysUntilExpiration <= 30)
        {
            AddWarning(
                checks,
                "Certificate",
                "CERTIFICATE_EXPIRING_SOON",
                "Certificado por expirar",
                "El certificado vence en 30 dias o menos; planifica renovacion antes de salida real.",
                $"Dias restantes: {Math.Max(0, daysUntilExpiration)}.");
        }
        else if (certificate.NotAfter > now)
        {
            AddSuccess(
                checks,
                "Certificate",
                "CERTIFICATE_EXPIRATION_WINDOW_OK",
                "Ventana de expiracion aceptable",
                "El certificado no vence dentro de los proximos 30 dias.",
                $"Dias restantes: {daysUntilExpiration}.");
        }

        if (certificate.HasPrivateKey)
        {
            AddSuccess(
                checks,
                "Certificate",
                "CERTIFICATE_PRIVATE_KEY_PRESENT",
                "Clave privada disponible",
                "El certificado indica que contiene clave privada para firma.");
        }
        else
        {
            AddError(
                checks,
                "Certificate",
                "CERTIFICATE_WITHOUT_PRIVATE_KEY",
                "Certificado sin clave privada",
                "El certificado activo no contiene clave privada y no puede firmar XML.",
                isBlocking: true);
        }

        diagnostics.MetadataUsable = certificate.NotBefore <= now
            && certificate.NotAfter > now
            && certificate.HasPrivateKey;

        if (!diagnostics.MetadataUsable)
        {
            AddInfo(
                checks,
                "CertificateTrust",
                "CERTIFICATE_TRUST_SKIPPED",
                "Diagnostico de confianza omitido",
                "Se omite la carga interna del certificado hasta resolver los errores de vigencia o clave privada.");

            return diagnostics;
        }

        try
        {
            using var material = await _certificateProvider.GetActiveCertificateMaterialAsync();
            diagnostics.MaterialLoaded = true;

            AddSuccess(
                checks,
                "Certificate",
                "CERTIFICATE_MATERIAL_LOADED",
                "Material de certificado cargado internamente",
                "El certificado activo pudo cargarse para diagnostico local sin exponer bytes, password ni clave privada.");

            AddSelfSignedCheck(checks, material.Certificate, environment, diagnostics);
            AddChainBuildCheck(checks, material.Certificate, environment, diagnostics);
            AddCertificateRucCheck(checks, company?.Ruc, material.Certificate, diagnostics);
        }
        catch (KeyNotFoundException ex) when (ex.Message == "CERTIFICATE_NOT_FOUND")
        {
            AddError(
                checks,
                "Certificate",
                "CERTIFICATE_NOT_FOUND",
                "Certificado activo no encontrado",
                "No existe un certificado SRI activo para cargar internamente.",
                isBlocking: true);
        }
        catch (InvalidOperationException ex)
        {
            AddCertificateMaterialError(checks, ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Unexpected SRI certificate readiness diagnostic failure. CompanyId {CompanyId} CertificateId {CertificateId}",
                certificate.CompanyId,
                certificate.Id);

            AddWarning(
                checks,
                "CertificateTrust",
                "CERTIFICATE_DIAGNOSTIC_FAILED",
                "Diagnostico local de certificado incompleto",
                "No se pudo completar el diagnostico local del certificado. Revisa logs del servidor sin exponer secretos.");
        }

        return diagnostics;
    }

    private static void AddCertificateMaterialError(
        List<SriFiscalReadinessCheckDto> checks,
        InvalidOperationException exception)
    {
        var code = exception.Message;

        switch (code)
        {
            case "CERTIFICATE_UNPROTECT_FAILED":
                AddError(
                    checks,
                    "Certificate",
                    "CERTIFICATE_UNPROTECT_FAILED",
                    "No se pudo desproteger el certificado",
                    "El certificado activo no pudo desprotegerse para diagnostico interno.",
                    isBlocking: true);
                break;
            case "CERTIFICATE_LOAD_FAILED":
                AddError(
                    checks,
                    "Certificate",
                    "CERTIFICATE_LOAD_FAILED",
                    "No se pudo cargar el certificado",
                    "El material del certificado activo no pudo cargarse para diagnostico interno.",
                    isBlocking: true);
                break;
            case "CERTIFICATE_WITHOUT_PRIVATE_KEY":
                AddError(
                    checks,
                    "Certificate",
                    "CERTIFICATE_WITHOUT_PRIVATE_KEY",
                    "Certificado sin clave privada",
                    "El certificado cargado internamente no contiene clave privada.",
                    isBlocking: true);
                break;
            case "CERTIFICATE_EXPIRED":
                AddError(
                    checks,
                    "Certificate",
                    "CERTIFICATE_EXPIRED",
                    "Certificado expirado",
                    "El certificado cargado internamente esta expirado.",
                    isBlocking: true);
                break;
            default:
                AddWarning(
                    checks,
                    "CertificateTrust",
                    "CERTIFICATE_DIAGNOSTIC_FAILED",
                    "Diagnostico local de certificado incompleto",
                    "No se pudo completar el diagnostico local del certificado.");
                break;
        }
    }

    private static void AddSelfSignedCheck(
        List<SriFiscalReadinessCheckDto> checks,
        X509Certificate2 certificate,
        int environment,
        CertificateReadinessDiagnostics diagnostics)
    {
        diagnostics.SelfSigned = IsSelfSigned(certificate);

        if (!diagnostics.SelfSigned)
        {
            AddSuccess(
                checks,
                "CertificateTrust",
                "CERTIFICATE_NOT_SELF_SIGNED",
                "Certificado no parece autofirmado",
                "Subject e issuer son diferentes en los metadatos del certificado.");

            return;
        }

        var blocksCurrentEnvironment = environment == 2;
        AddCheck(
            checks,
            "CertificateTrust",
            "CERTIFICATE_SELF_SIGNED",
            blocksCurrentEnvironment ? SeverityError : SeverityWarning,
            "Certificado posiblemente autofirmado",
            "El certificado parece autofirmado. SRI normalmente requiere un certificado emitido por una entidad certificadora reconocida.",
            null,
            blocksCurrentEnvironment);
    }

    private void AddChainBuildCheck(
        List<SriFiscalReadinessCheckDto> checks,
        X509Certificate2 certificate,
        int environment,
        CertificateReadinessDiagnostics diagnostics)
    {
        try
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.VerificationTime = DateTime.UtcNow;

            diagnostics.ChainBuildEvaluated = true;
            diagnostics.ChainBuildSucceeded = chain.Build(certificate);

            if (diagnostics.ChainBuildSucceeded)
            {
                AddSuccess(
                    checks,
                    "CertificateTrust",
                    "CERTIFICATE_CHAIN_BUILD_OK",
                    "Cadena local construida",
                    "El certificado construyo cadena localmente con revocacion en modo NoCheck.",
                    "Este es un diagnostico local; no garantiza aceptacion por SRI.");

                return;
            }

            var details = BuildChainStatusDetails(chain);
            var blocksCurrentEnvironment = environment == 2;

            AddCheck(
                checks,
                "CertificateTrust",
                "CERTIFICATE_CHAIN_BUILD_FAILED",
                blocksCurrentEnvironment ? SeverityError : SeverityWarning,
                "Posible problema de confianza local",
                "La cadena local del certificado no pudo construirse. La tienda local puede diferir de la validacion de SRI.",
                details,
                blocksCurrentEnvironment);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SRI certificate chain diagnostic failed.");

            diagnostics.ChainBuildEvaluated = false;
            diagnostics.ChainBuildSucceeded = false;

            var blocksCurrentEnvironment = environment == 2;
            AddCheck(
                checks,
                "CertificateTrust",
                "CERTIFICATE_CHAIN_DIAGNOSTIC_FAILED",
                blocksCurrentEnvironment ? SeverityError : SeverityWarning,
                "Diagnostico de cadena no completado",
                "No se pudo ejecutar el diagnostico local de cadena del certificado.",
                null,
                blocksCurrentEnvironment);
        }
    }

    private static void AddCertificateRucCheck(
        List<SriFiscalReadinessCheckDto> checks,
        string? companyRuc,
        X509Certificate2 certificate,
        CertificateReadinessDiagnostics diagnostics)
    {
        var normalizedCompanyRuc = NormalizeOptional(companyRuc);

        if (!IsNumeric(normalizedCompanyRuc, 13))
        {
            AddInfo(
                checks,
                "Certificate",
                "CERTIFICATE_RUC_NOT_EVALUATED",
                "RUC de certificado no evaluado",
                "No se compara el RUC del certificado porque el RUC de la empresa aun no es valido.");

            return;
        }

        var hints = ExtractRucHints(certificate).ToList();

        if (hints.Any(h => h.Value == normalizedCompanyRuc))
        {
            AddSuccess(
                checks,
                "Certificate",
                "CERTIFICATE_RUC_MATCH",
                "RUC encontrado en certificado",
                "Se encontro el RUC de la empresa dentro de los metadatos inspeccionados del certificado.",
                BuildRucHintDetails(hints.Where(h => h.Value == normalizedCompanyRuc)));

            return;
        }

        if (hints.Count > 0)
        {
            diagnostics.HasRucMismatch = true;
            AddError(
                checks,
                "Certificate",
                "CERTIFICATE_RUC_MISMATCH",
                "RUC de certificado no coincide",
                "Se detecto un RUC de 13 digitos en el certificado que no coincide con el RUC de la empresa.",
                BuildRucHintDetails(hints),
                isBlocking: true);

            return;
        }

        AddWarning(
            checks,
            "Certificate",
            "CERTIFICATE_RUC_NOT_CONFIRMED",
            "RUC no confirmado en certificado",
            "No se pudo confirmar que el certificado contenga el RUC de la empresa. Esto puede depender del formato del proveedor.");
    }

    private async Task<bool> AddDocumentSequenceChecksAsync(
        List<SriFiscalReadinessCheckDto> checks,
        int companyId,
        int establishmentId,
        int emissionPointId)
    {
        var maxUsedSequential = await _context.Sales
            .AsNoTracking()
            .Where(s =>
                s.CompanyId == companyId
                && s.EstablishmentId == establishmentId
                && s.EmissionPointId == emissionPointId
                && s.DocumentType == SaleDocumentType.Invoice
                && s.Sequential != null)
            .MaxAsync(s => (int?)s.Sequential) ?? 0;

        var sequence = await _context.DocumentSequences
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.CompanyId == companyId
                && s.EstablishmentId == establishmentId
                && s.EmissionPointId == emissionPointId
                && s.DocumentType == SaleDocumentType.Invoice);

        if (sequence is null)
        {
            AddWarning(
                checks,
                "DocumentSequence",
                "INVOICE_SEQUENCE_NOT_CONFIGURED",
                "Secuencia de factura no inicializada",
                "No existe secuencia explicita de factura. El sistema puede crearla automaticamente, pero para salida real se recomienda inicializar el secuencial correcto.",
                $"Maximo secuencial usado: {maxUsedSequential.ToString(CultureInfo.InvariantCulture)}.");

            return true;
        }

        var nextNumber = sequence.CurrentNumber + 1;

        if (nextNumber <= maxUsedSequential)
        {
            AddError(
                checks,
                "DocumentSequence",
                "INVOICE_SEQUENCE_BELOW_USED_NUMBER",
                "Secuencia de factura por debajo del uso actual",
                "El siguiente secuencial de factura no puede ser menor o igual al maximo ya usado.",
                $"Actual: {sequence.CurrentNumber.ToString(CultureInfo.InvariantCulture)}; siguiente: {nextNumber.ToString(CultureInfo.InvariantCulture)}; maximo usado: {maxUsedSequential.ToString(CultureInfo.InvariantCulture)}.",
                isBlocking: true);

            return false;
        }

        AddSuccess(
            checks,
            "DocumentSequence",
            "INVOICE_SEQUENCE_READY",
            "Secuencia de factura lista",
            "Existe secuencia explicita de factura para empresa, establecimiento y punto de emision actuales.",
            $"Actual: {sequence.CurrentNumber.ToString(CultureInfo.InvariantCulture)}; siguiente: {nextNumber.ToString(CultureInfo.InvariantCulture)}; maximo usado: {maxUsedSequential.ToString(CultureInfo.InvariantCulture)}.");

        return true;
    }

    private void AddProductionSafetyChecks(List<SriFiscalReadinessCheckDto> checks, int environment)
    {
        if (environment == 1)
        {
            AddInfo(
                checks,
                "ProductionSafety",
                "SRI_SANDBOX_ENVIRONMENT",
                "Ambiente de pruebas",
                "Ambiente de pruebas. Los comprobantes no tienen validez tributaria.");

            return;
        }

        if (environment != 2)
        {
            AddWarning(
                checks,
                "ProductionSafety",
                "SRI_PRODUCTION_SAFETY_NOT_EVALUATED",
                "Seguridad de produccion no evaluada",
                "No se puede evaluar el bloqueo de produccion porque el ambiente SRI es invalido.");

            return;
        }

        if (_sriOptions.AllowProductionSubmission)
        {
            AddSuccess(
                checks,
                "ProductionSafety",
                "SRI_PRODUCTION_ALLOWED",
                "Envio a produccion permitido por configuracion",
                "Produccion esta seleccionada y la configuracion permite intentar envio a produccion.");

            return;
        }

        AddError(
            checks,
            "ProductionSafety",
            "SRI_PRODUCTION_BLOCKED_BY_OPTIONS",
            "Envio a produccion bloqueado",
            "Produccion esta seleccionada, pero el envio a produccion esta bloqueado por configuracion.",
            isBlocking: true);
    }

    private static void AddSuccess(
        List<SriFiscalReadinessCheckDto> checks,
        string category,
        string code,
        string title,
        string message,
        string? details = null)
        => AddCheck(checks, category, code, SeveritySuccess, title, message, details, isBlocking: false);

    private static void AddWarning(
        List<SriFiscalReadinessCheckDto> checks,
        string category,
        string code,
        string title,
        string message,
        string? details = null)
        => AddCheck(checks, category, code, SeverityWarning, title, message, details, isBlocking: false);

    private static void AddError(
        List<SriFiscalReadinessCheckDto> checks,
        string category,
        string code,
        string title,
        string message,
        string? details = null,
        bool isBlocking = true)
        => AddCheck(checks, category, code, SeverityError, title, message, details, isBlocking);

    private static void AddInfo(
        List<SriFiscalReadinessCheckDto> checks,
        string category,
        string code,
        string title,
        string message,
        string? details = null)
        => AddCheck(checks, category, code, SeverityInfo, title, message, details, isBlocking: false);

    private static void AddCheck(
        List<SriFiscalReadinessCheckDto> checks,
        string category,
        string code,
        string severity,
        string title,
        string message,
        string? details,
        bool isBlocking)
    {
        checks.Add(new SriFiscalReadinessCheckDto
        {
            Category = category,
            Code = code,
            Severity = severity,
            Title = title,
            Message = message,
            Details = details,
            IsBlocking = isBlocking
        });
    }

    private static string BuildCertificateMetadataDetails(CompanySriCertificate certificate, DateTime now)
    {
        var daysUntilExpiration = (int)Math.Ceiling((certificate.NotAfter - now).TotalDays);

        return string.Join(
            "; ",
            $"Archivo: {certificate.FileName}",
            $"Subject: {certificate.Subject}",
            $"Issuer: {certificate.Issuer}",
            $"Thumbprint: {certificate.Thumbprint}",
            $"Serial: {certificate.SerialNumber}",
            $"Expira: {FormatUtcDate(certificate.NotAfter)}",
            $"Dias restantes: {Math.Max(0, daysUntilExpiration).ToString(CultureInfo.InvariantCulture)}");
    }

    private static string BuildChainStatusDetails(X509Chain chain)
    {
        var statuses = chain.ChainStatus
            .Select(status => $"{status.Status}: {status.StatusInformation?.Trim()}")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToList();

        if (statuses.Count == 0)
        {
            return "La cadena local fallo sin estados detallados. RevocationMode=NoCheck.";
        }

        return $"RevocationMode=NoCheck; Estados: {string.Join(" | ", statuses)}";
    }

    private static string BuildRucHintDetails(IEnumerable<CertificateRucHint> hints)
    {
        var values = hints
            .Select(h => $"{h.Value} ({h.Source})")
            .Distinct()
            .ToList();

        return values.Count == 0
            ? "Sin valores RUC detectados."
            : $"Valores detectados: {string.Join(", ", values)}.";
    }

    private static IEnumerable<CertificateRucHint> ExtractRucHints(X509Certificate2 certificate)
    {
        foreach (var hint in ExtractRucHints(certificate.Subject, "Subject"))
        {
            yield return hint;
        }

        foreach (var hint in ExtractRucHints(certificate.SubjectName.Name, "SubjectName"))
        {
            yield return hint;
        }

        foreach (var hint in ExtractRucHints(certificate.SerialNumber, "SerialNumber"))
        {
            yield return hint;
        }

        foreach (var extension in certificate.Extensions)
        {
            var source = string.IsNullOrWhiteSpace(extension.Oid?.FriendlyName)
                ? extension.Oid?.Value ?? "Extension"
                : extension.Oid.FriendlyName;

            foreach (var hint in ExtractRucHints(extension.Format(false), source))
            {
                yield return hint;
            }
        }
    }

    private static IEnumerable<CertificateRucHint> ExtractRucHints(string? value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(value, @"(?<!\d)\d{13}(?!\d)"))
        {
            yield return new CertificateRucHint(match.Value, source);
        }
    }

    private static bool IsSelfSigned(X509Certificate2 certificate)
        => string.Equals(
            NormalizeOptional(certificate.Subject),
            NormalizeOptional(certificate.Issuer),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsNumeric(string? value, int expectedLength)
    {
        var normalized = NormalizeOptional(value);

        return normalized is not null
            && normalized.Length == expectedLength
            && normalized.All(char.IsDigit);
    }

    private static bool IsNumericInRange(string? value, int minLength, int maxLength)
    {
        var normalized = NormalizeOptional(value);

        return normalized is not null
            && normalized.Length >= minLength
            && normalized.Length <= maxLength
            && normalized.All(char.IsDigit);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string FormatYesNo(bool value)
        => value ? "SI" : "NO";

    private static string FormatUtcDate(DateTime value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string GetEnvironmentLabel(int environment)
        => environment switch
        {
            1 => "Pruebas",
            2 => "Produccion",
            _ => "Desconocido"
        };

    private sealed class CertificateReadinessDiagnostics
    {
        public bool MetadataUsable { get; set; }
        public bool MaterialLoaded { get; set; }
        public bool SelfSigned { get; set; }
        public bool ChainBuildEvaluated { get; set; }
        public bool ChainBuildSucceeded { get; set; }
        public bool HasRucMismatch { get; set; }

        public bool IsUsableForSandbox
            => MetadataUsable
                && MaterialLoaded
                && !HasRucMismatch;

        public bool IsUsableForProduction
            => MetadataUsable
                && MaterialLoaded
                && !SelfSigned
                && ChainBuildEvaluated
                && ChainBuildSucceeded
                && !HasRucMismatch;
    }

    private sealed record CertificateRucHint(string Value, string Source);
}
