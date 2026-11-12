class UserProfile {
  const UserProfile({
    required this.id,
    required this.fullName,
    required this.email,
    required this.role,
    required this.roleCode,
    required this.schoolId,
    required this.schoolName,
    this.branchId,
    this.permissions = const [],
  });

  final int id;
  final String fullName;
  final String email;
  final String role;
  final String roleCode;
  final int schoolId;
  final String schoolName;
  final int? branchId;
  final List<String> permissions;

  factory UserProfile.fromJson(Map<String, dynamic> json) => UserProfile(
        id: asInt(json['id']),
        fullName: asText(json['fullName']),
        email: asText(json['email']),
        role: asText(json['role']),
        roleCode: asText(json['roleCode']),
        schoolId: asInt(json['schoolId']),
        schoolName: asText(json['schoolName']),
        branchId: asIntOrNull(json['branchId']),
        permissions: (json['permissions'] as List<dynamic>? ?? [])
            .map((value) => value.toString())
            .toList(),
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'fullName': fullName,
        'email': email,
        'role': role,
        'roleCode': roleCode,
        'schoolId': schoolId,
        'schoolName': schoolName,
        'branchId': branchId,
        'permissions': permissions,
      };
}

class DashboardCard {
  const DashboardCard({
    required this.label,
    required this.value,
    this.unit,
    this.hint,
    this.tone = 'neutral',
  });

  final String label;
  final String value;
  final String? unit;
  final String? hint;
  final String tone;

  factory DashboardCard.fromJson(Map<String, dynamic> json) => DashboardCard(
        label: asText(json['label']),
        value: asText(json['value']),
        unit: json['unit']?.toString(),
        hint: json['hint']?.toString(),
        tone: (json['tone']?.toString() ?? 'neutral'),
      );
}

class ActivityItem {
  const ActivityItem({required this.title, this.detail, this.atUtc, this.tone = 'neutral'});

  final String title;
  final String? detail;
  final String? atUtc;
  final String tone;

  factory ActivityItem.fromJson(Map<String, dynamic> json) => ActivityItem(
        title: asText(json['title']),
        detail: json['detail']?.toString(),
        atUtc: json['atUtc']?.toString(),
        tone: (json['tone']?.toString() ?? 'neutral'),
      );
}

class Dashboard {
  const Dashboard({
    required this.role,
    this.academicSession,
    this.cards = const [],
    this.activity = const [],
  });

  final String role;
  final String? academicSession;
  final List<DashboardCard> cards;
  final List<ActivityItem> activity;

  factory Dashboard.fromJson(Map<String, dynamic> json) => Dashboard(
        role: asText(json['role']),
        academicSession: json['academicSession']?.toString(),
        cards: (json['cards'] as List<dynamic>? ?? [])
            .whereType<Map<String, dynamic>>()
            .map(DashboardCard.fromJson)
            .toList(),
        activity: (json['recentActivity'] as List<dynamic>? ?? [])
            .whereType<Map<String, dynamic>>()
            .map(ActivityItem.fromJson)
            .toList(),
      );
}

class ClassRoom {
  const ClassRoom({required this.id, required this.name, this.code = ''});

  final int id;
  final String name;
  final String code;

  factory ClassRoom.fromJson(Map<String, dynamic> json) => ClassRoom(
        id: asInt(json['id']),
        name: asText(json['name']),
        code: asText(json['code']),
      );
}

class Section {
  const Section({required this.id, required this.name, this.classId = 0});

  final int id;
  final String name;
  final int classId;

  factory Section.fromJson(Map<String, dynamic> json) => Section(
        id: asInt(json['id']),
        name: asText(json['name']),
        classId: asInt(json['classId']),
      );
}

class RosterStudent {
  RosterStudent({
    required this.studentId,
    required this.fullName,
    required this.admissionNo,
    this.rollNo,
    this.status,
  });

  final int studentId;
  final String fullName;
  final String admissionNo;
  final String? rollNo;
  String? status;

  factory RosterStudent.fromJson(Map<String, dynamic> json) => RosterStudent(
        studentId: asInt(json['studentId']),
        fullName: asText(json['fullName']),
        admissionNo: asText(json['admissionNo']),
        rollNo: json['rollNo']?.toString(),
        status: json['status']?.toString(),
      );
}

