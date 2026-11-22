/**
 * Kept local (rather than imported from notification-stream-url) so this tiny helper is
 * directly runnable by the Node unit tests without extension-resolution tricks.
 * Keep in sync with the production constants in notification-stream-url.ts.
 */
const PRODUCTION_PORTAL_HOSTNAME = 'school.wintechinov.com';
const PRODUCTION_API_ORIGIN = 'https://school-erp-api-3j9e.onrender.com';

/** Public APK download endpoint served by the API. */
export const APP_DOWNLOAD_PATH = '/api/app/download';

/**
 * The login page on the production portal is served from a different origin than the API,
 * so it needs the absolute API URL. Local and staging hosts use the same-origin path,
 * which works through the dev proxy and the staging rewrite.
 */
export function appDownloadUrl(hostname: string): string {
  return hostname.trim().toLowerCase() === PRODUCTION_PORTAL_HOSTNAME
    ? `${PRODUCTION_API_ORIGIN}${APP_DOWNLOAD_PATH}`
    : APP_DOWNLOAD_PATH;
}
