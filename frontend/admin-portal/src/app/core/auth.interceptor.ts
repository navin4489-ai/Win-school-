import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, shareReplay, switchMap, throwError } from 'rxjs';
import { AuthResponse } from './models';
import { AuthService } from './auth.service';

// One refresh at a time: when several requests hit 401 together they all wait for the
// same refresh instead of each spending (and invalidating) the single-use refresh token.
let refreshInFlight: Observable<AuthResponse> | null = null;

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  // Only our own API gets the bearer token, never third-party URLs.
  const isApi = request.url.startsWith('/api/') || request.url.startsWith('api/');
  const token = auth.accessToken();
  const authorized = token && isApi
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      const isAuthCall = request.url.includes('/auth/login') || request.url.includes('/auth/refresh');
      const unauthenticated = error instanceof HttpErrorResponse && error.status === 401;

      if (!isApi || !unauthenticated || isAuthCall) {
        return throwError(() => error);
      }

      if (!auth.getRefreshToken()) {
        signOut(auth, router);
        return throwError(() => error);
      }

      refreshInFlight ??= auth.refresh().pipe(
        finalize(() => (refreshInFlight = null)),
        shareReplay(1)
      );

      return refreshInFlight.pipe(
        catchError(refreshError => {
          signOut(auth, router);
          return throwError(() => refreshError);
        }),
        switchMap(refreshed =>
          next(request.clone({ setHeaders: { Authorization: `Bearer ${refreshed.accessToken}` } })).pipe(
            catchError((retryError: unknown) => {
              // Still rejected with a fresh token: the session is no longer valid.
              if (retryError instanceof HttpErrorResponse && retryError.status === 401) signOut(auth, router);
              return throwError(() => retryError);
            })
          )
        )
      );
    })
  );
};

function signOut(auth: AuthService, router: Router): void {
  auth.clearSession();
  if (!router.url.startsWith('/login')) void router.navigate(['/login'], { queryParams: { expired: 1 } });
}
