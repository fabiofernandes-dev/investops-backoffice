import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotificationService } from '../state/notification.service';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const notifications = inject(NotificationService);
  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      const message =
        error.status === 0
          ? 'Falha de conexão com a API.'
          : `Erro ${error.status}: ${error.error?.title ?? error.message}`;
      notifications.error(message);
      return throwError(() => error);
    }),
  );
};
