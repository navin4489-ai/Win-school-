export interface UserProfile {
  id: number;
  fullName: string;
  email: string;
  role: string;
  roleCode: string;
  schoolId: number;
  schoolName: string;
  schoolLogoUrl?: string | null;
  branchId?: number | null;
  employeeId?: number | null;
  guardianId?: number | null;
  studentId?: number | null;
  permissions: string[];
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  user: UserProfile;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface DashboardCard {
  key: string;
  label: string;
  value: string;
  unit?: string | null;
  hint?: string | null;
  tone: string;
}

export interface ChartPoint { label: string; value: number; }

export interface ActivityItem {
  title: string;
  detail?: string | null;
  atUtc: string;
  tone: string;
}

export interface TimetableSlot {
  period: string;
  subject: string;
  class: string;
  room?: string | null;
  startTime: string;
  endTime: string;
}

export interface Dashboard {
  role: string;
  academicSession?: string | null;
  cards: DashboardCard[];
  feeCollectionTrend: ChartPoint[];
  attendanceTrend: ChartPoint[];
  admissionTrend: ChartPoint[];
  recentActivity: ActivityItem[];
  todayTimetable: TimetableSlot[];
}

export interface StudentListItem {
  id: number;
  admissionNo: string;
  fullName: string;
  className: string;
  sectionName: string;
  rollNo?: string | null;
  phone?: string | null;
  photoUrl?: string | null;
  status: string;
  admissionDate: string;
}

export interface Guardian {
  id: number;
  fullName: string;
  relation: string;
  phone: string;
  email?: string | null;
  isPrimary: boolean;
  isEmergencyContact: boolean;
}

export interface StudentDocument {
  id: number;
  documentId: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  documentType: string;
  isVerified: boolean;
  createdAtUtc: string;
  url?: string | null;
}

export interface StudentDetail {
  id: number;
  admissionNo: string;
  fullName: string;
  firstName: string;
  middleName?: string | null;
  lastName: string;
  academicSessionId: number;
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  rollNo?: string | null;
  dateOfBirth: string;
  gender: string;
  bloodGroup?: string | null;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  admissionDate: string;
  status: string;
  photoUrl?: string | null;
  previousSchool?: string | null;
  guardians: Guardian[];
  enrollments: { id: number; sessionName: string; className: string; sectionName: string; rollNo?: string | null; startDate: string; endDate?: string | null; status: string }[];
  documents: StudentDocument[];
}

export interface Admission {
  id: number;
  applicationNo: string;
  academicSessionId: number;
  classId?: number | null;
  className?: string | null;
  sectionId?: number | null;
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  gender: string;
  guardianName: string;
  guardianPhone: string;
  guardianEmail?: string | null;
  previousSchool?: string | null;
  status: string;
  appliedAtUtc: string;
  interviewAtUtc?: string | null;
  rejectionReason?: string | null;
  studentId?: number | null;
  notes?: string | null;
  source?: string | null;
  isFeePaid: boolean;
}

export interface ClassRoom { id: number; name: string; code: string; stream?: string | null; displayOrder: number; }
export interface Section { id: number; classId: number; name: string; capacity: number; classTeacherId?: number | null; studentCount: number; }
export interface AcademicSession { id: number; name: string; startDate: string; endDate: string; isCurrent: boolean; isClosed: boolean; }

export interface Installment {
  id: number;
  studentFeeAssignmentId: number;
  name: string;
  dueDate: string;
  amount: number;
  paidAmount: number;
  penaltyAmount: number;
  totalAmount: number;
  balance: number;
  status: string;
  isOverdue: boolean;
}

export interface PaymentOrder {
  orderId: string;
  paymentId: string;
  amount: number;
  currency: string;
  checkoutUrl: string;
  expiresAtUtc: string;
  gateway: string;
  keyId: string;
  studentId: number;
  studentName: string;
  installmentName?: string | null;
}

export interface Payment {
  id: number;
  receiptNo: string;
  studentId: number;
  studentName: string;
  amount: number;
  mode: string;
  status: string;
  paidAtUtc: string;
  referenceNo?: string | null;
  refundedAmount: number;
}

export interface Ledger {
  studentId: number;
  studentName: string;
  admissionNo: string;
  className: string;
  totalBilled: number;
  discount: number;
  totalPaid: number;
  balance: number;
  overdue: number;
  penaltyTotal: number;
  platformFee: number;
  installments: Installment[];
  payments: Payment[];
  assignments: FeeAssignment[];
}

export interface FeeAssignment {
  id: number;
  feeStructureId: number;
  planName: string;
  totalAmount: number;
  discountAmount: number;
  netAmount: number;
  platformFeeAmount: number;
  paidAmount: number;
  isActive: boolean;
  assignedAtUtc: string;
}

export interface RosterEntry {
  studentId: number;
  admissionNo: string;
  fullName: string;
  rollNo?: string | null;
  status?: string | null;
  remarks?: string | null;
}

export interface ClassAttendance {
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  date: string;
  isSubmitted: boolean;
  students: RosterEntry[];
}

export interface AttendanceSummary {
  studentId: number;
  from: string;
  to: string;
  totalDays: number;
  present: number;
  absent: number;
  late: number;
  halfDay: number;
  percentage: number;
  records: { date: string; status: string; remarks?: string | null }[];
}

export interface Exam {
  id: number;
  academicSessionId: number;
  name: string;
  examType: string;
  startDate: string;
  endDate: string;
  status: string;
  isRankEnabled: boolean;
  subjectCount: number;
}

export interface ExamResultRow {
  studentId: number;
  studentName: string;
  admissionNo: string;
  className: string;
  totalMarks: number;
  obtainedMarks: number;
  percentage: number;
  grade: string;
  rank?: number | null;
  resultStatus: string;
}

export interface ExamSubject {
  id: number;
  classId: number;
  className: string;
  subjectId: number;
  subjectName: string;
  maxTheoryMarks: number;
  maxPracticalMarks: number;
  passMarks: number;
  examDate?: string | null;
}

export interface StudentMark {
  studentId: number;
  studentName: string;
  admissionNo: string;
  theoryMarks?: number | null;
  practicalMarks?: number | null;
  isAbsent: boolean;
  remarks?: string | null;
}

export interface ReportType { key: string; name: string; description: string; }

export type PaymentMode = 'Cash' | 'Upi' | 'Card' | 'NetBanking' | 'Cheque' | 'BankTransfer' | 'Online';

export interface CollectionPoint { label: string; amount: number; count: number; }

export interface CollectionSummary {
  from: string;
  to: string;
  totalCollected: number;
  paymentCount: number;
  totalRefunded: number;
  byMode: CollectionPoint[];
  daily: CollectionPoint[];
}

export interface Defaulter {
  studentId: number;
  studentName: string;
  admissionNo: string;
  className: string;
  guardianPhone?: string | null;
  outstanding: number;
  oldestDueDate: string;
}

export interface NotificationItem {
  id: number;
  title: string;
  message: string;
  category: string;
  channel: string;
  status: string;
  createdAtUtc: string;
  readAtUtc?: string | null;
}

export interface Lookup { id: number; name: string; code?: string | null; }

export interface Child {
  studentId: number;
  admissionNo: string;
  fullName: string;
  className: string;
  sectionName: string;
  photoUrl?: string | null;
  rollNo?: string | null;
}
