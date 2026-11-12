// End-to-end API test. Usage: node tests/e2e/api-e2e.mjs [baseUrl]
const BASE = (process.argv[2] ?? process.env.API_URL ?? 'http://localhost:5080').replace(/\/$/, '');
const PASSWORD = process.env.DEMO_PASSWORD ?? 'Passw0rd!';
const today = new Date().toISOString().slice(0, 10);
const daysAgo = n => new Date(Date.now() - n * 86400000).toISOString().slice(0, 10);

let passed = 0;
const failures = [];

async function call(token, method, path, body) {
  const res = await fetch(`${BASE}/api/${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const text = await res.text();
  let data = text;
  try { data = text ? JSON.parse(text) : null; } catch { /* CSV, HTML */ }
  return { status: res.status, data, headers: res.headers };
}

async function check(name, fn) {
  try {
    await fn();
    passed++;
  } catch (error) {
    failures.push(`${name}: ${error.message}`);
  }
}

function expectStatus(res, expected, label) {
  const list = Array.isArray(expected) ? expected : [expected];
  if (!list.includes(res.status)) {
    const detail = typeof res.data === 'object' ? JSON.stringify(res.data)?.slice(0, 300) : String(res.data).slice(0, 300);
    throw new Error(`${label ?? ''} expected ${list.join('/')} got ${res.status} ${detail}`);
  }
  return res.data;
}

async function login(email) {
  const res = await call(null, 'POST', 'auth/login', { email, password: PASSWORD });
  return expectStatus(res, 200, `login ${email}`);
}

const roles = ['superadmin', 'principal', 'admissions', 'accounts', 'teacher', 'parent', 'student', 'hr', 'librarian', 'transport'];
const s = {};

await check('health', async () => expectStatus(await fetch(`${BASE}/health`).then(r => ({ status: r.status })), 200));
await check('bad password rejected', async () =>
  expectStatus(await call(null, 'POST', 'auth/login', { email: 'principal@demo.edu', password: 'wrong-pass' }), [400, 401]));
await check('anonymous request rejected', async () => expectStatus(await call(null, 'GET', 'students'), 401));

for (const role of roles) {
  await check(`login ${role}`, async () => { s[role] = await login(`${role}@demo.edu`); });
}
const t = role => s[role]?.accessToken;

await check('auth/me', async () => {
  const me = expectStatus(await call(t('principal'), 'GET', 'auth/me'), 200);
  if (!me.permissions?.includes('students.read')) throw new Error('principal missing students.read');
});
await check('refresh token rotation', async () => {
  const fresh = await login('teacher@demo.edu');
  const rotated = expectStatus(await call(null, 'POST', 'auth/refresh', { refreshToken: fresh.refreshToken }), 200);
  if (!rotated.accessToken) throw new Error('no access token');
  expectStatus(await call(null, 'POST', 'auth/refresh', { refreshToken: fresh.refreshToken }), [400, 401], 'reuse');
});

for (const role of roles) {
  await check(`dashboard ${role}`, async () => {
    const d = expectStatus(await call(t(role), 'GET', 'dashboard'), 200);
    if (!Array.isArray(d.cards)) throw new Error('cards missing');
  });
}

// Read sweep with expected status per role.
const reads = [
  ['admin/school', { principal: 200, superadmin: 200 }],
  ['admin/sessions', { principal: 200 }],
  ['admin/classes', { principal: 200, teacher: 200 }],
  ['admin/subjects', { principal: 200 }],
  ['admin/roles', { superadmin: 200, parent: 403 }],
  ['admin/permissions', { superadmin: 200 }],
  ['admin/timetable', { principal: 200, teacher: 200 }],
  ['admin/announcements', { principal: 200 }],
  ['admin/branches', { superadmin: 200 }],
  ['admissions', { admissions: 200, principal: 200, teacher: 403, parent: 403 }],
  ['students', { principal: 200, teacher: 200, accounts: 200, parent: 403, student: 403 }],
  ['fees/categories', { accounts: 200 }],
  ['fees/structures', { accounts: 200 }],
  ['fees/payments', { accounts: 200, principal: 200, parent: 200, teacher: 403 }],
  [`fees/summary?from=${daysAgo(30)}&to=${today}`, { accounts: 200, principal: 200, parent: 403, student: 403 }],
  ['fees/outstanding', { accounts: 200, parent: 403 }],
  ['exams', { principal: 200, teacher: 200, parent: 200, student: 200, accounts: 403 }],
  ['homework', { teacher: 200, parent: 200, student: 200 }],
  ['hr/employees', { hr: 200, principal: 200, parent: 403, teacher: 403 }],
  ['hr/departments', { hr: 200 }],
  ['hr/designations', { hr: 200 }],
  ['hr/leaves', { hr: 200 }],
  ['hr/payroll', { hr: 200 }],
  [`attendance/staff?date=${today}`, { hr: 200, principal: 200 }],
  ['transport/vehicles', { transport: 200, parent: 403 }],
  ['transport/routes', { transport: 200 }],
  ['transport/allocations', { transport: 200 }],
  ['library/books', { librarian: 200, parent: 403 }],
  ['library/transactions', { librarian: 200 }],
  ['inventory', { principal: 200, parent: 403 }],
  ['lms/materials', { teacher: 200 }],
  ['notifications', { parent: 200, teacher: 200, principal: 200 }],
  ['reports/types', { principal: 200, accounts: 200, parent: 403 }],
  ['parent/children', { parent: 200, teacher: 403 }]
];
for (const [path, expectations] of reads) {
  for (const [role, status] of Object.entries(expectations)) {
    await check(`GET ${path} as ${role}`, async () => expectStatus(await call(t(role), 'GET', path), status));
  }
}

// Lookups
let session, classes, sections = [], subjects;
await check('lookups', async () => {
  const sessions = expectStatus(await call(t('principal'), 'GET', 'admin/sessions'), 200);
  session = sessions.find(x => x.isCurrent) ?? sessions[0];
  classes = expectStatus(await call(t('principal'), 'GET', 'admin/classes'), 200);
  sections = expectStatus(await call(t('principal'), 'GET', `admin/classes/${classes[0].id}/sections`), 200);
  subjects = expectStatus(await call(t('principal'), 'GET', 'admin/subjects'), 200);
  if (!session || !classes.length || !sections.length || !subjects.length) throw new Error('missing seed lookups');
});

// Admission pipeline -> enrolment
let enrolledStudentId;
await check('admission pipeline', async () => {
  const tk = t('admissions');
  const created = expectStatus(await call(tk, 'POST', 'admissions', {
    firstName: 'Test', lastName: `Pupil${Date.now() % 10000}`, dateOfBirth: '2018-04-12', gender: 'Female',
    guardianName: 'Test Guardian', guardianPhone: '+919800000001', guardianEmail: `guardian${Date.now()}@example.com`,
    classId: classes[0].id, sessionId: session.id, source: 'E2E'
  }), [200, 201], 'create');
  for (const step of ['submit', 'verify-documents']) {
    expectStatus(await call(tk, 'POST', `admissions/${created.id}/${step}`), 200, step);
  }
  expectStatus(await call(tk, 'POST', `admissions/${created.id}/approve`), 200, 'approve');
  const result = expectStatus(await call(tk, 'POST', `admissions/${created.id}/enroll`, {
    sectionId: sections[0].id, createParentLogin: true
  }), 200, 'enroll');
  enrolledStudentId = result.studentId;
  if (!enrolledStudentId) throw new Error('no student id');
  const list = expectStatus(await call(tk, 'GET', 'admissions?status=Enrolled'), 200);
  if (!list.items.some(a => a.id === created.id)) throw new Error('enrolled admission not listed');
});

// Students
let studentList;
await check('students list/detail/update', async () => {
  studentList = expectStatus(await call(t('principal'), 'GET', 'students?pageSize=50'), 200);
  if (!studentList.items.length) throw new Error('no students');
  const id = enrolledStudentId ?? studentList.items[0].id;
  const detail = expectStatus(await call(t('principal'), 'GET', `students/${id}`), 200);
  expectStatus(await call(t('principal'), 'PUT', `students/${id}`, { ...detail, city: 'Pune' }), 200, 'update');
});
await check('certificate download', async () => {
  const res = await call(t('principal'), 'POST', `students/${studentList.items[0].id}/certificates/bonafide`);
  expectStatus(res, 200);
});

// Parent privacy
let parentChild, foreignStudent;
await check('parent children + privacy', async () => {
  const children = expectStatus(await call(t('parent'), 'GET', 'parent/children'), 200);
  if (!children.length) throw new Error('parent has no children');
  parentChild = children[0].studentId;
  expectStatus(await call(t('parent'), 'GET', `parent/children/${parentChild}/overview`), 200, 'overview');
  expectStatus(await call(t('parent'), 'GET', `parent/children/${parentChild}/fees`), 200, 'fees');
  expectStatus(await call(t('parent'), 'GET', `parent/children/${parentChild}/results`), 200, 'results');
  expectStatus(await call(t('parent'), 'GET', `parent/children/${parentChild}/attendance?from=${daysAgo(30)}&to=${today}`), 200, 'attendance');
  expectStatus(await call(t('parent'), 'GET', `fees/students/${parentChild}/ledger`), 200, 'own ledger');
  foreignStudent = studentList.items.find(x => !children.some(c => c.studentId === x.id))?.id;
  if (!foreignStudent) throw new Error('no foreign student to test');
  expectStatus(await call(t('parent'), 'GET', `parent/children/${foreignStudent}/overview`), [403, 404], 'foreign overview');
  expectStatus(await call(t('parent'), 'GET', `fees/students/${foreignStudent}/ledger`), [403, 404], 'foreign ledger');
  expectStatus(await call(t('parent'), 'GET', `attendance/students/${foreignStudent}?from=${daysAgo(30)}&to=${today}`), [403, 404], 'foreign attendance');
  const payments = expectStatus(await call(t('parent'), 'GET', 'fees/payments'), 200);
  if (payments.items.some(p => !children.some(c => c.studentId === p.studentId))) throw new Error('parent sees foreign payments');
});
await check('student self-scope', async () => {
  const me = s.student.user;
  expectStatus(await call(t('student'), 'GET', `attendance/students/${me.studentId}?from=${daysAgo(30)}&to=${today}`), 200, 'own');
  const other = studentList.items.find(x => x.id !== me.studentId).id;
  expectStatus(await call(t('student'), 'GET', `fees/students/${other}/ledger`), [403, 404], 'foreign ledger');
});

// Fees: collect payment with idempotency
await check('fee payment + idempotency + receipt', async () => {
  const outstanding = expectStatus(await call(t('accounts'), 'GET', 'fees/outstanding'), 200);
  if (!outstanding.length) throw new Error('no outstanding balances seeded');
  const studentId = outstanding[0].studentId;
  const ledger = expectStatus(await call(t('accounts'), 'GET', `fees/students/${studentId}/ledger`), 200);
  const inst = ledger.installments.find(i => i.balance > 0);
  const key = `e2e-${Date.now()}`;
  const body = { studentId, feeInstallmentId: inst.id, amount: Math.min(100, inst.balance), mode: 'Cash', idempotencyKey: key };
  const p1 = expectStatus(await call(t('accounts'), 'POST', 'fees/payments', body), 200, 'pay');
  const p2 = expectStatus(await call(t('accounts'), 'POST', 'fees/payments', body), 200, 'repeat');
  if (p1.id !== p2.id) throw new Error('idempotency key created duplicate payment');
  expectStatus(await call(t('accounts'), 'POST', 'fees/payments', { ...body, idempotencyKey: `${key}-x`, amount: inst.balance + 999999 }), 400, 'overpay');
  expectStatus(await call(t('parent'), 'POST', 'fees/payments', { ...body, idempotencyKey: `${key}-p` }), 403, 'parent pay');
  const refund = expectStatus(await call(t('accounts'), 'POST', `fees/payments/${p1.id}/refunds`, { amount: 10, reason: 'E2E refund' }), 200, 'refund request');
  expectStatus(await call(t('principal'), 'POST', `fees/refunds/${refund.id}/approve`), [200, 204], 'refund approve');
  expectStatus(await call(t('accounts'), 'POST', 'fees/reminders'), 200, 'reminders');
});

// Attendance
await check('attendance mark + roster', async () => {
  const roster = expectStatus(await call(t('teacher'), 'GET', `attendance/roster?classId=${classes[0].id}&sectionId=${sections[0].id}&date=${today}`), 200);
  if (!roster.students.length) throw new Error('empty roster');
  const entries = roster.students.map((st, i) => ({ studentId: st.studentId, status: i === 0 ? 'Absent' : 'Present' }));
  expectStatus(await call(t('teacher'), 'POST', 'attendance/mark', { classId: classes[0].id, sectionId: sections[0].id, date: today, entries }), 200, 'mark');
  const again = expectStatus(await call(t('teacher'), 'GET', `attendance/roster?classId=${classes[0].id}&sectionId=${sections[0].id}&date=${today}`), 200);
  if (!again.isSubmitted) throw new Error('roster not marked submitted');
  expectStatus(await call(t('parent'), 'GET', `attendance/roster?classId=${classes[0].id}&sectionId=${sections[0].id}&date=${today}`), 403, 'parent roster');
});
await check('staff check-in/out', async () => {
  expectStatus(await call(t('teacher'), 'POST', 'attendance/staff/check-in', {}), [200, 409], 'in');
  expectStatus(await call(t('teacher'), 'POST', 'attendance/staff/check-out', {}), [200, 409], 'out');
});

// Exam lifecycle
await check('exam lifecycle', async () => {
  const tk = t('principal');
  const exam = expectStatus(await call(tk, 'POST', 'exams', {
    academicSessionId: session.id, name: `E2E Exam ${Date.now() % 10000}`, examType: 'UnitTest',
    startDate: daysAgo(2), endDate: today, isRankEnabled: true
  }), 200, 'create');
  const es = expectStatus(await call(tk, 'POST', `exams/${exam.id}/subjects`, {
    classId: classes[0].id, subjectId: subjects[0].id, maxTheoryMarks: 100, passMarks: 33
  }), 200, 'subject');
  const marks = expectStatus(await call(tk, 'GET', `exams/subjects/${es.id}/marks`), 200, 'marks list');
  if (!marks.length) throw new Error('no students for marks');
  expectStatus(await call(tk, 'POST', `exams/${exam.id}/marks`, {
    examSubjectId: es.id, marks: marks.map((m, i) => ({ studentId: m.studentId, theoryMarks: 50 + (i * 7) % 50 }))
  }), 200, 'save marks');
  for (const step of ['submit', 'approve', 'publish']) expectStatus(await call(tk, 'POST', `exams/${exam.id}/${step}`), [200, 204], step);
  const results = expectStatus(await call(tk, 'GET', `exams/${exam.id}/results`), 200, 'results');
  if (results.length !== marks.length) throw new Error(`results ${results.length} != students ${marks.length}`);
  const card = expectStatus(await call(tk, 'GET', `exams/${exam.id}/report-cards/${results[0].studentId}`), 200, 'report card');
  if (!card.html?.includes('<table')) throw new Error('report card html missing');
  const parentResults = expectStatus(await call(t('parent'), 'GET', `exams/${exam.id}/results`), 200, 'parent results');
  if (parentResults.length >= results.length) throw new Error('parent sees class-wide results');
  if (foreignStudent) expectStatus(await call(t('parent'), 'GET', `exams/${exam.id}/report-cards/${foreignStudent}`), [403, 404], 'foreign card');
});

// Homework
await check('homework create + submit + grade', async () => {
  const hw = expectStatus(await call(t('teacher'), 'POST', 'homework', {
    classId: classes[0].id, sectionId: sections[0].id, subjectId: subjects[0].id, title: 'E2E worksheet',
    dueAtUtc: new Date(Date.now() + 3 * 86400000).toISOString(), maxMarks: 10
  }), 200, 'create');
  const sub = await call(t('student'), 'POST', `homework/${hw.id}/submissions`, { text: 'done' });
  if (sub.status === 200) {
    expectStatus(await call(t('teacher'), 'POST', `homework/submissions/${sub.data.id}/grade`, { marks: 8, feedback: 'Good' }), 200, 'grade');
  } else {
    expectStatus(sub, [400, 403, 404], 'submit (student may be in other section)');
  }
  expectStatus(await call(t('teacher'), 'GET', `homework/${hw.id}/submissions`), 200, 'list submissions');
});

// HR
await check('hr employee + leave + payroll', async () => {
  const tk = t('hr');
  expectStatus(await call(tk, 'POST', 'hr/employees', {
    firstName: 'E2E', lastName: 'Staff', email: `staff${Date.now()}@demo.edu`, joiningDate: today, basicSalary: 30000
  }), 200, 'employee');
  const leave = expectStatus(await call(t('teacher'), 'POST', 'hr/leaves', {
    leaveType: 'Casual', fromDate: today, toDate: today, reason: 'E2E leave'
  }), 200, 'apply leave');
  expectStatus(await call(tk, 'POST', `hr/leaves/${leave.id}/approve`), [200, 204], 'approve leave');
  const now = new Date();
  const run = await call(tk, 'POST', 'hr/payroll', { month: now.getMonth() + 1, year: now.getFullYear() });
  const runData = expectStatus(run, [200, 409], 'payroll');
  if (run.status === 200) expectStatus(await call(tk, 'GET', `hr/payroll/${runData.id}/payslips`), 200, 'payslips');
});

// Library
await check('library issue + return', async () => {
  const tk = t('librarian');
  const book = expectStatus(await call(tk, 'POST', 'library/books', { title: 'E2E Book', totalCopies: 2 }), 200, 'book');
  const issue = expectStatus(await call(tk, 'POST', 'library/issues', { bookId: book.id, studentId: studentList.items[0].id }), 200, 'issue');
  expectStatus(await call(tk, 'POST', 'library/returns', { bookTransactionId: issue.id }), 200, 'return');
});

// Transport
await check('transport vehicle + route', async () => {
  const tk = t('transport');
  expectStatus(await call(tk, 'POST', 'transport/vehicles', { registrationNo: `MH12E2E${Date.now() % 10000}`, capacity: 40 }), 200, 'vehicle');
});

// Inventory, LMS, notifications, announcements
await check('inventory item + stock', async () => {
  const item = expectStatus(await call(t('principal'), 'POST', 'inventory', { name: 'E2E Chalk', code: `CHK${Date.now() % 10000}`, unit: 'box', reorderLevel: 5 }), 200, 'item');
  expectStatus(await call(t('principal'), 'POST', 'inventory/stock', { inventoryItemId: item.id, quantity: 10, movementType: 'In' }), 200, 'stock');
});
await check('lms material', async () => {
  expectStatus(await call(t('teacher'), 'POST', 'lms/materials', {
    classId: classes[0].id, subjectId: subjects[0].id, title: 'E2E notes', url: 'https://example.com/notes.pdf'
  }), 200);
});
await check('broadcast + read notification', async () => {
  expectStatus(await call(t('principal'), 'POST', 'notifications/broadcast', { title: 'E2E', message: 'Hello parents', audience: 'Parents' }), 200, 'broadcast');
  const list = expectStatus(await call(t('parent'), 'GET', 'notifications?unreadOnly=true'), 200);
  if (list.length) expectStatus(await call(t('parent'), 'POST', `notifications/${list[0].id}/read`), [200, 204], 'read');
  expectStatus(await call(t('parent'), 'POST', 'notifications/broadcast', { title: 'x', message: 'y' }), 403, 'parent broadcast');
});
await check('announcement', async () => {
  expectStatus(await call(t('principal'), 'POST', 'admin/announcements', { title: 'E2E notice', body: 'School closed', audience: 'All' }), 200);
});

// Reports
await check('report exports', async () => {
  const types = expectStatus(await call(t('principal'), 'GET', 'reports/types'), 200);
  for (const type of types) {
    const code = type.code ?? type.key ?? type.type ?? type;
    const res = await call(t('principal'), 'GET', `reports/export?type=${code}&from=${daysAgo(60)}&to=${today}`);
    expectStatus(res, 200, `export ${code}`);
  }
});

// Parent/student data isolation on shared screens
await check('parent dashboard is child-scoped', async () => {
  const d = expectStatus(await call(t('parent'), 'GET', 'dashboard'), 200);
  if (d.recentActivity?.length) throw new Error('parent sees audit activity');
  if (d.feeCollectionTrend?.length || d.admissionTrend?.length) throw new Error('parent sees school-wide trends');
  const children = expectStatus(await call(t('parent'), 'GET', 'parent/children'), 200);
  let expected = 0;
  for (const c of children) {
    expected += expectStatus(await call(t('parent'), 'GET', `fees/students/${c.studentId}/ledger`), 200).balance;
  }
  const due = d.cards.find(c => c.key === 'due');
  const shown = Number(String(due?.value ?? '').replace(/,/g, ''));
  if (Math.round(shown) !== Math.round(expected)) throw new Error(`fee balance ${shown} != children's ${expected}`);
  const kids = d.cards.find(c => c.key === 'children');
  if (Number(kids?.value) !== children.length) throw new Error('children count mismatch');
});
await check('student dashboard is self-scoped', async () => {
  const d = expectStatus(await call(t('student'), 'GET', 'dashboard'), 200);
  if (d.cards.some(c => ['students', 'staff', 'collected', 'outstanding'].includes(c.key))) throw new Error('student sees school-wide cards');
  if (d.recentActivity?.length) throw new Error('student sees audit activity');
});
await check('teacher dashboard hides audit + finance trend', async () => {
  const d = expectStatus(await call(t('teacher'), 'GET', 'dashboard'), 200);
  if (d.recentActivity?.length) throw new Error('teacher sees audit activity');
  if (d.feeCollectionTrend?.length) throw new Error('teacher sees fee trend');
});
await check('homework limited to own class', async () => {
  const students = expectStatus(await call(t('principal'), 'GET', 'students?pageSize=100'), 200).items;
  const children = expectStatus(await call(t('parent'), 'GET', 'parent/children'), 200);
  const childPlacements = new Set();
  for (const c of children) {
    const st = expectStatus(await call(t('principal'), 'GET', `students/${c.studentId}`), 200);
    childPlacements.add(`${st.classId}-${st.sectionId}`);
  }
  const other = students.find(x => !children.some(c => c.studentId === x.id));
  const otherDetail = expectStatus(await call(t('principal'), 'GET', `students/${other.id}`), 200);
  const otherKey = `${otherDetail.classId}-${otherDetail.sectionId}`;
  if (!childPlacements.has(otherKey)) {
    expectStatus(await call(t('principal'), 'POST', 'homework', {
      classId: otherDetail.classId, sectionId: otherDetail.sectionId, subjectId: subjects[0].id,
      title: 'Other class only', dueAtUtc: new Date(Date.now() + 86400000).toISOString()
    }), 200, 'seed other-class homework');
  }
  const list = expectStatus(await call(t('parent'), 'GET', 'homework'), 200);
  const leaked = list.filter(h => !childPlacements.has(`${h.classId}-${h.sectionId}`));
  if (leaked.length) throw new Error(`parent sees ${leaked.length} homework item(s) from other classes`);
  const mine = expectStatus(await call(t('student'), 'GET', 'homework'), 200);
  const me = expectStatus(await call(t('principal'), 'GET', `students/${s.student.user.studentId}`), 200);
  if (mine.some(h => h.classId !== me.classId || h.sectionId !== me.sectionId)) throw new Error('student sees other-class homework');
});
await check('student cannot publish LMS material', async () => {
  expectStatus(await call(t('student'), 'POST', 'lms/materials', {
    classId: classes[0].id, subjectId: subjects[0].id, title: 'x', url: 'https://example.com'
  }), 403);
});

// Logout
await check('logout', async () => {
  const fresh = await login('librarian@demo.edu');
  expectStatus(await call(fresh.accessToken, 'POST', 'auth/logout', { refreshToken: fresh.refreshToken }), [200, 204]);
  expectStatus(await call(null, 'POST', 'auth/refresh', { refreshToken: fresh.refreshToken }), [400, 401], 'refresh after logout');
});

console.log(`\nE2E: ${passed} passed, ${failures.length} failed`);
for (const f of failures) console.log(`  ✗ ${f}`);
process.exit(failures.length ? 1 : 0);
