/* ============================================================================
   0011_SeedPaymentMethods.sql

   Seeds cfg.PaymentMethod, which fills Payment Method on billing checkout → Capture Payment
   (ListPaymentMethods), for every hospital missing them.

   - CASH / CREDIT_CARD / DEBIT_CARD match the app's demo data (DataStore.Seed). The name is stored on
     each payment and shown title-cased.
   - There is no settings screen for payment methods: remove or deactivate (IsActive = 0) any the clinic
     does not take.
   - Safe to re-run: existing rows (same hospital + name) are left alone.
   ============================================================================ */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Methods TABLE (MethodName NVARCHAR(30));
INSERT INTO @Methods VALUES (N'CASH'), (N'CREDIT_CARD'), (N'DEBIT_CARD');

INSERT INTO cfg.PaymentMethod (HospitalId, MethodName, IsActive)
SELECT h.HospitalId, m.MethodName, 1
FROM   cfg.Hospital h CROSS JOIN @Methods m
WHERE  NOT EXISTS (SELECT 1 FROM cfg.PaymentMethod x WHERE x.HospitalId = h.HospitalId AND x.MethodName = m.MethodName);
PRINT CONCAT(@@ROWCOUNT, N' payment method row(s) added.');

COMMIT TRANSACTION;
