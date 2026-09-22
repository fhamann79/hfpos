import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SaleDocumentStatus, SaleDocumentType } from '../../models/sale-document.model';
import { SaleListItem } from '../../models/sale-list-item.model';
import { SalePaymentMethod } from '../../models/sale-payment-method.model';
import { RecentSalesPanel } from './recent-sales-panel';

describe('RecentSalesPanel bounded history', () => {
  let fixture: ComponentFixture<RecentSalesPanel>;
  let component: RecentSalesPanel;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [RecentSalesPanel] }).compileComponents();
    fixture = TestBed.createComponent(RecentSalesPanel);
    component = fixture.componentInstance;
    component.loading = false;
    component.sales = [sale()];
  });

  it('states clearly when the recent page is a bounded subset', () => {
    component.totalItems = 12430;
    fixture.detectChanges();

    const note = (fixture.nativeElement as HTMLElement).querySelector('.bounded-note');
    expect(note?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'Mostrando las últimas 1 de 12,430 ventas.'
    );
  });

  it('does not show a truncation note when every matching sale is visible', () => {
    component.totalItems = 1;
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.bounded-note')).toBeNull();
  });

  function sale(): SaleListItem {
    return {
      id: 1,
      businessDate: '2026-09-18',
      timeZoneIdSnapshot: 'America/Guayaquil',
      createdAt: '2026-09-18T15:00:00Z',
      status: 'Completed',
      paymentMethod: SalePaymentMethod.Cash,
      documentType: SaleDocumentType.Ticket,
      documentStatus: SaleDocumentStatus.NotRequired,
      number: null,
      customerName: null,
      customerIdentification: null,
      customerEmail: null,
      hasSriXmlDraft: false,
      hasSriSignedXml: false,
      sriSignatureStatusKnown: true,
      accessKey: null,
      sriSignedAt: null,
      sriSubmittedAt: null,
      sriReceptionStatus: null,
      sriAuthorizationStatus: null,
      sriLastSubmissionError: null,
      sriLastCheckedAt: null,
      total: 10,
      createdBy: 'cashier',
      isVoided: false,
    };
  }
});
