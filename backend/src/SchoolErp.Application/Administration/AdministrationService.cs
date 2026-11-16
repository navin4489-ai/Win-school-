using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Administration;

public sealed class AdministrationService
{
    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IDateTimeProvider _clock;

    public AdministrationService(IAppDbContext db, IUnitOfWork uow, ICurrentUser user, IDateTimeProvider clock)
    {
        _db = db;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    // ---------- Teacher assignments ----------

    public async Task<List<TeacherAssignmentDto>> GetTeacherAssignmentsAsync(int? classId, int? sectionId,
        CancellationToken ct = default)
    {
        var query =
            from t in _db.TeacherAssignments
            join e in _db.Employees on t.EmployeeId equals e.Id
            join c in _db.Classes on t.ClassId equals c.Id
            join s in _db.Sections on t.SectionId equals s.Id
            where t.SchoolId == SchoolId && !t.IsDeleted && !e.IsDeleted
            select new { t, e, c, s };

        if (classId is > 0) query = query.Where(x => x.t.ClassId == classId);
        if (sectionId is > 0) query = query.Where(x => x.t.SectionId == sectionId);

        var rows = await query.OrderBy(x => x.c.DisplayOrder).ThenBy(x => x.s.Name).ThenBy(x => x.e.FirstName)
            .ToListAsync(ct);

        var subjectIds = rows.Where(x => x.t.SubjectId is not null).Select(x => x.t.SubjectId!.Value).Distinct().ToList();
        var subjects = await _db.Subjects.Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return rows.Select(x => new TeacherAssignmentDto
        {
            Id = x.t.Id,
            EmployeeId = x.e.Id,
            EmployeeName = x.e.FullName,
            EmployeeNo = x.e.EmployeeNo,
            ClassId = x.c.Id,
            ClassName = x.c.Name,
            SectionId = x.s.Id,
            SectionName = x.s.Name,
            SubjectId = x.t.SubjectId,
            SubjectName = x.t.SubjectId is null ? null : subjects.GetValueOrDefault(x.t.SubjectId.Value),
            IsClassTeacher = x.t.IsClassTeacher
        }).ToList();
    }

    public async Task<TeacherAssignmentDto> SaveTeacherAssignmentAsync(int? id, SaveTeacherAssignmentRequest request,
        CancellationToken ct = default)
    {
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.SchoolId == SchoolId && !e.IsDeleted, ct)
            ?? throw DomainException.NotFound("Employee");

        if (employee.Status != EmploymentStatus.Active)
            throw DomainException.Validation("Only an active employee can be assigned.");

        var section = await _db.Sections
            .FirstOrDefaultAsync(s => s.Id == request.SectionId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Section");

        if (section.ClassId != request.ClassId)
            throw DomainException.Validation("The section does not belong to the class.");

        if (request.SubjectId is > 0)
        {
            var subjectExists = await _db.Subjects.AnyAsync(s => s.Id == request.SubjectId && s.SchoolId == SchoolId && !s.IsDeleted, ct);
            if (!subjectExists) throw DomainException.NotFound("Subject");
        }

        var duplicate = await _db.TeacherAssignments.AnyAsync(t =>
            t.SchoolId == SchoolId && t.EmployeeId == request.EmployeeId && t.ClassId == request.ClassId &&
            t.SectionId == request.SectionId && t.SubjectId == request.SubjectId && !t.IsDeleted, ct);

        if (id is null && duplicate)
            throw DomainException.Conflict("This teacher is already assigned to that class and section.");

        TeacherAssignment assignment;
        if (id is null)
        {
            assignment = new TeacherAssignment
            {
                SchoolId = SchoolId,
                EmployeeId = request.EmployeeId,
                ClassId = request.ClassId,
                SectionId = request.SectionId
            };
            _db.Add(assignment);
        }
        else
        {
            assignment = await _db.TeacherAssignments
                .FirstOrDefaultAsync(t => t.Id == id && t.SchoolId == SchoolId && !t.IsDeleted, ct)
                ?? throw DomainException.NotFound("Assignment");

            assignment.EmployeeId = request.EmployeeId;
            assignment.ClassId = request.ClassId;
            assignment.SectionId = request.SectionId;
        }

        assignment.SubjectId = request.SubjectId;
        assignment.IsClassTeacher = request.IsClassTeacher;

        // The section keeps a single class teacher; assigning one clears the previous holder.
        if (request.IsClassTeacher)
        {
            var other = await _db.TeacherAssignments
                .Where(t => t.SchoolId == SchoolId && t.SectionId == request.SectionId &&
                            t.IsClassTeacher && t.EmployeeId != request.EmployeeId && !t.IsDeleted)
                .ToListAsync(ct);

            foreach (var item in other) item.IsClassTeacher = false;

            section.ClassTeacherId = request.EmployeeId;
        }
        else if (section.ClassTeacherId == request.EmployeeId)
        {
            section.ClassTeacherId = null;
        }

        await _uow.SaveChangesAsync(ct);

        var className = await _db.Classes.Where(c => c.Id == request.ClassId).Select(c => c.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;
        var subjectName = request.SubjectId is null
            ? null
            : await _db.Subjects.Where(s => s.Id == request.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct);

        return new TeacherAssignmentDto
        {
            Id = assignment.Id,
            EmployeeId = employee.Id,
            EmployeeName = employee.FullName,
            EmployeeNo = employee.EmployeeNo,
            ClassId = request.ClassId,
            ClassName = className,
            SectionId = section.Id,
            SectionName = section.Name,
            SubjectId = request.SubjectId,
            SubjectName = subjectName,
            IsClassTeacher = assignment.IsClassTeacher
        };
    }

    public async Task DeleteTeacherAssignmentAsync(int id, CancellationToken ct = default)
    {
        var assignment = await _db.TeacherAssignments
            .FirstOrDefaultAsync(t => t.Id == id && t.SchoolId == SchoolId && !t.IsDeleted, ct)
            ?? throw DomainException.NotFound("Assignment");

        if (assignment.IsClassTeacher)
        {
            var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == assignment.SectionId, ct);
            if (section is not null && section.ClassTeacherId == assignment.EmployeeId)
                section.ClassTeacherId = null;
        }

        assignment.IsDeleted = true;
        await _uow.SaveChangesAsync(ct);
    }

    // ---------- School ----------

    public async Task<SchoolDto> GetSchoolAsync(CancellationToken ct = default)
    {
        var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == SchoolId, ct)
            ?? throw DomainException.NotFound("School");

        return new SchoolDto
        {
            Id = school.Id, Code = school.Code, Name = school.Name, LegalName = school.LegalName,
            LogoUrl = FileUrlSigner.SignStoredUrl(school.LogoUrl), Email = school.Email, Phone = school.Phone,
            AddressLine1 = school.AddressLine1, AddressLine2 = school.AddressLine2, City = school.City,
            State = school.State, PostalCode = school.PostalCode, Country = school.Country,
            TimeZone = school.TimeZone, CurrencyCode = school.CurrencyCode
        };
    }

