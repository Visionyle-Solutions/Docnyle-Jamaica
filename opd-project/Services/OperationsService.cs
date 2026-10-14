using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpdProject.Models;

namespace OpdProject.Services
{
    public class CountRow
    {
        public string Label { get; set; }
        public int Count { get; set; }
    }

    public class RevenueRow
    {
        public string DoctorName { get; set; }
        public decimal Invoiced { get; set; }
        public decimal Paid { get; set; }
        public int Count { get; set; }
    }

    /// <summary>Counts for today, this week (Monday–Sunday) and this calendar month.</summary>
    public class PeriodCounts
    {
        public int Today { get; set; }
        public int Week { get; set; }
        public int Month { get; set; }
    }

    public class DashboardStats
    {
        public PeriodCounts Consultations { get; set; }
        public PeriodCounts Appointments { get; set; }
        public PeriodCounts Cancelled { get; set; }
        /// <summary>Today's visits by appointment status (status code → count).</summary>
        public List<CountRow> TodayStatus { get; set; }
    }

    /// <summary>The five dashboard reports, each over an inclusive date range.</summary>
    public static class ReportService
    {
        private static bool InRange(DateTime d, DateTime from, DateTime to) { return d.Date >= from.Date && d.Date <= to.Date; }

        /// <summary>Appointment statuses that count as a consultation: the patient arrived and was (or is being) seen.</summary>
        private static readonly string[] SeenStatuses = { "CHECKED_IN", "IN_CONSULT", "COMPLETED" };

        /// <summary>Dashboard tile groups: "consultations" (visits that reached check-in), "appointments" (not cancelled), "cancelled".</summary>
        public static bool IsDashboardKind(string kind) { return kind == "consultations" || kind == "appointments" || kind == "cancelled"; }

        private static bool MatchesKind(Appointment a, string kind)
        {
            switch (kind)
            {
                case "consultations": return SeenStatuses.Contains(a.Status);
                case "appointments": return a.Status != "CANCELLED";
                case "cancelled": return a.Status == "CANCELLED";
                default: return false;
            }
        }

        /// <summary>"today", "week" (Monday–Sunday) or "month" (calendar month) as an inclusive date range; false for anything else.</summary>
        public static bool PeriodRange(string period, DateTime today, out DateTime from, out DateTime to)
        {
            today = today.Date;
            switch (period)
            {
                case "today": from = to = today; return true;
                case "week": from = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); to = from.AddDays(6); return true;
                case "month": from = new DateTime(today.Year, today.Month, 1); to = from.AddMonths(1).AddDays(-1); return true;
                default: from = to = today; return false;
            }
        }

        private static List<Appointment> DashboardAppointments(string hid, string doctorId, string kind, string period, DateTime today)
        {
            DateTime from, to;
            PeriodRange(period, today, out from, out to);
            return DataStore.Appointments.Where(a => a.HospitalId == hid && (doctorId == null || a.DoctorId == doctorId)
                && MatchesKind(a, kind) && InRange(a.SlotStart, from, to)).ToList();
        }

        /// <summary>Home dashboard: consultations, appointments and cancelled appointments for today / this week / this month,
        /// plus today's visits by status. A null doctorId counts the whole hospital.</summary>
        public static DashboardStats Dashboard(string hid, string doctorId, DateTime today)
        {
            lock (DataStore.Sync)
            {
                Func<string, PeriodCounts> counts = kind => new PeriodCounts
                {
                    Today = DashboardAppointments(hid, doctorId, kind, "today", today).Count,
                    Week = DashboardAppointments(hid, doctorId, kind, "week", today).Count,
                    Month = DashboardAppointments(hid, doctorId, kind, "month", today).Count,
                };
                return new DashboardStats
                {
                    Consultations = counts("consultations"),
                    Appointments = counts("appointments"),
                    Cancelled = counts("cancelled"),
                    TodayStatus = DashboardAppointments(hid, doctorId, "appointments", "today", today)
                        .GroupBy(a => a.Status).Select(g => new CountRow { Label = g.Key, Count = g.Count() }).OrderByDescending(r => r.Count).ToList(),
                };
            }
        }

        /// <summary>The appointments behind one dashboard number (optionally narrowed to one status), oldest first.</summary>
        public static List<AppointmentView> DashboardList(string hid, string doctorId, string kind, string period, string status, DateTime today)
        {
            lock (DataStore.Sync)
                return SchedulingService.ToViews(DashboardAppointments(hid, doctorId, kind, period, today)
                    .Where(a => status == null || a.Status == status).OrderBy(a => a.SlotStart));
        }

