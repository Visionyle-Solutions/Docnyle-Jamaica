using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Services
{
    /// <summary>
    /// Clinic stock: inventory items, batches (batch no + expiry) and the movement ledger.
    /// A batch is usable through its expiry date (the hospital's calendar date). Stock never goes negative: consultations may only
    /// pick what is available, and sign-off deducts earliest-expiry first or refuses if stock ran out meanwhile.
    /// </summary>
    public static partial class InventoryService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        public static readonly string[] Categories = { "MEDICATION", "INJECTION", "CONSUMABLE", "OTHER" };

        /* ── Items ── */

        public static List<InventoryItem> ListItems(string hospitalId, bool includeInactive)
        {
            if (Api) return ApiSummaries(includeInactive).Select(s => s.Item).ToList();
            lock (DataStore.Sync)
                return DataStore.InventoryItems.Where(i => i.HospitalId == hospitalId && (includeInactive || i.IsActive))
                    .OrderBy(i => i.ItemName).ToList();
        }

        public static InventoryItem GetItem(string inventoryItemId)
        {
            if (Api) return ApiGetItem(inventoryItemId);
            lock (DataStore.Sync) return DataStore.InventoryItems.FirstOrDefault(i => i.InventoryItemId == inventoryItemId);
        }

        /// <summary>API mode: SaveInventoryItem enforces the unique code (fault), so the pre-check passes.</summary>
        public static bool ItemCodeExists(string hospitalId, string code, string exceptItemId)
        {
            if (Api) return false;
            lock (DataStore.Sync)
                return DataStore.InventoryItems.Any(i => i.HospitalId == hospitalId && i.InventoryItemId != exceptItemId
                    && string.Equals(i.ItemCode, code, StringComparison.OrdinalIgnoreCase));
        }

        public static InventoryItem SaveItem(InventoryItem record)
        {
            if (Api) return ApiSaveItem(record);
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
            if (Api)
            {
                var i = ApiGetItem(inventoryItemId);
                if (i == null) return;
                cnHelperObj.UpdateInventoryItemActive(new INV_UpdateInventoryItemActiveReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, InventoryItemId = inventoryItemId, IsActive = !i.IsActive }).EnsureSuccess();
                ForgetItems();
                return;
            }
            lock (DataStore.Sync)
            {
                var i = GetItem(inventoryItemId);
                if (i != null) i.IsActive = !i.IsActive;
            }
        }

        /// <summary>Active items linked to the service (its "Used with" list), with their stock position.</summary>
        public static List<StockSummary> ItemsForService(string hospitalId, string serviceChargeId)
        {
            if (Api)
                return (cnHelperObj.ListItemsForService(AppSession.ApiHospitalId, AppSession.ApiUserId, serviceChargeId, AppSession.ApiHashValue).EnsureSuccess().Items ?? new List<INV_ServiceStockItemInfo>()).Select(i => new StockSummary
                {
                    Item = ListItems(hospitalId, false).FirstOrDefault(x => x.InventoryItemId == i.InventoryItemId)
                           ?? new InventoryItem { InventoryItemId = i.InventoryItemId, HospitalId = hospitalId, ItemName = i.ItemName, Category = i.Category, Unit = i.Unit, IsActive = true },
                    Available = i.Available,
                }).ToList();
            return ListItems(hospitalId, false).Where(i => i.ServiceChargeIds.Contains(serviceChargeId)).Select(Summary).ToList();
        }

        /* ── Stock position ── */

        private static bool Usable(StockBatch b) { return b.QtyOnHand > 0 && b.ExpiryDate.Date >= TimeZoneService.Today(b.HospitalId); }

        public static StockSummary Summary(InventoryItem item)
        {
            if (Api)
                return ApiSummaries(true).FirstOrDefault(s => s.Item.InventoryItemId == item.InventoryItemId) ?? new StockSummary { Item = item };
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
            if (Api) return ApiSummaries(includeInactive);
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
            if (Api) return ApiBatches(inventoryItemId, includeEmpty);
            lock (DataStore.Sync)
                return DataStore.StockBatches.Where(b => b.InventoryItemId == inventoryItemId && (includeEmpty || b.QtyOnHand > 0))
                    .OrderBy(b => b.ExpiryDate).ThenBy(b => b.ReceivedAt).ToList();
        }

        /// <summary>API mode: a batch this user was shown (ListStockBatches).</summary>
        public static StockBatch GetBatch(string stockBatchId)
        {
            if (Api) return SessionIndex.Get<StockBatch>("batch", stockBatchId);
            lock (DataStore.Sync) return DataStore.StockBatches.FirstOrDefault(b => b.StockBatchId == stockBatchId);
        }

        public static List<StockMovement> ListMovements(string inventoryItemId, int take)
        {
            if (Api)
                return (cnHelperObj.ListStockMovements(AppSession.ApiHospitalId, AppSession.ApiUserId, inventoryItemId, take.ToString(System.Globalization.CultureInfo.InvariantCulture), AppSession.ApiHashValue).EnsureSuccess().Movements ?? new List<INV_StockMovementInfo>())
                    .Select(m => new StockMovement
                    {
                        StockMovementId = m.StockMovementId, HospitalId = CallerHospital, InventoryItemId = inventoryItemId, StockBatchId = m.StockBatchId,
                        MovementType = m.MovementType, Qty = m.Qty, Reason = m.Reason ?? "", ConsultationId = m.ConsultationId, UserId = m.UserId,
                        CreatedAt = DocNyle.InstantOrMin(m.CreatedAt),
                    }).OrderByDescending(m => m.CreatedAt).Take(take).ToList();
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
            if (Api)
            {
                if (qty < 1) { error = "Quantity must be at least 1."; return null; }
                return ApiReceiveStock(inventoryItemId, batchNo, expiry, qty, unitCost, supplier, out error);
            }
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
            if (Api) return delta != 0 && ApiAdjustStock(stockBatchId, delta, reason);
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
            if (Api) return ConsultationService.ListServiceItems(consultationId).Where(s => s.InventoryItemId == inventoryItemId).Sum(s => s.ItemQty ?? 0);
            lock (DataStore.Sync)
                return DataStore.ConsultationServiceItems.Where(s => s.ConsultationId == consultationId && s.InventoryItemId == inventoryItemId).Sum(s => s.ItemQty ?? 0);
        }

        /// <summary>"Monocef 1g: 3 vial needed, 2 in stock" for every stocked item the consultation lists more of than is available.</summary>
        public static List<string> Shortages(string consultationId)
        {
            if (Api) return ApiShortages(consultationId);
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
            // API mode: ConfirmSignOff deducts the stock in the same transaction as the sign-off.
            if (Api) return new List<string>();
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

        private static bool Api { get { return DataSource.IsApi("Inventory"); } }

        private static string CallerHospital { get { return AppSession.ApiUser != null ? AppSession.ApiUser.HospitalId : null; } }

        /// <summary>ListInventoryItems with the stock position (available / expired / next expiry), cached for the request.</summary>
        private static List<StockSummary> ApiSummaries(bool includeInactive)
        {
            return RequestCache.Get("inv-items:" + includeInactive, () =>
                (cnHelperObj.ListInventoryItems(AppSession.ApiHospitalId, AppSession.ApiUserId, includeInactive ? "true" : "false", AppSession.ApiHashValue).EnsureSuccess().Items ?? new List<INV_InventoryItemSummaryInfo>()).Select(i => new StockSummary
                {
                    Item = new InventoryItem
                    {
                        InventoryItemId = i.InventoryItemId, HospitalId = CallerHospital, ItemCode = i.ItemCode, ItemName = i.ItemName, Category = i.Category,
                        Unit = i.Unit, UnitPrice = i.UnitPrice, TaxPercent = i.TaxPercent, ReorderLevel = i.ReorderLevel, IsActive = i.IsActive,
                    },
                    Available = i.Available, Expired = i.Expired, NextExpiry = DocNyle.ParseLocal(i.NextExpiry),
                }).OrderBy(s => s.Item.ItemName).ToList());
        }

        private static void ForgetItems() { RequestCache.RemovePrefix("inv-"); }

        private static InventoryItem ApiGetItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return RequestCache.Get("inv-item:" + id, () =>
            {
                var i = DocNyle.OrNull(() => cnHelperObj.GetInventoryItem(AppSession.ApiHospitalId, AppSession.ApiUserId, id, AppSession.ApiHashValue).EnsureSuccess())?.Item;
                if (i == null) return null;
                return new InventoryItem
                {
                    InventoryItemId = i.InventoryItemId, HospitalId = CallerHospital, ItemCode = i.ItemCode, ItemName = i.ItemName, Category = i.Category,
                    Unit = i.Unit, UnitPrice = i.UnitPrice, TaxPercent = i.TaxPercent, ReorderLevel = i.ReorderLevel, IsActive = i.IsActive,
                    ServiceChargeIds = (cnHelperObj.ListItemServices(AppSession.ApiHospitalId, AppSession.ApiUserId, id, AppSession.ApiHashValue).EnsureSuccess().Services ?? new List<INV_LinkedServiceInfo>()).Select(s => s.ServiceChargeId).ToList(),
                };
            });
        }

        private static InventoryItem ApiSaveItem(InventoryItem r)
        {
            var saved = cnHelperObj.SaveInventoryItem(new INV_SaveInventoryItemReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                InventoryItemId = string.IsNullOrEmpty(r.InventoryItemId) ? null : r.InventoryItemId, ItemCode = r.ItemCode, ItemName = r.ItemName,
                Category = r.Category, Unit = r.Unit, UnitPrice = r.UnitPrice, TaxPercent = r.TaxPercent, ReorderLevel = r.ReorderLevel,
            }).EnsureSuccess().Item;
            if (saved != null) r.InventoryItemId = saved.InventoryItemId;
            // The "Used with" services are a separate list on the service.
            cnHelperObj.SaveItemServices(new INV_SaveItemServicesReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, InventoryItemId = r.InventoryItemId, ServiceChargeIds = r.ServiceChargeIds ?? new List<string>() }).EnsureSuccess();
            ForgetItems();
            return ApiGetItem(r.InventoryItemId) ?? r;
        }

        private static StockBatch ToModel(INV_StockBatchInfo b)
        {
            return new StockBatch
            {
                StockBatchId = b.StockBatchId, HospitalId = CallerHospital, InventoryItemId = b.InventoryItemId, BatchNo = b.BatchNo,
                ExpiryDate = DocNyle.LocalOrMin(b.ExpiryDate).Date, QtyReceived = b.QtyReceived, QtyOnHand = b.QtyOnHand, UnitCost = b.UnitCost,
                Supplier = b.Supplier, ReceivedByUserId = b.ReceivedByUserId, ReceivedAt = DocNyle.InstantOrMin(b.ReceivedAt),
            };
        }

        private static List<StockBatch> ApiBatches(string itemId, bool includeEmpty)
        {
            return RequestCache.Get("inv-batches:" + itemId + ":" + includeEmpty, () =>
                (cnHelperObj.ListStockBatches(AppSession.ApiHospitalId, AppSession.ApiUserId, itemId, includeEmpty ? "true" : "false", AppSession.ApiHashValue).EnsureSuccess().Batches ?? new List<INV_StockBatchInfo>())
                    .Select(b => SessionIndex.Put("batch", b.StockBatchId, ToModel(b))).OrderBy(b => b.ExpiryDate).ThenBy(b => b.ReceivedAt).ToList());
        }

        /// <summary>RecordStockReceipt tops up a batch already on file (same expiry) or opens a new one; a mismatch comes back as a validation fault.</summary>
        private static StockBatch ApiReceiveStock(string itemId, string batchNo, DateTime expiry, int qty, decimal unitCost, string supplier, out string error)
        {
            error = null;
            try
            {
                var rows = cnHelperObj.RecordStockReceipt(new INV_RecordStockReceiptReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    InventoryItemId = itemId, BatchNo = batchNo, ExpiryDate = DocNyle.Date(expiry), Qty = qty, UnitCost = unitCost, Supplier = supplier,
                }).EnsureSuccess().Batches ?? new List<INV_StockBatchInfo>();
                ForgetItems();
                var b = rows.FirstOrDefault(x => string.Equals(x.BatchNo, batchNo, StringComparison.OrdinalIgnoreCase)) ?? rows.FirstOrDefault();
                return b != null ? ToModel(b) : new StockBatch { InventoryItemId = itemId, BatchNo = batchNo, ExpiryDate = expiry.Date };
            }
            catch (ApiException ex) when (ex.Kind == ApiFaultKind.Validation || ex.Kind == ApiFaultKind.BusinessRule || ex.Kind == ApiFaultKind.NotFound)
            {
                error = ex.UserMessage;
                return null;
            }
        }

        private static bool ApiAdjustStock(string stockBatchId, int delta, string reason)
        {
            try
            {
                cnHelperObj.RecordStockAdjustment(new INV_RecordStockAdjustmentReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, StockBatchId = stockBatchId, Delta = delta, Reason = reason }).EnsureSuccess();
                ForgetItems();
                return true;
            }
            catch (ApiException ex) when (ex.Kind == ApiFaultKind.Validation || ex.Kind == ApiFaultKind.BusinessRule)
            {
                return false;
            }
        }

        /// <summary>ListStockShortages for a consultation, as "Monocef 1g: 3 vial needed, 2 in stock".</summary>
        private static List<string> ApiShortages(string consultationId)
        {
            return (cnHelperObj.ListStockShortages(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().Shortages ?? new List<CON_StockShortageInfo>())
                .Select(s => s.ItemName + ": " + s.Needed.ToString(CultureInfo.InvariantCulture) + " " + s.Unit + " needed, " + s.Available.ToString(CultureInfo.InvariantCulture) + " in stock")
                .ToList();
        }
    }
}
