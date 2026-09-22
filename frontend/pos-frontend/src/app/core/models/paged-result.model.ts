export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export interface PagedResultWithSummary<T, TSummary> extends PagedResult<T> {
  summary: TSummary | null;
}
