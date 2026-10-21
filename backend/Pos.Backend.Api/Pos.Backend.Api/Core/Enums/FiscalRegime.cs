namespace Pos.Backend.Api.Core.Enums;

public enum FiscalRegime { General = 0, PopularBusiness = 1, Entrepreneur = 2 }
public enum TaxpayerKind { Natural = 0, Juridical = 1 }
public enum FiscalProductKind { Goods = 0, Service = 1 }
public enum FiscalActivity { EligibleCommerce = 0, EligibleService = 1, Excluded = 2, Undetermined = 3 }
public enum FiscalTaxBasis
{
    GeneralRate = 0,
    NaturalAgriculturalFoodArt55_1 = 1,
    ZeroServicesArt56 = 2,
    ConstructionMaterials24_13 = 3,
    NotSubjectArt54 = 4,
    ExemptSpecialLaw = 5
}

[Flags]
public enum NaturalFoodProcessing { None = 0, Refrigerated = 1, Frozen = 2, Cut = 4, Packaged = 8 }
