using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Fees;

public sealed class FeeService
{
    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IDateTimeProvider _clock;
    private readonly IPaymentGateway _gateway;

    public FeeService(IAppDbContext db, IUnitOfWork uow, ICurrentUser user, IDateTimeProvider clock,
        IPaymentGateway gateway)
    {
        _db = db;
        _uow = uow;
        _user = user;
        _clock = clock;
        _gateway = gateway;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    // ---------- Categories ----------

    public async Task<List<FeeCategoryDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        await _db.FeeCategories.Where(c => c.SchoolId == SchoolId && !c.IsDeleted)
            .OrderBy(c => c.Name)
            .Select(c => new FeeCategoryDto { Id = c.Id, Name = c.Name, Code = c.Code, IsRecurring = c.IsRecurring })
            .ToListAsync(ct);

    public async Task<FeeCategoryDto> CreateCategoryAsync(SaveFeeCategoryRequest request, CancellationToken ct = default)
    {
        var duplicate = await _db.FeeCategories.AnyAsync(c =>
            c.SchoolId == SchoolId && c.Code == request.Code && !c.IsDeleted, ct);
        if (duplicate) throw DomainException.Conflict("A fee category with this code already exists.");

        var category = new FeeCategory
        {
            SchoolId = SchoolId,
            Name = request.Name.Trim(),
            Code = request.Code.Trim(),
            IsRecurring = request.IsRecurring
        };
        _db.Add(category);
        await _uow.SaveChangesAsync(ct);

        return new FeeCategoryDto
        {
            Id = category.Id, Name = category.Name, Code = category.Code, IsRecurring = category.IsRecurring
        };
    }

    public async Task<FeeCategoryDto> UpdateCategoryAsync(int id, SaveFeeCategoryRequest request, CancellationToken ct = default)
    {
        var category = await _db.FeeCategories.FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == SchoolId && !c.IsDeleted, ct)
            ?? throw DomainException.NotFound("Fee category");

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        if (name.Length == 0 || code.Length == 0) throw DomainException.Validation("Name and code are required.");

        var duplicate = await _db.FeeCategories.AnyAsync(c =>
            c.SchoolId == SchoolId && c.Code == code && c.Id != id && !c.IsDeleted, ct);
        if (duplicate) throw DomainException.Conflict("A fee category with this code already exists.");

        category.Name = name;
        category.Code = code;
        category.IsRecurring = request.IsRecurring;
        await _uow.SaveChangesAsync(ct);

        return new FeeCategoryDto
        {
            Id = category.Id, Name = category.Name, Code = category.Code, IsRecurring = category.IsRecurring
        };
    }

