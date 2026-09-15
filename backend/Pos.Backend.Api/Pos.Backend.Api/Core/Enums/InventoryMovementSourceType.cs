namespace Pos.Backend.Api.Core.Enums;

public enum InventoryMovementSourceType
{
    ManualEntry = 1,
    ManualExit = 2,
    ManualAdjustment = 3,
    Sale = 4,
    SaleVoid = 5,
    PurchaseReceipt = 6,
    PurchaseReceiptCancel = 7,
    CreditNoteReturn = 8
}
