using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Admissions;

public sealed class AdmissionService
{
    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IPasswordHasher _passwords;
    private readonly IDateTimeProvider _clock;

    public AdmissionService(IAppDbContext db, IUnitOfWork uow, ICurrentUser user, IPasswordHasher passwords,
        IDateTimeProvider clock)
    {
        _db = db;
        _uow = uow;
        _user = user;
        _passwords = passwords;
        _clock = clock;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    public async Task<PagedResult<AdmissionDto>> ListAsync(AdmissionQuery query, CancellationToken ct = default)
    {
        var source =
            from a in _db.Admissions
            join klass in _db.Classes on a.ClassId equals klass.Id into classes
            from klass in classes.DefaultIfEmpty()
            where a.SchoolId == SchoolId && !a.IsDeleted
            select new { a, ClassName = klass == null ? null : klass.Name };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            foreach (var word in query.Search.Trim().ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
            {
                source = source.Where(x =>
                    x.a.ApplicationNo.ToLower().Contains(word) ||
                    x.a.FirstName.ToLower().Contains(word) ||
                    x.a.LastName.ToLower().Contains(word) ||
                    x.a.GuardianPhone.Contains(word) ||
                    x.a.GuardianName.ToLower().Contains(word));
            }
        }

        if (query.Status is not null) source = source.Where(x => x.a.Status == query.Status);
        if (query.SessionId is > 0) source = source.Where(x => x.a.AcademicSessionId == query.SessionId);
        if (query.ClassId is > 0) source = source.Where(x => x.a.ClassId == query.ClassId);

        var paged = await source.OrderByDescending(x => x.a.AppliedAtUtc)
            .Select(x => new AdmissionDto
            {
                Id = x.a.Id, ApplicationNo = x.a.ApplicationNo, AcademicSessionId = x.a.AcademicSessionId,
                ClassId = x.a.ClassId, ClassName = x.ClassName, SectionId = x.a.SectionId,
                FirstName = x.a.FirstName, LastName = x.a.LastName, DateOfBirth = x.a.DateOfBirth,
                Gender = x.a.Gender, GuardianName = x.a.GuardianName, GuardianPhone = x.a.GuardianPhone,
                GuardianEmail = x.a.GuardianEmail, PreviousSchool = x.a.PreviousSchool, Status = x.a.Status,
                AppliedAtUtc = x.a.AppliedAtUtc, InterviewAtUtc = x.a.InterviewAtUtc,
                RejectionReason = x.a.RejectionReason, StudentId = x.a.StudentId, Notes = x.a.Notes,
                Source = x.a.Source, IsFeePaid = x.a.IsFeePaid
            })
            .ToPagedResultAsync(query.Page, query.PageSize, ct);

        return paged;
    }

    public async Task<AdmissionDto> GetAsync(int id, CancellationToken ct = default)
    {
        var admission = await _db.Admissions.FirstOrDefaultAsync(a => a.Id == id && a.SchoolId == SchoolId && !a.IsDeleted, ct)
            ?? throw DomainException.NotFound("Admission");

        var className = admission.ClassId is null
            ? null
            : await _db.Classes.Where(c => c.Id == admission.ClassId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        return Map(admission, className);
    }

    public async Task<AdmissionDto> CreateEnquiryAsync(CreateEnquiryRequest request, CancellationToken ct = default)
    {
        var session = await ResolveSessionAsync(request.SessionId, ct);
        if (session.IsClosed) throw DomainException.Conflict("The academic session is closed for admissions.");

        if (request.ClassId is > 0)
        {
            var exists = await _db.Classes.AnyAsync(c => c.Id == request.ClassId && c.SchoolId == SchoolId && !c.IsDeleted, ct);
            if (!exists) throw DomainException.NotFound("Class");
        }

        if (request.DateOfBirth == default) throw DomainException.Validation("Date of birth is required.");

        var admission = new Admission
        {
            SchoolId = SchoolId,
            BranchId = _user.BranchId,
            AcademicSessionId = session.Id,
            ApplicationNo = await NextApplicationNoAsync(ct),
            ClassId = request.ClassId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            GuardianName = request.GuardianName.Trim(),
            GuardianPhone = request.GuardianPhone.Trim(),
            GuardianEmail = request.GuardianEmail?.Trim().ToLowerInvariant(),
            PreviousSchool = request.PreviousSchool,
            Source = request.Source,
            Notes = request.Notes,
            Status = AdmissionStatus.Enquiry,
            AppliedAtUtc = _clock.UtcNow
        };

        _db.Add(admission);
        await _uow.SaveChangesAsync(ct);
        return Map(admission, null);
    }

    public Task<AdmissionDto> SubmitAsync(int id, CancellationToken ct = default) =>
        TransitionAsync(id, new[] { AdmissionStatus.Enquiry }, AdmissionStatus.Application, null, ct);

    public Task<AdmissionDto> VerifyDocumentsAsync(int id, CancellationToken ct = default) =>
        TransitionAsync(id, new[] { AdmissionStatus.Application }, AdmissionStatus.DocumentsVerified, null, ct);

    public async Task<AdmissionDto> ScheduleInterviewAsync(int id, ScheduleInterviewRequest request, CancellationToken ct = default)
    {
        if (request.InterviewAtUtc <= _clock.UtcNow)
            throw DomainException.Validation("Interview time must be in the future.");

        var admission = await GetEntityAsync(id, ct);
        if (admission.Status is not (AdmissionStatus.Application or AdmissionStatus.DocumentsVerified))
            throw DomainException.Conflict("Interview can only be scheduled after documents are verified.");

        admission.Status = AdmissionStatus.InterviewScheduled;
        admission.InterviewAtUtc = request.InterviewAtUtc;
        await _uow.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AdmissionDto> ApproveAsync(int id, CancellationToken ct = default)
    {
        var admission = await GetEntityAsync(id, ct);
        if (admission.Status is AdmissionStatus.Enrolled or AdmissionStatus.Rejected or AdmissionStatus.Cancelled)
            throw DomainException.Conflict("This application cannot be approved in its current state.");

        var session = await _db.AcademicSessions.FirstAsync(s => s.Id == admission.AcademicSessionId, ct);
        if (session.IsClosed) throw DomainException.Conflict("The academic session is closed.");

        admission.Status = AdmissionStatus.Approved;
        admission.ApprovedAtUtc = _clock.UtcNow;
        admission.ApprovedBy = _user.UserId;
        admission.RejectionReason = null;

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AdmissionDto> RejectAsync(int id, RejectAdmissionRequest request, CancellationToken ct = default)
    {
        var admission = await GetEntityAsync(id, ct);
        if (admission.Status == AdmissionStatus.Enrolled)
            throw DomainException.Conflict("An enrolled admission cannot be rejected.");

        admission.Status = AdmissionStatus.Rejected;
        admission.RejectionReason = request.Reason.Trim();
        await _uow.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AdmissionDto> WaitlistAsync(int id, CancellationToken ct = default)
    {
        var admission = await GetEntityAsync(id, ct);
        if (admission.Status is AdmissionStatus.Enrolled or AdmissionStatus.Rejected or AdmissionStatus.Cancelled)
            throw DomainException.Conflict("This application cannot be waitlisted.");

        admission.Status = AdmissionStatus.Waitlisted;
        await _uow.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<EnrollmentResult> EnrollAsync(int id, EnrollAdmissionRequest request, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.ParentPassword) &&
            (request.ParentPassword.Length < 8 || !request.ParentPassword.Any(char.IsLetter) || !request.ParentPassword.Any(char.IsDigit)))
            throw DomainException.Validation("Parent password must be at least 8 characters and contain letters and digits.");

        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var admission = await _db.Admissions
                .FirstOrDefaultAsync(a => a.Id == id && a.SchoolId == SchoolId && !a.IsDeleted, token)
                ?? throw DomainException.NotFound("Admission");

            if (admission.Status == AdmissionStatus.Enrolled || admission.StudentId is not null)
                throw DomainException.Conflict("This application is already enrolled.");

            if (admission.Status != AdmissionStatus.Approved)
                throw DomainException.Conflict("Only an approved application can be enrolled.");

            var session = await _db.AcademicSessions.FirstAsync(s => s.Id == admission.AcademicSessionId, token);
            if (session.IsClosed) throw DomainException.Conflict("The academic session is closed.");

            var section = await _db.Sections
                .FirstOrDefaultAsync(s => s.Id == request.SectionId && s.SchoolId == SchoolId && !s.IsDeleted, token)
                ?? throw DomainException.NotFound("Section");

            if (admission.ClassId is not null && admission.ClassId != section.ClassId)
                throw DomainException.Validation("The selected section does not belong to the applied class.");

            // One enrolment at a time per school, so admission and receipt numbers never collide.
            await _uow.LockAsync($"enrol-{SchoolId}", token);
            await _uow.LockAsync($"receipt-no-{SchoolId}", token);

            var admissionNo = string.IsNullOrWhiteSpace(request.AdmissionNo)
                ? await NextAdmissionNoAsync(token)
                : request.AdmissionNo.Trim();

            var duplicate = await _db.Students.IgnoreQueryFilters()
                .AnyAsync(s => s.SchoolId == SchoolId && s.AdmissionNo == admissionNo, token);
            if (duplicate) throw DomainException.Conflict($"Admission number {admissionNo} is already in use.");

            var student = new Student
            {
                SchoolId = SchoolId,
                BranchId = admission.BranchId ?? _user.BranchId,
                AdmissionNo = admissionNo,
                AcademicSessionId = session.Id,
                ClassId = section.ClassId,
                SectionId = section.Id,
                RollNo = request.RollNo,
                FirstName = admission.FirstName,
                LastName = admission.LastName,
                DateOfBirth = admission.DateOfBirth,
                Gender = admission.Gender,
                Email = admission.GuardianEmail,
                Phone = admission.GuardianPhone,
                AdmissionDate = _clock.Today,
                Status = StudentStatus.Active,
                PreviousSchool = admission.PreviousSchool
            };
            _db.Add(student);
            await _uow.SaveChangesAsync(token);

            _db.Add(new StudentEnrollment
            {
                SchoolId = SchoolId,
                StudentId = student.Id,
                AcademicSessionId = session.Id,
                ClassId = section.ClassId,
                SectionId = section.Id,
                RollNo = request.RollNo,
                StartDate = _clock.Today,
                Status = "Active"
            });

            var guardian = await FindGuardianAsync(admission, token);
            if (guardian is null)
            {
                guardian = new Guardian
                {
                    SchoolId = SchoolId,
                    FirstName = FirstWord(admission.GuardianName),
                    LastName = RestWords(admission.GuardianName),
                    Relation = "Guardian",
                    Email = admission.GuardianEmail,
                    Phone = admission.GuardianPhone,
                    Address = null
                };
                _db.Add(guardian);
                await _uow.SaveChangesAsync(token);
            }

            var linkExists = await _db.StudentParents
                .AnyAsync(sp => sp.StudentId == student.Id && sp.GuardianId == guardian.Id, token);
            if (!linkExists)
            {
                _db.Add(new StudentParent
                {
                    SchoolId = SchoolId,
                    StudentId = student.Id,
                    GuardianId = guardian.Id,
                    IsPrimary = true,
                    IsEmergencyContact = true
                });
            }

            int? parentUserId = null;
            string? temporaryPassword = null;
            if (request.CreateParentLogin && guardian.UserAccountId is not null)
            {
                parentUserId = guardian.UserAccountId;
            }
            else if (request.CreateParentLogin)
            {
                // Parents sign in with the admission number; the email is only a fallback ID.
                var email = string.IsNullOrWhiteSpace(admission.GuardianEmail)
                    ? $"{student.AdmissionNo.ToLowerInvariant()}@parent.local"
                    : admission.GuardianEmail!.Trim().ToLowerInvariant();
                var existingUser = await _db.UserAccounts.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.SchoolId == SchoolId && u.NormalizedEmail == email && !u.IsDeleted, token);

                if (existingUser is not null)
                {
                    existingUser.GuardianId ??= guardian.Id;
                    parentUserId = existingUser.Id;
                }
                else
                {
                    var parentRole = await _db.Roles
                        .FirstOrDefaultAsync(r => r.SchoolId == SchoolId && r.Code == RoleCodes.Parent && !r.IsDeleted, token)
                        ?? throw DomainException.Conflict("The Parent role is not configured for this school.");

                    temporaryPassword = string.IsNullOrWhiteSpace(request.ParentPassword) ? GeneratePassword() : request.ParentPassword!;
                    var parentUser = new UserAccount
                    {
                        SchoolId = SchoolId,
                        BranchId = student.BranchId,
                        Email = email,
                        NormalizedEmail = email,
                        FullName = admission.GuardianName,
                        Phone = admission.GuardianPhone,
                        PasswordHash = _passwords.Hash(temporaryPassword),
                        RoleId = parentRole.Id,
                        GuardianId = guardian.Id,
                        IsActive = true,
                        EmailConfirmed = true,
                        TokenVersion = 1
                    };
                    _db.Add(parentUser);
                    await _uow.SaveChangesAsync(token);
                    parentUserId = parentUser.Id;
                    guardian.UserAccountId = parentUser.Id;
                }
            }

            admission.Status = AdmissionStatus.Enrolled;
            admission.StudentId = student.Id;
            admission.ClassId = section.ClassId;
            admission.SectionId = section.Id;

            if (request.AdmissionFeeAmount is > 0)
            {
                var school = await _db.Schools.FirstAsync(s => s.Id == SchoolId, token);
                var payment = new Payment
                {
                    SchoolId = SchoolId,
                    BranchId = student.BranchId,
                    StudentId = student.Id,
                    Amount = request.AdmissionFeeAmount.Value,
                    Mode = PaymentMode.Cash,
                    Status = PaymentStatus.Success,
                    PaidAtUtc = _clock.UtcNow,
                    ReceiptNo = await NextReceiptNoAsync(school.Code, token),
                    ReferenceNo = "Admission fee",
                    CollectedBy = _user.UserId
                };
                _db.Add(payment);
                admission.IsFeePaid = true;
            }

            _db.Add(new Notification
            {
                SchoolId = SchoolId,
                BranchId = student.BranchId,
                GuardianId = guardian.Id,
                UserAccountId = parentUserId,
                StudentId = student.Id,
                Title = "Admission confirmed",
                Message = $"Admission confirmed for {student.FullName} ({admissionNo}). Welcome to our school.",
                Channel = NotificationChannel.Sms,
                Status = NotificationStatus.Pending,
                Category = "Admission",
                Recipient = guardian.Phone
            });

            await _uow.SaveChangesAsync(token);

            return new EnrollmentResult
            {
                StudentId = student.Id,
                AdmissionNo = admissionNo,
                StudentName = student.FullName,
                GuardianId = guardian.Id,
                ParentUserAccountId = parentUserId,
                ParentEmail = parentUserId is null ? null : admission.GuardianEmail,
                ParentLoginCreated = parentUserId is not null && temporaryPassword is not null,
                TemporaryPassword = temporaryPassword
            };
        }, ct);
    }

    private async Task<AdmissionDto> TransitionAsync(int id, AdmissionStatus[] from, AdmissionStatus to,
        string? note, CancellationToken ct)
    {
        var admission = await GetEntityAsync(id, ct);
        if (!from.Contains(admission.Status))
            throw DomainException.Conflict($"Application cannot move from {admission.Status} to {to}.");

        admission.Status = to;
        if (note is not null) admission.Notes = note;
        await _uow.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task<Admission> GetEntityAsync(int id, CancellationToken ct) =>
        await _db.Admissions.FirstOrDefaultAsync(a => a.Id == id && a.SchoolId == SchoolId && !a.IsDeleted, ct)
        ?? throw DomainException.NotFound("Admission");

    private async Task<AcademicSession> ResolveSessionAsync(int? sessionId, CancellationToken ct)
    {
        if (sessionId is > 0)
        {
            return await _db.AcademicSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
                ?? throw DomainException.NotFound("Academic session");
        }

        return await _db.AcademicSessions
            .FirstOrDefaultAsync(s => s.SchoolId == SchoolId && s.IsCurrent && !s.IsDeleted, ct)
            ?? throw DomainException.Validation("No current academic session is configured.");
    }

    private async Task<Guardian?> FindGuardianAsync(Admission admission, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(admission.GuardianEmail))
        {
            var byEmail = await _db.Guardians.FirstOrDefaultAsync(g =>
                g.SchoolId == SchoolId && g.Email == admission.GuardianEmail && !g.IsDeleted, ct);
            if (byEmail is not null) return byEmail;
        }

        return await _db.Guardians.FirstOrDefaultAsync(g =>
            g.SchoolId == SchoolId && g.Phone == admission.GuardianPhone && !g.IsDeleted, ct);
    }

    private async Task<string> NextApplicationNoAsync(CancellationToken ct)
    {
        var year = _clock.Today.Year;
        for (var i = 1; i < 10000; i++)
        {
            var candidate = $"ADM-{year}-{i:0000}";
            var exists = await _db.Admissions.IgnoreQueryFilters()
                .AnyAsync(a => a.SchoolId == SchoolId && a.ApplicationNo == candidate, ct);
            if (!exists) return candidate;
        }

        throw DomainException.Conflict("Application number sequence exhausted for the year.");
    }

    private async Task<string> NextAdmissionNoAsync(CancellationToken ct)
    {
        var year = _clock.Today.Year;
        for (var i = 1; i < 100000; i++)
        {
            var candidate = $"STU-{year}-{i:00000}";
            var exists = await _db.Students.IgnoreQueryFilters()
                .AnyAsync(s => s.SchoolId == SchoolId && s.AdmissionNo == candidate, ct);
            if (!exists) return candidate;
        }

        throw DomainException.Conflict("Admission number sequence exhausted for the year.");
    }

    private async Task<string> NextReceiptNoAsync(string schoolCode, CancellationToken ct)
    {
        var prefix = $"RCPT-{schoolCode}-{_clock.Today:yyyyMMdd}-";
        for (var i = 1; i < 100000; i++)
        {
            var candidate = prefix + i.ToString("0000");
            var exists = await _db.Payments.IgnoreQueryFilters()
                .AnyAsync(p => p.SchoolId == SchoolId && p.ReceiptNo == candidate, ct);
            if (!exists) return candidate;
        }

        throw DomainException.Conflict("Receipt number sequence exhausted for today.");
    }

    private static string GeneratePassword()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        return $"Wel{token}1!";
    }

    private static string FirstWord(string name)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? name : parts[0];
    }

    private static string RestWords(string name)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1 ? string.Empty : string.Join(' ', parts.Skip(1));
    }

    private static AdmissionDto Map(Admission a, string? className) => new()
    {
        Id = a.Id, ApplicationNo = a.ApplicationNo, AcademicSessionId = a.AcademicSessionId,
        ClassId = a.ClassId, ClassName = className, SectionId = a.SectionId,
        FirstName = a.FirstName, LastName = a.LastName, DateOfBirth = a.DateOfBirth, Gender = a.Gender,
        GuardianName = a.GuardianName, GuardianPhone = a.GuardianPhone, GuardianEmail = a.GuardianEmail,
        PreviousSchool = a.PreviousSchool, Status = a.Status, AppliedAtUtc = a.AppliedAtUtc,
        InterviewAtUtc = a.InterviewAtUtc, RejectionReason = a.RejectionReason, StudentId = a.StudentId,
        Notes = a.Notes, Source = a.Source, IsFeePaid = a.IsFeePaid
    };
}
