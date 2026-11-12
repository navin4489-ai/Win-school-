import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';

type QueryValue = string | number | boolean | null | undefined;

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  get<T>(path: string, query?: Record<string, QueryValue>): Observable<T> {
    return this.http.get<T>(`/api/${path}`, { params: toParams(query) });
  }

  post<T>(path: string, body: unknown = {}): Observable<T> {
    return this.http.post<T>(`/api/${path}`, body);
  }

  put<T>(path: string, body: unknown = {}): Observable<T> {
    return this.http.put<T>(`/api/${path}`, body);
  }

  delete<T>(path: string): Observable<T> {
    return this.http.delete<T>(`/api/${path}`);
  }

  upload<T>(path: string, formData: FormData): Observable<T> {
    return this.http.post<T>(`/api/${path}`, formData);
  }

  download(path: string, query?: Record<string, QueryValue>): Observable<HttpResponse<Blob>> {
    return this.http.get(`/api/${path}`, {
      params: toParams(query),
      observe: 'response',
      responseType: 'blob'
    });
  }

  downloadPost(path: string): Observable<HttpResponse<Blob>> {
    return this.http.post(`/api/${path}`, {}, {
      observe: 'response',
      responseType: 'blob'
    });
  }

  saveBlob(response: HttpResponse<Blob>, fallbackName: string): void {
    const blob = response.body;
    if (!blob) return;

    const disposition = response.headers.get('content-disposition') ?? '';
    const match = /filename\*?=(?:UTF-8'')?["']?([^;"']+)/i.exec(disposition);
    const fileName = match?.[1] ? decodeURIComponent(match[1]) : fallbackName;

    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}

function toParams(query?: Record<string, QueryValue>): HttpParams {
  let params = new HttpParams();
  if (!query) return params;

  for (const [key, value] of Object.entries(query)) {
    if (value === null || value === undefined || value === '') continue;
    params = params.set(key, String(value));
  }
  return params;
}