        public static List<CountRow> ConsultationVolume(string hid, DateTime from, DateTime to)
        {
            lock (DataStore.Sync)
                return DataStore.Consultations.Where(c => c.HospitalId == hid && InRange(c.VisitDate, from, to))
                    .GroupBy(c => c.VisitDate.Date).OrderBy(g => g.Key).Select(g => new CountRow { Label = g.Key.ToString("yyyy-MM-dd"), Count = g.Count() }).ToList();
        }

        public static List<CountRow> DiagnosisFrequency(string hid, DateTime from, DateTime to)
        {
            lock (DataStore.Sync)
                return DataStore.Diagnoses.Join(DataStore.Consultations, d => d.ConsultationId, c => c.ConsultationId, (d, c) => new { d, c })
                    .Where(x => x.c.HospitalId == hid && InRange(x.c.VisitDate, from, to))
                    .GroupBy(x => x.d.Icd10Code + " " + x.d.Icd10Description).Select(g => new CountRow { Label = g.Key, Count = g.Count() })
                    .OrderByDescending(r => r.Count).ToList();
        }

        public static List<CountRow> Prescribing(string hid, DateTime from, DateTime to)
        {
            lock (DataStore.Sync)
                return DataStore.Prescriptions.Join(DataStore.Consultations, p => p.ConsultationId, c => c.ConsultationId, (p, c) => new { p, c })
                    .Where(x => x.c.HospitalId == hid && InRange(x.c.VisitDate, from, to))
                    .GroupBy(x => x.p.MedicineName).Select(g => new CountRow { Label = g.Key, Count = g.Count() })
                    .OrderByDescending(r => r.Count).ToList();
        }

        public static List<CountRow> AppointmentStatus(string hid, DateTime from, DateTime to, out int total, out int noShowRate)
        {
            lock (DataStore.Sync)
            {
                var appts = DataStore.Appointments.Where(a => a.HospitalId == hid && InRange(a.SlotStart, from, to)).ToList();
                total = appts.Count;
                noShowRate = total > 0 ? (int)Math.Round(appts.Count(a => a.Status == "NO_SHOW") * 100.0 / total) : 0;
                return appts.GroupBy(a => a.Status).Select(g => new CountRow { Label = g.Key, Count = g.Count() }).OrderByDescending(r => r.Count).ToList();
            }
        }

