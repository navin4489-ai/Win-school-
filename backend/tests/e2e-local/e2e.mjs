#!/usr/bin/env node
/**
 * Local end-to-end QA sweep for the SchoolErp API (13 areas).
 *
 * NEVER point this at a production host. It only talks to a locally running
 * instance that uses an isolated in-memory database (data is wiped on restart):
 *
 *   $env:ASPNETCORE_ENVIRONMENT='Development'
 *   $env:Database__Provider='InMemory'
 *   $env:ASPNETCORE_URLS='http://127.0.0.1:5088'
 *   $env:Bootstrap__AdminEmail='e2e-admin@local.test'
 *   $env:Bootstrap__AdminPassword='E2ePassword12345!'
 *   $env:Bootstrap__SchoolCode='E2E'
 *   $env:Bootstrap__SchoolName='E2E Test School'
 *   dotnet run --project backend/src/SchoolErp.Api --no-build --no-launch-profile
 *
 *   node backend/tests/e2e-local/e2e.mjs
 *
 * Exit code 0 = every check passed; 1 = at least one check failed.
 */

import crypto from 'node:crypto';

const BASE = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5088';

const SUPER_EMAIL = 'e2e-admin@local.test';
const SUPER_PASSWORD = 'E2ePassword12345!';
const SUPER_SCHOOL = 'E2E';

const ALL_MODULES = ['Admissions', 'Fees', 'Attendance', 'Exams', 'Homework', 'LMS',
  'HR', 'Transport', 'Library', 'Inventory', 'Reports', 'Communication'];

const results = [];
const log = (msg) => console.log(msg);

function check(area, name, pass, detail) {
  results.push({ area, name, pass: !!pass, detail });
  log(`  [${pass ? 'PASS' : 'FAIL'}] area ${area}: ${name}${detail ? ` — ${detail}` : ''}`);
}

function bodyBrief(res) {
  const t = res.text ?? '';
  return t.length > 300 ? t.slice(0, 300) + '…' : t;
}

async function call(method, path, { token, body, form, headers = {} } = {}) {
  const h = { ...headers };
  if (token) h.Authorization = `Bearer ${token}`;
  let payload;
  if (form) {
    payload = form;
  } else if (body !== undefined) {
    h['Content-Type'] = 'application/json';
    payload = JSON.stringify(body);
  }
  const res = await fetch(BASE + path, { method, headers: h, body: payload });
  const buf = Buffer.from(await res.arrayBuffer());
  let json = null;
  try { json = JSON.parse(buf.toString('utf8')); } catch { /* not json */ }
  return { status: res.status, headers: res.headers, buf, json, text: buf.toString('utf8') };
}

function must(label, res, expected = 200) {
  const ok = Array.isArray(expected) ? expected.includes(res.status) : res.status === expected;
  if (!ok) {
    log(`SETUP FAILURE: ${label}: expected HTTP ${expected}, got ${res.status}`);
    log(`  ${bodyBrief(res)}`);
    process.exit(1);
  }
  return res;
}

