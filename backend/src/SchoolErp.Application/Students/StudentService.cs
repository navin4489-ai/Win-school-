using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Students;

public sealed class StudentService
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".doc", ".docx" };

    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf", "image/jpeg", "image/png", "image/webp",
            "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IFileStorage _storage;
    private readonly IDateTimeProvider _clock;

    public StudentService(IAppDbContext db, IUnitOfWork uow, ICurrentUser user, IFileStorage storage,
        IDateTimeProvider clock)
    {
        _db = db;
        _uow = uow;
        _user = user;
        _storage = storage;
        _clock = clock;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    public async Task<PagedResult<StudentListItemDto>> ListAsync(StudentQuery query, CancellationToken ct = default)
    {
        var source =
            from s in _db.Students
            join klass in _db.Classes on s.ClassId equals klass.Id
            join section in _db.Sections on s.SectionId equals section.Id
            where s.SchoolId == SchoolId && !s.IsDeleted
            select new { s, ClassName = klass.Name, SectionName = section.Name };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Case-insensitive; every word must match one of the fields, so "ajay kumar",
            // "Kumar Ajay" and "AJAY" all find Ajay Kumar.
            foreach (var word in query.Search.Trim().ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
            {
                source = source.Where(x =>
                    x.s.AdmissionNo.ToLower().Contains(word) ||
                    x.s.FirstName.ToLower().Contains(word) ||
                    x.s.LastName.ToLower().Contains(word) ||
                    (x.s.Phone != null && x.s.Phone.Contains(word)));
            }
        }

        if (query.ClassId is > 0) source = source.Where(x => x.s.ClassId == query.ClassId);
        if (query.SectionId is > 0) source = source.Where(x => x.s.SectionId == query.SectionId);
        if (query.SessionId is > 0) source = source.Where(x => x.s.AcademicSessionId == query.SessionId);
        if (query.Status is not null) source = source.Where(x => x.s.Status == query.Status);
        else source = source.Where(x => x.s.Status == StudentStatus.Active);

        var page = await source.OrderBy(x => x.ClassName).ThenBy(x => x.s.FirstName)
            .Select(x => new StudentListItemDto
            {
                Id = x.s.Id, AdmissionNo = x.s.AdmissionNo,
                FullName = x.s.FirstName + " " + x.s.LastName,
                ClassName = x.ClassName, SectionName = x.SectionName, RollNo = x.s.RollNo,
                Phone = x.s.Phone, PhotoUrl = x.s.PhotoUrl, Status = x.s.Status,
                AdmissionDate = x.s.AdmissionDate
            })
            .ToPagedResultAsync(query.Page, query.PageSize, ct);

        // Sign stored file URLs after materialization (the signer cannot run inside an EF query).
        foreach (var item in page.Items) item.PhotoUrl = FileUrlSigner.SignStoredUrl(item.PhotoUrl);
        return page;
    }

    public async Task<StudentDetailDto> GetAsync(int id, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Student");

        var className = await _db.Classes.Where(c => c.Id == student.ClassId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var sectionName = await _db.Sections.Where(s => s.Id == student.SectionId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

        var guardians = await (
            from sp in _db.StudentParents
            join g in _db.Guardians on sp.GuardianId equals g.Id
            where sp.StudentId == student.Id && !sp.IsDeleted
            select new GuardianDto
            {
                Id = g.Id, FullName = g.FirstName + " " + g.LastName, Relation = g.Relation,
                Phone = g.Phone, Email = g.Email, IsPrimary = sp.IsPrimary,
                IsEmergencyContact = sp.IsEmergencyContact
            }).ToListAsync(ct);

        var enrollments = await (
            from e in _db.StudentEnrollments
            join session in _db.AcademicSessions on e.AcademicSessionId equals session.Id
            join klass in _db.Classes on e.ClassId equals klass.Id
            join section in _db.Sections on e.SectionId equals section.Id
            where e.StudentId == student.Id && !e.IsDeleted
            orderby e.StartDate descending
            select new EnrollmentDto
            {
                Id = e.Id, AcademicSessionId = e.AcademicSessionId, SessionName = session.Name,
                ClassId = e.ClassId, ClassName = klass.Name, SectionId = e.SectionId,
                SectionName = section.Name, RollNo = e.RollNo, StartDate = e.StartDate,
                EndDate = e.EndDate, Status = e.Status
            }).ToListAsync(ct);

        var documents = await (
            from sd in _db.StudentDocuments
            join d in _db.Documents on sd.DocumentId equals d.Id
            where sd.StudentId == student.Id && !sd.IsDeleted && !d.IsDeleted
            orderby sd.CreatedAtUtc descending
            select new StudentDocumentDto
            {
                Id = sd.Id, DocumentId = d.Id, FileName = d.OriginalFileName, ContentType = d.ContentType,
                SizeBytes = d.SizeBytes, DocumentType = sd.DocumentType, IsVerified = sd.IsVerified,
                CreatedAtUtc = sd.CreatedAtUtc, Url = d.PublicUrl
            }).ToListAsync(ct);
        foreach (var d in documents) d.Url = FileUrlSigner.SignStoredUrl(d.Url);

        return new StudentDetailDto
        {
            Id = student.Id, AdmissionNo = student.AdmissionNo, FullName = student.FullName,
            FirstName = student.FirstName, MiddleName = student.MiddleName, LastName = student.LastName,
            AcademicSessionId = student.AcademicSessionId, ClassId = student.ClassId, ClassName = className,
            SectionId = student.SectionId, SectionName = sectionName, RollNo = student.RollNo,
            DateOfBirth = student.DateOfBirth, Gender = student.Gender, BloodGroup = student.BloodGroup,
            Email = student.Email, Phone = student.Phone, Address = student.Address, City = student.City,
            State = student.State, PostalCode = student.PostalCode, AdmissionDate = student.AdmissionDate,
            Status = student.Status, PhotoUrl = FileUrlSigner.SignStoredUrl(student.PhotoUrl), PreviousSchool = student.PreviousSchool,
            Guardians = guardians, Enrollments = enrollments, Documents = documents
        };
    }

    public async Task<StudentDetailDto> UpdateAsync(int id, UpdateStudentRequest request, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Student");

        student.FirstName = request.FirstName.Trim();
        student.MiddleName = request.MiddleName?.Trim();
        student.LastName = request.LastName.Trim();
        student.DateOfBirth = request.DateOfBirth;
        student.Gender = request.Gender;
        student.BloodGroup = request.BloodGroup;
        student.Email = request.Email;
        student.Phone = request.Phone;
        student.Address = request.Address;
        student.City = request.City;
        student.State = request.State;
        student.PostalCode = request.PostalCode;
        // These two fields are not exposed in the edit form. A null means "not supplied",
        // so keep the stored value instead of clearing it when an edit is saved.
        if (request.MedicalNotes is not null) student.MedicalNotes = request.MedicalNotes;
        student.PreviousSchool = request.PreviousSchool;
        if (request.PhotoUrl is not null) student.PhotoUrl = request.PhotoUrl;
        student.ModifiedAtUtc = _clock.UtcNow;

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<StudentDetailDto> PromoteAsync(int id, PromoteStudentRequest request, CancellationToken ct = default)
    {
        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == SchoolId && !s.IsDeleted, token)
                ?? throw DomainException.NotFound("Student");

            if (student.Status != StudentStatus.Active)
                throw DomainException.Conflict("Only an active student can be promoted.");

            var session = await _db.AcademicSessions.FirstOrDefaultAsync(s => s.Id == request.AcademicSessionId && s.SchoolId == SchoolId, token)
                ?? throw DomainException.NotFound("Academic session");
            if (session.IsClosed) throw DomainException.Conflict("The target academic session is closed.");

            var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == request.SectionId && s.SchoolId == SchoolId, token)
                ?? throw DomainException.NotFound("Section");
            if (section.ClassId != request.ClassId)
                throw DomainException.Validation("The section does not belong to the selected class.");

            var activeEnrollments = await _db.StudentEnrollments
                .Where(e => e.StudentId == student.Id && e.Status == "Active" && !e.IsDeleted)
                .ToListAsync(token);

            foreach (var enrollment in activeEnrollments)
            {
                enrollment.Status = "Promoted";
                enrollment.EndDate = _clock.Today;
            }

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

            student.AcademicSessionId = session.Id;
            student.ClassId = section.ClassId;
            student.SectionId = section.Id;
            student.RollNo = request.RollNo;

            await _uow.SaveChangesAsync(token);
            return await GetAsync(student.Id, token);
        }, ct);
    }

    public async Task WithdrawAsync(int id, WithdrawStudentRequest request, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Student");

        if (student.Status != StudentStatus.Active)
            throw DomainException.Conflict("Only an active student can be withdrawn.");

        student.Status = StudentStatus.Withdrawn;
        student.WithdrawalReason = request.Reason.Trim();
        student.WithdrawalDate = request.WithdrawalDate ?? _clock.Today;

        var enrollments = await _db.StudentEnrollments
            .Where(e => e.StudentId == student.Id && e.Status == "Active" && !e.IsDeleted)
            .ToListAsync(ct);
        foreach (var enrollment in enrollments)
        {
            enrollment.Status = "Withdrawn";
            enrollment.EndDate = student.WithdrawalDate;
        }

        var assignments = await _db.StudentFeeAssignments
            .Where(a => a.StudentId == student.Id && a.IsActive && !a.IsDeleted)
            .ToListAsync(ct);
        foreach (var assignment in assignments) assignment.IsActive = false;

        var guardian = await GetPrimaryGuardianAsync(student.Id, ct);
        if (guardian is not null)
        {
            _db.Add(new Notification
            {
                SchoolId = SchoolId,
                StudentId = student.Id,
                GuardianId = guardian.Id,
                UserAccountId = guardian.UserAccountId,
                Title = "Student withdrawal recorded",
                Message = $"{student.FullName} has been withdrawn effective {student.WithdrawalDate:dd MMM yyyy}.",
                Channel = NotificationChannel.InApp,
                Status = NotificationStatus.Pending,
                Category = "Student",
                Recipient = guardian.Phone
            });
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task<StudentDocumentDto> UploadDocumentAsync(int studentId, Stream content, string fileName,
        string contentType, long length, DocumentType documentType, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Student");

        if (length <= 0) throw DomainException.Validation("The uploaded file is empty.");
        if (length > MaxUploadBytes) throw DomainException.Validation("File exceeds the 10 MB limit.");

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension))
            throw DomainException.Validation("Only PDF, image and Word documents are allowed.");
        if (!AllowedContentTypes.Contains(contentType))
            throw DomainException.Validation("The file content type is not allowed.");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxUploadBytes) throw DomainException.Validation("File exceeds the 10 MB limit.");
        buffer.Position = 0;

        var checksum = Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        buffer.Position = 0;

        var stored = await _storage.SaveAsync(buffer, fileName, contentType, ct);

        var document = new Document
        {
            SchoolId = SchoolId,
            BranchId = student.BranchId,
            FileName = stored.Path,
            OriginalFileName = Path.GetFileName(fileName),
            ContentType = contentType,
            SizeBytes = buffer.Length,
            StoragePath = stored.Path,
            PublicUrl = stored.Url,
            Checksum = checksum,
            UploadedBy = _user.UserId,
            IsScanned = false,
            IsClean = true
        };
        _db.Add(document);
        await _uow.SaveChangesAsync(ct);

        var link = new StudentDocument
        {
            SchoolId = SchoolId,
            StudentId = student.Id,
            DocumentId = document.Id,
            DocumentType = documentType,
            IsVerified = false
        };
        _db.Add(link);
        await _uow.SaveChangesAsync(ct);

        return new StudentDocumentDto
        {
            Id = link.Id, DocumentId = document.Id, FileName = document.OriginalFileName,
            ContentType = document.ContentType, SizeBytes = document.SizeBytes,
            DocumentType = link.DocumentType, IsVerified = false,
            CreatedAtUtc = document.CreatedAtUtc, Url = document.PublicUrl
        };
    }

    public async Task<CertificateDto> GenerateCertificateAsync(int studentId, string type, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Student");

        var school = await _db.Schools.FirstAsync(s => s.Id == SchoolId, ct);
        var className = await _db.Classes.Where(c => c.Id == student.ClassId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var sectionName = await _db.Sections.Where(s => s.Id == student.SectionId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

        var title = type.ToLowerInvariant() switch
        {
            "bonafide" => "Bonafide Certificate",
            "transfer" => "Transfer Certificate",
            "character" => "Character Certificate",
            _ => throw DomainException.Validation("Certificate type must be bonafide, transfer or character.")
        };

        var kind = type.ToLowerInvariant();
        string body;
        if (kind == "transfer")
        {
            var outstanding = await (
                from i in _db.FeeInstallments
                join a in _db.StudentFeeAssignments on i.StudentFeeAssignmentId equals a.Id
                where a.StudentId == student.Id && a.SchoolId == SchoolId && a.IsActive && !a.IsDeleted && !i.IsDeleted &&
                      i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived
                select i.Amount + i.PenaltyAmount - i.PaidAmount).ToListAsync(ct);
            var balance = outstanding.Where(x => x > 0).Sum();
            body =
                $"This is to certify that {student.FullName}, admission no. {student.AdmissionNo}, was a student of {className} - {sectionName} " +
                $"from {student.AdmissionDate:dd MMM yyyy} and has been withdrawn on {student.WithdrawalDate?.ToString("dd MMM yyyy") ?? "this date"}. " +
                (balance == 0
                    ? "All dues have been settled as per records."
                    : $"Outstanding dues: Rs. {balance:0.00} as per records.");
        }
        else if (kind == "character")
        {
            body =
                $"This is to certify that {student.FullName}, admission no. {student.AdmissionNo}, is a student of {className} - {sectionName} " +
                "and bears a good moral character. We wish them success in future endeavours.";
        }
        else
        {
            var guardian = await GetPrimaryGuardianAsync(student.Id, ct)
                ?? await (from sp in _db.StudentParents
                          join g in _db.Guardians on sp.GuardianId equals g.Id
                          where sp.StudentId == student.Id && !sp.IsDeleted && !g.IsDeleted
                          orderby sp.Id
                          select g).FirstOrDefaultAsync(ct);
            var guardianName = guardian is null ? string.Empty : $"{guardian.FirstName} {guardian.LastName}".Trim();
            if (guardianName.Length == 0) guardianName = "their parent/guardian";
            body =
                $"This is to certify that {student.FullName}, son/daughter of {guardianName}, admission no. {student.AdmissionNo}, " +
                $"is a bonafide student of {className} - {sectionName} for the current academic session.";
        }

        const string style = "body{font-family:Georgia,serif;margin:48px;color:#1f2937}" +
            ".head{text-align:center;border-bottom:2px solid #0f766e;padding-bottom:16px}" +
            "h1{margin:0;font-size:26px;color:#0f766e}h2{margin-top:28px;text-align:center;text-decoration:underline}" +
            ".meta{margin-top:24px;line-height:1.9}.sign{margin-top:64px;text-align:right}";

        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>" +
            WebUtility.HtmlEncode(title) + "</title><style>" + style + "</style></head><body>" +
            "<div class=\"head\"><h1>" + WebUtility.HtmlEncode(school.Name) + "</h1><p>" +
            WebUtility.HtmlEncode(school.AddressLine1 ?? string.Empty) + " " +
            WebUtility.HtmlEncode(school.City ?? string.Empty) + "</p></div>" +
            "<h2>" + WebUtility.HtmlEncode(title) + "</h2>" +
            "<p class=\"meta\">" + WebUtility.HtmlEncode(body) + "</p>" +
            "<p class=\"meta\">Date of issue: " + _clock.Today.ToString("dd MMMM yyyy") + "</p>" +
            "<div class=\"sign\"><p>__________________________</p><p>Principal</p></div>" +
            "</body></html>";

        return new CertificateDto
        {
            FileName = $"{type.ToLowerInvariant()}-{student.AdmissionNo}.html",
            Content = html
        };
    }

    private async Task<Guardian?> GetPrimaryGuardianAsync(int studentId, CancellationToken ct) =>
        await (from sp in _db.StudentParents
               join g in _db.Guardians on sp.GuardianId equals g.Id
               where sp.StudentId == studentId && sp.IsPrimary && !sp.IsDeleted
               select g).FirstOrDefaultAsync(ct);
}
