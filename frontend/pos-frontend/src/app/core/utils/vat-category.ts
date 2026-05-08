export enum ProductVatCategory {
  Vat15 = 0,
  Vat5 = 1,
  Vat0 = 2,
  VatExempt = 3,
  VatNotSubject = 4,
}

export interface VatCategoryOption {
  value: ProductVatCategory;
  label: string;
  shortLabel: string;
  rateLabel: string;
}

export interface TaxSummary {
  subtotal: number;
  taxAmount: number;
  total: number;
  vat15Subtotal: number;
  vat5Subtotal: number;
  vat0Subtotal: number;
  vatExemptSubtotal: number;
  vatNotSubjectSubtotal: number;
}

export interface TaxableLine {
  quantity: number;
  unitPrice: number;
  vatCategory?: ProductVatCategory | number | string | null;
  product?: { vatCategory?: ProductVatCategory | number | string | null } | null;
}

export const DEFAULT_VAT_CATEGORY = ProductVatCategory.Vat15;

export const VAT_CATEGORY_OPTIONS: VatCategoryOption[] = [
  { value: ProductVatCategory.Vat15, label: 'IVA 15%', shortLabel: 'IVA 15%', rateLabel: '15%' },
  { value: ProductVatCategory.Vat5, label: 'IVA 5%', shortLabel: 'IVA 5%', rateLabel: '5%' },
  { value: ProductVatCategory.Vat0, label: 'IVA 0%', shortLabel: 'IVA 0%', rateLabel: '0%' },
  { value: ProductVatCategory.VatExempt, label: 'Exento IVA', shortLabel: 'Exento', rateLabel: '0%' },
  { value: ProductVatCategory.VatNotSubject, label: 'No objeto IVA', shortLabel: 'No objeto', rateLabel: '0%' },
];

const VAT_RATE_BY_CATEGORY: Record<ProductVatCategory, number> = {
  [ProductVatCategory.Vat15]: 0.15,
  [ProductVatCategory.Vat5]: 0.05,
  [ProductVatCategory.Vat0]: 0,
  [ProductVatCategory.VatExempt]: 0,
  [ProductVatCategory.VatNotSubject]: 0,
};

export function normalizeVatCategory(value: unknown): ProductVatCategory {
  if (typeof value === 'number' && isVatCategory(value)) {
    return value;
  }

  if (typeof value === 'string') {
    const trimmed = value.trim();
    const numeric = Number(trimmed);

    if (Number.isFinite(numeric) && isVatCategory(numeric)) {
      return numeric;
    }

    const enumValue = ProductVatCategory[trimmed as keyof typeof ProductVatCategory];
    if (typeof enumValue === 'number' && isVatCategory(enumValue)) {
      return enumValue;
    }
  }

  return DEFAULT_VAT_CATEGORY;
}

export function getVatCategoryOption(value: unknown): VatCategoryOption {
  const category = normalizeVatCategory(value);
  return VAT_CATEGORY_OPTIONS.find((option) => option.value === category) ?? VAT_CATEGORY_OPTIONS[0];
}

export function getVatRate(value: unknown): number {
  return VAT_RATE_BY_CATEGORY[normalizeVatCategory(value)];
}

export function calculateTaxSummary(lines: TaxableLine[]): TaxSummary {
  const summary = emptyTaxSummary();

  for (const line of lines) {
    const vatCategory = normalizeVatCategory(line.vatCategory ?? line.product?.vatCategory);
    const taxableSubtotal = roundMoney(line.quantity * line.unitPrice);
    const taxAmount = roundMoney(taxableSubtotal * getVatRate(vatCategory));

    summary.subtotal = roundMoney(summary.subtotal + taxableSubtotal);
    summary.taxAmount = roundMoney(summary.taxAmount + taxAmount);

    if (vatCategory === ProductVatCategory.Vat15) {
      summary.vat15Subtotal = roundMoney(summary.vat15Subtotal + taxableSubtotal);
    } else if (vatCategory === ProductVatCategory.Vat5) {
      summary.vat5Subtotal = roundMoney(summary.vat5Subtotal + taxableSubtotal);
    } else if (vatCategory === ProductVatCategory.Vat0) {
      summary.vat0Subtotal = roundMoney(summary.vat0Subtotal + taxableSubtotal);
    } else if (vatCategory === ProductVatCategory.VatExempt) {
      summary.vatExemptSubtotal = roundMoney(summary.vatExemptSubtotal + taxableSubtotal);
    } else {
      summary.vatNotSubjectSubtotal = roundMoney(summary.vatNotSubjectSubtotal + taxableSubtotal);
    }
  }

  summary.total = roundMoney(summary.subtotal + summary.taxAmount);
  return summary;
}

export function calculateLineTotal(quantity: number, unitPrice: number, vatCategory: unknown): number {
  const taxableSubtotal = roundMoney(quantity * unitPrice);
  return roundMoney(taxableSubtotal + roundMoney(taxableSubtotal * getVatRate(vatCategory)));
}

export function roundMoney(value: number): number {
  return Math.round((Number(value) + Number.EPSILON) * 100) / 100;
}

function emptyTaxSummary(): TaxSummary {
  return {
    subtotal: 0,
    taxAmount: 0,
    total: 0,
    vat15Subtotal: 0,
    vat5Subtotal: 0,
    vat0Subtotal: 0,
    vatExemptSubtotal: 0,
    vatNotSubjectSubtotal: 0,
  };
}

function isVatCategory(value: number): value is ProductVatCategory {
  return VAT_CATEGORY_OPTIONS.some((option) => option.value === value);
}
