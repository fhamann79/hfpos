import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DialogModule } from 'primeng/dialog';
import { Observable, Subject, Subscription, catchError, map, of, switchMap, timer } from 'rxjs';
import { PosProduct } from '../../models/pos-product.model';
import { PosProductCatalogService } from '../../services/pos-product-catalog.service';
import { PosQuickProductsService } from '../../services/pos-quick-products.service';

type QuickTab = 'favorites' | 'top';

@Component({
  selector: 'app-pos-quick-products',
  standalone: true,
  imports: [CurrencyPipe, DialogModule],
  templateUrl: './pos-quick-products.html',
  styleUrl: './pos-quick-products.scss',
})
export class PosQuickProducts implements OnInit {
  private readonly catalog = inject(PosProductCatalogService);
  private readonly api = inject(PosQuickProductsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly editSearch = new Subject<string>();
  private loadVersion = 0;
  private draftRevision: number | null = null;
  private topIds: number[] | null = null;
  private activation?: Subscription;

  readonly blocked = input.required<boolean>();
  readonly searching = input.required<boolean>();
  readonly canManage = input.required<boolean>();
  readonly idle = input.required<boolean>();
  readonly selectProduct = output<{ product: PosProduct; trigger: HTMLButtonElement; keyboard: boolean }>();

  readonly limit = 12;
  readonly tab = signal<QuickTab>('favorites');
  readonly products = signal<PosProduct[]>([]);
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly activationError = signal('');
  readonly selecting = signal(false);
  readonly editorVisible = signal(false);
  readonly draftIds = signal<number[]>([]);
  readonly draftProducts = signal<PosProduct[]>([]);
  readonly editMatches = signal<PosProduct[]>([]);
  readonly editError = signal('');
  readonly editSearchError = signal('');
  readonly editSearching = signal(false);
  readonly editLoading = signal(false);
  readonly saving = signal(false);
  readonly stale = signal(false);

  constructor() {
    effect(() => {
      if (this.blocked() || this.searching()) this.cancelActivation();
    });
  }

  ngOnInit(): void {
    this.editSearch.pipe(
      switchMap(term => term
        ? timer(180).pipe(switchMap(() => this.catalog.searchProducts(term, 20)),
            map(products => ({ products, failed: false })),
            catchError(() => of({ products: [], failed: true })))
        : of({ products: [], failed: false })),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(({ products, failed }) => {
      this.editMatches.set(products);
      this.editSearching.set(false);
      this.editSearchError.set(failed ? 'No se pudo buscar en el catálogo.' : '');
    });
    this.loadTab('favorites');
  }

  loadTab(tab: QuickTab, refreshRanking = false): void {
    if (refreshRanking && (!this.idle() || this.blocked())) return;
    this.cancelActivation();
    const version = ++this.loadVersion;
    this.tab.set(tab);
    this.activationError.set('');
    this.products.set([]);
    this.state.set('loading');
    const ids = tab === 'favorites'
      ? this.api.getFavorites().pipe(map(snapshot => snapshot.productIds))
      : this.topIds !== null && !refreshRanking
        ? of(this.topIds)
        : this.api.getTopSellers().pipe(map(ranking => {
            if (version === this.loadVersion) this.topIds = ranking;
            return ranking;
          }));

    ids.pipe(switchMap(productIds => this.lookupOrdered(productIds)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: products => {
        if (version !== this.loadVersion) return;
        this.products.set(products.filter(product => product.isActive));
        this.state.set('ready');
      },
      error: () => {
        if (version === this.loadVersion) this.state.set('error');
      },
    });
  }

  activate(product: PosProduct, event: MouseEvent): void {
    if (this.blocked() || this.searching() || this.editorVisible() || this.selecting()) return;
    const trigger = event.currentTarget as HTMLButtonElement;
    const version = this.loadVersion;
    this.selecting.set(true);
    this.activationError.set('');
    this.activation = this.catalog.refreshCartProducts([product.id]).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ([current]) => {
        this.selecting.set(false);
        if (version !== this.loadVersion || this.blocked() || this.searching() || this.editorVisible()) return;
        if (!current?.isActive) {
          this.activationError.set('El producto ya no está disponible.');
          return;
        }
        this.products.update(products => products.map(p => p.id === current.id ? current : p));
        this.selectProduct.emit({ product: current, trigger, keyboard: event.detail === 0 });
      },
      error: () => {
        this.selecting.set(false);
        if (version === this.loadVersion) this.activationError.set('No se pudo verificar el precio y stock actuales.');
      },
    });
  }

