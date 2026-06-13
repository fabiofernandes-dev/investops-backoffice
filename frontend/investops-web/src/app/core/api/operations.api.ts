import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditLog, ImportOperationRequest, OperationDetail, OperationListItem, OperationsQuery, Paged, ProcessingEvent } from '../models';
import { toHttpParams } from './http-params.util';

export interface ImportOperationResult {
  id: string;
  created: boolean;
}

@Injectable({ providedIn: 'root' })
export class OperationsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/operations`;

  list(query: OperationsQuery): Observable<Paged<OperationListItem>> {
    return this.http.get<Paged<OperationListItem>>(this.baseUrl, { params: toHttpParams({ ...query }) });
  }

  getById(id: string): Observable<OperationDetail> {
    return this.http.get<OperationDetail>(`${this.baseUrl}/${id}`);
  }

  events(id: string): Observable<ProcessingEvent[]> {
    return this.http.get<ProcessingEvent[]>(`${this.baseUrl}/${id}/events`);
  }

  audit(id: string): Observable<AuditLog[]> {
    return this.http.get<AuditLog[]>(`${this.baseUrl}/${id}/audit`);
  }

  import(request: ImportOperationRequest): Observable<ImportOperationResult> {
    return this.http.post<ImportOperationResult>(`${this.baseUrl}/import`, request);
  }
}