    public async Task<SchoolDto> UpdateSchoolAsync(UpdateSchoolRequest request, CancellationToken ct = default)
    {
        var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == SchoolId, ct)
            ?? throw DomainException.NotFound("School");

        school.Name = request.Name.Trim();
        school.LegalName = request.LegalName?.Trim();
        school.LogoUrl = request.LogoUrl;
        school.Email = request.Email;
        school.Phone = request.Phone;
        school.AddressLine1 = request.AddressLine1;
        school.AddressLine2 = request.AddressLine2;
        school.City = request.City;
        school.State = request.State;
        school.PostalCode = request.PostalCode;
        school.Country = request.Country ?? school.Country;
        school.TimeZone = request.TimeZone ?? school.TimeZone;
        school.CurrencyCode = request.CurrencyCode ?? school.CurrencyCode;
        school.ModifiedAtUtc = _clock.UtcNow;

        await _uow.SaveChangesAsync(ct);
        return await GetSchoolAsync(ct);
    }

    // ---------- Branches ----------

    public async Task<List<BranchDto>> GetBranchesAsync(CancellationToken ct = default) =>
        await _db.Branches.Where(b => b.SchoolId == SchoolId && !b.IsDeleted)
            .OrderByDescending(b => b.IsMainBranch).ThenBy(b => b.Name)
            .Select(b => new BranchDto
            {
                Id = b.Id, Name = b.Name, Code = b.Code, Address = b.Address,
                Phone = b.Phone, IsMainBranch = b.IsMainBranch
            }).ToListAsync(ct);

    public async Task<BranchDto> SaveBranchAsync(int? id, SaveBranchRequest request, CancellationToken ct = default)
    {
        var duplicate = await _db.Branches.AnyAsync(b => b.SchoolId == SchoolId && b.Code == request.Code && b.Id != (id ?? 0), ct);
        if (duplicate) throw DomainException.Conflict("A branch with this code already exists.");

        Branch branch;
        if (id is null or 0)
        {
            branch = new Branch { SchoolId = SchoolId };
            _db.Add(branch);
        }
        else
        {
            branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == id && b.SchoolId == SchoolId, ct)
                ?? throw DomainException.NotFound("Branch");
        }

        branch.Name = request.Name.Trim();
        branch.Code = request.Code.Trim();
        branch.Address = request.Address;
        branch.Phone = request.Phone;
        branch.IsMainBranch = request.IsMainBranch;

        if (request.IsMainBranch)
        {
            var others = await _db.Branches
                .Where(b => b.SchoolId == SchoolId && b.Id != branch.Id && b.IsMainBranch)
                .ToListAsync(ct);
            foreach (var other in others) other.IsMainBranch = false;
        }

        await _uow.SaveChangesAsync(ct);
        return new BranchDto
        {
            Id = branch.Id, Name = branch.Name, Code = branch.Code, Address = branch.Address,
            Phone = branch.Phone, IsMainBranch = branch.IsMainBranch
        };
    }

    // ---------- Academic sessions ----------

    public async Task<List<AcademicSessionDto>> GetSessionsAsync(CancellationToken ct = default) =>
        await _db.AcademicSessions.Where(s => s.SchoolId == SchoolId && !s.IsDeleted)
            .OrderByDescending(s => s.StartDate)
            .Select(s => new AcademicSessionDto
            {
                Id = s.Id, Name = s.Name, StartDate = s.StartDate, EndDate = s.EndDate,
                IsCurrent = s.IsCurrent, IsClosed = s.IsClosed
            }).ToListAsync(ct);

    public async Task<AcademicSessionDto> SaveSessionAsync(int? id, SaveAcademicSessionRequest request, CancellationToken ct = default)
    {
        if (request.EndDate <= request.StartDate)
            throw DomainException.Validation("Session end date must be after the start date.");

        AcademicSession session;
        if (id is null or 0)
        {
            session = new AcademicSession { SchoolId = SchoolId };
            _db.Add(session);
        }
        else
        {
            session = await _db.AcademicSessions.FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == SchoolId, ct)
                ?? throw DomainException.NotFound("Academic session");

            if (session.IsClosed) throw DomainException.Conflict("A closed academic session cannot be modified.");
        }

        session.Name = request.Name.Trim();
        session.StartDate = request.StartDate;
        session.EndDate = request.EndDate;

        if (request.IsCurrent && !session.IsClosed)
        {
            var current = await _db.AcademicSessions
                .Where(s => s.SchoolId == SchoolId && s.Id != session.Id && s.IsCurrent)
                .ToListAsync(ct);
            foreach (var other in current) other.IsCurrent = false;
            session.IsCurrent = true;
        }

        await _uow.SaveChangesAsync(ct);
        return new AcademicSessionDto
        {
            Id = session.Id, Name = session.Name, StartDate = session.StartDate,
            EndDate = session.EndDate, IsCurrent = session.IsCurrent, IsClosed = session.IsClosed
        };
    }

    public async Task CloseSessionAsync(int id, CancellationToken ct = default)
    {
        var session = await _db.AcademicSessions.FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == SchoolId, ct)
            ?? throw DomainException.NotFound("Academic session");

        if (session.IsClosed) throw DomainException.Conflict("Session is already closed.");
        session.IsClosed = true;
        session.IsCurrent = false;
        await _uow.SaveChangesAsync(ct);
    }

    // ---------- Classes / sections / subjects ----------

    public async Task<List<ClassDto>> GetClassesAsync(CancellationToken ct = default) =>
        await _db.Classes.Where(c => c.SchoolId == SchoolId && !c.IsDeleted)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new ClassDto
            {
                Id = c.Id, Name = c.Name, Code = c.Code, Stream = c.Stream, DisplayOrder = c.DisplayOrder
            }).ToListAsync(ct);

    public async Task<ClassDto> SaveClassAsync(int? id, SaveClassRequest request, CancellationToken ct = default)
    {
        var duplicate = await _db.Classes.AnyAsync(c => c.SchoolId == SchoolId && c.Code == request.Code && c.Id != (id ?? 0), ct);
        if (duplicate) throw DomainException.Conflict("A class with this code already exists.");

        SchoolClass entity;
        if (id is null or 0)
        {
            entity = new SchoolClass { SchoolId = SchoolId };
            _db.Add(entity);
        }
        else
        {
            entity = await _db.Classes.FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == SchoolId, ct)
                ?? throw DomainException.NotFound("Class");
        }

        entity.Name = request.Name.Trim();
        entity.Code = request.Code.Trim();
        entity.Stream = request.Stream;
        entity.DisplayOrder = request.DisplayOrder;

        await _uow.SaveChangesAsync(ct);
        return new ClassDto
        {
            Id = entity.Id, Name = entity.Name, Code = entity.Code,
            Stream = entity.Stream, DisplayOrder = entity.DisplayOrder
        };
    }

    public async Task<List<SectionDto>> GetSectionsAsync(int classId, CancellationToken ct = default)
    {
        var counts = await _db.Students
            .Where(s => s.SchoolId == SchoolId && s.ClassId == classId && s.Status == StudentStatus.Active && !s.IsDeleted)
            .GroupBy(s => s.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SectionId, x => x.Count, ct);

        var sections = await _db.Sections
            .Where(s => s.SchoolId == SchoolId && s.ClassId == classId && !s.IsDeleted)
            .OrderBy(s => s.Name).ToListAsync(ct);

        return sections.Select(s => new SectionDto
        {
            Id = s.Id, ClassId = s.ClassId, Name = s.Name, Capacity = s.Capacity,
            ClassTeacherId = s.ClassTeacherId, StudentCount = counts.GetValueOrDefault(s.Id)
        }).ToList();
    }

    public async Task<SectionDto> SaveSectionAsync(int classId, int? id, SaveSectionRequest request, CancellationToken ct = default)
    {
        var exists = await _db.Classes.AnyAsync(c => c.Id == classId && c.SchoolId == SchoolId && !c.IsDeleted, ct);
        if (!exists) throw DomainException.NotFound("Class");

        var duplicate = await _db.Sections.AnyAsync(s =>
            s.SchoolId == SchoolId && s.ClassId == classId && s.Name == request.Name && s.Id != (id ?? 0), ct);
        if (duplicate) throw DomainException.Conflict("A section with this name already exists for the class.");

        Section entity;
        if (id is null or 0)
        {
            entity = new Section { SchoolId = SchoolId, ClassId = classId };
            _db.Add(entity);
        }
        else
        {
            entity = await _db.Sections.FirstOrDefaultAsync(s => s.Id == id && s.ClassId == classId && s.SchoolId == SchoolId, ct)
                ?? throw DomainException.NotFound("Section");
        }

        entity.Name = request.Name.Trim();
        entity.Capacity = request.Capacity;
        if (request.ClassTeacherId is > 0 &&
            !await _db.Employees.AnyAsync(e => e.Id == request.ClassTeacherId && e.SchoolId == SchoolId && !e.IsDeleted, ct))
            throw DomainException.NotFound("Class teacher");
        entity.ClassTeacherId = request.ClassTeacherId is > 0 ? request.ClassTeacherId : null;

        await _uow.SaveChangesAsync(ct);
        return new SectionDto
        {
            Id = entity.Id, ClassId = entity.ClassId, Name = entity.Name,
            Capacity = entity.Capacity, ClassTeacherId = entity.ClassTeacherId
        };
    }

    public async Task DeleteClassAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Classes.FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == SchoolId && !c.IsDeleted, ct)
            ?? throw DomainException.NotFound("Class");

        if (await _db.Students.AnyAsync(s => s.SchoolId == SchoolId && s.ClassId == id && !s.IsDeleted, ct))
            throw DomainException.Conflict($"{entity.Name} has students enrolled. Move or remove them before deleting the class.");

        var sections = await _db.Sections.Where(s => s.SchoolId == SchoolId && s.ClassId == id && !s.IsDeleted).ToListAsync(ct);
        var classSubjects = await _db.ClassSubjects.Where(cs => cs.SchoolId == SchoolId && cs.ClassId == id && !cs.IsDeleted).ToListAsync(ct);
        var assignments = await _db.TeacherAssignments.Where(t => t.SchoolId == SchoolId && t.ClassId == id && !t.IsDeleted).ToListAsync(ct);

        foreach (var section in sections) section.IsDeleted = true;
        foreach (var classSubject in classSubjects) classSubject.IsDeleted = true;
        foreach (var assignment in assignments) assignment.IsDeleted = true;
        entity.IsDeleted = true;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteSectionAsync(int classId, int id, CancellationToken ct = default)
    {
        var entity = await _db.Sections.FirstOrDefaultAsync(s =>
            s.Id == id && s.ClassId == classId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Section");

        if (await _db.Students.AnyAsync(s => s.SchoolId == SchoolId && s.SectionId == id && !s.IsDeleted, ct))
            throw DomainException.Conflict($"Section {entity.Name} has students enrolled. Move them to another section before deleting it.");

        var assignments = await _db.TeacherAssignments.Where(t => t.SchoolId == SchoolId && t.SectionId == id && !t.IsDeleted).ToListAsync(ct);
        foreach (var assignment in assignments) assignment.IsDeleted = true;
        entity.IsDeleted = true;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteSubjectAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Subject");

        var usage = new List<string>();
        if (await _db.Homework.AnyAsync(h => h.SchoolId == SchoolId && h.SubjectId == id && !h.IsDeleted, ct)) usage.Add("homework");
        if (await _db.ExamSubjects.AnyAsync(es => es.SchoolId == SchoolId && es.SubjectId == id && !es.IsDeleted, ct)) usage.Add("exams");
        if (await _db.ClassSubjects.AnyAsync(cs => cs.SchoolId == SchoolId && cs.SubjectId == id && !cs.IsDeleted, ct)) usage.Add("class subjects");
        if (await _db.TeacherAssignments.AnyAsync(t => t.SchoolId == SchoolId && t.SubjectId == id && !t.IsDeleted, ct)) usage.Add("teacher assignments");
        if (await _db.TimetableEntries.AnyAsync(t => t.SchoolId == SchoolId && t.SubjectId == id && !t.IsDeleted, ct)) usage.Add("timetable");

        if (usage.Count > 0)
            throw DomainException.Conflict($"{entity.Name} is in use by {string.Join(", ", usage)} and cannot be deleted.");

        entity.IsDeleted = true;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<SubjectDto>> GetSubjectsAsync(CancellationToken ct = default) =>
        await _db.Subjects.Where(s => s.SchoolId == SchoolId && !s.IsDeleted)
            .OrderBy(s => s.Name)
            .Select(s => new SubjectDto { Id = s.Id, Name = s.Name, Code = s.Code, IsElective = s.IsElective })
            .ToListAsync(ct);

    public async Task<SubjectDto> SaveSubjectAsync(int? id, SaveSubjectRequest request, CancellationToken ct = default)
    {
        var duplicate = await _db.Subjects.AnyAsync(s => s.SchoolId == SchoolId && s.Code == request.Code && s.Id != (id ?? 0), ct);
        if (duplicate) throw DomainException.Conflict("A subject with this code already exists.");

        Subject entity;
        if (id is null or 0)
        {
            entity = new Subject { SchoolId = SchoolId };
            _db.Add(entity);
        }
        else
        {
            entity = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == SchoolId, ct)
                ?? throw DomainException.NotFound("Subject");
        }

        entity.Name = request.Name.Trim();
        entity.Code = request.Code.Trim();
        entity.IsElective = request.IsElective;

        await _uow.SaveChangesAsync(ct);
        return new SubjectDto { Id = entity.Id, Name = entity.Name, Code = entity.Code, IsElective = entity.IsElective };
    }

    public async Task<List<ClassSubjectDto>> GetClassSubjectsAsync(int classId, CancellationToken ct = default)
    {
        var query =
            from cs in _db.ClassSubjects
            join subject in _db.Subjects on cs.SubjectId equals subject.Id
            join teacher in _db.Employees on cs.TeacherId equals teacher.Id into teachers
            from teacher in teachers.DefaultIfEmpty()
            where cs.SchoolId == SchoolId && cs.ClassId == classId && !cs.IsDeleted
            orderby subject.Name
            select new ClassSubjectDto
            {
                Id = cs.Id,
                ClassId = cs.ClassId,
                SubjectId = cs.SubjectId,
                SubjectName = subject.Name,
                TeacherId = cs.TeacherId,
                TeacherName = teacher == null ? null : teacher.FirstName + " " + teacher.LastName
            };

        return await query.ToListAsync(ct);
    }

    public async Task<ClassSubjectDto> SaveClassSubjectAsync(int classId, int? id, SaveClassSubjectRequest request, CancellationToken ct = default)
    {
        var classExists = await _db.Classes.AnyAsync(c => c.Id == classId && c.SchoolId == SchoolId && !c.IsDeleted, ct);
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == request.SubjectId && s.SchoolId == SchoolId && !s.IsDeleted, ct);
        if (!classExists) throw DomainException.NotFound("Class");
        if (subject is null) throw DomainException.NotFound("Subject");

        var duplicate = await _db.ClassSubjects.AnyAsync(cs =>
            cs.SchoolId == SchoolId && cs.ClassId == classId && cs.SubjectId == request.SubjectId && cs.Id != (id ?? 0), ct);
        if (duplicate) throw DomainException.Conflict("This subject is already mapped to the class.");

        ClassSubject entity;
        if (id is null or 0)
        {
            entity = new ClassSubject { SchoolId = SchoolId, ClassId = classId };
            _db.Add(entity);
        }
        else
        {
            entity = await _db.ClassSubjects.FirstOrDefaultAsync(cs => cs.Id == id && cs.ClassId == classId && cs.SchoolId == SchoolId, ct)
                ?? throw DomainException.NotFound("Class subject");
        }

        entity.SubjectId = request.SubjectId;
        entity.TeacherId = request.TeacherId;
        await _uow.SaveChangesAsync(ct);

        return new ClassSubjectDto
        {
            Id = entity.Id, ClassId = entity.ClassId, SubjectId = entity.SubjectId,
            SubjectName = subject.Name, TeacherId = entity.TeacherId
        };
    }

    // ---------- Roles and permissions ----------

    public async Task<List<RoleDto>> GetRolesAsync(CancellationToken ct = default)
    {
        var roles = await _db.Roles.Where(r => r.SchoolId == SchoolId && !r.IsDeleted)
            .OrderBy(r => r.Name).ToListAsync(ct);

        var map = await (
            from rp in _db.RolePermissions
            join p in _db.Permissions on rp.PermissionId equals p.Id
            join r in _db.Roles on rp.RoleId equals r.Id
            where r.SchoolId == SchoolId
            select new { rp.RoleId, p.Code }).ToListAsync(ct);

        return roles.Select(r => new RoleDto
        {
            Id = r.Id, Name = r.Name, Code = r.Code, Description = r.Description, IsSystem = r.IsSystem,
            Permissions = map.Where(x => x.RoleId == r.Id).Select(x => x.Code).ToList()
        }).ToList();
    }

    public async Task<List<PermissionDto>> GetPermissionsAsync(CancellationToken ct = default) =>
        await _db.Permissions.OrderBy(p => p.Module).ThenBy(p => p.Action)
            .Select(p => new PermissionDto
            {
                Id = p.Id, Code = p.Code, Module = p.Module, Action = p.Action, Description = p.Description
            }).ToListAsync(ct);

    public async Task<RoleDto> UpdateRolePermissionsAsync(int roleId, List<int> permissionIds, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId && r.SchoolId == SchoolId, ct)
            ?? throw DomainException.NotFound("Role");

        if (role.Code == RoleCodes.SuperAdmin && !_user.IsInRole(RoleCodes.SuperAdmin))
            throw DomainException.Forbidden("Only a Super Admin can change Super Admin permissions.");

        var validIds = await _db.Permissions.Where(p => permissionIds.Contains(p.Id))
            .Select(p => p.Id).ToListAsync(ct);

        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        foreach (var rp in existing) _db.Remove(rp);

        foreach (var permissionId in validIds.Distinct())
            _db.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });

        await _uow.SaveChangesAsync(ct);

        var codes = await _db.Permissions.Where(p => validIds.Contains(p.Id)).Select(p => p.Code).ToListAsync(ct);
        return new RoleDto
        {
            Id = role.Id, Name = role.Name, Code = role.Code,
            Description = role.Description, IsSystem = role.IsSystem, Permissions = codes
        };
    }

    // ---------- Timetable ----------

    public async Task<List<TimetableEntryDto>> GetTimetableAsync(int? classId, int? sectionId, int? employeeId,
        CancellationToken ct = default)
    {
        var query =
            from t in _db.TimetableEntries
            join subject in _db.Subjects on t.SubjectId equals subject.Id
            join teacher in _db.Employees on t.EmployeeId equals teacher.Id
            join klass in _db.Classes on t.ClassId equals klass.Id
            where t.SchoolId == SchoolId && !t.IsDeleted
            select new { t, subject, teacher, klass };

        if (classId is > 0) query = query.Where(x => x.t.ClassId == classId);
        if (sectionId is > 0) query = query.Where(x => x.t.SectionId == sectionId);
        if (employeeId is > 0) query = query.Where(x => x.t.EmployeeId == employeeId);

        return await query.OrderBy(x => x.t.DayOfWeek).ThenBy(x => x.t.PeriodNo)
            .Select(x => new TimetableEntryDto
            {
                Id = x.t.Id, ClassId = x.t.ClassId, SectionId = x.t.SectionId,
                SubjectId = x.t.SubjectId, SubjectName = x.subject.Name,
                EmployeeId = x.t.EmployeeId, TeacherName = x.teacher.FirstName + " " + x.teacher.LastName,
                DayOfWeek = x.t.DayOfWeek, PeriodNo = x.t.PeriodNo,
                StartTime = x.t.StartTime, EndTime = x.t.EndTime, Room = x.t.Room
            }).ToListAsync(ct);
    }

    public async Task<TimetableEntryDto> SaveTimetableEntryAsync(int? id, SaveTimetableEntryRequest request,
        CancellationToken ct = default)
    {
        if (request.EndTime <= request.StartTime)
            throw DomainException.Validation("Period end time must be after the start time.");

        var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == request.SectionId && s.SchoolId == SchoolId, ct)
            ?? throw DomainException.NotFound("Section");
        if (section.ClassId != request.ClassId)
            throw DomainException.Validation("The section does not belong to the selected class.");
        if (!await _db.Subjects.AnyAsync(s => s.Id == request.SubjectId && s.SchoolId == SchoolId && !s.IsDeleted, ct))
            throw DomainException.NotFound("Subject");
        if (!await _db.Employees.AnyAsync(e => e.Id == request.EmployeeId && e.SchoolId == SchoolId && !e.IsDeleted, ct))
            throw DomainException.NotFound("Teacher");

        var conflict = await _db.TimetableEntries.AnyAsync(t =>
            t.SchoolId == SchoolId && t.Id != (id ?? 0) && t.DayOfWeek == request.DayOfWeek &&
            t.PeriodNo == request.PeriodNo &&
            (t.SectionId == request.SectionId || t.EmployeeId == request.EmployeeId ||
             (request.Room != null && t.Room == request.Room)), ct);

        if (conflict)
            throw DomainException.Conflict("A timetable conflict exists for this section, teacher or room.");

        TimetableEntry entity;
        if (id is null or 0)
        {
            entity = new TimetableEntry { SchoolId = SchoolId };
            _db.Add(entity);
        }
        else
        {
            entity = await _db.TimetableEntries.FirstOrDefaultAsync(t => t.Id == id && t.SchoolId == SchoolId, ct)
                ?? throw DomainException.NotFound("Timetable entry");
        }

        entity.ClassId = request.ClassId;
        entity.SectionId = request.SectionId;
        entity.SubjectId = request.SubjectId;
        entity.EmployeeId = request.EmployeeId;
        entity.DayOfWeek = request.DayOfWeek;
        entity.PeriodNo = request.PeriodNo;
        entity.StartTime = request.StartTime;
        entity.EndTime = request.EndTime;
        entity.Room = request.Room;

        await _uow.SaveChangesAsync(ct);

        var subject = await _db.Subjects.FirstAsync(s => s.Id == request.SubjectId, ct);
        var teacher = await _db.Employees.FirstAsync(e => e.Id == request.EmployeeId, ct);

        return new TimetableEntryDto
        {
            Id = entity.Id, ClassId = entity.ClassId, SectionId = entity.SectionId,
            SubjectId = entity.SubjectId, SubjectName = subject.Name,
            EmployeeId = entity.EmployeeId, TeacherName = teacher.FullName,
            DayOfWeek = entity.DayOfWeek, PeriodNo = entity.PeriodNo,
            StartTime = entity.StartTime, EndTime = entity.EndTime, Room = entity.Room
        };
    }

    // ---------- Announcements ----------

    public async Task<List<AnnouncementDto>> GetAnnouncementsAsync(CancellationToken ct = default) =>
        await _db.Announcements.Where(a => a.SchoolId == SchoolId && !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new AnnouncementDto
            {
                Id = a.Id, Title = a.Title, Body = a.Body, Audience = a.Audience,
                PublishedAtUtc = a.PublishedAtUtc, ExpiresAtUtc = a.ExpiresAtUtc, CreatedAtUtc = a.CreatedAtUtc
            }).ToListAsync(ct);

    public async Task<AnnouncementDto> CreateAnnouncementAsync(SaveAnnouncementRequest request, CancellationToken ct = default)
    {
        var announcement = new Announcement
        {
            SchoolId = SchoolId,
            BranchId = _user.BranchId,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            Audience = request.Audience,
            PublishedAtUtc = _clock.UtcNow,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CreatedByEmployeeId = _user.EmployeeId
        };

        _db.Add(announcement);
        await _uow.SaveChangesAsync(ct);

        return new AnnouncementDto
        {
            Id = announcement.Id, Title = announcement.Title, Body = announcement.Body,
            Audience = announcement.Audience, PublishedAtUtc = announcement.PublishedAtUtc,
            ExpiresAtUtc = announcement.ExpiresAtUtc, CreatedAtUtc = announcement.CreatedAtUtc
        };
    }

    public async Task<AnnouncementDto> UpdateAnnouncementAsync(int id, SaveAnnouncementRequest request,
        CancellationToken ct = default)
    {
        var announcement = await _db.Announcements
            .FirstOrDefaultAsync(a => a.Id == id && a.SchoolId == SchoolId && !a.IsDeleted, ct)
            ?? throw DomainException.NotFound("Announcement");

        announcement.Title = request.Title.Trim();
        announcement.Body = request.Body.Trim();
        announcement.Audience = request.Audience;
        announcement.ExpiresAtUtc = request.ExpiresAtUtc;
        await _uow.SaveChangesAsync(ct);

        return new AnnouncementDto
        {
            Id = announcement.Id, Title = announcement.Title, Body = announcement.Body,
            Audience = announcement.Audience, PublishedAtUtc = announcement.PublishedAtUtc,
            ExpiresAtUtc = announcement.ExpiresAtUtc, CreatedAtUtc = announcement.CreatedAtUtc
        };
    }

    public async Task DeleteAnnouncementAsync(int id, CancellationToken ct = default)
    {
        var announcement = await _db.Announcements
            .FirstOrDefaultAsync(a => a.Id == id && a.SchoolId == SchoolId && !a.IsDeleted, ct)
            ?? throw DomainException.NotFound("Announcement");

        announcement.IsDeleted = true;
        await _uow.SaveChangesAsync(ct);
    }
}