class ClassAttendance {
  ClassAttendance({
    required this.classId,
    required this.className,
    required this.sectionId,
    required this.sectionName,
    required this.date,
    required this.students,
    this.isSubmitted = false,
  });

  final int classId;
  final String className;
  final int sectionId;
  final String sectionName;
  final String date;
  final List<RosterStudent> students;
  final bool isSubmitted;

  factory ClassAttendance.fromJson(Map<String, dynamic> json) => ClassAttendance(
        classId: asInt(json['classId']),
        className: asText(json['className']),
        sectionId: asInt(json['sectionId']),
        sectionName: asText(json['sectionName']),
        date: asText(json['date']),
        isSubmitted: json['isSubmitted'] == true,
        students: (json['students'] as List<dynamic>? ?? [])
            .whereType<Map<String, dynamic>>()
            .map(RosterStudent.fromJson)
            .toList(),
      );
}

class Installment {
  const Installment({
    required this.id,
    required this.name,
    required this.dueDate,
    required this.amount,
    required this.paidAmount,
    required this.balance,
    required this.status,
    required this.isOverdue,
  });

  final int id;
  final String name;
  final String dueDate;
  final double amount;
  final double paidAmount;
  final double balance;
  final String status;
  final bool isOverdue;

  factory Installment.fromJson(Map<String, dynamic> json) => Installment(
        id: asInt(json['id']),
        name: asText(json['name']),
        dueDate: asText(json['dueDate']),
        amount: asDouble(json['amount']),
        paidAmount: asDouble(json['paidAmount']),
        balance: asDouble(json['balance']),
        status: asText(json['status']),
        isOverdue: json['isOverdue'] == true,
      );
}

class Ledger {
  const Ledger({
    required this.studentId,
    required this.studentName,
    required this.admissionNo,
    required this.className,
    required this.totalBilled,
    required this.totalPaid,
    required this.balance,
    required this.overdue,
    this.installments = const [],
  });

  final int studentId;
  final String studentName;
  final String admissionNo;
  final String className;
  final double totalBilled;
  final double totalPaid;
  final double balance;
  final double overdue;
  final List<Installment> installments;

  factory Ledger.fromJson(Map<String, dynamic> json) => Ledger(
        studentId: asInt(json['studentId']),
        studentName: asText(json['studentName']),
        admissionNo: asText(json['admissionNo']),
        className: asText(json['className']),
        totalBilled: asDouble(json['totalBilled']),
        totalPaid: asDouble(json['totalPaid']),
        balance: asDouble(json['balance']),
        overdue: asDouble(json['overdue']),
        installments: (json['installments'] as List<dynamic>? ?? [])
            .whereType<Map<String, dynamic>>()
            .map(Installment.fromJson)
            .toList(),
      );
}

class Exam {
  const Exam({
    required this.id,
    required this.name,
    required this.examType,
    required this.startDate,
    required this.endDate,
    required this.status,
    this.subjectCount = 0,
  });

  final int id;
  final String name;
  final String examType;
  final String startDate;
  final String endDate;
  final String status;
  final int subjectCount;

  factory Exam.fromJson(Map<String, dynamic> json) => Exam(
        id: asInt(json['id']),
        name: asText(json['name']),
        examType: asText(json['examType']),
        startDate: asText(json['startDate']),
        endDate: asText(json['endDate']),
        status: asText(json['status']),
        subjectCount: asInt(json['subjectCount']),
      );
}

class ExamResult {
  const ExamResult({
    required this.studentId,
    required this.studentName,
    required this.admissionNo,
    required this.totalMarks,
    required this.obtainedMarks,
    required this.percentage,
    required this.resultStatus,
    this.grade,
    this.rank,
  });

  final int studentId;
  final String studentName;
  final String admissionNo;
  final double totalMarks;
  final double obtainedMarks;
  final double percentage;
  final String resultStatus;
  final String? grade;
  final int? rank;

