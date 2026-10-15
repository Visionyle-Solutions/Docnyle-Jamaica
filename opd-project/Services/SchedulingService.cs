using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>
    /// Appointments, the slot engine (weekly windows, leave, slot length, buffer), doctor profiles,
    /// per-doctor overrides and charges, appointment types, intake questions and consent.
    /// </summary>
    public static class SchedulingService
    {
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

        public static void RefreshDoctorNames(string hospitalId)
        {
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
            lock (DataStore.Sync)
                return DataStore.Appointments
                    .Where(a => a.HospitalId == hospitalId && a.SlotStart.Date == date.Date && (doctorId == null || a.DoctorId == doctorId))
                    .OrderBy(a => a.SlotStart).Select(ToView).ToList();
        }

        public static List<AppointmentView> ListAppointmentsForPatient(string patientId)
        {
            lock (DataStore.Sync)
                return DataStore.Appointments.Where(a => a.PatientId == patientId).OrderByDescending(a => a.SlotStart).Select(ToView).ToList();
        }

        public static Appointment CreateAppointment(string hospitalId, string patientId, string doctorId, DateTime start, DateTime end, string type, string notes)
        {
            var row = new Appointment
            {
                AppointmentId = DataStore.NextId("APT"), HospitalId = hospitalId, PatientId = patientId, DoctorId = doctorId,
                SlotStart = start, SlotEnd = end, Type = type, Status = "BOOKED", Notes = notes ?? ""
            };
            lock (DataStore.Sync) DataStore.Appointments.Add(row);
            return row;
        }

        /// <summary>Applies a lifecycle move. Returns null (and changes nothing) for a move the lifecycle does not allow.</summary>
        public static Appointment UpdateStatus(string appointmentId, string status, string modifiedBy)
        {
            lock (DataStore.Sync)
            {
                var a = DataStore.Appointments.FirstOrDefault(x => x.AppointmentId == appointmentId);
                if (a == null) return null;
                if (a.Status == status) return a;
                string[] allowed;
                if (!Transitions.TryGetValue(a.Status, out allowed) || !allowed.Contains(status)) return null;
                a.Status = status;
                if (status == "CHECKED_IN") { a.CheckedInAt = DateTime.Now; a.CheckedInBy = modifiedBy; }
                if (status == "IN_CONSULT") a.ConsultStartedAt = DateTime.Now;
                if (status == "COMPLETED") a.CompletedAt = DateTime.Now;
                a.ModifiedAt = DateTime.Now;
                a.ModifiedBy = modifiedBy;
                return a;
            }
        }

        /// <summary>Free, not-yet-started slots on the date with the same doctor and the same length as the appointment being moved.</summary>
        public static List<TimeSlot> ListRescheduleSlots(Appointment appt, DateTime date)
        {
            var minutes = (int)(appt.SlotEnd - appt.SlotStart).TotalMinutes;
            var now = DateTime.Now;
            return ListAvailableSlots(appt.DoctorId, appt.HospitalId, date, minutes).Where(s => !s.IsBooked && s.SlotStart > now).ToList();
        }

        /// <summary>
        /// Cancels the appointment and re-books the same patient at the chosen start time. Checked and booked under one
        /// lock, so a slot taken by someone else since the list was shown is refused (returns null, nothing changes).
        /// </summary>
        public static Appointment Reschedule(string appointmentId, DateTime newStart, string modifiedBy)
        {
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
                var taken = DataStore.Appointments.Where(a => a.DoctorId == doctorId && a.SlotStart.Date == day && a.Status != "CANCELLED")
                    .Select(a => new { Start = (int)a.SlotStart.TimeOfDay.TotalMinutes, End = (int)a.SlotEnd.TimeOfDay.TotalMinutes + buffer }).ToList();
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
            lock (DataStore.Sync)
                return DataStore.SlotConfig.Where(s => s.DoctorId == doctorId && s.HospitalId == hospitalId && s.IsActive)
                    .OrderBy(s => Array.IndexOf(WeekDays, s.DayOfWeek)).ThenBy(s => s.StartTime).ToList();
        }

        public static void AddSlotWindow(string doctorId, string hospitalId, string day, string start, string end)
        {
            lock (DataStore.Sync)
                DataStore.SlotConfig.Add(new DoctorSlotConfig { SlotConfigId = DataStore.NextId("DSC"), DoctorId = doctorId, HospitalId = hospitalId, DayOfWeek = day, StartTime = start, EndTime = end, IsActive = true });
        }

        public static void RemoveSlotWindow(string slotConfigId)
        {
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
            lock (DataStore.Sync) return DataStore.Leave.Where(l => l.DoctorId == doctorId && l.HospitalId == hospitalId).OrderBy(l => l.LeaveDate).ToList();
        }

        public static bool AddLeave(string doctorId, string hospitalId, DateTime date, string reason)
        {
            lock (DataStore.Sync)
            {
                if (DataStore.Leave.Any(l => l.DoctorId == doctorId && l.HospitalId == hospitalId && l.LeaveDate.Date == date.Date)) return false;
                DataStore.Leave.Add(new DoctorLeave { LeaveId = DataStore.NextId("LV"), DoctorId = doctorId, HospitalId = hospitalId, LeaveDate = date.Date, Reason = string.IsNullOrWhiteSpace(reason) ? "Leave" : reason });
                return true;
            }
        }

        public static void RemoveLeave(string leaveId)
        {
            lock (DataStore.Sync) DataStore.Leave.RemoveAll(l => l.LeaveId == leaveId);
        }

        /* ── Appointment types ── */

        public static List<AppointmentType> ListAppointmentTypes(string hospitalId, bool includeInactive)
        {
            lock (DataStore.Sync)
                return DataStore.AppointmentTypes.Where(t => t.HospitalId == hospitalId && (includeInactive || t.IsActive)).OrderBy(t => t.SortOrder).ToList();
        }

        public static AppointmentType GetAppointmentType(string hospitalId, string code)
        {
            lock (DataStore.Sync) return DataStore.AppointmentTypes.FirstOrDefault(t => t.HospitalId == hospitalId && t.Code == code);
        }

        public static AppointmentType GetAppointmentTypeById(string id)
        {
            lock (DataStore.Sync) return DataStore.AppointmentTypes.FirstOrDefault(t => t.AppointmentTypeId == id);
        }

        /// <summary>Adds (no id) or updates a type. New codes are derived from the name and made unique.</summary>
        public static AppointmentType SaveAppointmentType(string hospitalId, string id, string name, int? duration, string color, string feeCategory, bool chargeable)
        {
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
            lock (DataStore.Sync)
            {
                var t = DataStore.AppointmentTypes.FirstOrDefault(x => x.AppointmentTypeId == id);
                if (t != null) t.IsActive = !t.IsActive;
            }
        }

        /* ── Doctor profiles & charges ── */

        public static List<string> DoctorSpecialtyIds(string doctorId)
        {
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
            lock (DataStore.Sync)
            {
                var u = record.UserId != null ? DataStore.Users.FirstOrDefault(x => x.UserId == record.UserId) : null;
                if (u == null)
                {
                    u = new AppUser { UserId = DataStore.NextId("USR"), HospitalId = hospitalId, Role = "DOCTOR", Password = "demo123", IsActive = true, Phone = "" };
                    DataStore.Users.Add(u);
                    foreach (var cat in new[] { "NEW", "FOLLOWUP" })
                        DataStore.Fees.Add(new DoctorFee { FeeScheduleId = DataStore.NextId("FEE"), HospitalId = hospitalId, DoctorId = u.UserId, ConsultationType = cat, FeeAmount = 0, Currency = "", EffectiveFrom = DateTime.Today });
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
            lock (DataStore.Sync)
            {
                var f = DataStore.Fees.FirstOrDefault(x => x.DoctorId == doctorId && x.ConsultationType == category
                    && x.EffectiveFrom.Date <= onDate.Date && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value.Date >= onDate.Date));
                return f != null ? f.FeeAmount : 0;
            }
        }

        public static List<DoctorFee> ListFees(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.Fees.Where(f => f.HospitalId == hospitalId).OrderBy(f => f.DoctorId).ThenBy(f => f.EffectiveFrom).ToList();
        }

        public static DoctorCharge GetDoctorCharge(string hospitalId, string doctorId)
        {
            int unit; decimal rate;
            int.TryParse(ResolveSetting(hospitalId, doctorId, "CHARGE_UNIT_MINS").Value, out unit);
            decimal.TryParse(ResolveSetting(hospitalId, doctorId, "CHARGE_UNIT_RATE").Value, NumberStyles.Any, CultureInfo.InvariantCulture, out rate);
            return new DoctorCharge
            {
                Model = ResolveSetting(hospitalId, doctorId, "CHARGE_MODEL").Value ?? "FLAT",
                NewFee = FlatFee(doctorId, "NEW", DateTime.Today), FollowupFee = FlatFee(doctorId, "FOLLOWUP", DateTime.Today),
                UnitMinutes = unit > 0 ? unit : 15, UnitRate = rate,
            };
        }

        /// <summary>FLAT fees are versioned: a changed amount closes the current fee row and starts a new one today.</summary>
        public static void SaveDoctorCharge(string hospitalId, string doctorId, string model, decimal newFee, decimal followupFee, int unitMinutes, decimal unitRate)
        {
            bool isUnit = model == "UNIT";
            SaveDoctorSettings(hospitalId, doctorId, new Dictionary<string, string>
            {
                { "CHARGE_MODEL", isUnit ? "UNIT" : "" },
                { "CHARGE_UNIT_MINS", isUnit ? unitMinutes.ToString() : "" },
                { "CHARGE_UNIT_RATE", isUnit ? unitRate.ToString(CultureInfo.InvariantCulture) : "" },
            });
            lock (DataStore.Sync)
            {
                var today = DateTime.Today;
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
            lock (DataStore.Sync) return DataStore.IntakeQuestions.Where(q => q.HospitalId == hospitalId && q.IsActive).OrderBy(q => q.SortOrder).ToList();
        }

        public static void AddIntakeQuestion(string hospitalId, string label, string type, string options)
        {
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
            lock (DataStore.Sync)
            {
                var q = DataStore.IntakeQuestions.FirstOrDefault(x => x.QuestionId == questionId);
                if (q != null) q.IsActive = false;
            }
        }

        public static void SubmitIntake(string hospitalId, string appointmentId, string patientId, List<IntakeAnswer> answers)
        {
            lock (DataStore.Sync)
            {
                DataStore.IntakeResponses.RemoveAll(r => r.AppointmentId == appointmentId);
                DataStore.IntakeResponses.Add(new IntakeResponse { ResponseId = DataStore.NextId("IR"), HospitalId = hospitalId, AppointmentId = appointmentId, PatientId = patientId, Answers = answers, SubmittedAt = DateTime.Now });
            }
        }

        public static ConsentForm GetConsentForm(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.ConsentForms.FirstOrDefault(f => f.HospitalId == hospitalId && f.IsActive);
        }

        /// <summary>Edits bump the form version so earlier signatures keep pointing at what was signed.</summary>
        public static ConsentForm SaveConsentForm(string hospitalId, string title, string body)
        {
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
            lock (DataStore.Sync)
            {
                if (c.AppointmentId != null)
                {
                    var linked = DataStore.Appointments.FirstOrDefault(a => a.AppointmentId == c.AppointmentId);
                    if (linked != null) return linked;
                }
                var rank = new Dictionary<string, int> { { "IN_CONSULT", 0 }, { "CHECKED_IN", 1 }, { "BOOKED", 2 }, { "COMPLETED", 3 } };
                return DataStore.Appointments
                    .Where(a => a.PatientId == c.PatientId && a.DoctorId == c.DoctorId && a.SlotStart.Date == c.VisitDate.Date && a.Status != "CANCELLED")
                    .OrderBy(a => rank.ContainsKey(a.Status) ? rank[a.Status] : 9).FirstOrDefault();
            }
        }

        public static IntakeResponse GetIntakeForAppointment(string appointmentId)
        {
            lock (DataStore.Sync) return DataStore.IntakeResponses.FirstOrDefault(r => r.AppointmentId == appointmentId);
        }

        public static ConsentRecord GetConsentRecord(Consultation c, Appointment appt)
        {
            lock (DataStore.Sync)
                return DataStore.ConsentRecords.LastOrDefault(r => r.ConsultationId == c.ConsultationId || (appt != null && r.AppointmentId == appt.AppointmentId));
        }

        /// <summary>Append-only consent signature; snapshots the current form title, text and version.</summary>
        public static ConsentRecord RecordConsent(Consultation c, string signedByName)
        {
            var form = GetConsentForm(c.HospitalId);
            if (form == null) return null;
            var appt = AppointmentForConsultation(c);
            var row = new ConsentRecord
            {
                ConsentRecordId = DataStore.NextId("CR"), ConsentFormId = form.ConsentFormId, ConsultationId = c.ConsultationId,
                AppointmentId = appt != null ? appt.AppointmentId : null, PatientId = c.PatientId, SignedByName = signedByName,
                Method = "IN_CLINIC", FormVersion = form.Version, TitleSnapshot = form.Title, BodySnapshot = form.BodyText, SignedAt = DateTime.Now
            };
            lock (DataStore.Sync) DataStore.ConsentRecords.Add(row);
            return row;
        }
    }
}
