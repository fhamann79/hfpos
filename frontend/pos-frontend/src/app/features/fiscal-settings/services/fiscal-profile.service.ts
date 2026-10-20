import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { DeclareFiscalProfile, DeclareProductFiscalClassification, FiscalProfile, ProductFiscalClassification, ProductFiscalState } from '../models/fiscal-profile.model';

@Injectable({ providedIn: 'root' })
export class FiscalProfileService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/api/FiscalSettings/profiles`;
  history() { return this.http.get<FiscalProfile[]>(this.url); }
  declare(value: DeclareFiscalProfile) { return this.http.post<FiscalProfile>(this.url, value); }
  product(id: number) { return this.http.get<ProductFiscalState>(`${this.url}/products/${id}`); }
  declareProduct(id: number, value: DeclareProductFiscalClassification) {
    return this.http.post<ProductFiscalClassification>(`${this.url}/products/${id}`, value);
  }
}
