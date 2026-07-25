import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Divergence, DivergencesQuery, ImportReconciliationResult, Paged } from '../models';
import { toHttpParams } from './http-params.util';

@Injectable({ providedIn: 'root' })
export class ReconciliationApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/reconciliations`;

  import(source: string, referenceDate: string, file: File): Observable<ImportReconciliationResult> {
    const form = new FormData();
    form.append('source', source);
    form.append('referenceDate', referenceDate);
    form.append('file', file);
    return this.http.post<ImportReconciliationResult>(`${this.baseUrl}/import`, form);
  }

  listDivergences(query: DivergencesQuery): Observable<Paged<Divergence>> {
    return this.http.get<Paged<Divergence>>(`${this.baseUrl}/divergences`, { params: toHttpParams({ ...query }) });
  }

  resolve(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/divergences/${id}/resolve`, {});
  }

  ignore(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/divergences/${id}/ignore`, {});
  }
}