  suppressRepeat(event: KeyboardEvent): void {
    if (event.repeat && (event.key === 'Enter' || event.key === ' ')) event.preventDefault();
  }

  navigateTabs(event: KeyboardEvent): void {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const next = event.key === 'Home' ? 'favorites' : event.key === 'End' ? 'top'
      : this.tab() === 'favorites' ? 'top' : 'favorites';
    if (next !== this.tab()) this.loadTab(next);
    const tablist = event.currentTarget as HTMLElement;
    tablist.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next === 'favorites' ? 0 : 1]?.focus();
  }

  openEditor(): void {
    if (!this.canManage() || !this.idle() || this.blocked() || this.selecting()) return;
    this.editorVisible.set(true);
    this.reloadEditor();
  }

  reloadEditor(): void {
    if (this.editLoading()) return;
    this.draftRevision = null;
    this.editError.set('');
    this.stale.set(false);
    this.editLoading.set(true);
    this.api.getFavorites().pipe(
      switchMap(snapshot => this.lookupOrdered(snapshot.productIds).pipe(map(products => ({ snapshot, products })))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: ({ snapshot, products }) => {
        this.editLoading.set(false);
        if (!this.editorVisible()) return;
        this.draftRevision = snapshot.revision;
        this.draftIds.set([...snapshot.productIds]);
        this.draftProducts.set(products);
      },
      error: () => {
        this.editLoading.set(false);
        this.stale.set(true);
        this.editError.set('No se pudieron cargar los favoritos.');
      },
    });
  }

  closeEditor(): void {
    if (!this.editorVisible() || this.saving()) return;
    this.editorVisible.set(false);
    this.searchEditor('');
  }

  searchEditor(value: string): void {
    const term = value.trim();
    this.editMatches.set([]);
    this.editSearching.set(!!term);
    this.editSearchError.set('');
    this.editSearch.next(term);
  }

  addFavorite(product: PosProduct): void {
    if (this.draftIds().includes(product.id) || this.draftIds().length >= this.limit) return;
    this.draftIds.update(ids => [...ids, product.id]);
    this.draftProducts.update(products => [...products.filter(p => p.id !== product.id), product]);
  }

  removeFavorite(id: number): void {
    this.draftIds.update(ids => ids.filter(productId => productId !== id));
  }

  moveFavorite(index: number, direction: -1 | 1): void {
    const ids = [...this.draftIds()];
    const target = index + direction;
    if (index < 0 || index >= ids.length || target < 0 || target >= ids.length) return;
    [ids[index], ids[target]] = [ids[target], ids[index]];
    this.draftIds.set(ids);
  }

  favoriteName(id: number): string {
    return this.draftProducts().find(product => product.id === id)?.name ?? `Producto no disponible (#${id})`;
  }

  save(): void {
    if (!this.canManage() || !this.idle() || !this.editorVisible() || this.draftRevision === null
        || this.editLoading() || this.saving() || this.stale()) return;
    this.saving.set(true);
    this.editError.set('');
    this.api.saveFavorites({ revision: this.draftRevision, productIds: this.draftIds() })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => {
          this.saving.set(false);
          this.closeEditor();
          this.loadTab('favorites');
        },
        error: (error: unknown) => {
          this.saving.set(false);
          const conflict = error instanceof HttpErrorResponse && error.status === 409;
          this.stale.set(conflict);
          this.editError.set(conflict
            ? 'Otra sesión cambió los favoritos. Recarga la lista antes de volver a editar.'
            : 'No se guardaron los favoritos. Comprueba que los productos añadidos sigan activos.');
        },
      });
  }

  private cancelActivation(): void {
    this.activation?.unsubscribe();
    this.activation = undefined;
    this.selecting.set(false);
  }

  private lookupOrdered(ids: number[]): Observable<PosProduct[]> {
    if (!ids.length) return of([]);
    return this.catalog.refreshCartProducts(ids).pipe(map(products => {
      const byId = new Map(products.map(product => [product.id, product]));
      return ids.flatMap(id => {
        const product = byId.get(id);
        return product ? [product] : [];
      });
    }));
  }
}