    public async Task DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        var category = await _db.FeeCategories.FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == SchoolId && !c.IsDeleted, ct)
            ?? throw DomainException.NotFound("Fee category");

        var inUse = await (
            from item in _db.FeeStructureItems
            join structure in _db.FeeStructures on item.FeeStructureId equals structure.Id
            where item.SchoolId == SchoolId && item.FeeCategoryId == id && !item.IsDeleted && !structure.IsDeleted
            select item.Id).AnyAsync(ct);
        if (inUse) throw DomainException.Conflict("This fee head is used in a fee plan and cannot be deleted.");

        category.IsDeleted = true;
        await _uow.SaveChangesAsync(ct);
    }

    // ---------- Structures ----------

    public async Task<List<FeeStructureDto>> GetStructuresAsync(int? sessionId, int? classId, CancellationToken ct = default)
    {
        var query = _db.FeeStructures.Where(f => f.SchoolId == SchoolId && !f.IsDeleted);
        if (sessionId is > 0) query = query.Where(f => f.AcademicSessionId == sessionId);
        if (classId is > 0) query = query.Where(f => f.ClassId == classId);

        var structures = await query.OrderByDescending(f => f.Id).ToListAsync(ct);
        var ids = structures.Select(s => s.Id).ToList();

        var items = await (
            from item in _db.FeeStructureItems
            join category in _db.FeeCategories on item.FeeCategoryId equals category.Id
            where ids.Contains(item.FeeStructureId) && !item.IsDeleted
            select new { item, CategoryName = category.Name }).ToListAsync(ct);

        var assigned = await _db.StudentFeeAssignments
            .Where(a => a.SchoolId == SchoolId && !a.IsDeleted && ids.Contains(a.FeeStructureId))
            .Select(a => a.FeeStructureId).Distinct().ToListAsync(ct);

        var sessions = await _db.AcademicSessions.Where(s => s.SchoolId == SchoolId).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var classes = await _db.Classes.Where(c => c.SchoolId == SchoolId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return structures.Select(f => new FeeStructureDto
        {
            Id = f.Id, AcademicSessionId = f.AcademicSessionId,
            SessionName = sessions.GetValueOrDefault(f.AcademicSessionId, string.Empty),
            ClassId = f.ClassId, ClassName = classes.GetValueOrDefault(f.ClassId, string.Empty),
            Name = f.Name, TotalAmount = f.TotalAmount, IsActive = f.IsActive,
            IsAssigned = assigned.Contains(f.Id),
            Items = items.Where(i => i.item.FeeStructureId == f.Id).Select(i => new FeeStructureItemDto
            {
                Id = i.item.Id, FeeCategoryId = i.item.FeeCategoryId, CategoryName = i.CategoryName,
                Amount = i.item.Amount, Frequency = i.item.Frequency, DueDay = i.item.DueDay
            }).ToList()
        }).ToList();
    }

    public async Task<FeeStructureDto> CreateStructureAsync(SaveFeeStructureRequest request, CancellationToken ct = default)
    {
        if (request.Items.Count == 0) throw DomainException.Validation("At least one fee head is required.");

        var session = await _db.AcademicSessions.FirstOrDefaultAsync(s => s.Id == request.AcademicSessionId && s.SchoolId == SchoolId, ct)
            ?? throw DomainException.NotFound("Academic session");
        if (session.IsClosed) throw DomainException.Conflict("The session is closed.");

        var classExists = await _db.Classes.AnyAsync(c => c.Id == request.ClassId && c.SchoolId == SchoolId && !c.IsDeleted, ct);
        if (!classExists) throw DomainException.NotFound("Class");

        var duplicate = await _db.FeeStructures.AnyAsync(f =>
            f.SchoolId == SchoolId && f.AcademicSessionId == session.Id && f.ClassId == request.ClassId &&
            f.Name == request.Name && !f.IsDeleted, ct);
        if (duplicate) throw DomainException.Conflict("A fee structure with this name already exists for the class and session.");

        var categoryIds = request.Items.Select(i => i.FeeCategoryId).Distinct().ToList();
        var validCategories = await _db.FeeCategories
            .CountAsync(c => c.SchoolId == SchoolId && categoryIds.Contains(c.Id) && !c.IsDeleted, ct);
        if (validCategories != categoryIds.Count) throw DomainException.Validation("One or more fee categories are invalid.");

        var total = Math.Round(request.Items.Sum(i => i.Amount), 2, MidpointRounding.AwayFromZero);

        var structure = new FeeStructure
        {
            SchoolId = SchoolId,
            AcademicSessionId = session.Id,
            ClassId = request.ClassId,
            Name = request.Name.Trim(),
            TotalAmount = total
        };
        _db.Add(structure);
        await _uow.SaveChangesAsync(ct);

        var items = request.Items.Select(i => new FeeStructureItem
        {
            SchoolId = SchoolId,
            FeeStructureId = structure.Id,
            FeeCategoryId = i.FeeCategoryId,
            Amount = i.Amount,
            Frequency = i.Frequency,
            DueDay = i.DueDay
        }).ToList();

        foreach (var item in items) _db.Add(item);
        await _uow.SaveChangesAsync(ct);

        return new FeeStructureDto
        {
            Id = structure.Id, AcademicSessionId = structure.AcademicSessionId,
            SessionName = session.Name, ClassId = structure.ClassId,
            ClassName = await _db.Classes.Where(c => c.Id == structure.ClassId).Select(c => c.Name).FirstAsync(ct),
            Name = structure.Name, TotalAmount = structure.TotalAmount, IsActive = true,
            Items = items.Select(i => new FeeStructureItemDto
            {
                Id = i.Id, FeeCategoryId = i.FeeCategoryId,
                CategoryName = _db.FeeCategories.Where(c => c.Id == i.FeeCategoryId).Select(c => c.Name).First(),
                Amount = i.Amount, Frequency = i.Frequency, DueDay = i.DueDay
            }).ToList()
        };
    }

    // ---------- Structure maintenance ----------

    private const string PlanLockedMessage =
        "This plan is already assigned to students; deactivate it and create a new plan.";

    public async Task<FeeStructureDto> UpdateStructureAsync(int id, SaveFeeStructureRequest request, CancellationToken ct = default)
    {
        if (request.Items.Count == 0) throw DomainException.Validation("At least one fee head is required.");

        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var structure = await _db.FeeStructures
                .FirstOrDefaultAsync(f => f.Id == id && f.SchoolId == SchoolId && !f.IsDeleted, token)
                ?? throw DomainException.NotFound("Fee structure");

            if (await IsStructureAssignedAsync(structure.Id, token)) throw DomainException.Conflict(PlanLockedMessage);

            var session = await _db.AcademicSessions.FirstOrDefaultAsync(s => s.Id == request.AcademicSessionId && s.SchoolId == SchoolId, token)
                ?? throw DomainException.NotFound("Academic session");
            if (session.IsClosed) throw DomainException.Conflict("The session is closed.");

            var classExists = await _db.Classes.AnyAsync(c => c.Id == request.ClassId && c.SchoolId == SchoolId && !c.IsDeleted, token);
            if (!classExists) throw DomainException.NotFound("Class");

            var name = request.Name.Trim();
            var duplicate = await _db.FeeStructures.AnyAsync(f =>
                f.SchoolId == SchoolId && f.Id != structure.Id && f.AcademicSessionId == session.Id &&
                f.ClassId == request.ClassId && f.Name == name && !f.IsDeleted, token);
            if (duplicate) throw DomainException.Conflict("A fee structure with this name already exists for the class and session.");

            var categoryIds = request.Items.Select(i => i.FeeCategoryId).Distinct().ToList();
            var validCategories = await _db.FeeCategories
                .CountAsync(c => c.SchoolId == SchoolId && categoryIds.Contains(c.Id) && !c.IsDeleted, token);
            if (validCategories != categoryIds.Count) throw DomainException.Validation("One or more fee categories are invalid.");

            structure.Name = name;
            structure.AcademicSessionId = session.Id;
            structure.ClassId = request.ClassId;
            structure.TotalAmount = Math.Round(request.Items.Sum(i => i.Amount), 2, MidpointRounding.AwayFromZero);

            var oldItems = await _db.FeeStructureItems
                .Where(i => i.FeeStructureId == structure.Id && i.SchoolId == SchoolId && !i.IsDeleted)
                .ToListAsync(token);
            foreach (var item in oldItems) item.IsDeleted = true;

            foreach (var input in request.Items)
            {
                _db.Add(new FeeStructureItem
                {
                    SchoolId = SchoolId,
                    FeeStructureId = structure.Id,
                    FeeCategoryId = input.FeeCategoryId,
                    Amount = input.Amount,
                    Frequency = input.Frequency,
                    DueDay = input.DueDay
                });
            }

            await _uow.SaveChangesAsync(token);
            return true;
        }, ct);

        var all = await GetStructuresAsync(null, null, ct);
        return all.First(s => s.Id == id);
    }

    public async Task<FeeStructureDto> SetStructureActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        var structure = await _db.FeeStructures
            .FirstOrDefaultAsync(f => f.Id == id && f.SchoolId == SchoolId && !f.IsDeleted, ct)
            ?? throw DomainException.NotFound("Fee structure");

        if (structure.IsActive != active)
        {
            structure.IsActive = active;
            await _uow.SaveChangesAsync(ct);
        }

        var all = await GetStructuresAsync(null, null, ct);
        return all.First(s => s.Id == id);
    }

    public async Task DeleteStructureAsync(int id, CancellationToken ct = default)
    {
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var structure = await _db.FeeStructures
                .FirstOrDefaultAsync(f => f.Id == id && f.SchoolId == SchoolId && !f.IsDeleted, token)
                ?? throw DomainException.NotFound("Fee structure");

            if (await IsStructureAssignedAsync(structure.Id, token)) throw DomainException.Conflict(PlanLockedMessage);

            var items = await _db.FeeStructureItems
                .Where(i => i.FeeStructureId == structure.Id && i.SchoolId == SchoolId && !i.IsDeleted)
                .ToListAsync(token);
            foreach (var item in items) item.IsDeleted = true;
            structure.IsDeleted = true;

            await _uow.SaveChangesAsync(token);
            return true;
        }, ct);
    }

    private Task<bool> IsStructureAssignedAsync(int structureId, CancellationToken ct) =>
        _db.StudentFeeAssignments.AnyAsync(a =>
            a.SchoolId == SchoolId && a.FeeStructureId == structureId && !a.IsDeleted, ct);

    // ---------- Assignment changes ----------

    /// <summary>Cancels a fee plan for a student. Only allowed while nothing has been paid against it.</summary>
    public async Task<StudentLedgerDto> CancelAssignmentAsync(int assignmentId, FeeReasonRequest request, CancellationToken ct = default)
    {
        var reason = RequireReason(request.Reason);

        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var assignment = await LoadAssignmentAsync(assignmentId, token);
            if (!assignment.IsActive) throw DomainException.Conflict("This fee plan is already cancelled.");

            var installments = await _db.FeeInstallments
                .Where(i => i.StudentFeeAssignmentId == assignment.Id && i.SchoolId == SchoolId && !i.IsDeleted)
                .ToListAsync(token);

            if (installments.Sum(i => i.PaidAmount) > 0)
                throw DomainException.Conflict("Payments exist; refund them first or waive the remaining installments.");

            foreach (var installment in installments) installment.IsDeleted = true;
            assignment.IsActive = false;

            AddReasonAudit("CancelFeePlan", nameof(StudentFeeAssignment), assignment.Id, reason, null);
            await _uow.SaveChangesAsync(token);
            return await LedgerForAsync(assignment.StudentId, token);
        }, ct);
    }

    /// <summary>Waives the unpaid balance of one installment.</summary>
    public async Task<StudentLedgerDto> WaiveInstallmentAsync(int installmentId, FeeReasonRequest request, CancellationToken ct = default)
    {
        var reason = RequireReason(request.Reason);

        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var (installment, assignment) = await LoadOpenInstallmentAsync(installmentId, token);

            // Only the unpaid part of the principal reduces what the student is billed.
            var waived = Math.Round(Math.Min(installment.Amount, Math.Max(0, installment.TotalAmount - installment.PaidAmount)),
                2, MidpointRounding.AwayFromZero);

            installment.Status = InstallmentStatus.Waived;
            assignment.NetAmount = Math.Round(assignment.NetAmount - waived, 2, MidpointRounding.AwayFromZero);

            AddReasonAudit("WaiveInstallment", nameof(FeeInstallment), installment.Id, reason, waived);
            await _uow.SaveChangesAsync(token);
            return await LedgerForAsync(assignment.StudentId, token);
        }, ct);
    }

    /// <summary>Changes the amount, due date or name of an installment that is not yet settled.</summary>
    public async Task<StudentLedgerDto> UpdateInstallmentAsync(int installmentId, UpdateInstallmentRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) throw DomainException.Validation("Installment name is required.");
        if (request.DueDate == default) throw DomainException.Validation("Due date is required.");
        var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0) throw DomainException.Validation("Installment amount must be positive.");

        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var (installment, assignment) = await LoadOpenInstallmentAsync(installmentId, token);

            if (amount + installment.PenaltyAmount < installment.PaidAmount)
                throw DomainException.Validation(
                    $"The amount cannot be less than what has already been paid ({Math.Max(0, installment.PaidAmount - installment.PenaltyAmount):0.00}).");

            var difference = amount - installment.Amount;
            installment.Amount = amount;
            installment.Name = name;
            installment.DueDate = request.DueDate;
            installment.Status = RecomputeStatus(installment);
            assignment.NetAmount = Math.Round(assignment.NetAmount + difference, 2, MidpointRounding.AwayFromZero);

            await _uow.SaveChangesAsync(token);
            return await LedgerForAsync(assignment.StudentId, token);
        }, ct);
    }

    /// <summary>
    /// Gives an extra discount on a fee plan. It is taken off the latest unpaid
    /// installments first, never below what has been paid on each.
    /// </summary>
    public async Task<StudentLedgerDto> AddDiscountAsync(int assignmentId, FeeDiscountRequest request, CancellationToken ct = default)
    {
        var reason = RequireReason(request.Reason);
        var discount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (discount <= 0) throw DomainException.Validation("Discount must be positive.");

        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var assignment = await LoadAssignmentAsync(assignmentId, token);
            if (!assignment.IsActive) throw DomainException.Conflict("This fee plan is cancelled.");

            var open = await _db.FeeInstallments
                .Where(i => i.StudentFeeAssignmentId == assignment.Id && i.SchoolId == SchoolId && !i.IsDeleted &&
                            i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived)
                .OrderByDescending(i => i.DueDate).ThenByDescending(i => i.Id)
                .ToListAsync(token);

            var reducible = open.Sum(Reducible);
            if (discount > reducible)
                throw DomainException.Validation($"Discount exceeds the unpaid fee of {reducible:0.00}.");

            var remaining = discount;
            foreach (var installment in open)
            {
                if (remaining <= 0) break;
                var cut = Math.Min(remaining, Reducible(installment));
                if (cut <= 0) continue;
                installment.Amount = Math.Round(installment.Amount - cut, 2, MidpointRounding.AwayFromZero);
                installment.Status = RecomputeStatus(installment);
                remaining -= cut;
            }

            assignment.DiscountAmount = Math.Round(assignment.DiscountAmount + discount, 2, MidpointRounding.AwayFromZero);
            assignment.NetAmount = Math.Round(assignment.NetAmount - discount, 2, MidpointRounding.AwayFromZero);

            AddReasonAudit("AddFeeDiscount", nameof(StudentFeeAssignment), assignment.Id, reason, discount);
            await _uow.SaveChangesAsync(token);
            return await LedgerForAsync(assignment.StudentId, token);
        }, ct);
    }

    private static decimal Reducible(FeeInstallment i) =>
        Math.Max(0, Math.Min(i.Amount, i.TotalAmount - i.PaidAmount));

    private InstallmentStatus RecomputeStatus(FeeInstallment installment)
    {
        if (installment.PaidAmount >= installment.TotalAmount)
            return installment.PaidAmount > 0 ? InstallmentStatus.Paid : InstallmentStatus.Waived;
        if (installment.PaidAmount > 0) return InstallmentStatus.PartiallyPaid;
        return installment.DueDate < _clock.Today && installment.Status == InstallmentStatus.Overdue
            ? InstallmentStatus.Overdue
            : InstallmentStatus.Pending;
    }

    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) throw DomainException.Validation("A reason is required.");
        return trimmed;
    }

    private async Task<StudentFeeAssignment> LoadAssignmentAsync(int assignmentId, CancellationToken ct) =>
        await _db.StudentFeeAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.SchoolId == SchoolId && !a.IsDeleted, ct)
        ?? throw DomainException.NotFound("Fee assignment");

    private async Task<(FeeInstallment Installment, StudentFeeAssignment Assignment)> LoadOpenInstallmentAsync(
        int installmentId, CancellationToken ct)
    {
        var installment = await _db.FeeInstallments
            .FirstOrDefaultAsync(i => i.Id == installmentId && i.SchoolId == SchoolId && !i.IsDeleted, ct)
            ?? throw DomainException.NotFound("Fee installment");

        if (installment.Status == InstallmentStatus.Paid)
            throw DomainException.Conflict("This installment is already paid.");
        if (installment.Status == InstallmentStatus.Waived)
            throw DomainException.Conflict("This installment is already waived.");

        var assignment = await LoadAssignmentAsync(installment.StudentFeeAssignmentId, ct);
        if (!assignment.IsActive) throw DomainException.Conflict("This fee plan is cancelled.");

        return (installment, assignment);
    }

    private async Task<StudentLedgerDto> LedgerForAsync(int studentId, CancellationToken ct)
    {
        var student = await _db.Students.FirstAsync(s => s.Id == studentId && s.SchoolId == SchoolId, ct);
        return await BuildLedgerAsync(student, ct);
    }

    private void AddReasonAudit(string action, string entityName, int entityId, string reason, decimal? amount)
    {
        _db.Add(new AuditLog
        {
            SchoolId = SchoolId,
            UserAccountId = _user.UserId,
            UserEmail = _user.Email,
            Action = action,
            EntityName = entityName,
            EntityId = entityId.ToString(),
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { reason, amount }),
            IpAddress = _user.IpAddress,
            UserAgent = _user.UserAgent,
            CreatedAtUtc = _clock.UtcNow
        });
    }

    // ---------- Assignment ----------

    public async Task<StudentLedgerDto> AssignAsync(AssignFeeRequest request, CancellationToken ct = default)
    {
        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == request.StudentId && s.SchoolId == SchoolId && !s.IsDeleted, token)
                ?? throw DomainException.NotFound("Student");

            if (student.Status != StudentStatus.Active)
                throw DomainException.Conflict("Fees can only be assigned to an active student.");

            var structure = await _db.FeeStructures
                .FirstOrDefaultAsync(f => f.Id == request.FeeStructureId && f.SchoolId == SchoolId && !f.IsDeleted, token)
                ?? throw DomainException.NotFound("Fee structure");

            if (!structure.IsActive) throw DomainException.Conflict("The fee structure is inactive.");
            if (structure.ClassId != student.ClassId)
                throw DomainException.Validation("The fee structure does not belong to the student's class.");
            if (structure.AcademicSessionId != student.AcademicSessionId)
                throw DomainException.Validation("The fee structure does not belong to the student's academic session.");

            var duplicate = await _db.StudentFeeAssignments.AnyAsync(a =>
                a.SchoolId == SchoolId && a.StudentId == student.Id && a.FeeStructureId == structure.Id &&
                a.IsActive && !a.IsDeleted, token);
            if (duplicate) throw DomainException.Conflict("This fee structure is already assigned to the student.");

            var discount = Math.Round(request.DiscountAmount, 2, MidpointRounding.AwayFromZero);
            if (discount < 0 || discount > structure.TotalAmount)
                throw DomainException.Validation("Discount must be between zero and the total fee.");

            var net = Math.Round(structure.TotalAmount - discount, 2, MidpointRounding.AwayFromZero);
            var platformFee = await PlatformFeeForSessionAsync(structure.AcademicSessionId, token);
            if (platformFee > 0)
            {
                // Charged once per student per session, however many fee plans they have.
                var alreadyCharged = await _db.StudentFeeAssignments
                    .Where(a => a.SchoolId == SchoolId && a.StudentId == student.Id &&
                                a.AcademicSessionId == structure.AcademicSessionId && a.IsActive && !a.IsDeleted)
                    .SumAsync(a => a.PlatformFeeAmount, token);
                platformFee = Math.Max(0, platformFee - alreadyCharged);
            }

            var assignment = new StudentFeeAssignment
            {
                SchoolId = SchoolId,
                StudentId = student.Id,
                FeeStructureId = structure.Id,
                AcademicSessionId = structure.AcademicSessionId,
                TotalAmount = structure.TotalAmount,
                DiscountAmount = discount,
                NetAmount = net + platformFee,
                PlatformFeeAmount = platformFee,
                AssignedAtUtc = _clock.UtcNow
            };
            _db.Add(assignment);
            await _uow.SaveChangesAsync(token);

            var installments = request.Installments is { Count: > 0 }
                ? request.Installments
                : BuildDefaultInstallments(structure, net);

            var sum = Math.Round(installments.Sum(i => i.Amount), 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(sum - net) > 0.01m)
                throw DomainException.Validation($"Installment amounts ({sum:0.00}) must equal the net payable ({net:0.00}).");

            // The platform fee is spread evenly over the installments, remainder on the last.
            var platformShare = installments.Count == 0 ? 0
                : Math.Round(platformFee / installments.Count, 2, MidpointRounding.AwayFromZero);
            var platformRemainder = platformFee - platformShare * installments.Count;

            for (var index = 0; index < installments.Count; index++)
            {
                var input = installments[index];
                if (input.Amount <= 0) throw DomainException.Validation("Installment amounts must be positive.");
                var share = index == installments.Count - 1 ? platformShare + platformRemainder : platformShare;
                _db.Add(new FeeInstallment
                {
                    SchoolId = SchoolId,
                    StudentFeeAssignmentId = assignment.Id,
                    Name = input.Name.Trim(),
                    DueDate = input.DueDate == default ? _clock.Today : input.DueDate,
                    Amount = input.Amount + share,
                    PaidAmount = 0,
                    Status = InstallmentStatus.Pending
                });
            }

            var guardian = await GetPrimaryGuardianAsync(student.Id, token);
            if (guardian is not null)
            {
                _db.Add(new Notification
                {
                    SchoolId = SchoolId,
                    StudentId = student.Id,
                    GuardianId = guardian.Id,
                    UserAccountId = guardian.UserAccountId,
                    Title = "Fee plan assigned",
                    Message = $"A fee plan of {net + platformFee:0.00} has been assigned to {student.FullName}. Please review the installments.",
                    Channel = NotificationChannel.InApp,
                    Status = NotificationStatus.Pending,
                    Category = "Fees",
                    Recipient = guardian.Phone
                });
            }

            await _uow.SaveChangesAsync(token);
            return await BuildLedgerAsync(student, token);
        }, ct);
    }

    public async Task<StudentLedgerDto> GetLedgerAsync(int studentId, CancellationToken ct = default)
    {
        await EnsureCanReadStudentAsync(studentId, ct);

        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Student");

        return await BuildLedgerAsync(student, ct);
    }

    // ---------- Payments ----------

    public object GetPaymentConfig() => new
    {
        onlineEnabled = _gateway.IsEnabled,
        gateway = _gateway.Name,
        keyId = _gateway.IsEnabled ? _gateway.PublicKey : string.Empty
    };

    /// <summary>
    /// Opens a payment session with the gateway. Parents may do this for their own
    /// children; staff need the fees.collect permission.
    /// </summary>
    public async Task<PaymentOrderDto> CreatePaymentOrderAsync(CreatePaymentOrderRequest request,
        CancellationToken ct = default)
    {
        await EnsureCanPayForStudentAsync(request.StudentId, ct);

        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.Id == request.StudentId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Student");

        var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0) throw DomainException.Validation("The amount must be greater than zero.");

        string? installmentName = null;
        int? installmentId = null;
        if (request.FeeInstallmentId is > 0)
        {
            var installment = await (
                from i in _db.FeeInstallments
                join a in _db.StudentFeeAssignments on i.StudentFeeAssignmentId equals a.Id
                where i.Id == request.FeeInstallmentId && i.SchoolId == SchoolId && !i.IsDeleted &&
                      a.StudentId == student.Id
                select i).FirstOrDefaultAsync(ct)
                ?? throw DomainException.NotFound("Fee installment");

            if (installment.Status is InstallmentStatus.Paid or InstallmentStatus.Waived)
                throw DomainException.Conflict("This installment is already settled.");

            var balance = installment.TotalAmount - installment.PaidAmount;
            if (amount > balance + 0.01m)
                throw DomainException.Validation($"The amount exceeds the installment balance of {balance:0.00}.");

            installmentName = installment.Name;
            installmentId = installment.Id;
        }
        else
        {
            // Without an installment the payment is applied to the earliest due one, so it
            // can never exceed that installment's balance.
            var next = await (
                from i in _db.FeeInstallments
                join a in _db.StudentFeeAssignments on i.StudentFeeAssignmentId equals a.Id
                where a.StudentId == student.Id && a.IsActive && !a.IsDeleted && !i.IsDeleted &&
                      i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived &&
                      i.Amount + i.PenaltyAmount > i.PaidAmount
                orderby i.DueDate
                select i).FirstOrDefaultAsync(ct)
                ?? throw DomainException.Validation("The student has no outstanding fees.");
            var balance = next.TotalAmount - next.PaidAmount;
            if (amount > balance + 0.01m)
                throw DomainException.Validation($"The amount exceeds the outstanding balance of {balance:0.00}.");
        }

        var reference = $"FEE-{student.Id}-{student.AdmissionNo}";
        var order = await _gateway.CreateOrderAsync(amount, reference,
            new GatewayOrderBinding(SchoolId, student.Id, installmentId), ct);

        return new PaymentOrderDto
        {
            OrderId = order.OrderId,
            PaymentId = order.PaymentId,
            Amount = order.Amount,
            Currency = order.Currency,
            CheckoutUrl = order.CheckoutUrl,
            ExpiresAtUtc = order.ExpiresAtUtc,
            Gateway = _gateway.Name,
            KeyId = _gateway.PublicKey,
            StudentId = student.Id,
            StudentName = student.FullName,
            InstallmentName = installmentName
        };
    }

    /// <summary>Completes an online payment started with <see cref="CreatePaymentOrderAsync"/>.</summary>
    public async Task<PaymentDto> RecordOnlinePaymentAsync(OnlinePaymentRequest request, CancellationToken ct = default)
    {
        await EnsureCanPayForStudentAsync(request.StudentId, ct);

        return await RecordPaymentAsync(new RecordPaymentRequest
        {
            StudentId = request.StudentId,
            FeeInstallmentId = request.FeeInstallmentId,
            Amount = request.Amount,
            Mode = PaymentMode.Online,
            GatewayOrderId = request.GatewayOrderId,
            GatewayPaymentId = request.GatewayPaymentId,
            GatewaySignature = request.GatewaySignature,
            IdempotencyKey = request.IdempotencyKey
        }, ct);
    }

    public async Task<PaymentDto> RecordPaymentAsync(RecordPaymentRequest request, CancellationToken ct = default)
    {
        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existing = await _db.Payments
                    .FirstOrDefaultAsync(p => p.SchoolId == SchoolId && p.IdempotencyKey == request.IdempotencyKey, token);
                if (existing is not null) return await MapPaymentAsync(existing, token);
            }

            var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == request.StudentId && s.SchoolId == SchoolId && !s.IsDeleted, token)
                ?? throw DomainException.NotFound("Student");

            FeeInstallment? installment = null;
            StudentFeeAssignment? assignment = null;

            if (request.FeeInstallmentId is > 0)
            {
                installment = await _db.FeeInstallments
                    .FirstOrDefaultAsync(i => i.Id == request.FeeInstallmentId && i.SchoolId == SchoolId && !i.IsDeleted, token)
                    ?? throw DomainException.NotFound("Fee installment");

                assignment = await _db.StudentFeeAssignments
                    .FirstOrDefaultAsync(a => a.Id == installment.StudentFeeAssignmentId && a.SchoolId == SchoolId, token)
                    ?? throw DomainException.NotFound("Fee assignment");

                if (assignment.StudentId != student.Id)
                    throw DomainException.Validation("The installment does not belong to this student.");
            }
            else
            {
                installment = await (
                    from i in _db.FeeInstallments
                    join a in _db.StudentFeeAssignments on i.StudentFeeAssignmentId equals a.Id
                    where a.StudentId == student.Id && a.IsActive && !a.IsDeleted && !i.IsDeleted &&
                          i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived &&
                          i.Amount + i.PenaltyAmount > i.PaidAmount
                    orderby i.DueDate
                    select i).FirstOrDefaultAsync(token)
                    ?? throw DomainException.Validation("The student has no outstanding installments.");

                assignment = await _db.StudentFeeAssignments.FirstAsync(a => a.Id == installment.StudentFeeAssignmentId, token);
            }

            if (installment.Status is InstallmentStatus.Paid or InstallmentStatus.Waived)
                throw DomainException.Conflict("This installment is already settled.");

            // The balance includes the late fee that the penalty job has accrued.
            var balance = installment.TotalAmount - installment.PaidAmount;
            if (request.Amount > balance + 0.01m)
                throw DomainException.Validation($"Payment exceeds the installment balance of {balance:0.00}.");

            var gatewayPaymentId = request.GatewayPaymentId;
            var gatewayOrderId = request.GatewayOrderId;

            if (request.Mode == PaymentMode.Online)
            {
                if (string.IsNullOrWhiteSpace(gatewayOrderId) || string.IsNullOrWhiteSpace(gatewayPaymentId))
                    throw DomainException.Validation("Online payments require a gateway order and payment id.");

                var alreadyCredited = await _db.Payments.IgnoreQueryFilters()
                    .AnyAsync(p => p.GatewayPaymentId == gatewayPaymentId && !p.IsDeleted, token);
                if (alreadyCredited)
                    throw DomainException.Conflict("This online payment has already been recorded.");

                // The order must have been opened for this exact school, student, installment
                // and amount, so nobody can complete a payment with someone else's order.
                var binding = new GatewayOrderBinding(SchoolId, student.Id,
                    request.FeeInstallmentId is > 0 ? request.FeeInstallmentId : null);
                var orderMatches = await _gateway.OrderMatchesAsync(gatewayOrderId, binding, request.Amount, token);
                var verification = orderMatches
                    ? await _gateway.VerifyAsync(gatewayOrderId, gatewayPaymentId, request.GatewaySignature,
                        request.Amount, token)
                    : new GatewayVerification(false, null,
                        "The payment order does not match this student, installment or amount.", null);
                if (!verification.Success)
                {
                    _db.Add(new PaymentTransaction
                    {
                        SchoolId = SchoolId,
                        StudentId = student.Id,
                        Gateway = _gateway.Name,
                        GatewayOrderId = gatewayOrderId,
                        GatewayPaymentId = gatewayPaymentId,
                        Amount = request.Amount,
                        Status = PaymentTransactionStatus.Failed,
                        RawResponse = verification.RawResponse,
                        FailureReason = verification.FailureReason
                    });
                    await _uow.SaveChangesAsync(token);
                    throw new DomainException("payment_failed", verification.FailureReason ?? "The payment could not be verified.");
                }

                gatewayPaymentId = verification.GatewayPaymentId ?? gatewayPaymentId;
            }

            var school = await _db.Schools.FirstAsync(s => s.Id == SchoolId, token);
            await _uow.LockAsync($"receipt-no-{SchoolId}", token);

            // Late fee is settled first; record how much of this payment covered it so the
            // receipt stays correct even if more late fee is added later.
            var penaltyAlreadyCovered = Math.Max(0, installment.PaidAmount - installment.Amount);
            var penaltyDue = Math.Max(0, installment.PenaltyAmount - penaltyAlreadyCovered);
            var penaltyPortion = Math.Min(request.Amount, penaltyDue);

            var payment = new Payment
            {
                SchoolId = SchoolId,
                BranchId = student.BranchId,
                StudentId = student.Id,
                StudentFeeAssignmentId = assignment.Id,
                FeeInstallmentId = installment.Id,
                ReceiptNo = await NextReceiptNoAsync(school.Code, token),
                Amount = request.Amount,
                PenaltyPortion = penaltyPortion,
                Mode = request.Mode,
                Status = PaymentStatus.Success,
                PaidAtUtc = _clock.UtcNow,
                ReferenceNo = request.ReferenceNo,
                GatewayOrderId = gatewayOrderId,
                GatewayPaymentId = gatewayPaymentId,
                IdempotencyKey = request.IdempotencyKey,
                CollectedBy = _user.UserId,
                Remarks = request.Remarks
            };
            _db.Add(payment);

            installment.PaidAmount = Math.Round(installment.PaidAmount + request.Amount, 2, MidpointRounding.AwayFromZero);
            installment.Status = installment.PaidAmount >= installment.TotalAmount
                ? InstallmentStatus.Paid
                : InstallmentStatus.PartiallyPaid;

            _db.Add(new PaymentTransaction
            {
                SchoolId = SchoolId,
                StudentId = student.Id,
                Gateway = request.Mode == PaymentMode.Online ? _gateway.Name : "Manual",
                GatewayOrderId = gatewayOrderId,
                GatewayPaymentId = gatewayPaymentId,
                Amount = request.Amount,
                Status = PaymentTransactionStatus.Success,
                RawResponse = null
            });

            await _uow.SaveChangesAsync(token);

            _db.Add(new Notification
            {
                SchoolId = SchoolId,
                StudentId = student.Id,
                GuardianId = null,
                Title = "Fee payment received",
                Message = $"Payment of {request.Amount:0.00} received for {student.FullName}. Receipt {payment.ReceiptNo}.",
                Channel = NotificationChannel.InApp,
                Status = NotificationStatus.Pending,
                Category = "Fees",
                Recipient = student.Phone
            });

            await _uow.SaveChangesAsync(token);
            return await MapPaymentAsync(payment, token);
        }, ct);
    }

    public async Task<PagedResult<PaymentDto>> GetPaymentsAsync(PaymentQuery query, CancellationToken ct = default)
    {
        var source =
            from p in _db.Payments
            join s in _db.Students on p.StudentId equals s.Id
            where p.SchoolId == SchoolId && !p.IsDeleted
            select new { p, StudentName = s.FirstName + " " + s.LastName };

        if (_user.IsInRole(RoleCodes.Student))
        {
            var ownStudentId = _user.StudentId ?? -1;
            source = source.Where(x => x.p.StudentId == ownStudentId);
        }
        else if (_user.IsInRole(RoleCodes.Parent))
        {
            var guardianId = _user.GuardianId ?? -1;
            source = source.Where(x => _db.StudentParents.Any(sp =>
                sp.GuardianId == guardianId && sp.StudentId == x.p.StudentId && !sp.IsDeleted));
        }

        if (query.StudentId is > 0) source = source.Where(x => x.p.StudentId == query.StudentId);
        if (query.From is not null)
        {
            var from = query.From.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            source = source.Where(x => x.p.PaidAtUtc >= from);
        }
        if (query.To is not null)
        {
            var to = query.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            source = source.Where(x => x.p.PaidAtUtc < to);
        }
        if (query.Mode is not null) source = source.Where(x => x.p.Mode == query.Mode);

        return await source.OrderByDescending(x => x.p.PaidAtUtc)
            .Select(x => new PaymentDto
            {
                Id = x.p.Id, ReceiptNo = x.p.ReceiptNo, StudentId = x.p.StudentId,
                StudentName = x.StudentName, Amount = x.p.Amount, Mode = x.p.Mode,
                Status = x.p.Status, PaidAtUtc = x.p.PaidAtUtc, ReferenceNo = x.p.ReferenceNo,
                RefundedAmount = x.p.RefundedAmount
            })
            .ToPagedResultAsync(query.Page, query.PageSize, ct);
    }

    public async Task<Refund> RequestRefundAsync(int paymentId, RefundRequest request, CancellationToken ct = default)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId && p.SchoolId == SchoolId && !p.IsDeleted, ct)
            ?? throw DomainException.NotFound("Payment");

        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Pending)
            throw DomainException.Conflict("Only a successful payment can be refunded.");

        var pending = await _db.Refunds
            .Where(r => r.PaymentId == payment.Id && r.SchoolId == SchoolId && !r.IsDeleted && r.Status == RefundStatus.Pending)
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;
        var refundable = payment.Amount - payment.RefundedAmount - pending;
        if (request.Amount > refundable + 0.01m)
            throw DomainException.Validation(pending > 0
                ? $"Refundable amount is {Math.Max(0, refundable):0.00} ({pending:0.00} is already waiting for approval)."
                : $"Refundable amount is {Math.Max(0, refundable):0.00}.");

        var refund = new Refund
        {
            SchoolId = SchoolId,
            PaymentId = payment.Id,
            StudentId = payment.StudentId,
            Amount = request.Amount,
            Reason = request.Reason.Trim(),
            Status = RefundStatus.Pending,
            RequestedBy = _user.UserId ?? 0
        };
        _db.Add(refund);
        await _uow.SaveChangesAsync(ct);
        return refund;
    }

    public async Task ApproveRefundAsync(int refundId, CancellationToken ct = default)
    {
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var refund = await _db.Refunds.FirstOrDefaultAsync(r => r.Id == refundId && r.SchoolId == SchoolId && !r.IsDeleted, token)
                ?? throw DomainException.NotFound("Refund");
            if (refund.Status != RefundStatus.Pending) throw DomainException.Conflict("Refund is not pending approval.");

            var payment = await _db.Payments.FirstAsync(p => p.Id == refund.PaymentId, token);
            if (refund.Amount > payment.Amount - payment.RefundedAmount + 0.01m)
                throw DomainException.Conflict(
                    $"Only {payment.Amount - payment.RefundedAmount:0.00} of this payment can still be refunded.");

            // A cash, cheque or bank-transfer payment never touched the gateway, so there
            // is nothing to reverse there — the refund is simply recorded against the
            // ledger. Only an online payment is settled with the provider.
            if (payment.Mode == PaymentMode.Online)
            {
                var result = await _gateway.RefundAsync(payment.GatewayPaymentId ?? payment.ReferenceNo ?? payment.ReceiptNo,
                    refund.Amount, token);
                if (!result.Success)
                    throw new DomainException("refund_failed", result.FailureReason ?? "The refund could not be processed.");

                refund.ReferenceNo = result.GatewayPaymentId;
            }

            refund.Status = RefundStatus.Processed;
            refund.ApprovedBy = _user.UserId;
            refund.ApprovedAtUtc = _clock.UtcNow;
            refund.ProcessedAtUtc = _clock.UtcNow;

            payment.RefundedAmount = Math.Round(payment.RefundedAmount + refund.Amount, 2, MidpointRounding.AwayFromZero);
            payment.Status = payment.RefundedAmount >= payment.Amount
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;

            if (payment.FeeInstallmentId is > 0)
            {
                var installment = await _db.FeeInstallments.FirstOrDefaultAsync(i => i.Id == payment.FeeInstallmentId, token);
                if (installment is not null)
                {
                    installment.PaidAmount = Math.Max(0, Math.Round(installment.PaidAmount - refund.Amount, 2));
                    installment.Status = installment.PaidAmount <= 0
                        ? InstallmentStatus.Pending
                        : installment.PaidAmount >= installment.TotalAmount
                            ? InstallmentStatus.Paid
                            : InstallmentStatus.PartiallyPaid;
                }
            }

            await _uow.SaveChangesAsync(token);
            return true;
        }, ct);
    }

    // ---------- Reports ----------

    public async Task<CollectionSummaryDto> GetCollectionSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        EnsureFinanceStaff();
        if (to < from) throw DomainException.Validation("The end date must be on or after the start date.");

        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var payments = await _db.Payments
            .Where(p => p.SchoolId == SchoolId && !p.IsDeleted && p.Status != PaymentStatus.Failed &&
                        p.PaidAtUtc >= start && p.PaidAtUtc < end)
            .Select(p => new { p.Amount, p.Mode, p.PaidAtUtc })
            .ToListAsync(ct);

        var refunds = await _db.Refunds
            .Where(r => r.SchoolId == SchoolId && !r.IsDeleted &&
                        (r.Status == RefundStatus.Approved || r.Status == RefundStatus.Processed) &&
                        r.ApprovedAtUtc >= start && r.ApprovedAtUtc < end)
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0;

        var byMode = payments.GroupBy(p => p.Mode)
            .Select(g => new CollectionPointDto { Label = g.Key.ToString(), Amount = g.Sum(x => x.Amount), Count = g.Count() })
            .OrderByDescending(x => x.Amount).ToList();

        var daily = payments.GroupBy(p => DateOnly.FromDateTime(p.PaidAtUtc))
            .Select(g => new CollectionPointDto { Label = g.Key.ToString("dd MMM"), Amount = g.Sum(x => x.Amount), Count = g.Count() })
            .OrderBy(x => x.Label).ToList();

        return new CollectionSummaryDto
        {
            From = from, To = to, TotalCollected = payments.Sum(p => p.Amount),
            PaymentCount = payments.Count, TotalRefunded = refunds, ByMode = byMode, Daily = daily
        };
    }

    public async Task<List<DefaulterDto>> GetOutstandingAsync(int? classId, CancellationToken ct = default)
    {
        EnsureFinanceStaff();
        var query =
            from i in _db.FeeInstallments
            join a in _db.StudentFeeAssignments on i.StudentFeeAssignmentId equals a.Id
            join s in _db.Students on a.StudentId equals s.Id
            join klass in _db.Classes on s.ClassId equals klass.Id
            where i.SchoolId == SchoolId && !i.IsDeleted && !a.IsDeleted && a.IsActive &&
                  i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived &&
                  i.Amount + i.PenaltyAmount > i.PaidAmount
            select new { i, s, klass };

        if (classId is > 0) query = query.Where(x => x.s.ClassId == classId);

        var rows = await query.ToListAsync(ct);
        var studentIds = rows.Select(r => r.s.Id).Distinct().ToList();

        var phones = await (
            from sp in _db.StudentParents
            join g in _db.Guardians on sp.GuardianId equals g.Id
            where studentIds.Contains(sp.StudentId) && sp.IsPrimary
            select new { sp.StudentId, g.Phone }).ToDictionaryAsync(x => x.StudentId, x => x.Phone, ct);

        return rows.GroupBy(r => r.s.Id).Select(g =>
        {
            var student = g.First().s;
            return new DefaulterDto
            {
                StudentId = student.Id,
                StudentName = student.FullName,
                AdmissionNo = student.AdmissionNo,
                ClassName = g.First().klass.Name,
                GuardianPhone = phones.GetValueOrDefault(student.Id),
                Outstanding = Math.Round(g.Sum(x => x.i.Amount + x.i.PenaltyAmount - x.i.PaidAmount), 2),
                OldestDueDate = g.Min(x => x.i.DueDate)
            };
        }).OrderByDescending(x => x.Outstanding).ToList();
    }

    public async Task<ReminderResult> QueueFeeRemindersAsync(CancellationToken ct = default)
    {
        EnsureFinanceStaff();
        var overdue = await (
            from i in _db.FeeInstallments
            join a in _db.StudentFeeAssignments on i.StudentFeeAssignmentId equals a.Id
            join s in _db.Students on a.StudentId equals s.Id
            where i.SchoolId == SchoolId && !i.IsDeleted && a.IsActive &&
                  i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived &&
                  i.Amount + i.PenaltyAmount > i.PaidAmount && i.DueDate < _clock.Today
            select new { i, s }).ToListAsync(ct);

        var studentIds = overdue.Select(x => x.s.Id).Distinct().ToList();
        var guardianMap = await (
            from sp in _db.StudentParents
            join g in _db.Guardians on sp.GuardianId equals g.Id
            where studentIds.Contains(sp.StudentId) && sp.IsPrimary
            select new { sp.StudentId, Guardian = g }).ToDictionaryAsync(x => x.StudentId, x => x.Guardian, ct);

        var queued = 0;
        var skipped = 0;
        var today = _clock.Today;

        foreach (var group in overdue.GroupBy(x => x.s.Id))
        {
            var student = group.First().s;
            if (!guardianMap.TryGetValue(student.Id, out var guardian))
            {
                skipped++;
                continue;
            }

            var alreadyQueued = await _db.Notifications.AnyAsync(n =>
                n.SchoolId == SchoolId && n.StudentId == student.Id && n.Category == "FeeReminder" &&
                n.CreatedAtUtc >= today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct);

            if (alreadyQueued)
            {
                skipped++;
                continue;
            }

            var due = group.Sum(x => x.i.Amount + x.i.PenaltyAmount - x.i.PaidAmount);
            _db.Add(new Notification
            {
                SchoolId = SchoolId,
                StudentId = student.Id,
                GuardianId = guardian.Id,
                UserAccountId = guardian.UserAccountId,
                Title = "Fee payment reminder",
                Message = $"An amount of {due:0.00} is overdue for {student.FullName}. Please pay at the earliest.",
                Channel = NotificationChannel.Sms,
                Status = NotificationStatus.Pending,
                Category = "FeeReminder",
                Recipient = guardian.Phone
            });
            queued++;
        }

        if (queued > 0) await _uow.SaveChangesAsync(ct);
        return new ReminderResult { Queued = queued, Skipped = skipped };
    }

    // ---------- Documents ----------

    /// <summary>Printable receipt for a single payment. Parents may fetch their own.</summary>
    public async Task<FeeDocumentDto> GetReceiptAsync(int paymentId, CancellationToken ct = default)
    {
        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.SchoolId == SchoolId && !p.IsDeleted, ct)
            ?? throw DomainException.NotFound("Payment");

        await EnsureCanReadStudentAsync(payment.StudentId, ct);

        var school = await _db.Schools.FirstAsync(s => s.Id == SchoolId, ct);
        var student = await _db.Students.FirstAsync(s => s.Id == payment.StudentId, ct);
        var className = await _db.Classes.Where(c => c.Id == student.ClassId).Select(c => c.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var installmentName = "Fee payment";
        var penaltyTotal = payment.PenaltyPortion;

        if (payment.FeeInstallmentId is > 0)
        {
            var installment = await _db.FeeInstallments
                .FirstOrDefaultAsync(i => i.Id == payment.FeeInstallmentId && !i.IsDeleted, ct);
            if (installment is not null) installmentName = installment.Name;
        }

        return FeeDocumentBuilder.BuildReceipt(school, school.Code, student, className, payment,
            installmentName, penaltyTotal);
    }

    /// <summary>Printable statement of everything billed and paid for a student.</summary>
    public async Task<FeeDocumentDto> GetStatementAsync(int studentId, CancellationToken ct = default)
    {
        await EnsureCanReadStudentAsync(studentId, ct);

        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.Id == studentId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Student");

        var school = await _db.Schools.FirstAsync(s => s.Id == SchoolId, ct);
        var ledger = await BuildLedgerAsync(student, ct);
        var className = await _db.Classes.Where(c => c.Id == student.ClassId).Select(c => c.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return FeeDocumentBuilder.BuildStatement(school, student, className, ledger);
    }

    // ---------- Internals ----------

    private void EnsureFinanceStaff()
    {
        if (_user.IsInRole(RoleCodes.Parent) || _user.IsInRole(RoleCodes.Student))
            throw DomainException.Forbidden();
    }

    private async Task EnsureCanReadStudentAsync(int studentId, CancellationToken ct)
    {
        if (_user.IsInRole(RoleCodes.Student))
        {
            if (_user.StudentId != studentId) throw DomainException.NotFound("Student");
            return;
        }

        if (_user.IsInRole(RoleCodes.Parent))
        {
            var guardianId = _user.GuardianId;
            if (guardianId is null && _user.UserId is int userId)
            {
                guardianId = await _db.UserAccounts.IgnoreQueryFilters()
                    .Where(u => u.Id == userId)
                    .Select(u => u.GuardianId)
                    .FirstOrDefaultAsync(ct);
            }

            var linked = guardianId is not null && await _db.StudentParents.AnyAsync(sp =>
                sp.GuardianId == guardianId && sp.StudentId == studentId && !sp.IsDeleted, ct);
            if (!linked) throw DomainException.NotFound("Student");
        }
    }

    /// <summary>
    /// A parent may only raise and complete payments for their own children. Staff need
    /// fees.collect, which the API layer enforces before reaching this point.
    /// </summary>
    private async Task EnsureCanPayForStudentAsync(int studentId, CancellationToken ct)
    {
        if (_user.IsInRole(RoleCodes.Student))
        {
            if (_user.StudentId != studentId) throw DomainException.Forbidden("You can only pay your own fees.");
            return;
        }

        if (_user.IsInRole(RoleCodes.Parent))
        {
            var guardianId = _user.GuardianId;
            if (guardianId is null && _user.UserId is int userId)
            {
                guardianId = await _db.UserAccounts.IgnoreQueryFilters()
                    .Where(u => u.Id == userId)
                    .Select(u => u.GuardianId)
                    .FirstOrDefaultAsync(ct);
            }

            var linked = guardianId is not null && await _db.StudentParents.AnyAsync(sp =>
                sp.GuardianId == guardianId && sp.StudentId == studentId && !sp.IsDeleted, ct);

            if (!linked) throw DomainException.Forbidden("You can only pay the fees of your own children.");
            return;
        }

        // Everyone else needs the collect permission, which the endpoint policy already
        // checks. This guard protects the service if it is ever called from elsewhere.
        if (!_user.HasPermission(Permissions.FeesCollect))
            throw DomainException.Forbidden("You are not allowed to collect fees.");
    }

    private async Task<StudentLedgerDto> BuildLedgerAsync(Student student, CancellationToken ct)
    {
        var assignments = await _db.StudentFeeAssignments
            .Where(a => a.StudentId == student.Id && a.SchoolId == SchoolId && !a.IsDeleted)
            .ToListAsync(ct);

        var assignmentIds = assignments.Select(a => a.Id).ToList();

        var installments = await _db.FeeInstallments
            .Where(i => assignmentIds.Contains(i.StudentFeeAssignmentId) && !i.IsDeleted)
            .OrderBy(i => i.DueDate).ToListAsync(ct);

        var payments = await _db.Payments
            .Where(p => p.StudentId == student.Id && p.SchoolId == SchoolId && !p.IsDeleted)
            .OrderByDescending(p => p.PaidAtUtc).ToListAsync(ct);

        var today = _clock.Today;
        var className = await _db.Classes.Where(c => c.Id == student.ClassId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

        var structureIds = assignments.Select(a => a.FeeStructureId).Distinct().ToList();
        var planNames = await _db.FeeStructures.Where(f => structureIds.Contains(f.Id) && f.SchoolId == SchoolId)
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        var active = assignments.Where(a => a.IsActive).ToList();

        // A waived installment owes nothing more, whatever its billed amount was.
        static decimal Outstanding(FeeInstallment i) =>
            i.Status == InstallmentStatus.Waived ? 0 : i.TotalAmount - i.PaidAmount;

        return new StudentLedgerDto
        {
            StudentId = student.Id,
            StudentName = student.FullName,
            AdmissionNo = student.AdmissionNo,
            ClassName = className,
            TotalBilled = Math.Round(active.Sum(a => a.NetAmount), 2),
            Discount = Math.Round(active.Sum(a => a.DiscountAmount), 2),
            PlatformFee = Math.Round(active.Sum(a => a.PlatformFeeAmount), 2),
            TotalPaid = Math.Round(payments.Where(p => p.Status != PaymentStatus.Failed)
                .Sum(p => p.Amount - p.RefundedAmount), 2),
            Balance = Math.Round(installments.Sum(Outstanding), 2),
            Overdue = Math.Round(installments
                .Where(i => i.DueDate < today && i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived)
                .Sum(i => i.TotalAmount - i.PaidAmount), 2),
            PenaltyTotal = Math.Round(installments.Sum(i => i.PenaltyAmount), 2),
            Installments = installments.Select(i => new InstallmentDto
            {
                Id = i.Id, StudentFeeAssignmentId = i.StudentFeeAssignmentId, Name = i.Name, DueDate = i.DueDate, Amount = i.Amount,
                PaidAmount = i.PaidAmount, PenaltyAmount = i.PenaltyAmount, TotalAmount = i.TotalAmount,
                Balance = Outstanding(i), Status = i.Status,
                IsOverdue = i.DueDate < today && i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived
            }).ToList(),
            Assignments = assignments.OrderByDescending(a => a.IsActive).ThenByDescending(a => a.AssignedAtUtc)
                .Select(a => new StudentFeeAssignmentDto
                {
                    Id = a.Id, FeeStructureId = a.FeeStructureId,
                    PlanName = planNames.GetValueOrDefault(a.FeeStructureId, string.Empty),
                    TotalAmount = a.TotalAmount, DiscountAmount = a.DiscountAmount, NetAmount = a.NetAmount,
                    PlatformFeeAmount = a.PlatformFeeAmount,
                    PaidAmount = Math.Round(installments.Where(i => i.StudentFeeAssignmentId == a.Id).Sum(i => i.PaidAmount), 2),
                    IsActive = a.IsActive, AssignedAtUtc = a.AssignedAtUtc
                }).ToList(),
            Payments = payments.Select(p => new PaymentDto
            {
                Id = p.Id, ReceiptNo = p.ReceiptNo, StudentId = p.StudentId, StudentName = student.FullName,
                Amount = p.Amount, Mode = p.Mode, Status = p.Status, PaidAtUtc = p.PaidAtUtc,
                ReferenceNo = p.ReferenceNo, RefundedAmount = p.RefundedAmount
            }).ToList()
        };
    }

    /// <summary>
    /// Per-student monthly platform fee for one academic session (monthly amount × months
    /// in the session). Zero unless the school is billed per student.
    /// </summary>
    private async Task<decimal> PlatformFeeForSessionAsync(int academicSessionId, CancellationToken ct)
    {
        var school = await _db.Schools.Where(s => s.Id == SchoolId)
            .Select(s => new { s.PlatformFeeType, s.PlatformFeeAmount }).FirstOrDefaultAsync(ct);
        if (school is null || school.PlatformFeeType != "PerStudentMonthly" || school.PlatformFeeAmount <= 0) return 0;

        var session = await _db.AcademicSessions.Where(s => s.Id == academicSessionId)
            .Select(s => new { s.StartDate, s.EndDate }).FirstOrDefaultAsync(ct);
        var months = session is null ? 12
            : Math.Max(1, (session.EndDate.Year - session.StartDate.Year) * 12 + session.EndDate.Month - session.StartDate.Month + 1);

        return Math.Round(school.PlatformFeeAmount * months, 2, MidpointRounding.AwayFromZero);
    }

    private static List<InstallmentInput> BuildDefaultInstallments(FeeStructure structure, decimal net)
    {
        var quarters = 4;
        var baseAmount = Math.Round(net / quarters, 2, MidpointRounding.AwayFromZero);
        var remainder = net - (baseAmount * quarters);
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5)); // IST

        return Enumerable.Range(0, quarters).Select(i => new InstallmentInput
        {
            Name = $"Quarter {i + 1}",
            DueDate = start.AddMonths(i * 3),
            Amount = i == quarters - 1 ? baseAmount + remainder : baseAmount
        }).ToList();
    }

    private async Task<PaymentDto> MapPaymentAsync(Payment payment, CancellationToken ct)
    {
        var studentName = await _db.Students.Where(s => s.Id == payment.StudentId)
            .Select(s => s.FirstName + " " + s.LastName).FirstOrDefaultAsync(ct) ?? string.Empty;

        return new PaymentDto
        {
            Id = payment.Id, ReceiptNo = payment.ReceiptNo, StudentId = payment.StudentId,
            StudentName = studentName, Amount = payment.Amount, Mode = payment.Mode,
            Status = payment.Status, PaidAtUtc = payment.PaidAtUtc,
            ReferenceNo = payment.ReferenceNo, RefundedAmount = payment.RefundedAmount
        };
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

    private async Task<Guardian?> GetPrimaryGuardianAsync(int studentId, CancellationToken ct) =>
        await (from sp in _db.StudentParents
               join g in _db.Guardians on sp.GuardianId equals g.Id
               where sp.StudentId == studentId && sp.IsPrimary && !sp.IsDeleted
               select g).FirstOrDefaultAsync(ct);
}
