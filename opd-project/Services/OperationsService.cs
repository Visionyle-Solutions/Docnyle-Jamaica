using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

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

    /// <summary>Outgoing message counts by delivery status.</summary>
    public class MessageStats
    {
        public int Total { get; set; }
        public int Sent { get; set; }
        public int Simulated { get; set; }
        public int Failed { get; set; }
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
    public static partial class ReportService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        /// <summary>Inclusive range of hospital-local calendar dates; pass a local date, never a UTC instant.</summary>
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
            return AppointmentsInRange(hid, doctorId, kind, from, to);
        }

        /* Callers hold DataStore.Sync */
        private static List<Appointment> AppointmentsInRange(string hid, string doctorId, string kind, DateTime from, DateTime to)
        {
            return DataStore.Appointments.Where(a => a.HospitalId == hid && (doctorId == null || a.DoctorId == doctorId)
                && MatchesKind(a, kind) && InRange(a.AppointmentDate, from, to)).ToList();
        }

        /// <summary>Appointment Summary with a custom date range (both days inclusive), oldest first.</summary>
        public static List<AppointmentView> AppointmentList(string hid, string doctorId, string kind, DateTime from, DateTime to)
        {
            if (Api) return ApiRange(doctorId, kind, null, from.Date, to.Date);
            lock (DataStore.Sync)
                return SchedulingService.ToViews(AppointmentsInRange(hid, doctorId, kind, from.Date, to.Date).OrderBy(a => a.SlotStart));
        }

        /// <summary>Home dashboard: consultations, appointments and cancelled appointments for today / this week / this month,
        /// plus today's visits by status. A null doctorId counts the whole hospital.</summary>
        public static DashboardStats Dashboard(string hid, string doctorId, DateTime today)
        {
            if (Api) return ApiDashboard(today);
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
            if (Api)
            {
                DateTime from, to;
                PeriodRange(period, today, out from, out to);
                return ApiRange(doctorId, kind, status, from, to);
            }
            lock (DataStore.Sync)
                return SchedulingService.ToViews(DashboardAppointments(hid, doctorId, kind, period, today)
                    .Where(a => status == null || a.Status == status).OrderBy(a => a.SlotStart));
        }

        public static List<CountRow> ConsultationVolume(string hid, DateTime from, DateTime to)
        {
            if (Api)
                return (cnHelperObj.ListConsultationVolumeReport(AppSession.ApiHospitalId, AppSession.ApiUserId, DocNyle.Date(from), DocNyle.Date(to), AppSession.ApiHashValue).EnsureSuccess().Rows ?? new List<RPT_ConsultationVolumeInfo>())
                    .Select(x => new { Date = DocNyle.ParseLocal(x.Date), x.ConsultationCount }).OrderBy(x => x.Date)
                    .Select(x => new CountRow { Label = x.Date.HasValue ? HospitalService.FormatDate(hid, x.Date.Value) : "", Count = x.ConsultationCount }).ToList();
            lock (DataStore.Sync)
                return DataStore.Consultations.Where(c => c.HospitalId == hid && InRange(c.VisitLocalDate, from, to))
                    .GroupBy(c => c.VisitLocalDate).OrderBy(g => g.Key).Select(g => new CountRow { Label = HospitalService.FormatDate(hid, g.Key), Count = g.Count() }).ToList();
        }

        public static List<CountRow> DiagnosisFrequency(string hid, DateTime from, DateTime to)
        {
            if (Api)
                return (cnHelperObj.ListDiagnosisFrequencyReport(AppSession.ApiHospitalId, AppSession.ApiUserId, DocNyle.Date(from), DocNyle.Date(to), AppSession.ApiHashValue).EnsureSuccess().Rows ?? new List<RPT_DiagnosisFrequencyInfo>())
                    .Select(x => new CountRow { Label = x.Icd10Code + " " + x.Description, Count = x.DiagnosisCount }).OrderByDescending(x => x.Count).ToList();
            lock (DataStore.Sync)
                return DataStore.Diagnoses.Join(DataStore.Consultations, d => d.ConsultationId, c => c.ConsultationId, (d, c) => new { d, c })
                    .Where(x => x.c.HospitalId == hid && InRange(x.c.VisitLocalDate, from, to))
                    .GroupBy(x => x.d.Icd10Code + " " + x.d.Icd10Description).Select(g => new CountRow { Label = g.Key, Count = g.Count() })
                    .OrderByDescending(r => r.Count).ToList();
        }

        public static List<CountRow> Prescribing(string hid, DateTime from, DateTime to)
        {
            if (Api)
                return (cnHelperObj.ListPrescribingReport(AppSession.ApiHospitalId, AppSession.ApiUserId, DocNyle.Date(from), DocNyle.Date(to), AppSession.ApiHashValue).EnsureSuccess().Rows ?? new List<RPT_PrescribingInfo>())
                    .Select(x => new CountRow { Label = x.MedicineName, Count = x.PrescriptionCount }).OrderByDescending(x => x.Count).ToList();
            lock (DataStore.Sync)
                return DataStore.Prescriptions.Join(DataStore.Consultations, p => p.ConsultationId, c => c.ConsultationId, (p, c) => new { p, c })
                    .Where(x => x.c.HospitalId == hid && InRange(x.c.VisitLocalDate, from, to))
                    .GroupBy(x => x.p.MedicineName).Select(g => new CountRow { Label = g.Key, Count = g.Count() })
                    .OrderByDescending(r => r.Count).ToList();
        }

        public static List<CountRow> AppointmentStatus(string hid, DateTime from, DateTime to, out int total, out int noShowRate)
        {
            if (Api)
            {
                var rep = cnHelperObj.ListNoShowReport(AppSession.ApiHospitalId, AppSession.ApiUserId, DocNyle.Date(from), DocNyle.Date(to), AppSession.ApiHashValue).EnsureSuccess().Report ?? new RPT_NoShowReportInfo();
                total = rep.Total;
                noShowRate = rep.NoShowRate;
                return (rep.ByStatus ?? new List<RPT_StatusCountInfo>()).Select(x => new CountRow { Label = x.Status, Count = x.StatusCount })
                    .OrderByDescending(x => x.Count).ToList();
            }
            lock (DataStore.Sync)
            {
                var appts = DataStore.Appointments.Where(a => a.HospitalId == hid && InRange(a.AppointmentDate, from, to)).ToList();
                total = appts.Count;
                noShowRate = total > 0 ? (int)Math.Round(appts.Count(a => a.Status == "NO_SHOW") * 100.0 / total) : 0;
                return appts.GroupBy(a => a.Status).Select(g => new CountRow { Label = g.Key, Count = g.Count() }).OrderByDescending(r => r.Count).ToList();
            }
        }

        public static List<RevenueRow> Revenue(string hid, DateTime from, DateTime to)
        {
            if (Api)
                return (cnHelperObj.ListRevenueReport(AppSession.ApiHospitalId, AppSession.ApiUserId, DocNyle.Date(from), DocNyle.Date(to), AppSession.ApiHashValue).EnsureSuccess().Rows ?? new List<RPT_RevenueInfo>()).Select(x => new RevenueRow
                {
                    DoctorName = UserService.UserName(x.DoctorId), Invoiced = x.TotalInvoiced, Paid = x.TotalPaid, Count = x.InvoiceCount,
                }).ToList();
            lock (DataStore.Sync)
                return DataStore.Invoices.Where(i => i.HospitalId == hid && i.Status != "CANCELLED" && InRange(TimeZoneService.LocalDate(i.CreatedAt, TimeZoneService.HospitalZoneId(hid)), from, to))
                    .GroupBy(i => i.DoctorId).Select(g => new RevenueRow
                    {
                        DoctorName = UserService.UserName(g.Key), Invoiced = g.Sum(i => i.TotalAmount), Paid = g.Sum(i => i.PaidAmount), Count = g.Count()
                    }).ToList();
        }

        private static bool Api { get { return DataSource.IsApi("Operations"); } }

        /// <summary>GetAppointmentDashboard — the service scopes a doctor-only caller to their own appointments.</summary>
        private static DashboardStats ApiDashboard(DateTime today)
        {
            var d = cnHelperObj.GetAppointmentDashboard(AppSession.ApiHospitalId, AppSession.ApiUserId, DocNyle.Date(today), AppSession.ApiHashValue).EnsureSuccess().Dashboard ?? new SCH_AppointmentDashboardInfo();
            var counts = d.Counts ?? new List<SCH_DashboardCountInfo>();
            Func<string, PeriodCounts> pick = kind =>
            {
                var c = counts.FirstOrDefault(x => string.Equals(x.Kind, kind, StringComparison.OrdinalIgnoreCase));
                return c == null ? new PeriodCounts() : new PeriodCounts { Today = c.TodayCount, Week = c.WeekCount, Month = c.MonthCount };
            };
            return new DashboardStats
            {
                Consultations = pick("consultations"), Appointments = pick("appointments"), Cancelled = pick("cancelled"),
                TodayStatus = (d.TodayByStatus ?? new List<SCH_StatusCountInfo>()).Select(s => new CountRow { Label = s.Status, Count = s.Count })
                    .OrderByDescending(r => r.Count).ToList(),
            };
        }

        private static List<AppointmentView> ApiRange(string doctorId, string kind, string status, DateTime from, DateTime to)
        {
            return (cnHelperObj.ListAppointmentsForRange(AppSession.ApiHospitalId, AppSession.ApiUserId, DocNyle.Date(from), DocNyle.Date(to), doctorId, kind, status, AppSession.ApiHashValue).EnsureSuccess().Appointments ?? new List<SCH_AppointmentInfo>())
                .Select(SchedulingService.ToView).OrderBy(v => v.Appointment.SlotStart).ToList();
        }
    }

    /// <summary>Message templates, channel configuration and the outgoing message queue (simulated delivery).</summary>
    public static partial class NotificationService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        public static List<MessageTemplate> ListTemplates()
        {
            if (Api) return ApiTemplates();
            lock (DataStore.Sync) return DataStore.MessageTemplates.ToList();
        }

        public static MessageTemplate GetTemplate(string templateId)
        {
            if (Api) return ApiTemplates().FirstOrDefault(t => t.TemplateId == templateId);
            lock (DataStore.Sync) return DataStore.MessageTemplates.FirstOrDefault(t => t.TemplateId == templateId);
        }

        public static void UpdateTemplate(string templateId, string name, string channel, string text)
        {
            if (Api)
            {
                cnHelperObj.UpdateMessageTemplate(new MSG_UpdateMessageTemplateReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, MessageTemplateId = templateId, Name = name, Channel = channel, Text = text }).EnsureSuccess();
                RequestCache.Remove("msg-templates");
                return;
            }
            lock (DataStore.Sync)
            {
                var t = GetTemplate(templateId);
                if (t == null) return;
                t.Name = name; t.Channel = channel; t.Text = text;
            }
        }

        public static void SetTemplateEnabled(string templateId, bool enabled)
        {
            if (Api)
            {
                // UpdateMessageTemplateStatus toggles, so it is only called when the state differs.
                var t = GetTemplate(templateId);
                if (t == null || t.IsEnabled == enabled) return;
                cnHelperObj.UpdateMessageTemplateStatus(new MSG_UpdateMessageTemplateStatusReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, MessageTemplateId = templateId }).EnsureSuccess();
                RequestCache.Remove("msg-templates");
                return;
            }
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
            if (Api) return ApiQueue(patient, template, text);
            lock (DataStore.Sync)
            {
                bool enabled = template.Channel == "EMAIL" ? DataStore.EmailConfig.Enabled : DataStore.WhatsAppConfig.Enabled;
                var m = new QueuedMessage
                {
                    MessageId = DataStore.NextId("MSG"), HospitalId = hospitalId, PatientId = patient.PatientId, PatientName = patient.FullName,
                    PatientPhone = patient.Phone, Channel = template.Channel, TemplateName = template.Name, MessageText = text,
                    Status = enabled ? "SENT" : "SIMULATED", QueuedAt = DateTime.UtcNow, SentAt = DateTime.UtcNow
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
                vars["Time"] = HospitalService.FormatTime(hospitalId, a.SlotStart);   // AppointmentView.SlotStart is the clinic's wall clock
                Queue(hospitalId, a.Patient, tpl, Render(tpl.Text, vars));
                n++;
            }
            if (Api && n > 0) cnHelperObj.SubmitQueuedMessages(new MSG_SubmitQueuedMessagesReq() { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue }).EnsureSuccess();
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
            if (Api && n > 0) cnHelperObj.SubmitQueuedMessages(new MSG_SubmitQueuedMessagesReq() { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue }).EnsureSuccess();
            return n;
        }

        public static void SendSingle(string trigger, string hospitalId, Patient patient, string doctorName, IDictionary<string, string> extra)
        {
            var tpl = ListTemplates().FirstOrDefault(t => t.Trigger == trigger && t.IsEnabled);
            if (tpl == null || patient == null) return;
            var vars = BaseVars(HospitalService.GetHospital(hospitalId), patient, doctorName, TimeZoneService.Today(hospitalId));
            if (extra != null) foreach (var kv in extra) vars[kv.Key] = kv.Value;
            Queue(hospitalId, patient, tpl, Render(tpl.Text, vars));
        }

        public static List<QueuedMessage> ListLog(string hospitalId)
        {
            if (Api)
                return (cnHelperObj.ListMessageLog(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Messages ?? new List<MSG_QueuedMessageInfo>()).Select(m => new QueuedMessage
                {
                    MessageId = m.MessageId, HospitalId = hospitalId, PatientId = m.PatientId, PatientName = m.PatientName, Channel = m.Channel,
                    TemplateName = m.TemplateName, Status = m.Status, QueuedAt = DocNyle.InstantOrMin(m.QueuedAt), SentAt = DocNyle.ParseInstant(m.SentAt),
                }).OrderByDescending(m => m.QueuedAt).ToList();
            lock (DataStore.Sync) return DataStore.Messages.Where(m => m.HospitalId == hospitalId).OrderByDescending(m => m.QueuedAt).ToList();
        }

        /// <summary>Delivery counts for the message log header: GetMessageStats in API mode, else counted from <paramref name="log"/>.</summary>
        public static MessageStats Stats(List<QueuedMessage> log)
        {
            if (Api)
            {
                var s = cnHelperObj.GetMessageStats(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Stats ?? new MSG_MessageStatsInfo();
                return new MessageStats { Total = s.Total, Sent = s.Sent, Simulated = s.Simulated, Failed = s.Failed };
            }
            return new MessageStats
            {
                Total = log.Count, Sent = log.Count(m => m.Status == "SENT"), Simulated = log.Count(m => m.Status == "SIMULATED"),
                Failed = log.Count(m => m.Status == "FAILED"),
            };
        }

        public static ChannelConfig WhatsApp { get { return Api ? ApiChannel("WHATSAPP") : DataStore.WhatsAppConfig; } }
        public static ChannelConfig Email { get { return Api ? ApiChannel("EMAIL") : DataStore.EmailConfig; } }

        /// <summary>
        /// Saves a channel's settings (SaveNotificationConfig in API mode). A blank <see cref="ChannelConfig.ApiKey"/> keeps the stored key.
        /// </summary>
        public static void SaveChannelConfig(string channel, ChannelConfig values)
        {
            if (Api)
            {
                cnHelperObj.SaveNotificationConfig(new MSG_SaveNotificationConfigReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    Channel = channel, Provider = values.Provider, ApiKey = string.IsNullOrEmpty(values.ApiKey) ? null : values.ApiKey,
                    PhoneNumberId = values.PhoneNumberId, BusinessAccountId = values.BusinessAccountId, SmtpHost = values.SmtpHost,
                    SmtpPort = channel == "EMAIL" ? values.SmtpPort : (int?)null, FromEmail = values.FromEmail, FromName = values.FromName, Enabled = values.Enabled,
                }).EnsureSuccess();
                RequestCache.Remove("msg-config");
                return;
            }
            lock (DataStore.Sync)
            {
                var c = channel == "EMAIL" ? DataStore.EmailConfig : DataStore.WhatsAppConfig;
                c.Provider = values.Provider; c.PhoneNumberId = values.PhoneNumberId; c.BusinessAccountId = values.BusinessAccountId;
                c.SmtpHost = values.SmtpHost; c.SmtpPort = values.SmtpPort; c.FromEmail = values.FromEmail; c.FromName = values.FromName; c.Enabled = values.Enabled;
                if (!string.IsNullOrEmpty(values.ApiKey)) c.ApiKey = values.ApiKey;
            }
        }

        private static bool Api { get { return DataSource.IsApi("Operations"); } }

        /// <summary>ListMessageTemplates needs admin_hospital; for other callers no template is available (null list).</summary>
        private static List<MessageTemplate> ApiTemplates()
        {
            return RequestCache.Get("msg-templates", () =>
            {
                try
                {
                    return (cnHelperObj.ListMessageTemplates(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Templates ?? new List<MSG_MessageTemplateInfo>()).Select(t => new MessageTemplate
                    {
                        TemplateId = t.MessageTemplateId, Name = t.Name, Trigger = t.TriggerEvent, Channel = t.Channel, IsEnabled = t.IsEnabled, Text = t.TemplateText,
                    }).ToList();
                }
                catch (ApiException ex) when (ex.Kind == ApiFaultKind.Forbidden)
                {
                    return new List<MessageTemplate>();
                }
            });
        }

        /// <summary>GetNotificationConfig never returns the API key — only whether one is stored (HasApiKey).</summary>
        private static ChannelConfig ApiChannel(string channel)
        {
            var rows = RequestCache.Get("msg-config", () => cnHelperObj.GetNotificationConfig(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Configs ?? new List<MSG_ChannelConfigInfo>());
            var c = rows.FirstOrDefault(x => string.Equals(x.Channel, channel, StringComparison.OrdinalIgnoreCase));
            if (c == null) return new ChannelConfig { Provider = "", SmtpPort = 587 };
            return new ChannelConfig
            {
                Provider = c.Provider, ApiKey = null, HasApiKey = c.HasApiKey, PhoneNumberId = c.PhoneNumberId, BusinessAccountId = c.BusinessAccountId,
                SmtpHost = c.SmtpHost, SmtpPort = c.SmtpPort ?? 587, FromEmail = c.FromEmail, FromName = c.FromName, Enabled = c.IsEnabled,
            };
        }

        private static QueuedMessage ApiQueue(Patient patient, MessageTemplate template, string text)
        {
            var m = cnHelperObj.CreateQueuedMessage(new MSG_CreateQueuedMessageReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                PatientId = patient.PatientId, Channel = template.Channel, TemplateName = template.Name, PatientName = patient.FullName,
                PatientPhone = string.IsNullOrEmpty(patient.Phone) ? null : patient.Phone, MessageText = text,
            }).EnsureSuccess().QueuedMessage;
            return m == null ? null : new QueuedMessage
            {
                MessageId = m.MessageId, PatientId = m.PatientId, PatientName = m.PatientName, Channel = m.Channel, TemplateName = m.TemplateName,
                MessageText = text, Status = m.Status, QueuedAt = DocNyle.InstantOrMin(m.QueuedAt), SentAt = DocNyle.ParseInstant(m.SentAt),
            };
        }
    }

    public class ExportTable
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public int Count { get; set; }
    }

    /// <summary>CSV export of the in-memory tables and CSV import of patients.</summary>
    public static partial class ExportService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

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
            if (Api)
                return (cnHelperObj.ListExportTables(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().ExportTables ?? new List<SEC_ExportTableInfo>())
                    .Select(t => new ExportTable { Key = t.TableKey, Label = TableLabel(t.TableKey), Count = t.RowCount }).ToList();
            lock (DataStore.Sync)
                return Tables.Select(t => new ExportTable { Key = t[0], Label = t[1], Count = Rows(t[0], hospitalId).Count }).ToList();
        }

        public static string TableLabel(string key)
        {
            var t = Tables.FirstOrDefault(x => x[0] == key);
            if (t == null && key == "paymentTransactions") return "Payment Transactions";   // the API's key for payments
            return t != null ? t[1] : key;
        }

        /// <summary>Serialises a table's simple (non-collection) properties to CSV; null when empty or unknown.</summary>
        public static string ToCsv(string key, string hospitalId)
        {
            if (Api) return ApiCsv(key);
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
            if (Api) return ApiImport(rows);
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

        private static bool Api { get { return DataSource.IsApi("Operations"); } }

        /// <summary>ListExportData returns the CSV built by the service (UTF-8 with BOM, base64).</summary>
        private static string ApiCsv(string key)
        {
            var f = DocNyle.OrNull(() => cnHelperObj.ListExportData(AppSession.ApiHospitalId, AppSession.ApiUserId, key, AppSession.ApiHashValue).EnsureSuccess())?.File;
            if (f == null || string.IsNullOrEmpty(f.ContentBase64)) return null;
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(f.ContentBase64)).TrimStart('﻿');
            // Header row only = no data.
            return text.Replace("\r\n", "\n").Split('\n').Count(l => l.Trim().Length > 0) < 2 ? null : text;
        }

        /// <summary>CreatePatientsBatch — every row or none (RegisteredVia CSV_IMPORT); a bad row comes back as fault 50204 naming it.</summary>
        private static int ApiImport(List<Dictionary<string, string>> rows)
        {
            Func<Dictionary<string, string>, string, string> get = (r, k) => r.ContainsKey(k) && !string.IsNullOrWhiteSpace(r[k]) ? r[k].Trim() : null;
            var created = cnHelperObj.CreatePatientsBatch(new PAT_CreatePatientsBatchReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                Rows = rows.Select(r => new PAT_ImportPatientRowInfo
                {
                    FullName = r["FullName"], Gender = r["Gender"].ToUpperInvariant(),
                    Dob = DocNyle.Date(DateTime.Parse(r["Dob"], CultureInfo.InvariantCulture)), Phone = r["Phone"],
                    Email = get(r, "Email"), Address = get(r, "Address"), NationalHealthId = get(r, "NationalHealthId"),
                }).ToList(),
            }).EnsureSuccess().Patients;
            return created != null ? created.Count : rows.Count;
        }
    }
}
