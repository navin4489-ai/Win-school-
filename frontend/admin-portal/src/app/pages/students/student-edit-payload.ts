/**
 * Builds the PUT /api/students/{id} body from the edit form.
 *
 * Blank optional inputs are sent as null so the API can store a real "not
 * supplied" value. Fields that are not editable in the portal (photo and
 * medical notes) are deliberately omitted so they cannot be cleared by accident.
 */
export interface StudentEditFormValue {
  firstName: string;
  middleName?: string | null;
  lastName: string;
  dateOfBirth: string;
  gender: string;
  bloodGroup?: string | null;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  previousSchool?: string | null;
}

export interface StudentEditPayload {
  firstName: string;
  middleName: string | null;
  lastName: string;
  dateOfBirth: string;
  gender: string;
  bloodGroup: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  previousSchool: string | null;
}

function nullable(value?: string | null): string | null {
  const trimmed = (value ?? '').trim();
  return trimmed || null;
}

export function studentEditPayload(value: StudentEditFormValue): StudentEditPayload {
  return {
    firstName: value.firstName.trim(),
    middleName: nullable(value.middleName),
    lastName: value.lastName.trim(),
    dateOfBirth: value.dateOfBirth,
    gender: value.gender,
    bloodGroup: nullable(value.bloodGroup),
    email: nullable(value.email),
    phone: nullable(value.phone),
    address: nullable(value.address),
    city: nullable(value.city),
    state: nullable(value.state),
    postalCode: nullable(value.postalCode),
    previousSchool: nullable(value.previousSchool)
  };
}