  factory ExamResult.fromJson(Map<String, dynamic> json) => ExamResult(
        studentId: asInt(json['studentId']),
        studentName: asText(json['studentName']),
        admissionNo: asText(json['admissionNo']),
        totalMarks: asDouble(json['totalMarks']),
        obtainedMarks: asDouble(json['obtainedMarks']),
        percentage: asDouble(json['percentage']),
        resultStatus: asText(json['resultStatus']),
        grade: json['grade']?.toString(),
        rank: asIntOrNull(json['rank']),
      );
}

class NotificationItem {
  const NotificationItem({
    required this.id,
    required this.title,
    required this.message,
    required this.category,
    required this.channel,
    required this.status,
    required this.createdAtUtc,
    this.readAtUtc,
  });

  final int id;
  final String title;
  final String message;
  final String category;
  final String channel;
  final String status;
  final String createdAtUtc;
  final String? readAtUtc;

  bool get isUnread => readAtUtc == null;

  factory NotificationItem.fromJson(Map<String, dynamic> json) => NotificationItem(
        id: asInt(json['id']),
        title: asText(json['title']),
        message: asText(json['message']),
        category: asText(json['category']),
        channel: asText(json['channel']),
        status: asText(json['status']),
        createdAtUtc: asText(json['createdAtUtc']),
        readAtUtc: json['readAtUtc']?.toString(),
      );
}

class StudentSummary {
  const StudentSummary({
    required this.id,
    required this.admissionNo,
    required this.fullName,
    required this.className,
    required this.sectionName,
    required this.status,
    this.phone,
  });

  final int id;
  final String admissionNo;
  final String fullName;
  final String className;
  final String sectionName;
  final String status;
  final String? phone;

  factory StudentSummary.fromJson(Map<String, dynamic> json) => StudentSummary(
        id: asInt(json['id']),
        admissionNo: asText(json['admissionNo']),
        fullName: asText(json['fullName']),
        className: asText(json['className']),
        sectionName: asText(json['sectionName']),
        status: asText(json['status']),
        phone: json['phone']?.toString(),
      );
}

class Guardian {
  const Guardian({required this.fullName, required this.relation, required this.phone});

  final String fullName;
  final String relation;
  final String phone;

  factory Guardian.fromJson(Map<String, dynamic> json) => Guardian(
        fullName: asText(json['fullName']),
        relation: asText(json['relation']),
        phone: asText(json['phone']),
      );
}

class StudentDetail {
  const StudentDetail({
    required this.id,
    required this.admissionNo,
    required this.fullName,
    required this.className,
    required this.sectionName,
    required this.status,
    required this.dateOfBirth,
    required this.gender,
    this.rollNo,
    this.phone,
    this.email,
    this.address,
    this.city,
    this.state,
    this.guardians = const [],
  });

  final int id;
  final String admissionNo;
  final String fullName;
  final String className;
  final String sectionName;
  final String status;
  final String dateOfBirth;
  final String gender;
  final String? rollNo;
  final String? phone;
  final String? email;
  final String? address;
  final String? city;
  final String? state;
  final List<Guardian> guardians;

  factory StudentDetail.fromJson(Map<String, dynamic> json) => StudentDetail(
        id: asInt(json['id']),
        admissionNo: asText(json['admissionNo']),
        fullName: asText(json['fullName']),
        className: asText(json['className']),
        sectionName: asText(json['sectionName']),
        status: asText(json['status']),
        dateOfBirth: asText(json['dateOfBirth']),
        gender: asText(json['gender']),
        rollNo: json['rollNo']?.toString(),
        phone: json['phone']?.toString(),
        email: json['email']?.toString(),
        address: json['address']?.toString(),
        city: json['city']?.toString(),
        state: json['state']?.toString(),
        guardians: (json['guardians'] as List<dynamic>? ?? [])
            .whereType<Map<String, dynamic>>()
            .map(Guardian.fromJson)
            .toList(),
      );
}

int asInt(dynamic value) {
  if (value is int) return value;
  return int.tryParse(value?.toString() ?? '') ?? 0;
}

int? asIntOrNull(dynamic value) {
  if (value == null) return null;
  if (value is int) return value;
  return int.tryParse(value.toString());
}

double asDouble(dynamic value) {
  if (value is num) return value.toDouble();
  return double.tryParse(value?.toString() ?? '') ?? 0;
}

String asText(dynamic value) => value?.toString() ?? '';
