import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AccountLookup, CustomerLookup, ProductLookup } from '../models';

@Injectable({ providedIn: 'root' })
export class LookupsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl;

  products(): Observable<ProductLookup[]> {
    return this.http.get<ProductLookup[]>(`${this.baseUrl}/products`);
  }

  customers(): Observable<CustomerLookup[]> {
    return this.http.get<CustomerLookup[]>(`${this.baseUrl}/customers`);
  }

  accounts(): Observable<AccountLookup[]> {
    return this.http.get<AccountLookup[]>(`${this.baseUrl}/accounts`);
  }
}
