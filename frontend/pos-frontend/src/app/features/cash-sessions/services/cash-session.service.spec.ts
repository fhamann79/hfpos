import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CashSessionStatus } from '../models/cash-session.model';
import { CashSessionService } from './cash-session.service';

describe('CashSessionService pagination', () => {
  let service: CashSessionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CashSessionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends server filters and page bounds and returns the global summary', () => {
    service
      .getAll({
        from: '2026-09-01',
        to: '2026-09-30',
        status: CashSessionStatus.Open,
        userId: 9,
        page: 2,
        pageSize: 30,
      })
      .subscribe((result) => {
        expect(result.totalItems).toBe(32);
        expect(result.summary?.openCount).toBe(32);
      });

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/CashSessions'));
    expect(request.request.params.get('from')).toBe('2026-09-01');
    expect(request.request.params.get('to')).toBe('2026-09-30');
    expect(request.request.params.get('status')).toBe(String(CashSessionStatus.Open));
    expect(request.request.params.get('userId')).toBe('9');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('30');
    request.flush({
      items: [],
      page: 2,
      pageSize: 30,
      totalItems: 32,
      totalPages: 2,
      summary: { openCount: 32, closedCount: 0 },
    });
  });

  it('keeps current-session lookup on its independent endpoint', () => {
    service.getCurrent().subscribe((session) => expect(session).toBeNull());

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/CashSessions/current'));
    expect(request.request.params.keys()).toEqual([]);
    request.flush(null);
  });
});
