import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Paged, Position, PositionsQuery } from '../models';
import { toHttpParams } from './http-params.util';

@Injectable({ providedIn: 'root' })
export class PositionsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/positions`;

  list(query: PositionsQuery): Observable<Paged<Position>> {
    return this.http.get<Paged<Position>>(this.baseUrl, { params: toHttpParams({ ...query }) });
  }

  byAccount(accountId: string): Observable<Position[]> {
    return this.http.get<Position[]>(`${this.baseUrl}/by-account/${accountId}`);
  }

  byCustomer(customerId: string): Observable<Position[]> {
    return this.http.get<Position[]>(`${this.baseUrl}/by-customer/${customerId}`);
  }
}
