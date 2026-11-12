import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.accessToken();
  const authorized = token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      const isAuthCall = request.url.includes('/auth/login') || request.url.includes('/auth/refresh');
      const unauthenticated = error instanceof HttpErrorResponse && error.status === 401;

      if (!unauthenticated || isAuthCall || !auth.getRefreshToken()) {
        return throwError(() => error);
      }

      return auth.refresh().pipe(
        switchMap(refreshed => {
          const retry = request.clone({
            setHeaders: { Authorization: `Bearer ${refreshed.accessToken}` }
          });
          return next(retry);
        }),
        catchError(refreshError => {
          auth.clearSession();
          void router.navigate(['/login']);
          return throwError(() => refreshError);
        })
      );
    })
  );
};
