/** Role code used by the backend for parent accounts. */
export const PARENT_ROLE = 'Parent';

export function isParentRole(roleCode?: string | null): boolean {
  return (roleCode ?? '').toLowerCase() === PARENT_ROLE.toLowerCase();
}
