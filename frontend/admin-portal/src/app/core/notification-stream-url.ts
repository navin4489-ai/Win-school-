/**
 * The stream talks to the API service directly: the static site's /api rewrite
 * buffers server-sent events, so a stream opened through it never delivers a
 * frame. Everything else keeps using the same-origin /api paths.
 *
 * Only the production portal needs the direct origin; local, staging and any
 * other host must use the relative path so they work through the dev proxy.
 */
export const PRODUCTION_PORTAL_HOSTNAME = 'school.wintechinov.com';
export const PRODUCTION_API_ORIGIN = 'https://school-erp-api-3j9e.onrender.com';
export const NOTIFICATION_STREAM_PATH = '/api/notifications/stream';

export function notificationStreamUrl(hostname: string): string {
  return hostname.trim().toLowerCase() === PRODUCTION_PORTAL_HOSTNAME
    ? `${PRODUCTION_API_ORIGIN}${NOTIFICATION_STREAM_PATH}`
    : NOTIFICATION_STREAM_PATH;
}
