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

        var sessions = await _db.AcademicSessions.Where(s => s.SchoolId == SchoolId).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var classes = await _db.Classes.Where(c => c.SchoolId == SchoolId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return structures.Select(f => new FeeStructureDto
        {
            Id = f.Id, AcademicSessionId = f.AcademicSessionId,
            SessionName = sessions.GetValueOrDefault(f.AcademicSessionId, string.Empty),
            ClassId = f.ClassId, ClassName = classes.GetValueOrDefault(f.ClassId, string.Empty),
            Name = f.Name, TotalAmount = f.TotalAmount, IsActive = f.IsActive,
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

            var assignment = new StudentFeeAssignment
            {
                SchoolId = SchoolId,
                StudentId = student.Id,
                FeeStructureId = structure.Id,
                AcademicSessionId = structure.AcademicSessionId,
                TotalAmount = structure.TotalAmount,
                DiscountAmount = discount,
                NetAmount = net,
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

            foreach (var input in installments)
            {
                if (input.Amount <= 0) throw DomainException.Validation("Installment amounts must be positive.");
                _db.Add(new FeeInstallment
                {
                    SchoolId = SchoolId,
                    StudentFeeAssignmentId = assignment.Id,
                    Name = input.Name.Trim(),
                    DueDate = input.DueDate == default ? _clock.Today : input.DueDate,
                    Amount = input.Amount,
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
                    Message = $"A fee plan of {net:0.00} has been assigned to {student.FullName}. Please review the installments.",
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
                          i.Amount > i.PaidAmount
                    orderby i.DueDate
                    select i).FirstOrDefaultAsync(token)
                    ?? throw DomainException.Validation("The student has no outstanding installments.");

                assignment = await _db.StudentFeeAssignments.FirstAsync(a => a.Id == installment.StudentFeeAssignmentId, token);
            }

            if (installment.Status is InstallmentStatus.Paid or InstallmentStatus.Waived)
                throw DomainException.Conflict("This installment is already settled.");

            var balance = installment.Amount - installment.PaidAmount;
            if (request.Amount > balance + 0.01m)
                throw DomainException.Validation($"Payment exceeds the installment balance of {balance:0.00}.");

            var gatewayPaymentId = request.GatewayPaymentId;
            var gatewayOrderId = request.GatewayOrderId;

            if (request.Mode == PaymentMode.Online)
            {
                if (string.IsNullOrWhiteSpace(gatewayOrderId) || string.IsNullOrWhiteSpace(gatewayPaymentId))
                    throw DomainException.Validation("Online payments require a gateway order and payment id.");

                var verification = await _gateway.VerifyAsync(gatewayOrderId, gatewayPaymentId, request.Amount, token);
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
            var payment = new Payment
            {
                SchoolId = SchoolId,
                BranchId = student.BranchId,
                StudentId = student.Id,
                StudentFeeAssignmentId = assignment.Id,
                FeeInstallmentId = installment.Id,
                ReceiptNo = await NextReceiptNoAsync(school.Code, token),
                Amount = request.Amount,
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
            installment.Status = installment.PaidAmount >= installment.Amount
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

        var refundable = payment.Amount - payment.RefundedAmount;
        if (request.Amount > refundable + 0.01m)
            throw DomainException.Validation($"Refundable amount is {refundable:0.00}.");

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

            var result = await _gateway.RefundAsync(payment.GatewayPaymentId ?? payment.ReferenceNo ?? payment.ReceiptNo,
                refund.Amount, token);
            if (!result.Success)
                throw new DomainException("refund_failed", result.FailureReason ?? "The refund could not be processed.");

            refund.Status = RefundStatus.Processed;
            refund.ApprovedBy = _user.UserId;
            refund.ApprovedAtUtc = _clock.UtcNow;
            refund.ProcessedAtUtc = _clock.UtcNow;
            refund.ReferenceNo = result.GatewayPaymentId;

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
                        : installment.PaidAmount >= installment.Amount
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
                  i.Amount > i.PaidAmount
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
                Outstanding = Math.Round(g.Sum(x => x.i.Amount - x.i.PaidAmount), 2),
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
                  i.Amount > i.PaidAmount && i.DueDate < _clock.Today
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

            var due = group.Sum(x => x.i.Amount - x.i.PaidAmount);
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

        return new StudentLedgerDto
        {
            StudentId = student.Id,
            StudentName = student.FullName,
            AdmissionNo = student.AdmissionNo,
            ClassName = className,
            TotalBilled = Math.Round(assignments.Sum(a => a.NetAmount), 2),
            Discount = Math.Round(assignments.Sum(a => a.DiscountAmount), 2),
            TotalPaid = Math.Round(payments.Where(p => p.Status != PaymentStatus.Failed)
                .Sum(p => p.Amount - p.RefundedAmount), 2),
            Balance = Math.Round(installments.Sum(i => i.Amount - i.PaidAmount), 2),
            Overdue = Math.Round(installments
                .Where(i => i.DueDate < today && i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived)
                .Sum(i => i.Amount - i.PaidAmount), 2),
            Installments = installments.Select(i => new InstallmentDto
            {
                Id = i.Id, Name = i.Name, DueDate = i.DueDate, Amount = i.Amount,
                PaidAmount = i.PaidAmount, Balance = i.Amount - i.PaidAmount, Status = i.Status,
                IsOverdue = i.DueDate < today && i.Status != InstallmentStatus.Paid
            }).ToList(),
            Payments = payments.Select(p => new PaymentDto
            {
                Id = p.Id, ReceiptNo = p.ReceiptNo, StudentId = p.StudentId, StudentName = student.FullName,
                Amount = p.Amount, Mode = p.Mode, Status = p.Status, PaidAtUtc = p.PaidAtUtc,
                ReferenceNo = p.ReferenceNo, RefundedAmount = p.RefundedAmount
            }).ToList()
        };
    }

    private static List<InstallmentInput> BuildDefaultInstallments(FeeStructure structure, decimal net)
    {
        var quarters = 4;
        var baseAmount = Math.Round(net / quarters, 2, MidpointRounding.AwayFromZero);
        var remainder = net - (baseAmount * quarters);
        var start = DateOnly.FromDateTime(DateTime.UtcNow);

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
