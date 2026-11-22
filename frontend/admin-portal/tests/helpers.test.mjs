import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  NOTIFICATION_STREAM_PATH,
  notificationStreamUrl
} from '../src/app/core/notification-stream-url.ts';
import { isParentRole } from '../src/app/core/roles.ts';
import { attendanceSaveMessage } from '../src/app/pages/attendance/attendance-messages.ts';
import { studentEditPayload } from '../src/app/pages/students/student-edit-payload.ts';

test('SSE stream uses the relative proxy path everywhere except the production portal', () => {
  assert.equal(
    notificationStreamUrl('school.wintechinov.com'),
    `https://school-erp-api-3j9e.onrender.com${NOTIFICATION_STREAM_PATH}`
  );
  assert.equal(notificationStreamUrl('SCHOOL.WINTECHINOV.COM'), `https://school-erp-api-3j9e.onrender.com${NOTIFICATION_STREAM_PATH}`);
  assert.equal(notificationStreamUrl('127.0.0.1'), NOTIFICATION_STREAM_PATH);
  assert.equal(notificationStreamUrl('localhost'), NOTIFICATION_STREAM_PATH);
  assert.equal(notificationStreamUrl('staging.example.test'), NOTIFICATION_STREAM_PATH);
});

test('parent role detection is case-insensitive and null-safe', () => {
  assert.equal(isParentRole('Parent'), true);
  assert.equal(isParentRole('parent'), true);
  assert.equal(isParentRole('Teacher'), false);
  assert.equal(isParentRole(null), false);
});

test('attendance save confirmation is formatted for the banner', () => {
  assert.equal(
    attendanceSaveMessage({ created: 2, updated: 3, notificationsQueued: 1 }),
    '2 records created, 3 updated, 1 parent notifications queued.'
  );
});

test('student edit payload trims values, nulls blanks and omits hidden fields', () => {
  const payload = studentEditPayload({
    firstName: '  Asha  ', middleName: '   ', lastName: 'Rao',
    dateOfBirth: '2016-05-01', gender: 'Female', bloodGroup: ' ',
    email: '', phone: ' 9999999999 ', address: '  ', city: 'Pune',
    state: '', postalCode: '', previousSchool: ' St Mary '
  });

  assert.deepEqual(payload, {
    firstName: 'Asha', middleName: null, lastName: 'Rao',
    dateOfBirth: '2016-05-01', gender: 'Female', bloodGroup: null,
    email: null, phone: '9999999999', address: null, city: 'Pune',
    state: null, postalCode: null, previousSchool: 'St Mary'
  });
  assert.equal('photoUrl' in payload, false);
  assert.equal('medicalNotes' in payload, false);
});
