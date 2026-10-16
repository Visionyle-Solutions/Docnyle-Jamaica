using System;
using System.Collections.Generic;

namespace OpdProject.Models
{
    /// <summary>
    /// Something the clinic keeps in stock and uses during a visit (an injection, a vial, a dressing pack).
    /// Separate from the Drug Formulary, which lists what doctors may prescribe for the patient to buy outside.
    /// Category: MEDICATION, INJECTION, CONSUMABLE or OTHER.
    /// </summary>
    public class InventoryItem
    {
        public string InventoryItemId { get; set; }
        public string HospitalId { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public string Category { get; set; }
        /// <summary>Unit stock is counted and billed in: vial, ampoule, tablet, piece …</summary>
        public string Unit { get; set; }
        /// <summary>Selling price per unit, billed at sign-off.</summary>
        public decimal UnitPrice { get; set; }
        public decimal TaxPercent { get; set; }
        /// <summary>On-hand quantity at or below which the item is flagged as low stock.</summary>
        public int ReorderLevel { get; set; }
        public bool IsActive { get; set; }
        /// <summary>Services / procedures this item is used with; it is offered in the Item field only for these.</summary>
        public List<string> ServiceChargeIds { get; set; } = new List<string>();
    }

    /// <summary>One receipt of an item: batch number, expiry and what is left of it. Used earliest-expiry first.</summary>
    public class StockBatch
    {
        public string StockBatchId { get; set; }
        public string HospitalId { get; set; }
        public string InventoryItemId { get; set; }
        public string BatchNo { get; set; }
        public DateTime ExpiryDate { get; set; }
        public int QtyReceived { get; set; }
        public int QtyOnHand { get; set; }
        public decimal UnitCost { get; set; }
        public string Supplier { get; set; }
        public string ReceivedByUserId { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    /// <summary>
    /// Append-only stock ledger. Qty is signed: + for RECEIPT, − for CONSUMPTION; ADJUSTMENT either way.
    /// A batch's QtyOnHand always equals the sum of its movements.
    /// </summary>
    public class StockMovement
    {
        public string StockMovementId { get; set; }
        public string HospitalId { get; set; }
        public string InventoryItemId { get; set; }
        public string StockBatchId { get; set; }
        public string MovementType { get; set; }
        public int Qty { get; set; }
        public string Reason { get; set; }
        /// <summary>Set for CONSUMPTION: the consultation the stock was used in.</summary>
        public string ConsultationId { get; set; }
        public string UserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Stock position of an item, for lists and the Services &amp; Procedures picker.</summary>
    public class StockSummary
    {
        public InventoryItem Item { get; set; }
        /// <summary>Usable stock: unexpired batches only.</summary>
        public int Available { get; set; }
        public int Expired { get; set; }
        public DateTime? NextExpiry { get; set; }
        public bool IsLow { get { return Item.IsActive && Available <= Item.ReorderLevel; } }
        public bool ExpiresSoon { get { return NextExpiry.HasValue && NextExpiry.Value <= OpdProject.Services.TimeZoneService.Today(Item.HospitalId).AddDays(InventoryRules.ExpiryWarningDays); } }
    }

    public static class InventoryRules
    {
        /// <summary>Batches expiring within this many days are flagged.</summary>
        public const int ExpiryWarningDays = 30;
    }
}