async function waitForServer() {
  for (let i = 0; i < 60; i++) {
    try {
      const r = await fetch(`${BASE}/health/ready`);
      if (r.ok) return true;
    } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error(`Server not reachable at ${BASE}`);
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function b64url(input) {
  return Buffer.from(input).toString('base64url');
}

function craftExpiredToken() {
  // Signed with the Development signing key so only the `exp` claim makes it invalid.
  const header = b64url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const now = Math.floor(Date.now() / 1000);
  const payload = b64url(JSON.stringify({
    iss: 'SchoolErp', aud: 'SchoolErp.Clients', sub: '1', token_version: '1',
    iat: now - 7200, nbf: now - 7200, exp: now - 3600,
  }));
  const key = process.env.E2E_JWT_KEY ?? 'dev-only-change-me-please-32-chars-minimum-key';
  const sig = crypto.createHmac('sha256', key).update(`${header}.${payload}`).digest('base64url');
  return `${header}.${payload}.${sig}`;
}

// ---------------------------------------------------------------------------

async function main() {
  log(`== SchoolErp local E2E sweep against ${BASE} ==`);
  await waitForServer();
  log('server ready.');

  // ---------------------------------------------------- 1. bootstrap login
  const superLogin = await call('POST', '/api/auth/login', {
    body: { email: SUPER_EMAIL, password: SUPER_PASSWORD, schoolCode: SUPER_SCHOOL },
  });
  check(1, 'super admin logs in with bootstrap credentials',
    superLogin.status === 200 && !!superLogin.json?.accessToken,
    `status=${superLogin.status} role=${superLogin.json?.user?.roleCode ?? bodyBrief(superLogin)}`);
  if (superLogin.status !== 200) process.exit(1);
  const superToken = superLogin.json.accessToken;

  // -------------------------------------------- 2. multi-tenant school setup
  const mkSchool = async (code, name, adminEmail, adminPassword) => {
    const r = await call('POST', '/api/platform/schools', {
      token: superToken,
      body: {
        code, name,
        adminFullName: `${name} Owner`, adminEmail, adminPassword,
        email: `${code.toLowerCase()}@school.local`, phone: '9999999999',
        city: 'Testville', state: 'TS', postalCode: '123456',
        enabledModules: ALL_MODULES,
      },
    });
    return must(`create school ${code}`, r);
  };
  const schoolA = (await mkSchool('ALPHA', 'Alpha', 'owner-alpha@local.test', 'AlphaPass12345!')).json;
  const schoolB = (await mkSchool('BETA', 'Beta', 'owner-beta@local.test', 'BetaPass12345!')).json;
  check(2, 'platform creates schools Alpha and Beta with owner accounts',
    schoolA.code === 'ALPHA' && schoolB.code === 'BETA',
    `A=${JSON.stringify({ id: schoolA.id, code: schoolA.code, name: schoolA.name })} ` +
    `B=${JSON.stringify({ id: schoolB.id, code: schoolB.code, name: schoolB.name })}`);

  const login = (email, password, schoolCode) =>
    call('POST', '/api/auth/login', { body: { email, password, schoolCode } });
  const ownerA = must('owner A login', await login('owner-alpha@local.test', 'AlphaPass12345!', 'ALPHA'));
  const ownerB = must('owner B login', await login('owner-beta@local.test', 'BetaPass12345!', 'BETA'));
  const tA = ownerA.json.accessToken;
  const tB = ownerB.json.accessToken;
  check(2, 'both school owners can sign in',
    ownerA.json.user?.schoolId === schoolA.id && ownerB.json.user?.schoolId === schoolB.id,
    `ownerA schoolId=${ownerA.json.user?.schoolId}, ownerB schoolId=${ownerB.json.user?.schoolId}`);

  const setupSchool = async (token, prefix) => {
    const cls = must(`class ${prefix}`,
      await call('POST', '/api/admin/classes', { token, body: { name: `Class 6 ${prefix}`, code: `C6${prefix}`, displayOrder: 1 } }), [200, 201]);
    const sec = must(`section ${prefix}`,
      await call('POST', `/api/admin/classes/${cls.json.id}/sections`, { token, body: { name: 'A', capacity: 40 } }), [200, 201]);
    const sessions = must(`sessions ${prefix}`, await call('GET', '/api/admin/sessions', { token }));
    const session = sessions.json.find((s) => s.isCurrent) ?? sessions.json[0];
    return { classId: cls.json.id, sectionId: sec.json.id, sessionId: session.id };
  };
  const setupA = await setupSchool(tA, 'A');
  const setupB = await setupSchool(tB, 'B');

  const enroll = async (token, s, opts) => {
    const create = await call('POST', '/api/admissions', {
      token,
      body: {
        firstName: opts.first, lastName: opts.last, dateOfBirth: '2012-05-14', gender: 'Male',
        guardianName: opts.guardian, guardianPhone: opts.phone, guardianEmail: opts.guardianEmail,
        classId: s.classId, sessionId: s.sessionId, source: 'E2E',
      },
    });
    must(`admission enquiry ${opts.first}`, create, [200, 201]);
    must(`approve ${opts.first}`, await call('POST', `/api/admissions/${create.json.id}/approve`, { token }), [200, 201]);
    const enrolled = await call('POST', `/api/admissions/${create.json.id}/enroll`, {
      token,
      body: {
        sectionId: s.sectionId, admissionNo: opts.admissionNo, rollNo: opts.rollNo,
        createParentLogin: true, parentPassword: opts.parentPassword,
      },
    });
    return must(`enroll ${opts.first}`, enrolled, [200, 201]).json;
  };

  const a1 = await enroll(tA, setupA, { first: 'Ajay', last: 'Kumar', guardian: 'Ramesh Kumar', phone: '9000000001', guardianEmail: null, admissionNo: 'A1001', rollNo: '1', parentPassword: 'ParentPass12345!' });
  const a2 = await enroll(tA, setupA, { first: 'Ravi', last: 'Sharma', guardian: 'Suresh Sharma', phone: '9000000002', guardianEmail: 'parent2-alpha@local.test', admissionNo: 'A1002', rollNo: '2', parentPassword: 'ParentPass12345!' });
  const b1 = await enroll(tB, setupB, { first: 'Ajay', last: 'Verma', guardian: 'Kavita Verma', phone: '9100000001', guardianEmail: null, admissionNo: 'B2001', rollNo: '1', parentPassword: 'ParentPass12345!' });
  const b2 = await enroll(tB, setupB, { first: 'Vikram', last: 'Mehta', guardian: 'Anil Mehta', phone: '9100000002', guardianEmail: null, admissionNo: 'B2002', rollNo: '2', parentPassword: 'ParentPass12345!' });
  check(2, '2 students enrolled in each school (Ajay Kumar exists in Alpha only)',
    a1.admissionNo === 'A1001' && a2.admissionNo === 'A1002' && b1.admissionNo === 'B2001' && b2.admissionNo === 'B2002',
    `A=[${a1.studentId} ${a1.studentName}], [${a2.studentId} ${a2.studentName}]; ` +
    `B=[${b1.studentId} ${b1.studentName}], [${b2.studentId} ${b2.studentName}]`);

  // ------------------------------------------------------------- 3. search
  const search = async (token, q) => {
    const r = await call('GET', `/api/students?search=${encodeURIComponent(q)}`, { token });
    must(`students search "${q}"`, r);
    return r.json;
  };
  const sLower = await search(tA, 'ajay kumar');
  const sUpper = await search(tA, 'AJAY KUMAR');
  const sRev = await search(tA, 'kumar ajay');
  const sB = await search(tB, 'ajay kumar');
  check(3, 'search "ajay kumar" (lower case) finds the student as school A owner',
    sLower.totalCount === 1 && sLower.items[0]?.fullName === 'Ajay Kumar',
    `totalCount=${sLower.totalCount} first=${sLower.items[0]?.fullName}`);
  check(3, 'search is case-insensitive ("AJAY KUMAR")',
    sUpper.totalCount === 1 && sUpper.items[0]?.fullName === 'Ajay Kumar',
    `totalCount=${sUpper.totalCount}`);
  check(3, 'reversed word order ("kumar ajay") still finds the student',
    sRev.totalCount === 1 && sRev.items[0]?.fullName === 'Ajay Kumar',
    `totalCount=${sRev.totalCount}`);
  check(3, 'same search as school B owner returns 0 rows (tenant isolation)',
    sB.totalCount === 0, `totalCount=${sB.totalCount}`);

  // ----------------------------------------------- 4. cross-tenant isolation
  // (a) student by id
  const crossStudent = await call('GET', `/api/students/${a1.studentId}`, { token: tB });
  check(4, 'school B cannot read school A student by id',
    crossStudent.status === 404 || crossStudent.status === 403,
    `status=${crossStudent.status} body=${bodyBrief(crossStudent)}`);

  // (b) fee ledger by id
  const crossLedger = await call('GET', `/api/fees/students/${a1.studentId}/ledger`, { token: tB });
  check(4, 'school B cannot read school A fee ledger by student id',
    crossLedger.status === 404 || crossLedger.status === 403,
    `status=${crossLedger.status} body=${bodyBrief(crossLedger)}`);

  // (c) attendance: first mark real attendance in school A as a positive control
  const today = new Date().toISOString().slice(0, 10);
  const marked = must('mark attendance in A', await call('POST', '/api/attendance/mark', {
    token: tA,
    body: {
      classId: setupA.classId, sectionId: setupA.sectionId, date: today,
      entries: [{ studentId: a1.studentId, status: 'Present' }, { studentId: a2.studentId, status: 'Absent' }],
    },
  }));
  const attFrom = '2026-01-01', attTo = '2026-12-31';
  const attA = must('attendance read A', await call('GET',
    `/api/attendance/students/${a1.studentId}?from=${attFrom}&to=${attTo}`, { token: tA }));
  const crossAtt = await call('GET',
    `/api/attendance/students/${a1.studentId}?from=${attFrom}&to=${attTo}`, { token: tB });
  check(4, 'school A sees its own attendance (positive control)',
    attA.status === 200 && attA.json.totalDays >= 1,
    `status=${attA.status} totalDays=${attA.json.totalDays} records=${attA.json.records?.length}`);
  check(4, 'school B token gets no school A attendance data',
    crossAtt.status === 200
      ? crossAtt.json.totalDays === 0 && (crossAtt.json.records?.length ?? 0) === 0
      : (crossAtt.status === 403 || crossAtt.status === 404),
    `status=${crossAtt.status} totalDays=${crossAtt.json?.totalDays} records=${JSON.stringify(crossAtt.json?.records)}`);

  // (d) files: upload as school A, then try to read as school B and anonymously
  const png = Buffer.from(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
    'base64');
  const probeForm = new FormData();
  probeForm.append('file', new Blob([png], { type: 'image/png' }), 'probe.png');
  const probeUpload = must('probe upload', await call('POST', '/api/files/upload', { token: tA, form: probeForm }));
  const probeUrl = probeUpload.json.url;
  const fileAsB = await call('GET', probeUrl, { token: tB });
  const fileAnon = await call('GET', probeUrl);
  check(4, 'school B token cannot read school A file by id',
    fileAsB.status === 403 || fileAsB.status === 404,
    `GET ${probeUrl} as school B → status=${fileAsB.status} bytes=${fileAsB.buf.length}`);
  check(4, 'anonymous request cannot read school A file by id',
    fileAnon.status === 403 || fileAnon.status === 404,
    `GET ${probeUrl} anonymously → status=${fileAnon.status} bytes=${fileAnon.buf.length}`);

  // ------------------------------------------------- 5. RBAC (teacher/parent)
  const roles = must('roles', await call('GET', '/api/users/roles', { token: tA })).json;
  const teacherRole = roles.find((r) => r.code === 'Teacher');
  const adminRole = roles.find((r) => r.code === 'Administrator');
  const teacherUser = must('create teacher', await call('POST', '/api/users', {
    token: tA,
    body: { fullName: 'Tina Teacher', email: 'teacher-alpha@local.test', roleId: teacherRole.id, password: 'TeacherPass12345!' },
  }), [200, 201]);
  const teacherLogin = must('teacher login', await login('teacher-alpha@local.test', 'TeacherPass12345!', 'ALPHA'));
  const tT = teacherLogin.json.accessToken;

  const tUsers = await call('GET', '/api/users', { token: tT });
  check(5, 'Teacher gets 403 on /api/users',
    tUsers.status === 403, `status=${tUsers.status} body=${bodyBrief(tUsers)}`);
  const tPlatform = await call('GET', '/api/platform/schools', { token: tT });
  check(5, 'Teacher gets 403 on /api/platform/schools',
    tPlatform.status === 403, `status=${tPlatform.status} body=${bodyBrief(tPlatform)}`);
  const tModules = await call('GET', '/api/platform/modules', { token: tT });
  check(5, 'Teacher gets 403 on /api/platform/modules',
    tModules.status === 403, `status=${tModules.status} body=${bodyBrief(tModules)}`);
  const superModules = await call('GET', '/api/platform/modules', { token: superToken });
  check(5, 'super admin still gets 200 on /api/platform/modules (positive control)',
    superModules.status === 200 && Array.isArray(superModules.json) && superModules.json.length > 0,
    `status=${superModules.status} modules=${Array.isArray(superModules.json) ? superModules.json.length : bodyBrief(superModules)}`);
  const tFeeWrite = await call('POST', '/api/fees/categories', {
    token: tT, body: { name: 'Nope', code: 'NOPE' },
  });
  check(5, 'Teacher gets 403 on fees setup write op (POST /api/fees/categories)',
    tFeeWrite.status === 403, `status=${tFeeWrite.status} body=${bodyBrief(tFeeWrite)}`);

  // Parent sign-in uses the admission number in the email field (area 6 login).
  const parentLogin = await login('A1001', 'ParentPass12345!', 'ALPHA');
  const tP = parentLogin.json?.accessToken;
  const parentLoginOk = parentLogin.status === 200 && !!tP;
  const parentUsers = await call('GET', '/api/users', { token: tP });
  check(5, 'Parent gets 403 on /api/users',
    parentLoginOk && parentUsers.status === 403,
    `loginStatus=${parentLogin.status} usersStatus=${parentUsers.status}`);
  const parentStudents = await call('GET', '/api/students', { token: tP });
  check(5, 'Parent gets 403 on the full student directory',
    parentStudents.status === 403, `status=${parentStudents.status}`);
  const parentChildren = await call('GET', '/api/parent/children', { token: tP });
  const kids = parentChildren.json ?? [];
  check(5, 'Parent sees only its own child (school A has 2 students)',
    parentChildren.status === 200 && kids.length === 1 && JSON.stringify(kids).includes('A1001'),
    `status=${parentChildren.status} children=${kids.length} payload=${JSON.stringify(kids).slice(0, 200)}`);
  const parentOtherLedger = await call('GET', `/api/fees/students/${a2.studentId}/ledger`, { token: tP });
  check(5, 'Parent gets 404 on another family\'s fee ledger',
    parentOtherLedger.status === 404 || parentOtherLedger.status === 403,
    `status=${parentOtherLedger.status} body=${bodyBrief(parentOtherLedger)}`);
  const parentOtherAtt = await call('GET',
    `/api/attendance/students/${a2.studentId}?from=${attFrom}&to=${attTo}`, { token: tP });
  check(5, 'Parent gets 404 on another child\'s attendance',
    parentOtherAtt.status === 404 || parentOtherAtt.status === 403,
    `status=${parentOtherAtt.status} body=${bodyBrief(parentOtherAtt)}`);

  // ------------------------------------------------------- 6. parent login
  check(6, 'parent signs in with the admission number in the email field',
    parentLoginOk,
    `POST /api/auth/login email=A1001 → status=${parentLogin.status} role=${parentLogin.json?.user?.roleCode}`);
  const parentOwnLedger = await call('GET', `/api/fees/students/${a1.studentId}/ledger`, { token: tP });
  check(6, 'parent can read its own child\'s fee ledger',
    parentOwnLedger.status === 200 && parentOwnLedger.json?.studentId === a1.studentId,
    `status=${parentOwnLedger.status} studentId=${parentOwnLedger.json?.studentId} balance=${parentOwnLedger.json?.balance}`);

  // ------------------------------------------------------------- 7. fees
  const feeCat = must('fee category', await call('POST', '/api/fees/categories', {
    token: tA, body: { name: 'Tuition Fee', code: 'TUITION', isRecurring: true },
  }), [200, 201]);
  const feeStruct = must('fee structure', await call('POST', '/api/fees/structures', {
    token: tA,
    body: {
      academicSessionId: setupA.sessionId, classId: setupA.classId, name: 'Class 6 Plan',
      items: [{ feeCategoryId: feeCat.json.id, amount: 5000, frequency: 'Annual', dueDay: 10 }],
    },
  }), [200, 201]);
  const assign = must('assign fee plan', await call('POST', '/api/fees/assignments', {
    token: tA, body: { studentId: a1.studentId, feeStructureId: feeStruct.json.id },
  }), [200, 201]);
  const installments = assign.json.installments ?? [];
  check(7, 'fee plan assigned to the class generates installments',
    installments.length >= 1 && installments[0]?.amount > 0,
    `installments=${installments.length} first=${JSON.stringify(installments[0])}`);

  const payConfig = must('payment-config', await call('GET', '/api/fees/payment-config', { token: tA }));
  check(7, 'payment-config reports onlineEnabled:false when no payment provider is configured',
    payConfig.json.onlineEnabled === false,
    `body=${JSON.stringify(payConfig.json)}`);

  const orderZero = await call('POST', '/api/fees/payments/order', {
    token: tA, body: { studentId: a1.studentId, amount: 0 },
  });
  check(7, 'POST /api/fees/payments/order rejects amount = 0 with 400 + clear message',
    orderZero.status === 400 && /amount/i.test(orderZero.text),
    `status=${orderZero.status} body=${bodyBrief(orderZero)}`);
  const orderNeg = await call('POST', '/api/fees/payments/order', {
    token: tA, body: { studentId: a1.studentId, amount: -250 },
  });
  check(7, 'POST /api/fees/payments/order rejects negative amount with 400',
    orderNeg.status === 400 && /amount/i.test(orderNeg.text),
    `status=${orderNeg.status} body=${bodyBrief(orderNeg)}`);
  const orderTooBig = await call('POST', '/api/fees/payments/order', {
    token: tA, body: { studentId: a1.studentId, amount: 999999, feeInstallmentId: installments[0].id },
  });
  check(7, 'POST /api/fees/payments/order rejects amount > installment balance with 400 + clear message',
    orderTooBig.status === 400 && /exceeds the installment balance/i.test(orderTooBig.text),
    `status=${orderTooBig.status} body=${bodyBrief(orderTooBig)}`);

  // --------------------------------------------------- 8. receipt document
  const payment = must('record manual cash payment', await call('POST', '/api/fees/payments', {
    token: tA,
    body: { studentId: a1.studentId, feeInstallmentId: installments[0].id, amount: 1000, mode: 'Cash', referenceNo: 'E2E-CASH' },
  }), [200, 201]);
  const receipt = await call('GET', `/api/fees/payments/${payment.json.id}/receipt`, { token: tA });
  const isPdf = (receipt.headers.get('content-type') ?? '').startsWith('application/pdf');
  const pdfMagic = receipt.buf.subarray(0, 4).toString('latin1') === '%PDF';
  check(8, 'receipt is application/pdf, non-trivial size and a real PDF (not HTML)',
    receipt.status === 200 && isPdf && pdfMagic && receipt.buf.length > 1500,
    `status=${receipt.status} content-type=${receipt.headers.get('content-type')} ` +
    `bytes=${receipt.buf.length} magic=${JSON.stringify(receipt.buf.subarray(0, 8).toString('latin1'))}`);
  const statement = await call('GET', `/api/fees/students/${a1.studentId}/statement`, { token: tA });
  check(8, 'fee statement document is also a real PDF',
    statement.status === 200 && (statement.headers.get('content-type') ?? '').startsWith('application/pdf') &&
    statement.buf.subarray(0, 4).toString('latin1') === '%PDF',
    `status=${statement.status} content-type=${statement.headers.get('content-type')} bytes=${statement.buf.length}`);

  // cross-tenant receipt probe (recorded under area 4)
  const crossReceipt = await call('GET', `/api/fees/payments/${payment.json.id}/receipt`, { token: tB });
  check(4, 'school B cannot read school A payment receipt by id',
    crossReceipt.status === 404 || crossReceipt.status === 403,
    `status=${crossReceipt.status} body=${bodyBrief(crossReceipt)}`);

  // ------------------------------------------------------ 9. notifications
  const beforeCount = must('unread count before', await call('GET', '/api/notifications/unread-count', { token: tT }));
  const broadcast = must('broadcast', await call('POST', '/api/notifications/broadcast', {
    token: tA,
    body: { title: 'E2E Broadcast', message: 'Hello from the local QA sweep', audience: 'All', channel: 'InApp' },
  }));
  check(9, 'owner broadcast with audience "All" reaches recipients',
    broadcast.status === 200 && broadcast.json.recipients >= 2,
    `status=${broadcast.status} recipients=${broadcast.json.recipients}`);

  const notifList = must('notifications list', await call('GET', '/api/notifications', { token: tT }));
  const mine = (notifList.json ?? []).find((n) => n.title === 'E2E Broadcast');
  check(9, 'recipient sees the broadcast in GET /api/notifications',
    !!mine, `status=${notifList.status} found=${!!mine} total=${notifList.json?.length}`);

  const afterCount = must('unread count after', await call('GET', '/api/notifications/unread-count', { token: tT }));
  check(9, 'unread-count rose after the broadcast',
    afterCount.json >= beforeCount.json + 1 && afterCount.json >= 1,
    `before=${beforeCount.json} after=${afterCount.json}`);

  if (mine) {
    const markRead = await call('POST', `/api/notifications/${mine.id}/read`, { token: tT });
    const finalCount = must('unread count after read', await call('GET', '/api/notifications/unread-count', { token: tT }));
    check(9, 'marking the notification read drops unread-count by one',
      markRead.status === 204 && finalCount.json === afterCount.json - 1,
      `markRead status=${markRead.status} after=${afterCount.json} final=${finalCount.json}`);
  } else {
    check(9, 'marking the notification read drops unread-count by one', false, 'broadcast not found for recipient');
  }

  // SSE stream: the first frame must arrive well within 5 seconds.
  try {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 5000);
    const started = Date.now();
    const stream = await fetch(`${BASE}/api/notifications/stream`, {
      headers: { Authorization: `Bearer ${tT}` }, signal: controller.signal,
    });
    const reader = stream.body.getReader();
    const first = await reader.read();
    const elapsed = Date.now() - started;
    clearTimeout(timer);
    const chunk = Buffer.from(first.value ?? []).toString('utf8');
    controller.abort();
    check(9, 'GET /api/notifications/stream delivers a "retry:" frame within ~5s',
      (stream.headers.get('content-type') ?? '').startsWith('text/event-stream') &&
      /retry:\s*\d+/.test(chunk) && elapsed < 5000,
      `content-type=${stream.headers.get('content-type')} elapsedMs=${elapsed} firstFrame=${JSON.stringify(chunk.slice(0, 80))}`);
  } catch (err) {
    check(9, 'GET /api/notifications/stream delivers a "retry:" frame within ~5s', false, `error=${err}`);
  }

  // ------------------------------------------------------------ 10. upload
  const upForm = new FormData();
  upForm.append('file', new Blob([png], { type: 'image/png' }), 'e2e.png');
  const upload = must('upload', await call('POST', '/api/files/upload', { token: tA, form: upForm }));
  const download = await call('GET', upload.json.url, { token: tA });
  const bytesMatch = download.buf.length === png.length && download.buf.equals(png);
  check(10, 'file round trip: POST /api/files/upload then GET /files/db/{id} returns identical bytes',
    upload.status === 200 && download.status === 200 && bytesMatch &&
    (download.headers.get('content-type') ?? '').startsWith('image/png'),
    `upload status=${upload.status} url=${upload.json.url} download status=${download.status} ` +
    `contentType=${download.headers.get('content-type')} expectedBytes=${png.length} gotBytes=${download.buf.length} match=${bytesMatch}`);

  // -------------------------------------------- 12. auth hardening (before
  // the rate-limit probe in area 11, which must stay the very last test).
  must('create chpw user', await call('POST', '/api/users', {
    token: tA,
    body: { fullName: 'Chpw User', email: 'chpw-alpha@local.test', roleId: adminRole.id, password: 'ChpwPass12345!' },
  }), [200, 201]);
  const chpwLogin = must('chpw login', await login('chpw-alpha@local.test', 'ChpwPass12345!', 'ALPHA'));
  const tC = chpwLogin.json.accessToken;
  const changed = await call('POST', '/api/auth/change-password', {
    token: tC, body: { currentPassword: 'ChpwPass12345!', newPassword: 'ChpwPass67890!' },
  });
  check(12, 'password change with the current password succeeds',
    changed.status === 204, `status=${changed.status} body=${bodyBrief(changed)}`);
  const oldTokenAfter = await call('GET', '/api/auth/me', { token: tC });
  check(12, 'old access token is rejected after the password change (token_version revocation)',
    oldTokenAfter.status === 401, `GET /api/auth/me with pre-change token → status=${oldTokenAfter.status}`);
  const reloginNew = await login('chpw-alpha@local.test', 'ChpwPass67890!', 'ALPHA');
  check(12, 'sign-in works with the new password',
    reloginNew.status === 200 && !!reloginNew.json?.accessToken,
    `status=${reloginNew.status}`);
  const oldPasswordNow = await login('chpw-alpha@local.test', 'ChpwPass12345!', 'ALPHA');
  check(12, 'old password no longer works and returns a generic 401 "Invalid credentials"',
    oldPasswordNow.status === 401 && /Invalid credentials/i.test(oldPasswordNow.json?.detail ?? ''),
    `status=${oldPasswordNow.status} body=${bodyBrief(oldPasswordNow)}`);

  // -------------------------------------------------- 13. injection searches
  const injectionPayloads = ["' OR 1=1--", "' OR '1'='1", '"; DROP TABLE Student;--', '{"$ne":null}', '%25 OR 1=1--'];
  let injectionOk = true;
  const injectionDetails = [];
  for (const p of injectionPayloads) {
    const r = await call('GET', `/api/students?search=${encodeURIComponent(p)}`, { token: tA });
    injectionDetails.push(`students(${JSON.stringify(p)})→${r.status}/${r.json?.totalCount}`);
    if (r.status !== 200 || r.json?.totalCount !== 0) injectionOk = false;
  }
  check(13, 'injection-style student searches return 200 with 0 rows, never 500',
    injectionOk, injectionDetails.join('; '));
  const injUsers = await call('GET', `/api/users?search=${encodeURIComponent("' OR 1=1--")}`, { token: tA });
  check(13, 'injection-style user search returns an empty list, never 500 or the full roster',
    injUsers.status === 200 && (injUsers.json?.length ?? -1) === 0,
    `status=${injUsers.status} rows=${Array.isArray(injUsers.json) ? injUsers.json.length : bodyBrief(injUsers)}`);
  const injAdm = await call('GET', `/api/admissions?search=${encodeURIComponent("' OR 1=1--")}`, { token: tA });
  check(13, 'injection-style admission search returns 200 with 0 rows, never 500',
    injAdm.status === 200 && injAdm.json?.totalCount === 0,
    `status=${injAdm.status} totalCount=${injAdm.json?.totalCount}`);

  // -------------------------------- 11. security headers / 401s / hardening
  const probe = await call('GET', '/api/students', { token: tA });
  const h = probe.headers;
  check(11, 'X-Content-Type-Options: nosniff present',
    (h.get('x-content-type-options') ?? '').toLowerCase() === 'nosniff',
    `value=${h.get('x-content-type-options')}`);
  check(11, 'X-Frame-Options present',
    !!h.get('x-frame-options'), `value=${h.get('x-frame-options')}`);
  check(11, 'Content-Security-Policy present on API responses',
    !!h.get('content-security-policy'), `value=${h.get('content-security-policy') ?? '(missing)'}`);
  check(11, 'Referrer-Policy present',
    !!h.get('referrer-policy'), `value=${h.get('referrer-policy')}`);
  check(11, 'Cache-Control: no-store on /api responses',
    (h.get('cache-control') ?? '').includes('no-store'), `value=${h.get('cache-control')}`);
  const hsts = h.get('strict-transport-security');
  check(11, 'Strict-Transport-Security set outside Development (n/a in Development)',
    true, `value=${hsts ?? '(absent — expected in Development; verified separately in Production mode)'} (informational)`);

  const noToken = await call('GET', '/api/students');
  check(11, 'request without a token gets 401',
    noToken.status === 401, `status=${noToken.status}`);
  const garbage = await call('GET', '/api/auth/me', { token: 'garbage.token.value' });
  check(11, 'garbage token gets 401',
    garbage.status === 401, `status=${garbage.status} body=${bodyBrief(garbage)}`);
  const expired = await call('GET', '/api/auth/me', { token: craftExpiredToken() });
  check(11, 'expired token gets 401',
    expired.status === 401, `status=${expired.status} body=${bodyBrief(expired)}`);

  // Login rate limiting — deliberately the LAST test of the whole sweep.
  log('  … waiting 61s so the login rate-limit window resets before the brute-force probe …');
  await sleep(61_000);
  const rlStatuses = [];
  for (let i = 0; i < 12; i++) {
    const r = await call('POST', '/api/auth/login', {
      body: { email: 'nobody-ratelimit@local.test', password: 'WrongPass12345!' },
    });
    rlStatuses.push(r.status);
  }
  check(11, 'login rate limit returns 429 after repeated bad logins (non-existent email)',
    rlStatuses[0] === 401 && rlStatuses.includes(429) && rlStatuses[rlStatuses.length - 1] === 429,
    `statuses=${rlStatuses.join(',')}`);

  // ------------------------------------------------------------- summary
  const areas = [...new Set(results.map((r) => r.area))].sort((a, b) => a - b);
  log('\n================ E2E SUMMARY ================');
  for (const area of areas) {
    const items = results.filter((r) => r.area === area);
    const ok = items.every((r) => r.pass);
    log(`Area ${String(area).padStart(2)}: ${ok ? 'PASS' : 'FAIL'} (${items.filter((r) => r.pass).length}/${items.length})`);
    for (const r of items.filter((x) => !x.pass)) log(`    ✗ ${r.name} — ${r.detail}`);
  }
  const passed = results.filter((r) => r.pass).length;
  log(`---------------------------------------------`);
  log(`TOTAL: ${passed}/${results.length} checks passed, ${results.length - passed} failed.`);
  process.exit(results.length === passed ? 0 : 1);
}

main().catch((err) => {
  console.error('E2E script crashed:', err);
  process.exit(1);
});
