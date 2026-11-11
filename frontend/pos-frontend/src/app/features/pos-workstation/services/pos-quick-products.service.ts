import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';

export interface PosFavorites {
  revision: number;
  productIds: number[];
}

@Injectable({ providedIn: 'root' })
export class PosQuickProductsService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/api/PosQuickProducts`;

  getFavorites() {
    return this.http.get<PosFavorites>(`${this.url}/favorites`);
  }

  saveFavorites(favorites: PosFavorites) {
    return this.http.put<PosFavorites>(`${this.url}/favorites`, favorites);
  }

  getTopSellers() {
    return this.http.get<number[]>(`${this.url}/top-sellers`);
  }
}