        public static List<RevenueRow> Revenue(string hid, DateTime from, DateTime to)
        {
            lock (DataStore.Sync)
                return DataStore.Invoices.Where(i => i.HospitalId == hid && i.Status != "CANCELLED" && InRange(i.CreatedAt, from, to))
                    .GroupBy(i => i.DoctorId).Select(g => new RevenueRow
                    {
                        DoctorName = UserService.UserName(g.Key), Invoiced = g.Sum(i => i.TotalAmount), Paid = g.Sum(i => i.PaidAmount), Count = g.Count()
                    }).ToList();
        }
    }

    /// <summary>Message templates, channel configuration and the outgoing message queue (simulated delivery).</summary>
    public static class NotificationService
    {
        public static List<MessageTemplate> ListTemplates()
        {
            lock (DataStore.Sync) return DataStore.MessageTemplates.ToList();
        }

        public static MessageTemplate GetTemplate(string templateId)
        {
            lock (DataStore.Sync) return DataStore.MessageTemplates.FirstOrDefault(t => t.TemplateId == templateId);
        }

        public static void UpdateTemplate(string templateId, string name, string channel, string text)
        {
            lock (DataStore.Sync)
            {
                var t = GetTemplate(templateId);
                if (t == null) return;
                t.Name = name; t.Channel = channel; t.Text = text;
            }
        }

        public static void SetTemplateEnabled(string templateId, bool enabled)
        {
            lock (DataStore.Sync)
            {
                var t = GetTemplate(templateId);
                if (t != null) t.IsEnabled = enabled;
            }
        }

        public static string Render(string text, IDictionary<string, string> vars)
        {
            foreach (var kv in vars) text = text.Replace("{" + kv.Key + "}", kv.Value ?? "");
            return text;
        }

        /// <summary>Queues a message; it is SENT when the channel is enabled, otherwise SIMULATED (no provider is wired up).</summary>
        public static QueuedMessage Queue(string hospitalId, Patient patient, MessageTemplate template, string text)
        {
            lock (DataStore.Sync)
            {
                bool enabled = template.Channel == "EMAIL" ? DataStore.EmailConfig.Enabled : DataStore.WhatsAppConfig.Enabled;
                var m = new QueuedMessage
                {
                    MessageId = DataStore.NextId("MSG"), HospitalId = hospitalId, PatientId = patient.PatientId, PatientName = patient.FullName,
                    PatientPhone = patient.Phone, Channel = template.Channel, TemplateName = template.Name, MessageText = text,
                    Status = enabled ? "SENT" : "SIMULATED", QueuedAt = DateTime.Now, SentAt = DateTime.Now
                };
                DataStore.Messages.Add(m);
                return m;
            }
        }

        private static Dictionary<string, string> BaseVars(Hospital h, Patient p, string doctorName, DateTime date)
        {
            return new Dictionary<string, string>
            {
                { "PatientName", p.FullName }, { "DoctorName", doctorName }, { "HospitalName", h.Name }, { "Phone", h.ContactNumber },
                { "Date", HospitalService.FormatDate(h.HospitalId, date) },
            };
        }

        /// <summary>Reminder for every BOOKED / CHECKED_IN appointment of the doctor on the date. Returns null when the template is disabled.</summary>
        public static int? SendReminders(string hospitalId, string doctorId, DateTime date)
        {
            var tpl = ListTemplates().FirstOrDefault(t => t.Trigger == "APPOINTMENT_REMINDER" && t.IsEnabled);
            if (tpl == null) return null;
            var h = HospitalService.GetHospital(hospitalId);
            int n = 0;
            foreach (var a in SchedulingService.ListAppointments(hospitalId, doctorId, date).Where(a => a.Status == "BOOKED" || a.Status == "CHECKED_IN"))
            {
                if (a.Patient == null) continue;
                var vars = BaseVars(h, a.Patient, a.DoctorName, date);
                vars["Time"] = HospitalService.FormatTime(hospitalId, a.SlotStart);
                Queue(hospitalId, a.Patient, tpl, Render(tpl.Text, vars));
                n++;
            }
            return n;
        }

        /// <summary>Marks every still-BOOKED appointment of the date as NO_SHOW and queues the no-show message.</summary>
        public static int? SendNoShowMessages(string hospitalId, string doctorId, DateTime date, string userId)
        {
            var tpl = ListTemplates().FirstOrDefault(t => t.Trigger == "NO_SHOW" && t.IsEnabled);
            if (tpl == null) return null;
            var h = HospitalService.GetHospital(hospitalId);
            int n = 0;
            foreach (var a in SchedulingService.ListAppointments(hospitalId, doctorId, date).Where(a => a.Status == "BOOKED"))
            {
                if (a.Patient == null) continue;
                SchedulingService.UpdateStatus(a.AppointmentId, "NO_SHOW", userId);
                Queue(hospitalId, a.Patient, tpl, Render(tpl.Text, BaseVars(h, a.Patient, a.DoctorName, date)));
                n++;
            }
            return n;
        }

        public static void SendSingle(string trigger, string hospitalId, Patient patient, string doctorName, IDictionary<string, string> extra)
        {
            var tpl = ListTemplates().FirstOrDefault(t => t.Trigger == trigger && t.IsEnabled);
            if (tpl == null || patient == null) return;
            var vars = BaseVars(HospitalService.GetHospital(hospitalId), patient, doctorName, DateTime.Today);
            if (extra != null) foreach (var kv in extra) vars[kv.Key] = kv.Value;
            Queue(hospitalId, patient, tpl, Render(tpl.Text, vars));
        }

        public static List<QueuedMessage> ListLog(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.Messages.Where(m => m.HospitalId == hospitalId).OrderByDescending(m => m.QueuedAt).ToList();
        }

        public static ChannelConfig WhatsApp { get { return DataStore.WhatsAppConfig; } }
        public static ChannelConfig Email { get { return DataStore.EmailConfig; } }
    }

    public class ExportTable
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public int Count { get; set; }
    }

    /// <summary>CSV export of the in-memory tables and CSV import of patients.</summary>
    public static class ExportService
    {
        private static IList Rows(string key, string hospitalId)
        {
            switch (key)
            {
                case "patients": return DataStore.Patients.Where(x => x.HospitalId == hospitalId).ToList();
                case "appointments": return DataStore.Appointments.Where(x => x.HospitalId == hospitalId).ToList();
                case "consultations": return DataStore.Consultations.Where(x => x.HospitalId == hospitalId).ToList();
                case "prescriptions":
                    var ids = new HashSet<string>(DataStore.Consultations.Where(c => c.HospitalId == hospitalId).Select(c => c.ConsultationId));
                    return DataStore.Prescriptions.Where(p => ids.Contains(p.ConsultationId)).ToList();
                case "invoices": return DataStore.Invoices.Where(x => x.HospitalId == hospitalId).ToList();
                case "payments": return DataStore.Payments.Where(x => x.HospitalId == hospitalId).ToList();
                case "receipts": return DataStore.Receipts.Where(x => x.HospitalId == hospitalId).ToList();
                case "labResults": return DataStore.LabResults.Where(x => x.HospitalId == hospitalId).ToList();
                case "auditLogs": return DataStore.AuditLogs.Where(x => x.HospitalId == hospitalId).ToList();
                default: return null;
            }
        }

        private static readonly string[][] Tables =
        {
            new[] { "patients", "Patients" }, new[] { "appointments", "Appointments" }, new[] { "consultations", "Consultations" },
            new[] { "prescriptions", "Prescriptions" }, new[] { "invoices", "Invoices" }, new[] { "payments", "Payment Transactions" },
            new[] { "receipts", "Receipts" }, new[] { "labResults", "Lab Results" }, new[] { "auditLogs", "Audit Logs" },
        };

        public static List<ExportTable> ListTables(string hospitalId)
        {
            lock (DataStore.Sync)
                return Tables.Select(t => new ExportTable { Key = t[0], Label = t[1], Count = Rows(t[0], hospitalId).Count }).ToList();
        }

        public static string TableLabel(string key)
        {
            var t = Tables.FirstOrDefault(x => x[0] == key);
            return t != null ? t[1] : key;
        }

        /// <summary>Serialises a table's simple (non-collection) properties to CSV; null when empty or unknown.</summary>
        public static string ToCsv(string key, string hospitalId)
        {
            lock (DataStore.Sync)
            {
                var rows = Rows(key, hospitalId);
                if (rows == null || rows.Count == 0) return null;
                var props = rows[0].GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.PropertyType == typeof(string) || p.PropertyType.IsValueType).ToList();
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", props.Select(p => p.Name)));
                foreach (var row in rows)
                    sb.AppendLine(string.Join(",", props.Select(p => Quote(Format(p.GetValue(row))))));
                return sb.ToString();
            }
        }

        private static string Format(object v)
        {
            if (v == null) return "";
            if (v is DateTime) return ((DateTime)v).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            if (v is IFormattable) return ((IFormattable)v).ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }

        private static string Quote(string s) { return "\"" + s.Replace("\"", "\"\"") + "\""; }

        public static readonly string[] PatientRequiredColumns = { "FullName", "Gender", "Dob", "Phone" };

        /// <summary>Splits one CSV line, honouring double-quoted fields.</summary>
        public static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else sb.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { result.Add(sb.ToString().Trim()); sb.Clear(); }
                else sb.Append(ch);
            }
            result.Add(sb.ToString().Trim());
            return result;
        }

        /// <summary>Parses a CSV file into header→value rows, collecting structural and validation errors per row.</summary>
        public static List<Dictionary<string, string>> ParseCsv(Stream stream, out List<string> headers, out List<string> errors)
        {
            var rows = new List<Dictionary<string, string>>();
            errors = new List<string>();
            headers = new List<string>();
            using (var reader = new StreamReader(stream))
            {
                var lines = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
                if (lines.Count < 2) { errors.Add("File must have a header row and at least one data row."); return rows; }
                headers = SplitCsvLine(lines[0]);
                for (int i = 1; i < lines.Count; i++)
                {
                    var vals = SplitCsvLine(lines[i]);
                    if (vals.Count != headers.Count) { errors.Add("Row " + (i + 1) + ": expected " + headers.Count + " columns, got " + vals.Count); continue; }
                    var row = new Dictionary<string, string>();
                    for (int j = 0; j < headers.Count; j++) row[headers[j]] = vals[j];
                    var missing = PatientRequiredColumns.Where(c => !row.ContainsKey(c) || string.IsNullOrWhiteSpace(row[c])).ToList();
                    DateTime dob;
                    if (missing.Count > 0) errors.Add("Row " + (i + 1) + ": Missing " + string.Join(", ", missing));
                    else if (!DateTime.TryParse(row["Dob"], CultureInfo.InvariantCulture, DateTimeStyles.None, out dob)) errors.Add("Row " + (i + 1) + ": Dob is not a valid date");
                    else if (!new[] { "MALE", "FEMALE", "OTHER" }.Contains(row["Gender"].ToUpperInvariant())) errors.Add("Row " + (i + 1) + ": Gender must be MALE, FEMALE or OTHER");
                    else rows.Add(row);
                }
            }
            return rows;
        }

        /// <summary>Registers each valid row as a patient (MRNs are issued as usual).</summary>
        public static int ImportPatients(string hospitalId, List<Dictionary<string, string>> rows)
        {
            Func<Dictionary<string, string>, string, string> get = (r, k) => r.ContainsKey(k) ? r[k] : "";
            foreach (var r in rows)
                PatientService.Create(new Patient
                {
                    HospitalId = hospitalId, FullName = r["FullName"], Gender = r["Gender"].ToUpperInvariant(),
                    Dob = DateTime.Parse(r["Dob"], CultureInfo.InvariantCulture), Phone = r["Phone"], Email = get(r, "Email"),
                    Address = get(r, "Address"), NationalHealthId = get(r, "NationalHealthId"), RegisteredVia = "CSV_IMPORT"
                });
            return rows.Count;
        }
    }
}
