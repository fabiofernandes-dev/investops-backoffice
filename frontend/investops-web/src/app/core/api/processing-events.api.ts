import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Paged, ProcessingEventItem, ProcessingEventsQuery } from '../models';
import { toHttpParams } from './http-params.util';

@Injectable({ providedIn: 'root' })
export class ProcessingEventsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/processing-events`;

  list(query: ProcessingEventsQuery): Observable<Paged<ProcessingEventItem>> {
    return this.http.get<Paged<ProcessingEventItem>>(this.baseUrl, { params: toHttpParams({ ...query }) });
  }
}
