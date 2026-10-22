using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Caching;
using System.Text.RegularExpressions;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Services
{
    /// <summary>
    /// Appointments, the slot engine (weekly windows, leave, slot length, buffer), doctor profiles,
    /// per-doctor overrides and charges, appointment types, intake questions and consent.
    /// </summary>
    public static partial class SchedulingService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        public static readonly string[] DayCodes = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
        public static readonly string[] WeekDays = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        public static readonly string[] DoctorOverridable = { "APPT_SLOT_MINS", "FREE_FOLLOWUP_ALLOWED", "FREE_FOLLOWUP_DAYS", "CHARGE_MODEL", "CHARGE_UNIT_MINS", "CHARGE_UNIT_RATE" };

        /// <summary>Allowed lifecycle moves — mirrors opd.usp_UpdateAppointmentStatus.</summary>
        private static readonly Dictionary<string, string[]> Transitions = new Dictionary<string, string[]>
        {
            { "BOOKED", new[] { "CHECKED_IN", "CANCELLED", "NO_SHOW" } },
            { "CHECKED_IN", new[] { "IN_CONSULT", "CANCELLED", "NO_SHOW" } },
            { "IN_CONSULT", new[] { "COMPLETED" } },
            { "COMPLETED", new string[0] }, { "CANCELLED", new string[0] }, { "NO_SHOW", new string[0] },
        };

        public static readonly Dictionary<string, string> StatusLabels = new Dictionary<string, string>
        {
            { "BOOKED", "Booked" }, { "CHECKED_IN", "Checked In" }, { "IN_CONSULT", "In Consult" },
            { "COMPLETED", "Completed" }, { "CANCELLED", "Cancelled" }, { "NO_SHOW", "No-Show" },
        };

        public static string DayCode(DateTime date) { return DayCodes[(int)date.DayOfWeek]; }

        private static int ToMinutes(string hhmm)
        {
            var p = hhmm.Split(':');
            return int.Parse(p[0]) * 60 + int.Parse(p[1]);
        }

        /* ── Doctor display names ── */

        /// <summary>Display-name template tokens: {prefix} {first} {middle} {last} {finitial} {minitial} {suffix} {suffixComma}.</summary>
        public static string RenderDoctorName(AppUser u, string template)
        {
            if (!string.IsNullOrWhiteSpace(u.DisplayNameOverride)) return u.DisplayNameOverride.Trim();
            if (string.IsNullOrEmpty(u.FirstName) && string.IsNullOrEmpty(u.LastName)) return u.FullName;
            var q = u.Qualifications ?? "";
            var s = (string.IsNullOrEmpty(template) ? "{prefix} {first} {last}" : template)
                .Replace("{prefix}", u.Prefix ?? "").Replace("{first}", u.FirstName ?? "")
                .Replace("{middle}", u.MiddleName ?? "").Replace("{last}", u.LastName ?? "")
                .Replace("{finitial}", string.IsNullOrEmpty(u.FirstName) ? "" : u.FirstName.Substring(0, 1))
                .Replace("{minitial}", string.IsNullOrEmpty(u.MiddleName) ? "" : u.MiddleName.Substring(0, 1))
                .Replace("{suffixComma}", q.Length > 0 ? ", " + q : "").Replace("{suffix}", q);
            s = Regex.Replace(s, @"\s+", " ");
            s = Regex.Replace(s, @"\s+,", ",");
            return s.Trim();
        }

        /// <summary>In API mode SaveHospitalSettings re-renders the names itself.</summary>
        public static void RefreshDoctorNames(string hospitalId)
        {
            if (Api) { HospitalCache.Clear("doctors"); HospitalCache.Clear("users"); return; }
            lock (DataStore.Sync)
            {
                var tpl = HospitalService.GetSetting(hospitalId, "DOCTOR_NAME_TEMPLATE");
                foreach (var u in DataStore.Users.Where(x => x.HospitalId == hospitalId && x.HasRole("DOCTOR")))
                    u.FullName = RenderDoctorName(u, tpl);
            }
        }

        /* ── Settings resolution (doctor override → hospital) ── */

        public static ResolvedSetting ResolveSetting(string hospitalId, string doctorId, string key)
        {
            if (Api)
            {
                ResolvedSetting s;
                if (doctorId != null && DoctorOverridable.Contains(key) && ApiDoctorSettings(doctorId).TryGetValue(key, out s)) return s;
                var hv = HospitalService.GetSetting(hospitalId, key);
                return new ResolvedSetting { Value = hv, Source = hv != null ? "HOSPITAL" : "NONE" };
            }
            lock (DataStore.Sync)
            {
                if (doctorId != null)
                {
                    var o = DataStore.DoctorSettings.FirstOrDefault(x => x.HospitalId == hospitalId && x.DoctorId == doctorId && x.SettingKey == key);
                    if (o != null) return new ResolvedSetting { Value = o.Value, Source = "DOCTOR" };
                }
                var v = HospitalService.GetSetting(hospitalId, key);
                return new ResolvedSetting { Value = v, Source = v != null ? "HOSPITAL" : "NONE" };
            }
        }

        public static int SlotMinutes(string hospitalId, string doctorId)
        {
            int v;
            return int.TryParse(ResolveSetting(hospitalId, doctorId, "APPT_SLOT_MINS").Value, out v) && v > 0 ? v : 15;
        }

        public static int BufferMinutes(string hospitalId)
        {
            int v;
            return int.TryParse(HospitalService.GetSetting(hospitalId, "APPT_BUFFER_MINS"), out v) ? v : 0;
        }

        public static Dictionary<string, ResolvedSetting> GetDoctorSettings(string hospitalId, string doctorId)
        {
            return DoctorOverridable.ToDictionary(k => k, k => ResolveSetting(hospitalId, doctorId, k));
        }

        /// <summary>Saves doctor overrides; an empty value removes the override (inherit the hospital value).</summary>
        public static void SaveDoctorSettings(string hospitalId, string doctorId, IDictionary<string, string> values)
        {
            if (Api) { ApiSaveDoctorSettings(doctorId, values); return; }
            lock (DataStore.Sync)
            {
                foreach (var kv in values)
                {
                    if (!DoctorOverridable.Contains(kv.Key)) continue;
                    DataStore.DoctorSettings.RemoveAll(x => x.HospitalId == hospitalId && x.DoctorId == doctorId && x.SettingKey == kv.Key);
                    if (!string.IsNullOrEmpty(kv.Value))
                        DataStore.DoctorSettings.Add(new DoctorSetting { HospitalId = hospitalId, DoctorId = doctorId, SettingKey = kv.Key, Value = kv.Value });
                }
            }
        }

        /* ── Appointments ── */

        public static Appointment GetAppointment(string appointmentId)
        {
            if (Api)
                return string.IsNullOrEmpty(appointmentId) ? null
                    : RequestCache.Get("appt:" + appointmentId, () => ToModel(DocNyle.OrNull(() => cnHelperObj.GetAppointment(AppSession.ApiHospitalId, AppSession.ApiUserId, appointmentId, AppSession.ApiHashValue).EnsureSuccess())?.Appointment));
            lock (DataStore.Sync) return DataStore.Appointments.FirstOrDefault(a => a.AppointmentId == appointmentId);
        }

        /// <summary>Patient and doctor details for each appointment. Callers hold DataStore.Sync.</summary>
        public static List<AppointmentView> ToViews(IEnumerable<Appointment> appointments)
        {
            return appointments.Select(ToView).ToList();
        }

        private static AppointmentView ToView(Appointment a)
        {
            var doctor = DataStore.Users.FirstOrDefault(u => u.UserId == a.DoctorId);
            return new AppointmentView
            {
                Appointment = a,
                Patient = DataStore.Patients.FirstOrDefault(p => p.PatientId == a.PatientId),
                DoctorName = doctor != null ? doctor.FullName : "",
                DoctorRoom = doctor != null ? doctor.Room ?? "" : "",
            };
        }

        /// <summary>One doctor's appointments for a date (null doctor = every doctor of the hospital).</summary>
        public static List<AppointmentView> ListAppointments(string hospitalId, string doctorId, DateTime date)
        {
            if (Api) return ApiListAppointments(doctorId, date);
            lock (DataStore.Sync)
                return DataStore.Appointments
                    .Where(a => a.HospitalId == hospitalId && a.AppointmentDate == date.Date && (doctorId == null || a.DoctorId == doctorId))
                    .OrderBy(a => a.SlotStart).Select(ToView).ToList();
        }

        public static List<AppointmentView> ListAppointmentsForPatient(string patientId)
        {
            if (Api)
                return (cnHelperObj.ListAppointmentsForPatient(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess().Appointments ?? new List<SCH_AppointmentInfo>())
                    .Select(ToView).OrderByDescending(v => v.Appointment.SlotStart).ToList();
            lock (DataStore.Sync)
                return DataStore.Appointments.Where(a => a.PatientId == patientId).OrderByDescending(a => a.SlotStart).Select(ToView).ToList();
        }

        /// <summary>
        /// Books a slot. <paramref name="start"/> / <paramref name="end"/> are wall-clock times in the hospital's timezone (a
        /// TimeSlot, or what the receptionist picked); the appointment stores them as UTC with the hospital's TimeZoneId.
        /// </summary>
        public static Appointment CreateAppointment(string hospitalId, string patientId, string doctorId, DateTime start, DateTime end, string type, string notes)
        {
            if (Api) return ApiCreateAppointment(patientId, doctorId, start, end, type, notes);
            var zone = TimeZoneService.HospitalZoneId(hospitalId);
            var row = new Appointment
            {
                AppointmentId = DataStore.NextId("APT"), HospitalId = hospitalId, PatientId = patientId, DoctorId = doctorId,
                SlotStart = TimeZoneService.ToUtc(start, zone), SlotEnd = TimeZoneService.ToUtc(end, zone),
                TimeZoneId = zone, AppointmentDate = start.Date, Type = type, Status = "BOOKED", Notes = notes ?? ""
            };
            lock (DataStore.Sync) DataStore.Appointments.Add(row);
            return row;
        }

        /// <summary>Applies a lifecycle move. Returns null (and changes nothing) for a move the lifecycle does not allow.</summary>
        public static Appointment UpdateStatus(string appointmentId, string status, string modifiedBy)
        {
            if (Api)
            {
                RequestCache.Remove("appt:" + appointmentId);
                return ApiUpdateStatus(appointmentId, status);
            }
            lock (DataStore.Sync)
            {
                var a = DataStore.Appointments.FirstOrDefault(x => x.AppointmentId == appointmentId);
                if (a == null) return null;
                if (a.Status == status) return a;
                string[] allowed;
                if (!Transitions.TryGetValue(a.Status, out allowed) || !allowed.Contains(status)) return null;
                a.Status = status;
                if (status == "CHECKED_IN") { a.CheckedInAt = DateTime.UtcNow; a.CheckedInBy = modifiedBy; }
                if (status == "IN_CONSULT") a.ConsultStartedAt = DateTime.UtcNow;
                if (status == "COMPLETED") a.CompletedAt = DateTime.UtcNow;
                a.ModifiedAt = DateTime.UtcNow;
                a.ModifiedBy = modifiedBy;
                return a;
            }
        }

        /// <summary>Free, not-yet-started slots on the date with the same doctor and the same length as the appointment being moved.</summary>
        public static List<TimeSlot> ListRescheduleSlots(Appointment appt, DateTime date)
        {
            var minutes = (int)(appt.SlotEnd - appt.SlotStart).TotalMinutes;
            var now = TimeZoneService.Now(appt.HospitalId);   // slots are on the clinic's wall clock
            return ListAvailableSlots(appt.DoctorId, appt.HospitalId, date, minutes).Where(s => !s.IsBooked && s.SlotStart > now).ToList();
        }

        /// <summary>
        /// Cancels the appointment and re-books the same patient at the chosen start time. Checked and booked under one
        /// lock, so a slot taken by someone else since the list was shown is refused (returns null, nothing changes).
        /// </summary>
        public static Appointment Reschedule(string appointmentId, DateTime newStart, string modifiedBy)
        {
            if (Api) return ApiReschedule(appointmentId, newStart);
            lock (DataStore.Sync)
            {
                var orig = GetAppointment(appointmentId);
                if (orig == null || orig.Status != "BOOKED") return null;
                var slot = ListRescheduleSlots(orig, newStart.Date).FirstOrDefault(s => s.SlotStart == newStart);
                if (slot == null) return null;
                UpdateStatus(appointmentId, "CANCELLED", modifiedBy);
                return CreateAppointment(orig.HospitalId, orig.PatientId, orig.DoctorId, slot.SlotStart, slot.SlotEnd, orig.Type, "Rescheduled");
            }
        }

        /// <summary>Books a series at the first free slot on each date; returns how many were booked.</summary>
        public static int BookRecurring(string hospitalId, string patientId, string doctorId, DateTime start, int everyDays, int count, string type)
        {
            if (Api) return ApiBookRecurring(patientId, doctorId, start, everyDays, count, type);
            var t = GetAppointmentType(hospitalId, type);
            int booked = 0;
            for (int i = 0; i < count; i++)
            {
                var date = start.Date.AddDays(i * everyDays);
                var slot = ListAvailableSlots(doctorId, hospitalId, date, t != null ? t.DurationMinutes : null).FirstOrDefault(s => !s.IsBooked);
                if (slot == null) continue;
                CreateAppointment(hospitalId, patientId, doctorId, slot.SlotStart, slot.SlotEnd, type, "Recurring " + (i + 1) + "/" + count);
                booked++;
            }
            return booked;
        }

        /// <summary>
        /// Slot engine: weekly windows for the weekday (none = closed), nothing on leave days, slot length =
        /// the appointment type's duration or the doctor's APPT_SLOT_MINS → hospital default, stepping by
        /// slot + APPT_BUFFER_MINS. IsBooked = overlaps a non-cancelled appointment (+ buffer).
        /// </summary>
        public static List<TimeSlot> ListAvailableSlots(string doctorId, string hospitalId, DateTime date, int? durationMinutes)
        {
            if (Api) return ApiListAvailableSlots(doctorId, date, durationMinutes);
            lock (DataStore.Sync)
            {
                var day = date.Date;
                if (DataStore.Leave.Any(l => l.DoctorId == doctorId && l.HospitalId == hospitalId && l.LeaveDate.Date == day)) return new List<TimeSlot>();
                var code = DayCode(day);
                var windows = DataStore.SlotConfig.Where(w => w.DoctorId == doctorId && w.IsActive && w.DayOfWeek == code && w.HospitalId == hospitalId)
                    .OrderBy(w => w.StartTime).ToList();
                var slots = new List<TimeSlot>();
                if (windows.Count == 0) return slots;
                int slot = durationMinutes.HasValue && durationMinutes.Value > 0 ? durationMinutes.Value : SlotMinutes(hospitalId, doctorId);
                int buffer = BufferMinutes(hospitalId);
                // Windows and slots are the clinic's wall clock; booked appointments (UTC) are compared in the same terms.
                var taken = DataStore.Appointments.Where(a => a.DoctorId == doctorId && a.AppointmentDate == day && a.Status != "CANCELLED")
                    .Select(a => new { Start = (int)a.SlotStartLocal.TimeOfDay.TotalMinutes, End = (int)a.SlotEndLocal.TimeOfDay.TotalMinutes + buffer }).ToList();
                foreach (var w in windows)
                {
                    int wEnd = ToMinutes(w.EndTime);
                    for (int m = ToMinutes(w.StartTime); m + slot <= wEnd; m += slot + buffer)
                    {
                        int end = m + slot;
                        slots.Add(new TimeSlot
                        {
                            SlotStart = day.AddMinutes(m), SlotEnd = day.AddMinutes(end),
                            IsBooked = taken.Any(t => m < t.End && end + buffer > t.Start),
                        });
                    }
                }
                return slots;
            }
        }

        public static DoctorSchedule GetDoctorSchedule(string doctorId, string hospitalId, DateTime date)
        {
            if (Api) return ApiDoctorSchedule(doctorId, date);
            lock (DataStore.Sync)
            {
                var code = DayCode(date);
                var leave = DataStore.Leave.FirstOrDefault(l => l.DoctorId == doctorId && l.HospitalId == hospitalId && l.LeaveDate.Date == date.Date);
                var weekly = DataStore.SlotConfig.Where(w => w.DoctorId == doctorId && w.IsActive && w.HospitalId == hospitalId).ToList();
                return new DoctorSchedule
                {
                    DoctorId = doctorId, Date = date.Date, DayOfWeek = code,
                    Windows = weekly.Where(w => w.DayOfWeek == code).OrderBy(w => w.StartTime).Select(w => new TimeWindow { StartTime = w.StartTime, EndTime = w.EndTime }).ToList(),
                    WorkingDays = WeekDays.Where(d => weekly.Any(w => w.DayOfWeek == d)).ToList(),
                    SlotMinutes = SlotMinutes(hospitalId, doctorId),
                    BufferMinutes = BufferMinutes(hospitalId),
                    OnLeave = leave != null, LeaveReason = leave != null ? leave.Reason : null,
                };
            }
        }

        private static readonly Dictionary<string, string> DayNames = new Dictionary<string, string>
        {
            { "MON", "Mondays" }, { "TUE", "Tuesdays" }, { "WED", "Wednesdays" }, { "THU", "Thursdays" }, { "FRI", "Fridays" }, { "SAT", "Saturdays" }, { "SUN", "Sundays" },
        };

        /// <summary>Why a date has no free slots — leave, closed weekday, or simply full.</summary>
        public static string NoSlotReason(DoctorSchedule s)
        {
            if (s.OnLeave) return "On leave (" + (s.LeaveReason ?? "leave") + ")";
            if (s.WorkingDays.Count == 0) return "No weekly timetable set (Schedule Settings → Doctors)";
            if (s.Windows.Count == 0) return "Doesn't consult on " + (DayNames.ContainsKey(s.DayOfWeek) ? DayNames[s.DayOfWeek] : "this day");
            return "All slots are booked";
        }

        /// <summary>"Mon–Sat" or "Mon, Wed, Fri" summary of working days.</summary>
        public static string DaysSummary(List<string> codes)
        {
            if (codes == null || codes.Count == 0) return "No timetable";
            var idx = codes.Select(c => Array.IndexOf(WeekDays, c)).OrderBy(i => i).ToList();
            bool contiguous = idx.Select((v, i) => i == 0 || v == idx[i - 1] + 1).All(b => b);
            Func<int, string> label = i => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(WeekDays[i].ToLowerInvariant());
            if (contiguous && idx.Count >= 3) return label(idx[0]) + "–" + label(idx[idx.Count - 1]);
            return string.Join(", ", idx.Select(label));
        }

        /* ── Weekly availability & leave ── */

        public static List<DoctorSlotConfig> ListSlotConfig(string doctorId, string hospitalId)
        {
            if (Api) return ApiSlotConfig(doctorId);
            lock (DataStore.Sync)
                return DataStore.SlotConfig.Where(s => s.DoctorId == doctorId && s.HospitalId == hospitalId && s.IsActive)
                    .OrderBy(s => Array.IndexOf(WeekDays, s.DayOfWeek)).ThenBy(s => s.StartTime).ToList();
        }

        public static void AddSlotWindow(string doctorId, string hospitalId, string day, string start, string end)
        {
            if (Api)
            {
                ApiSaveSlotConfig(doctorId, ApiSlotConfig(doctorId).Concat(new[] { new DoctorSlotConfig { DayOfWeek = day, StartTime = start, EndTime = end } }));
                return;
            }
            lock (DataStore.Sync)
                DataStore.SlotConfig.Add(new DoctorSlotConfig { SlotConfigId = DataStore.NextId("DSC"), DoctorId = doctorId, HospitalId = hospitalId, DayOfWeek = day, StartTime = start, EndTime = end, IsActive = true });
        }

        /// <summary>Removes one weekly window. API mode re-saves the doctor's remaining windows (SaveDoctorSlotConfig replaces them all).</summary>
        public static void RemoveSlotWindow(string slotConfigId, string doctorId)
        {
            if (Api)
            {
                var current = ApiSlotConfig(doctorId);
                if (current.All(w => w.SlotConfigId != slotConfigId)) return;
                ApiSaveSlotConfig(doctorId, current.Where(w => w.SlotConfigId != slotConfigId));
                return;
            }
            lock (DataStore.Sync) DataStore.SlotConfig.RemoveAll(s => s.SlotConfigId == slotConfigId);
        }

        /// <summary>True when the new window overlaps an existing one on the same day.</summary>
        public static bool WindowOverlaps(string doctorId, string hospitalId, string day, string start, string end)
        {
            int s = ToMinutes(start), e = ToMinutes(end);
            return ListSlotConfig(doctorId, hospitalId).Where(w => w.DayOfWeek == day).Any(w => s < ToMinutes(w.EndTime) && e > ToMinutes(w.StartTime));
        }

        public static List<DoctorLeave> ListLeave(string doctorId, string hospitalId)
        {
            if (Api) return ApiListLeave(doctorId);
            lock (DataStore.Sync) return DataStore.Leave.Where(l => l.DoctorId == doctorId && l.HospitalId == hospitalId).OrderBy(l => l.LeaveDate).ToList();
        }

        public static bool AddLeave(string doctorId, string hospitalId, DateTime date, string reason)
        {
            if (Api)
            {
                if (ApiListLeave(doctorId).Any(l => l.LeaveDate == date.Date)) return false;
                cnHelperObj.CreateDoctorLeave(new SCH_CreateDoctorLeaveReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, DoctorId = doctorId, LeaveDate = DocNyle.Date(date), Reason = string.IsNullOrWhiteSpace(reason) ? "Leave" : reason }).EnsureSuccess();
                return true;
            }
            lock (DataStore.Sync)
            {
                if (DataStore.Leave.Any(l => l.DoctorId == doctorId && l.HospitalId == hospitalId && l.LeaveDate.Date == date.Date)) return false;
                DataStore.Leave.Add(new DoctorLeave { LeaveId = DataStore.NextId("LV"), DoctorId = doctorId, HospitalId = hospitalId, LeaveDate = date.Date, Reason = string.IsNullOrWhiteSpace(reason) ? "Leave" : reason });
                return true;
            }
        }

        public static void RemoveLeave(string leaveId)
        {
            if (Api) { cnHelperObj.DeleteDoctorLeave(new SCH_DeleteDoctorLeaveReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, LeaveId = leaveId }).EnsureSuccess(); return; }
            lock (DataStore.Sync) DataStore.Leave.RemoveAll(l => l.LeaveId == leaveId);
        }

        /* ── Appointment types ── */

        public static List<AppointmentType> ListAppointmentTypes(string hospitalId, bool includeInactive)
        {
            if (Api) return ApiAppointmentTypes(includeInactive);
            lock (DataStore.Sync)
                return DataStore.AppointmentTypes.Where(t => t.HospitalId == hospitalId && (includeInactive || t.IsActive)).OrderBy(t => t.SortOrder).ToList();
        }

        public static AppointmentType GetAppointmentType(string hospitalId, string code)
        {
            if (Api) return ApiAppointmentTypes(true).FirstOrDefault(t => t.Code == code);
            lock (DataStore.Sync) return DataStore.AppointmentTypes.FirstOrDefault(t => t.HospitalId == hospitalId && t.Code == code);
        }

        public static AppointmentType GetAppointmentTypeById(string id)
        {
            if (Api) return ApiAppointmentTypes(true).FirstOrDefault(t => t.AppointmentTypeId == id);
            lock (DataStore.Sync) return DataStore.AppointmentTypes.FirstOrDefault(t => t.AppointmentTypeId == id);
        }

        /// <summary>Adds (no id) or updates a type. New codes are derived from the name and made unique.</summary>
        public static AppointmentType SaveAppointmentType(string hospitalId, string id, string name, int? duration, string color, string feeCategory, bool chargeable)
        {
            if (Api)
            {
                var saved = cnHelperObj.SaveAppointmentType(new SCH_SaveAppointmentTypeReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    AppointmentTypeId = id, Name = name, DurationMinutes = duration, Color = color, FeeCategory = feeCategory, IsChargeable = chargeable,
                }).EnsureSuccess().AppointmentType;
                HospitalCache.ClearPrefix("appt-types");
                return saved != null ? ToModel(saved) : null;
            }
            lock (DataStore.Sync)
            {
                var t = id != null ? DataStore.AppointmentTypes.FirstOrDefault(x => x.AppointmentTypeId == id) : null;
                if (t == null)
                {
                    var baseCode = Regex.Replace(name.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
                    if (baseCode.Length > 20) baseCode = baseCode.Substring(0, 20);
                    if (baseCode.Length == 0) baseCode = "TYPE";
                    var code = baseCode; int n = 1;
                    while (DataStore.AppointmentTypes.Any(x => x.HospitalId == hospitalId && x.Code == code)) code = baseCode + "_" + (++n);
                    t = new AppointmentType
                    {
                        AppointmentTypeId = DataStore.NextId("APT-TYPE"), HospitalId = hospitalId, Code = code, IsActive = true,
                        SortOrder = DataStore.AppointmentTypes.Count(x => x.HospitalId == hospitalId) + 1
                    };
                    DataStore.AppointmentTypes.Add(t);
                }
                t.Name = name; t.DurationMinutes = duration; t.Color = color; t.FeeCategory = feeCategory; t.IsChargeable = chargeable;
                return t;
            }
        }

        public static void ToggleAppointmentType(string id)
        {
            if (Api)
            {
                var t = GetAppointmentTypeById(id);
                if (t == null) return;
                cnHelperObj.UpdateAppointmentTypeActive(new SCH_UpdateAppointmentTypeActiveReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, AppointmentTypeId = id, IsActive = !t.IsActive }).EnsureSuccess();
                HospitalCache.ClearPrefix("appt-types");
                return;
            }
            lock (DataStore.Sync)
            {
                var t = DataStore.AppointmentTypes.FirstOrDefault(x => x.AppointmentTypeId == id);
                if (t != null) t.IsActive = !t.IsActive;
            }
        }

        /* ── Doctor profiles & charges ── */

        public static List<string> DoctorSpecialtyIds(string doctorId)
        {
            if (Api)
            {
                var d = ApiDoctor(doctorId);
                return d != null && d.SpecialtyIds != null ? d.SpecialtyIds.ToList() : new List<string>();
            }
            lock (DataStore.Sync)
            {
                var ids = DataStore.DoctorSpecialties.Where(d => d.DoctorId == doctorId).Select(d => d.SpecialtyId).ToList();
                if (ids.Count == 0)
                {
                    var u = DataStore.Users.FirstOrDefault(x => x.UserId == doctorId);
                    if (u != null && u.SpecialtyId != null) ids.Add(u.SpecialtyId);
                }
                return ids;
            }
        }

        /// <summary>Creates a doctor login (no id) or updates the doctor's profile; name follows the hospital template.</summary>
        public static AppUser SaveDoctorProfile(string hospitalId, AppUser record, List<string> specialtyIds)
        {
            if (Api) return ApiSaveDoctorProfile(record, specialtyIds);
            lock (DataStore.Sync)
            {
                var u = record.UserId != null ? DataStore.Users.FirstOrDefault(x => x.UserId == record.UserId) : null;
                if (u == null)
                {
                    if (string.IsNullOrEmpty(record.Password)) throw new ArgumentException("A password is required for a new doctor", "record");
                    u = new AppUser { UserId = DataStore.NextId("USR"), HospitalId = hospitalId, Role = "DOCTOR", Password = record.Password, IsActive = true, Phone = "" };
                    DataStore.Users.Add(u);
                    foreach (var cat in new[] { "NEW", "FOLLOWUP" })
                        DataStore.Fees.Add(new DoctorFee { FeeScheduleId = DataStore.NextId("FEE"), HospitalId = hospitalId, DoctorId = u.UserId, ConsultationType = cat, FeeAmount = 0, Currency = "", EffectiveFrom = TimeZoneService.Today(hospitalId) });
                }
                u.Prefix = record.Prefix; u.FirstName = record.FirstName; u.MiddleName = record.MiddleName; u.LastName = record.LastName;
                u.Qualifications = record.Qualifications; u.DisplayNameOverride = record.DisplayNameOverride; u.LicenseNumber = record.LicenseNumber;
                u.Email = record.Email; u.Phone = record.Phone; u.Room = record.Room; u.Languages = record.Languages; u.Bio = record.Bio;
                DataStore.DoctorSpecialties.RemoveAll(d => d.DoctorId == u.UserId);
                foreach (var sid in specialtyIds) DataStore.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = u.UserId, SpecialtyId = sid });
                u.SpecialtyId = specialtyIds.FirstOrDefault();
                u.FullName = RenderDoctorName(u, HospitalService.GetSetting(u.HospitalId, "DOCTOR_NAME_TEMPLATE"));
                return u;
            }
        }

        public static decimal FlatFee(string doctorId, string category, DateTime onDate)
        {
            if (Api)
            {
                var fee = cnHelperObj.CheckEffectiveFee(AppSession.ApiHospitalId, AppSession.ApiUserId, doctorId, category, DocNyle.Date(onDate), AppSession.ApiHashValue).EnsureSuccess().FeeSchedule;
                return fee != null ? fee.FeeAmount : 0;
            }
            lock (DataStore.Sync)
            {
                var f = DataStore.Fees.FirstOrDefault(x => x.DoctorId == doctorId && x.ConsultationType == category
                    && x.EffectiveFrom.Date <= onDate.Date && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value.Date >= onDate.Date));
                return f != null ? f.FeeAmount : 0;
            }
        }

        public static List<DoctorFee> ListFees(string hospitalId)
        {
            if (Api)
                return (cnHelperObj.ListDoctorFees(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().FeeSchedules ?? new List<SCH_FeeScheduleInfo>()).Select(ToModel)
                    .OrderBy(f => f.DoctorId).ThenBy(f => f.EffectiveFrom).ToList();
            lock (DataStore.Sync) return DataStore.Fees.Where(f => f.HospitalId == hospitalId).OrderBy(f => f.DoctorId).ThenBy(f => f.EffectiveFrom).ToList();
        }

        public static DoctorCharge GetDoctorCharge(string hospitalId, string doctorId)
        {
            if (Api) return ApiDoctorCharge(doctorId);
            int unit; decimal rate;
            int.TryParse(ResolveSetting(hospitalId, doctorId, "CHARGE_UNIT_MINS").Value, out unit);
            decimal.TryParse(ResolveSetting(hospitalId, doctorId, "CHARGE_UNIT_RATE").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out rate);
            return new DoctorCharge
            {
                Model = ResolveSetting(hospitalId, doctorId, "CHARGE_MODEL").Value ?? "FLAT",
                NewFee = FlatFee(doctorId, "NEW", TimeZoneService.Today(hospitalId)), FollowupFee = FlatFee(doctorId, "FOLLOWUP", TimeZoneService.Today(hospitalId)),
                UnitMinutes = unit > 0 ? unit : 15, UnitRate = rate,
            };
        }

        /// <summary>FLAT fees are versioned: a changed amount closes the current fee row and starts a new one today.</summary>
        public static void SaveDoctorCharge(string hospitalId, string doctorId, string model, decimal newFee, decimal followupFee, int unitMinutes, decimal unitRate)
        {
            bool isUnit = model == "UNIT";
            if (Api)
            {
                // SaveDoctorCharge versions the flat fees and stores the unit-charge overrides in one call.
                cnHelperObj.SaveDoctorCharge(new SCH_SaveDoctorChargeReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    DoctorId = doctorId, Model = isUnit ? "UNIT" : "FLAT", NewFee = newFee, FollowupFee = followupFee,
                    UnitMinutes = isUnit ? unitMinutes : (int?)null, UnitRate = isUnit ? unitRate : (decimal?)null,
                }).EnsureSuccess();
                RequestCache.Remove("doctor-settings:" + doctorId);
                return;
            }
            SaveDoctorSettings(hospitalId, doctorId, new Dictionary<string, string>
            {
                { "CHARGE_MODEL", isUnit ? "UNIT" : "" },
                { "CHARGE_UNIT_MINS", isUnit ? unitMinutes.ToString() : "" },
                { "CHARGE_UNIT_RATE", isUnit ? unitRate.ToString(CultureInfo.InvariantCulture) : "" },
            });
            lock (DataStore.Sync)
            {
                var today = TimeZoneService.Today(hospitalId);
                foreach (var pair in new[] { new KeyValuePair<string, decimal>("NEW", newFee), new KeyValuePair<string, decimal>("FOLLOWUP", followupFee) })
                {
                    var cur = DataStore.Fees.FirstOrDefault(f => f.DoctorId == doctorId && f.ConsultationType == pair.Key && f.EffectiveFrom <= today && (!f.EffectiveTo.HasValue || f.EffectiveTo >= today));
                    if (cur != null && cur.FeeAmount == pair.Value) continue;
                    if (cur != null && cur.EffectiveFrom == today) { cur.FeeAmount = pair.Value; continue; }
                    if (cur != null) cur.EffectiveTo = today.AddDays(-1);
                    DataStore.Fees.Add(new DoctorFee { FeeScheduleId = DataStore.NextId("FEE"), HospitalId = hospitalId, DoctorId = doctorId, ConsultationType = pair.Key, FeeAmount = pair.Value, Currency = cur != null ? cur.Currency : "", EffectiveFrom = today });
                }
            }
        }

        /* ── Intake & consent ── */

        public static List<IntakeQuestion> ListIntakeQuestions(string hospitalId)
        {
            if (Api) return ApiIntakeQuestions();
            lock (DataStore.Sync) return DataStore.IntakeQuestions.Where(q => q.HospitalId == hospitalId && q.IsActive).OrderBy(q => q.SortOrder).ToList();
        }

        public static void AddIntakeQuestion(string hospitalId, string label, string type, string options)
        {
            if (Api)
            {
                cnHelperObj.CreateIntakeQuestion(new SCH_CreateIntakeQuestionReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, Label = label, QuestionType = type, Options = options ?? "" }).EnsureSuccess();
                HospitalCache.Clear("intake-questions");
                return;
            }
            lock (DataStore.Sync)
                DataStore.IntakeQuestions.Add(new IntakeQuestion
                {
                    QuestionId = DataStore.NextId("IQ"), HospitalId = hospitalId, Label = label, QuestionType = type, Options = options ?? "",
                    SortOrder = DataStore.IntakeQuestions.Count(q => q.HospitalId == hospitalId) + 1, IsActive = true
                });
        }

        /// <summary>Soft delete — past answers keep their snapshotted labels.</summary>
        public static void RemoveIntakeQuestion(string questionId)
        {
            if (Api)
            {
                cnHelperObj.DeleteIntakeQuestion(new SCH_DeleteIntakeQuestionReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, QuestionId = questionId }).EnsureSuccess();
                HospitalCache.Clear("intake-questions");
                return;
            }
            lock (DataStore.Sync)
            {
                var q = DataStore.IntakeQuestions.FirstOrDefault(x => x.QuestionId == questionId);
                if (q != null) q.IsActive = false;
            }
        }

        public static void SubmitIntake(string hospitalId, string appointmentId, string patientId, List<IntakeAnswer> answers)
        {
            if (Api)
            {
                cnHelperObj.SubmitIntakeResponse(new SCH_SubmitIntakeResponseReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    AppointmentId = appointmentId, PatientId = patientId,
                    Answers = (answers ?? new List<IntakeAnswer>()).Select(a => new SCH_IntakeAnswerInfo { QuestionId = a.QuestionId, Answer = a.Answer }).ToList(),
                }).EnsureSuccess();
                return;
            }
            lock (DataStore.Sync)
            {
                DataStore.IntakeResponses.RemoveAll(r => r.AppointmentId == appointmentId);
                DataStore.IntakeResponses.Add(new IntakeResponse { ResponseId = DataStore.NextId("IR"), HospitalId = hospitalId, AppointmentId = appointmentId, PatientId = patientId, Answers = answers, SubmittedAt = DateTime.UtcNow });
            }
        }

        public static ConsentForm GetConsentForm(string hospitalId)
        {
            if (Api) return ToModel(cnHelperObj.GetConsentForm(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().ConsentForm);
            lock (DataStore.Sync) return DataStore.ConsentForms.FirstOrDefault(f => f.HospitalId == hospitalId && f.IsActive);
        }

        /// <summary>Edits bump the form version so earlier signatures keep pointing at what was signed.</summary>
        public static ConsentForm SaveConsentForm(string hospitalId, string title, string body)
        {
            if (Api) return ToModel(cnHelperObj.SaveConsentForm(new SCH_SaveConsentFormReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, Title = title, BodyText = body }).EnsureSuccess().ConsentForm);
            lock (DataStore.Sync)
            {
                var f = GetConsentForm(hospitalId);
                if (f == null)
                {
                    f = new ConsentForm { ConsentFormId = DataStore.NextId("CF"), HospitalId = hospitalId, Version = 0, IsActive = true };
                    DataStore.ConsentForms.Add(f);
                }
                if (f.Title != title || f.BodyText != body) f.Version++;
                f.Title = title;
                f.BodyText = body;
                return f;
            }
        }

        /// <summary>The appointment a consultation belongs to: same patient + doctor + day.</summary>
        public static Appointment AppointmentForConsultation(Consultation c)
        {
            if (Api)
            {
                if (c.AppointmentId != null)
                {
                    var linked = GetAppointment(c.AppointmentId);
                    if (linked != null) return linked;
                }
                var visit = ApiVisit(c.ConsultationId);
                return visit != null ? ToModel(visit.Appointment) : null;
            }
            lock (DataStore.Sync)
            {
                if (c.AppointmentId != null)
                {
                    var linked = DataStore.Appointments.FirstOrDefault(a => a.AppointmentId == c.AppointmentId);
                    if (linked != null) return linked;
                }
                var rank = new Dictionary<string, int> { { "IN_CONSULT", 0 }, { "CHECKED_IN", 1 }, { "BOOKED", 2 }, { "COMPLETED", 3 } };
                return DataStore.Appointments
                    .Where(a => a.PatientId == c.PatientId && a.DoctorId == c.DoctorId && a.AppointmentDate == c.VisitLocalDate && a.Status != "CANCELLED")
                    .OrderBy(a => rank.ContainsKey(a.Status) ? rank[a.Status] : 9).FirstOrDefault();
            }
        }

        /// <summary>API mode: the intake comes with the consultation's visit (GetVisitIntakeAndConsent), so <paramref name="consultationId"/> is required there.</summary>
        public static IntakeResponse GetIntakeForAppointment(string appointmentId, string consultationId = null)
        {
            if (Api)
            {
                var visit = ApiVisit(consultationId);
                return visit != null && visit.IntakeResponse != null && (appointmentId == null || visit.IntakeResponse.AppointmentId == null || visit.IntakeResponse.AppointmentId == appointmentId)
                    ? ToModel(visit.IntakeResponse) : null;
            }
            lock (DataStore.Sync) return DataStore.IntakeResponses.FirstOrDefault(r => r.AppointmentId == appointmentId);
        }

        public static ConsentRecord GetConsentRecord(Consultation c, Appointment appt)
        {
            if (Api)
            {
                var visit = ApiVisit(c.ConsultationId);
                return visit != null ? ToModel(visit.ConsentRecord) : null;
            }
            lock (DataStore.Sync)
                return DataStore.ConsentRecords.LastOrDefault(r => r.ConsultationId == c.ConsultationId || (appt != null && r.AppointmentId == appt.AppointmentId));
        }

        /// <summary>Append-only consent signature; snapshots the current form title, text and version.</summary>
        public static ConsentRecord RecordConsent(Consultation c, string signedByName)
        {
            if (Api) return ApiRecordConsent(c, signedByName);
            var form = GetConsentForm(c.HospitalId);
            if (form == null) return null;
            var appt = AppointmentForConsultation(c);
            var row = new ConsentRecord
            {
                ConsentRecordId = DataStore.NextId("CR"), ConsentFormId = form.ConsentFormId, ConsultationId = c.ConsultationId,
                AppointmentId = appt != null ? appt.AppointmentId : null, PatientId = c.PatientId, SignedByName = signedByName,
                Method = "IN_CLINIC", FormVersion = form.Version, TitleSnapshot = form.Title, BodySnapshot = form.BodyText, SignedAt = DateTime.UtcNow
            };
            lock (DataStore.Sync) DataStore.ConsentRecords.Add(row);
            return row;
        }

        private static bool Api { get { return DataSource.IsApi("Scheduling"); } }

        /* ── Doctors ── */

        /// <summary>The hospital's doctors with their profiles (ListDoctorProfiles, cached per hospital).</summary>
        internal static List<SCH_DoctorProfileInfo> ApiDoctorProfiles()
        {
            return HospitalCache.Get("doctors", () => cnHelperObj.ListDoctorProfiles(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Doctors ?? new List<SCH_DoctorProfileInfo>());
        }

        internal static AppUser DoctorFromProfile(SCH_DoctorProfileInfo p)
        {
            return new AppUser
            {
                UserId = p.UserId, HospitalId = AppSession.ApiUser != null ? AppSession.ApiUser.HospitalId : null,
                FullName = string.IsNullOrEmpty(p.DisplayName) ? ((p.Prefix + " " + p.FirstName + " " + p.LastName).Trim()) : p.DisplayName,
                Email = p.Email, Phone = p.Phone, Role = "DOCTOR", LicenseNumber = p.LicenseNumber, Prefix = p.Prefix, FirstName = p.FirstName,
                MiddleName = p.MiddleName, LastName = p.LastName, Qualifications = p.Qualifications, DisplayNameOverride = p.DisplayNameOverride,
                Room = p.Room, Languages = p.Languages, Bio = p.Bio, IsActive = true,
                SpecialtyId = p.SpecialtyIds != null ? p.SpecialtyIds.FirstOrDefault() : null,
            };
        }

        private static SCH_DoctorProfileInfo ApiDoctor(string doctorId)
        {
            return ApiDoctorProfiles().FirstOrDefault(d => string.Equals(d.UserId, doctorId, StringComparison.OrdinalIgnoreCase));
        }

        /* ── Portal (anonymous) ── */

        /// <summary>GetPortalBookingOptions for the portal hospital, cached application-wide for one minute (the operation is rate-limited per IP).</summary>
        internal static SCH_PortalOptionsInfo PortalOptions()
        {
            const string key = "portal-options";
            var hit = MemoryCache.Default.Get(key) as SCH_PortalOptionsInfo;
            if (hit != null) return hit;
            hit = cnHelperObj.GetPortalBookingOptions(CommonHelper.GetSHA265("PORTAL")).EnsureSuccess().Options;
            if (hit != null) MemoryCache.Default.Set(key, hit, DateTimeOffset.UtcNow.AddMinutes(1));
            return hit;
        }

        /* ── Settings ── */

        private static Dictionary<string, ResolvedSetting> ApiDoctorSettings(string doctorId)
        {
            return RequestCache.Get("doctor-settings:" + doctorId, () =>
                (cnHelperObj.GetDoctorSettings(AppSession.ApiHospitalId, AppSession.ApiUserId, doctorId, AppSession.ApiHashValue).EnsureSuccess().Settings ?? new List<SCH_ResolvedSettingInfo>())
                    .Where(s => s.Key != null).GroupBy(s => s.Key)
                    .ToDictionary(g => g.Key, g => new ResolvedSetting { Value = g.First().Value, Source = g.First().Source ?? "NONE" }));
        }

        private static void ApiSaveDoctorSettings(string doctorId, IDictionary<string, string> values)
        {
            cnHelperObj.SaveDoctorSettings(new SCH_SaveDoctorSettingsReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                DoctorId = doctorId,
                Values = values.Where(kv => DoctorOverridable.Contains(kv.Key)).Select(kv => new KeyValueInfo { Key = kv.Key, Value = kv.Value ?? "" }).ToList(),
            }).EnsureSuccess();
            RequestCache.Remove("doctor-settings:" + doctorId);
        }

        /* ── Appointments ── */

        internal static Appointment ToModel(SCH_AppointmentInfo a)
        {
            if (a == null) return null;
            var zone = string.IsNullOrEmpty(a.TimeZoneId) ? TimeZoneService.DefaultZoneId : a.TimeZoneId;
            var start = DocNyle.ParseInstant(a.SlotStart);
            var end = DocNyle.ParseInstant(a.SlotEnd);
            // Fall back to the wall-clock values when the UTC instants are missing.
            if (!start.HasValue && DocNyle.ParseLocal(a.SlotStartLocal).HasValue) start = TimeZoneService.ToUtc(DocNyle.ParseLocal(a.SlotStartLocal).Value, zone);
            if (!end.HasValue && DocNyle.ParseLocal(a.SlotEndLocal).HasValue) end = TimeZoneService.ToUtc(DocNyle.ParseLocal(a.SlotEndLocal).Value, zone);
            return new Appointment
            {
                AppointmentId = a.AppointmentId, HospitalId = AppSession.ApiUser != null ? AppSession.ApiUser.HospitalId : null,
                PatientId = a.PatientId, DoctorId = a.DoctorId, SlotStart = start ?? DateTime.MinValue, SlotEnd = end ?? DateTime.MinValue,
                TimeZoneId = zone, AppointmentDate = (DocNyle.ParseLocal(a.AppointmentDate) ?? DocNyle.ParseLocal(a.SlotStartLocal) ?? DateTime.MinValue).Date,
                Type = a.Type, Status = a.Status, Notes = a.Notes ?? "",
                CheckedInAt = DocNyle.ParseInstant(a.CheckedInAt), ConsultStartedAt = DocNyle.ParseInstant(a.ConsultStartedAt),
                CompletedAt = DocNyle.ParseInstant(a.CompletedAt),
            };
        }

        /// <summary>
        /// An appointment row with the patient's name and MRN as the API returns them. The full patient record is not
        /// loaded per row (GetPatient is an audited read); pages that need more read it with PatientService.Get.
        /// </summary>
        internal static AppointmentView ToView(SCH_AppointmentInfo a)
        {
            return new AppointmentView
            {
                Appointment = ToModel(a),
                Patient = new Patient { PatientId = a.PatientId, FullName = a.PatientName, Mrn = a.Mrn },
                DoctorName = a.DoctorName ?? "",
                DoctorRoom = a.DoctorRoom ?? "",
            };
        }

        private static List<AppointmentView> ApiListAppointments(string doctorId, DateTime date)
        {
            var resp = doctorId == null ? cnHelperObj.ListAppointmentsForHospitalDate(AppSession.ApiHospitalId, AppSession.ApiUserId, DocNyle.Date(date), AppSession.ApiHashValue).EnsureSuccess()
                                        : cnHelperObj.ListAppointmentsForDate(AppSession.ApiHospitalId, AppSession.ApiUserId, doctorId, DocNyle.Date(date), AppSession.ApiHashValue).EnsureSuccess();
            return (resp.Appointments ?? new List<SCH_AppointmentInfo>()).Select(ToView).OrderBy(v => v.Appointment.SlotStart).ToList();
        }

        private static Appointment ApiCreateAppointment(string patientId, string doctorId, DateTime start, DateTime end, string type, string notes)
        {
            return ToModel(cnHelperObj.CreateAppointment(new SCH_CreateAppointmentReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                PatientId = patientId, DoctorId = doctorId, SlotStartLocal = DocNyle.DateTimeLocal(start), SlotEndLocal = DocNyle.DateTimeLocal(end),
                Type = type, Notes = notes ?? "",
            }).EnsureSuccess().Appointment);
        }

        /// <summary>UpdateAppointmentStatus; a move the lifecycle does not allow comes back as a Conflict fault → null, like the in-memory store.</summary>
        private static Appointment ApiUpdateStatus(string appointmentId, string status)
        {
            try
            {
                return ToModel(cnHelperObj.UpdateAppointmentStatus(new SCH_UpdateAppointmentStatusReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, AppointmentId = appointmentId, Status = status }).EnsureSuccess().Appointment);
            }
            catch (ApiException ex) when (ex.Kind == ApiFaultKind.Conflict || ex.Kind == ApiFaultKind.NotFound || ex.Kind == ApiFaultKind.BusinessRule)
            {
                return null;
            }
        }

        /// <summary>UpdateAppointmentSlot; the slot taken since the list was shown (or the appointment no longer BOOKED) → null.</summary>
        private static Appointment ApiReschedule(string appointmentId, DateTime newStart)
        {
            try
            {
                return ToModel(cnHelperObj.UpdateAppointmentSlot(new SCH_UpdateAppointmentSlotReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    AppointmentId = appointmentId, NewSlotStartLocal = DocNyle.DateTimeLocal(newStart),
                }).EnsureSuccess().Appointment);
            }
            catch (ApiException ex) when (ex.Kind == ApiFaultKind.Conflict)
            {
                return null;
            }
        }

        private static int ApiBookRecurring(string patientId, string doctorId, DateTime start, int everyDays, int count, string type)
        {
            var booked = cnHelperObj.CreateRecurringAppointments(new SCH_CreateRecurringAppointmentsReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                PatientId = patientId, DoctorId = doctorId, StartDate = DocNyle.Date(start), EveryDays = everyDays, Count = count, Type = type,
            }).EnsureSuccess().Appointments;
            return booked != null ? booked.Count : 0;
        }

        private static List<TimeSlot> ApiListAvailableSlots(string doctorId, DateTime date, int? durationMinutes)
        {
            var dur = durationMinutes.HasValue && durationMinutes.Value > 0 ? durationMinutes.Value.ToString(CultureInfo.InvariantCulture) : null;
            return (cnHelperObj.ListAvailableSlots(AppSession.ApiHospitalId, AppSession.ApiUserId, doctorId, DocNyle.Date(date), dur, AppSession.ApiHashValue).EnsureSuccess().Slots ?? new List<SCH_AvailableSlotInfo>())
                .Select(s => new TimeSlot { SlotStart = DocNyle.LocalOrMin(s.SlotStart), SlotEnd = DocNyle.LocalOrMin(s.SlotEnd), IsBooked = s.IsBooked })
                .Where(s => s.SlotStart != DateTime.MinValue).OrderBy(s => s.SlotStart).ToList();
        }

        private static DoctorSchedule ApiDoctorSchedule(string doctorId, DateTime date)
        {
            var s = cnHelperObj.GetDoctorScheduleForDate(AppSession.ApiHospitalId, AppSession.ApiUserId, doctorId, DocNyle.Date(date), AppSession.ApiHashValue).EnsureSuccess().Schedule;
            if (s == null) return new DoctorSchedule { DoctorId = doctorId, Date = date.Date, DayOfWeek = DayCode(date), Windows = new List<TimeWindow>(), WorkingDays = new List<string>() };
            return new DoctorSchedule
            {
                DoctorId = doctorId, Date = date.Date, DayOfWeek = s.DayOfWeek ?? DayCode(date),
                Windows = (s.Windows ?? new List<SCH_SlotWindowInfo>()).OrderBy(w => w.StartTime).Select(w => new TimeWindow { StartTime = w.StartTime, EndTime = w.EndTime }).ToList(),
                WorkingDays = s.WorkingDays ?? new List<string>(), SlotMinutes = s.SlotMinutes, BufferMinutes = s.BufferMinutes,
                OnLeave = s.OnLeave, LeaveReason = s.LeaveReason,
            };
        }

        /* ── Weekly windows & leave ── */

        private static List<DoctorSlotConfig> ApiSlotConfig(string doctorId)
        {
            return RequestCache.Get("slot-config:" + doctorId, () =>
                (cnHelperObj.ListDoctorSlotConfig(AppSession.ApiHospitalId, AppSession.ApiUserId, doctorId, AppSession.ApiHashValue).EnsureSuccess().SlotConfigs ?? new List<SCH_SlotConfigInfo>())
                    .Select(w => new DoctorSlotConfig { SlotConfigId = w.SlotConfigId, DoctorId = doctorId, DayOfWeek = w.DayOfWeek, StartTime = w.StartTime, EndTime = w.EndTime, IsActive = true })
                    .OrderBy(s => Array.IndexOf(WeekDays, s.DayOfWeek)).ThenBy(s => s.StartTime).ToList());
        }

        /// <summary>SaveDoctorSlotConfig replaces the doctor's whole weekly timetable, so edits send the full list.</summary>
        private static void ApiSaveSlotConfig(string doctorId, IEnumerable<DoctorSlotConfig> windows)
        {
            cnHelperObj.SaveDoctorSlotConfig(new SCH_SaveDoctorSlotConfigReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                DoctorId = doctorId,
                Windows = windows.Select(w => new SCH_SlotWindowInfo { DayOfWeek = w.DayOfWeek, StartTime = w.StartTime, EndTime = w.EndTime }).ToList(),
            }).EnsureSuccess();
            RequestCache.Remove("slot-config:" + doctorId);
        }

        private static List<DoctorLeave> ApiListLeave(string doctorId)
        {
            return (cnHelperObj.ListDoctorLeave(AppSession.ApiHospitalId, AppSession.ApiUserId, doctorId, AppSession.ApiHashValue).EnsureSuccess().Leaves ?? new List<SCH_DoctorLeaveInfo>())
                .Select(l => new DoctorLeave { LeaveId = l.LeaveId, DoctorId = l.DoctorId ?? doctorId, LeaveDate = DocNyle.LocalOrMin(l.LeaveDate).Date, Reason = l.Reason })
                .OrderBy(l => l.LeaveDate).ToList();
        }

        /* ── Appointment types ── */

        private static List<AppointmentType> ApiAppointmentTypes(bool includeInactive)
        {
            var rows = HospitalCache.Get(includeInactive ? "appt-types-all" : "appt-types",
                () => cnHelperObj.ListAppointmentTypes(AppSession.ApiHospitalId, AppSession.ApiUserId, includeInactive ? "true" : "false", AppSession.ApiHashValue).EnsureSuccess().AppointmentTypes ?? new List<SCH_AppointmentTypeInfo>());
            return rows.Select(ToModel).OrderBy(t => t.SortOrder).ToList();
        }

        internal static AppointmentType ToModel(SCH_AppointmentTypeInfo t)
        {
            return new AppointmentType
            {
                AppointmentTypeId = t.AppointmentTypeId, Code = t.Code, Name = t.Name, DurationMinutes = t.DurationMinutes, Color = t.Color,
                IsChargeable = t.IsChargeable, FeeCategory = t.FeeCategory, SortOrder = t.SortOrder, IsActive = t.IsActive,
            };
        }

        /* ── Doctor profile & charges ── */

        private static AppUser ApiSaveDoctorProfile(AppUser record, List<string> specialtyIds)
        {
            var d = cnHelperObj.SaveDoctorProfile(new SCH_SaveDoctorProfileReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                DoctorId = record.UserId, Email = record.Email, Password = record.UserId == null ? record.Password : null,
                Prefix = record.Prefix, FirstName = record.FirstName, MiddleName = record.MiddleName, LastName = record.LastName,
                Qualifications = record.Qualifications, DisplayNameOverride = record.DisplayNameOverride, LicenseNumber = record.LicenseNumber,
                Phone = record.Phone, Room = record.Room, Languages = record.Languages, Bio = record.Bio, SpecialtyIds = specialtyIds ?? new List<string>(),
            }).EnsureSuccess().Doctor;
            HospitalCache.Clear("doctors");
            HospitalCache.Clear("users");
            return new AppUser
            {
                UserId = d.DoctorId, HospitalId = record.HospitalId, FullName = d.FullName, Email = d.Email, Phone = d.Phone, Role = "DOCTOR",
                LicenseNumber = d.LicenseNumber, Prefix = d.Prefix, FirstName = d.FirstName, MiddleName = d.MiddleName, LastName = d.LastName,
                Qualifications = d.Qualifications, DisplayNameOverride = d.DisplayNameOverride, Room = d.Room, Languages = d.Languages, Bio = d.Bio,
                SpecialtyId = d.SpecialtyId, IsActive = d.IsActive,
            };
        }

        private static DoctorCharge ApiDoctorCharge(string doctorId)
        {
            var c = cnHelperObj.GetDoctorCharge(AppSession.ApiHospitalId, AppSession.ApiUserId, doctorId, AppSession.ApiHashValue).EnsureSuccess().Charge;
            if (c == null) return new DoctorCharge { Model = "FLAT", UnitMinutes = 15 };
            return new DoctorCharge
            {
                Model = string.IsNullOrEmpty(c.Model) ? "FLAT" : c.Model, NewFee = c.NewFee ?? 0, FollowupFee = c.FollowupFee ?? 0,
                UnitMinutes = c.UnitMinutes.HasValue && c.UnitMinutes.Value > 0 ? c.UnitMinutes.Value : 15, UnitRate = c.UnitRate ?? 0,
            };
        }

        private static DoctorFee ToModel(SCH_FeeScheduleInfo f)
        {
            return new DoctorFee
            {
                FeeScheduleId = f.FeeScheduleId, DoctorId = f.DoctorId, ConsultationType = f.ConsultationType, FeeAmount = f.FeeAmount,
                Currency = f.Currency ?? "", EffectiveFrom = DocNyle.LocalOrMin(f.EffectiveFrom), EffectiveTo = DocNyle.ParseLocal(f.EffectiveTo),
            };
        }

        /* ── Intake & consent ── */

        private static List<IntakeQuestion> ApiIntakeQuestions()
        {
            return HospitalCache.Get("intake-questions", () => cnHelperObj.ListIntakeQuestions(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Questions ?? new List<SCH_IntakeQuestionInfo>())
                .Select(q => new IntakeQuestion { QuestionId = q.QuestionId, Label = q.Label, QuestionType = q.QuestionType, Options = q.Options ?? "", SortOrder = q.SortOrder, IsActive = true })
                .OrderBy(q => q.SortOrder).ToList();
        }

        private static ConsentForm ToModel(SCH_ConsentFormInfo f)
        {
            if (f == null) return null;
            return new ConsentForm { ConsentFormId = f.ConsentFormId, Title = f.Title, BodyText = f.BodyText, Version = f.Version, IsActive = true };
        }

        private static ConsentRecord ToModel(SCH_ConsentRecordInfo r)
        {
            if (r == null) return null;
            return new ConsentRecord
            {
                ConsentRecordId = r.ConsentRecordId, ConsentFormId = r.ConsentFormId, ConsultationId = r.ConsultationId, AppointmentId = r.AppointmentId,
                PatientId = r.PatientId, SignedByName = r.SignedByName, Method = r.Method, FormVersion = r.FormVersion,
                TitleSnapshot = r.TitleSnapshot, BodySnapshot = r.BodySnapshot, SignedAt = DocNyle.InstantOrMin(r.SignedAt),
            };
        }

        private static IntakeResponse ToModel(SCH_IntakeResponseInfo r)
        {
            if (r == null) return null;
            var labels = ApiIntakeQuestions().ToDictionary(q => q.QuestionId ?? "", q => q.Label);
            return new IntakeResponse
            {
                ResponseId = r.ResponseId, AppointmentId = r.AppointmentId, PatientId = r.PatientId, SubmittedAt = DocNyle.InstantOrMin(r.SubmittedAt),
                Answers = (r.Answers ?? new List<SCH_IntakeAnswerInfo>()).Select(a => new IntakeAnswer
                {
                    QuestionId = a.QuestionId, Answer = a.Answer,
                    Label = a.QuestionId != null && labels.ContainsKey(a.QuestionId) ? labels[a.QuestionId] : a.QuestionId,
                }).ToList(),
            };
        }

        /// <summary>GetVisitIntakeAndConsent for a consultation (request-cached): its appointment, intake, consent form and latest consent.</summary>
        private static SCH_VisitIntakeAndConsentInfo ApiVisit(string consultationId)
        {
            if (string.IsNullOrEmpty(consultationId)) return null;
            return RequestCache.Get("visit-intake:" + consultationId, () => cnHelperObj.GetVisitIntakeAndConsent(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().Visit);
        }

        private static ConsentRecord ApiRecordConsent(Consultation c, string signedByName)
        {
            var visit = ApiVisit(c.ConsultationId);
            var form = visit != null && visit.ConsentForm != null ? visit.ConsentForm : cnHelperObj.GetConsentForm(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().ConsentForm;
            if (form == null) return null;
            var appt = visit != null ? visit.Appointment : null;
            var rec = cnHelperObj.RecordConsent(new SCH_RecordConsentReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                ConsentFormId = form.ConsentFormId, ConsultationId = c.ConsultationId, AppointmentId = appt != null ? appt.AppointmentId : null,
                PatientId = c.PatientId, SignedByName = signedByName, Method = "IN_CLINIC",
            }).EnsureSuccess().ConsentRecord;
            RequestCache.Remove("visit-intake:" + c.ConsultationId);
            return ToModel(rec);
        }
    }
}
