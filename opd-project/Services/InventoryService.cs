using System;
using System.Collections.Generic;
using System.Linq;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>
    /// Clinic stock: inventory items, batches (batch no + expiry) and the movement ledger.
    /// A batch is usable through its expiry date (the hospital's calendar date). Stock never goes negative: consultations may only
    /// pick what is available, and sign-off deducts earliest-expiry first or refuses if stock ran out meanwhile.
    /// </summary>
    public static class InventoryService
    {
        public static readonly string[] Categories = { "MEDICATION", "INJECTION", "CONSUMABLE", "OTHER" };

        /* ── Items ── */

        public static List<InventoryItem> ListItems(string hospitalId, bool includeInactive)
        {
            lock (DataStore.Sync)
                return DataStore.InventoryItems.Where(i => i.HospitalId == hospitalId && (includeInactive || i.IsActive))
                    .OrderBy(i => i.ItemName).ToList();
        }

        public static InventoryItem GetItem(string inventoryItemId)
        {
            lock (DataStore.Sync) return DataStore.InventoryItems.FirstOrDefault(i => i.InventoryItemId == inventoryItemId);
        }

        public static bool ItemCodeExists(string hospitalId, string code, string exceptItemId)
        {
            lock (DataStore.Sync)
                return DataStore.InventoryItems.Any(i => i.HospitalId == hospitalId && i.InventoryItemId != exceptItemId
                    && string.Equals(i.ItemCode, code, StringComparison.OrdinalIgnoreCase));
        }

        public static InventoryItem SaveItem(InventoryItem record)
        {
            lock (DataStore.Sync)
            {
                var i = string.IsNullOrEmpty(record.InventoryItemId) ? null : GetItem(record.InventoryItemId);
                if (i == null)
                {
                    record.InventoryItemId = DataStore.NextId("ITM");
                    record.IsActive = true;
                    DataStore.InventoryItems.Add(record);
                    return record;
                }
                i.ItemCode = record.ItemCode; i.ItemName = record.ItemName; i.Category = record.Category; i.Unit = record.Unit;
                i.UnitPrice = record.UnitPrice; i.TaxPercent = record.TaxPercent; i.ReorderLevel = record.ReorderLevel;
                i.ServiceChargeIds = record.ServiceChargeIds ?? new List<string>();
                return i;
            }
        }

        public static void ToggleItem(string inventoryItemId)
        {
            lock (DataStore.Sync)
            {
                var i = GetItem(inventoryItemId);
                if (i != null) i.IsActive = !i.IsActive;
            }
        }

        /// <summary>Active items linked to the service (its "Used with" list), with their stock position.</summary>
        public static List<StockSummary> ItemsForService(string hospitalId, string serviceChargeId)
        {
            return ListItems(hospitalId, false).Where(i => i.ServiceChargeIds.Contains(serviceChargeId)).Select(Summary).ToList();
        }

        /* ── Stock position ── */

        private static bool Usable(StockBatch b) { return b.QtyOnHand > 0 && b.ExpiryDate.Date >= TimeZoneService.Today(b.HospitalId); }

        public static StockSummary Summary(InventoryItem item)
        {
            lock (DataStore.Sync)
            {
                var batches = DataStore.StockBatches.Where(b => b.InventoryItemId == item.InventoryItemId && b.QtyOnHand > 0).ToList();
                var usable = batches.Where(Usable).ToList();
                return new StockSummary
                {
                    Item = item,
                    Available = usable.Sum(b => b.QtyOnHand),
                    Expired = batches.Where(b => !Usable(b)).Sum(b => b.QtyOnHand),
                    NextExpiry = usable.Count > 0 ? usable.Min(b => b.ExpiryDate) : (DateTime?)null
                };
            }
        }

        public static List<StockSummary> Summaries(string hospitalId, bool includeInactive)
        {
            return ListItems(hospitalId, includeInactive).Select(Summary).ToList();
        }

        public static int Available(string inventoryItemId)
        {
            var item = GetItem(inventoryItemId);
            return item == null ? 0 : Summary(item).Available;
        }

        /// <summary>Batches newest-expiry last; empty batches only when asked for.</summary>
        public static List<StockBatch> ListBatches(string inventoryItemId, bool includeEmpty)
        {
            lock (DataStore.Sync)
                return DataStore.StockBatches.Where(b => b.InventoryItemId == inventoryItemId && (includeEmpty || b.QtyOnHand > 0))
                    .OrderBy(b => b.ExpiryDate).ThenBy(b => b.ReceivedAt).ToList();
        }

        public static StockBatch GetBatch(string stockBatchId)
        {
            lock (DataStore.Sync) return DataStore.StockBatches.FirstOrDefault(b => b.StockBatchId == stockBatchId);
        }

        public static List<StockMovement> ListMovements(string inventoryItemId, int take)
        {
            lock (DataStore.Sync)
                return DataStore.StockMovements.Where(m => m.InventoryItemId == inventoryItemId)
                    .OrderByDescending(m => m.CreatedAt).Take(take).ToList();
        }

        /* ── Receive / adjust ── */

        /// <summary>
        /// Records a delivery. A batch number already on file for the item is topped up, provided the expiry matches;
        /// returns null with <paramref name="error"/> set otherwise.
        /// </summary>
        public static StockBatch ReceiveStock(string hospitalId, string inventoryItemId, string batchNo, DateTime expiry, int qty,
            decimal unitCost, string supplier, string userId, out string error)
        {
            error = null;
            lock (DataStore.Sync)
            {
                var item = GetItem(inventoryItemId);
                if (item == null || item.HospitalId != hospitalId) { error = "Item not found."; return null; }
                if (qty < 1) { error = "Quantity must be at least 1."; return null; }
                var batch = DataStore.StockBatches.FirstOrDefault(b => b.InventoryItemId == inventoryItemId
                    && string.Equals(b.BatchNo, batchNo, StringComparison.OrdinalIgnoreCase));
                if (batch != null && batch.ExpiryDate.Date != expiry.Date)
                {
                    error = "Batch " + batch.BatchNo + " is already on file with expiry " + batch.ExpiryDate.ToString("dd MMM yyyy") + ".";
                    return null;
                }
                if (batch == null)
                {
                    batch = new StockBatch
                    {
                        StockBatchId = DataStore.NextId("BAT"), HospitalId = hospitalId, InventoryItemId = inventoryItemId,
                        BatchNo = batchNo, ExpiryDate = expiry.Date, UnitCost = unitCost, Supplier = supplier,
                        ReceivedByUserId = userId, ReceivedAt = DateTime.UtcNow
                    };
                    DataStore.StockBatches.Add(batch);
                }
                batch.QtyReceived += qty;
                batch.QtyOnHand += qty;
                AddMovement(batch, "RECEIPT", qty, string.IsNullOrEmpty(supplier) ? "Received" : "Received from " + supplier, null, userId);
                return batch;
            }
        }

        /// <summary>Corrects a batch by a signed quantity (count correction, damage, expiry write-off). Refuses to go below zero.</summary>
        public static bool AdjustStock(string stockBatchId, int delta, string reason, string userId)
        {
            lock (DataStore.Sync)
            {
                var batch = GetBatch(stockBatchId);
                if (batch == null || delta == 0 || batch.QtyOnHand + delta < 0) return false;
                batch.QtyOnHand += delta;
                AddMovement(batch, "ADJUSTMENT", delta, reason, null, userId);
                return true;
            }
        }

        /* ── Use in consultations ── */

        /// <summary>Quantity of the item already listed on the consultation (not yet deducted: that happens at sign-off).</summary>
        public static int QtyOnConsultation(string consultationId, string inventoryItemId)
        {
            lock (DataStore.Sync)
                return DataStore.ConsultationServiceItems.Where(s => s.ConsultationId == consultationId && s.InventoryItemId == inventoryItemId).Sum(s => s.ItemQty ?? 0);
        }

        /// <summary>"Monocef 1g: 3 vial needed, 2 in stock" for every stocked item the consultation lists more of than is available.</summary>
        public static List<string> Shortages(string consultationId)
        {
            lock (DataStore.Sync)
                return DataStore.ConsultationServiceItems.Where(s => s.ConsultationId == consultationId && s.InventoryItemId != null)
                    .GroupBy(s => s.InventoryItemId)
                    .Select(g => new { Item = GetItem(g.Key), Needed = g.Sum(s => s.ItemQty ?? 0) })
                    .Where(x => x.Item != null)
                    .Select(x => new { x.Item, x.Needed, Available = Summary(x.Item).Available })
                    .Where(x => x.Needed > x.Available)
                    .Select(x => x.Item.ItemName + ": " + x.Needed + " " + x.Item.Unit + " needed, " + x.Available + " in stock")
                    .ToList();
        }

        /// <summary>
        /// Deducts the consultation's stocked items, earliest-expiry batch first. All or nothing: if any item is short,
        /// nothing is deducted and the shortages are returned. Safe to call twice (a consultation is only deducted once).
        /// </summary>
        public static List<string> ConsumeForConsultation(string consultationId, string userId)
        {
            lock (DataStore.Sync)
            {
                if (DataStore.StockMovements.Any(m => m.ConsultationId == consultationId && m.MovementType == "CONSUMPTION")) return new List<string>();
                var shortages = Shortages(consultationId);
                if (shortages.Count > 0) return shortages;

                foreach (var line in DataStore.ConsultationServiceItems.Where(s => s.ConsultationId == consultationId && s.InventoryItemId != null).ToList())
                {
                    var remaining = line.ItemQty ?? 0;
                    foreach (var batch in DataStore.StockBatches.Where(b => b.InventoryItemId == line.InventoryItemId && Usable(b))
                                 .OrderBy(b => b.ExpiryDate).ThenBy(b => b.ReceivedAt).ToList())
                    {
                        if (remaining == 0) break;
                        var take = Math.Min(remaining, batch.QtyOnHand);
                        batch.QtyOnHand -= take;
                        remaining -= take;
                        AddMovement(batch, "CONSUMPTION", -take, "Used in consultation", consultationId, userId);
                    }
                }
                return new List<string>();
            }
        }

        private static void AddMovement(StockBatch batch, string type, int qty, string reason, string consultationId, string userId)
        {
            DataStore.StockMovements.Add(new StockMovement
            {
                StockMovementId = DataStore.NextId("STM"), HospitalId = batch.HospitalId, InventoryItemId = batch.InventoryItemId,
                StockBatchId = batch.StockBatchId, MovementType = type, Qty = qty, Reason = reason ?? "",
                ConsultationId = consultationId, UserId = userId, CreatedAt = DateTime.UtcNow
            });
        }
    }
}
