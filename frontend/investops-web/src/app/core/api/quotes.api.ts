import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Quote } from '../models';

@Injectable({ providedIn: 'root' })
export class QuotesApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/quotes`;

  getQuote(productId: string): Observable<Quote> {
    return this.http.get<Quote>(`${this.baseUrl}/${productId}`);
  }

  // Stream SSE de cotações via EventSource (GET). Cada `data:` é uma Quote.
  stream(productIds: string[]): Observable<Quote> {
    return new Observable<Quote>((subscriber) => {
      const url = `${this.baseUrl}/stream?productIds=${encodeURIComponent(productIds.join(','))}`;
      const source = new EventSource(url);

      source.onmessage = (event) => {
        try {
          subscriber.next(JSON.parse(event.data) as Quote);
        } catch {
          // ignora frames malformados
        }
      };

      // Frame de erro de aplicação (event: error com data) vs. erro de transporte (sem data; o EventSource reconecta sozinho).
      source.addEventListener('error', (event: MessageEvent) => {
        if (event.data) {
          source.close();
          subscriber.error(new Error('Falha no stream de cotações.'));
        }
      });

      return () => source.close();
    });
  }
}
