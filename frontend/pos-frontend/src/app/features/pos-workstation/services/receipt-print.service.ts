import { Injectable } from '@angular/core';

export const RECEIPT_PRINT_CSS = `
  @page { size: 80mm 200mm; margin: 4mm; }
  body { margin: 0; color: #111; background: white; font: 12px Arial, sans-serif; }
  article { width: 72mm; max-width: 100%; overflow-wrap: anywhere; }
  h2 { font-size: 16px; margin: 0 0 8px; }
  p { margin: 5px 0; }
  table { width: 100%; border-collapse: collapse; table-layout: fixed; }
  th, td { padding: 5px 0; text-align: left; vertical-align: top; overflow-wrap: anywhere; }
  th:last-child, td:last-child { width: 22mm; text-align: right; }
  small { display: block; } .totals { border-top: 1px dashed #555; padding-top: 6px; }
`;

@Injectable({ providedIn: 'root' })
export class ReceiptPrintService {
  print(receipt: HTMLElement): { ok: boolean; cleanup: () => void } {
    const frame = document.createElement('iframe');
    frame.title = 'Impresión de ticket interno';
    frame.style.cssText = 'position:fixed;width:80mm;height:1px;border:0;visibility:hidden';
    let timer: ReturnType<typeof setTimeout> | undefined;
    const cleanup = () => { if (timer) clearTimeout(timer); frame.remove(); };
    try {
      document.body.appendChild(frame);
      const doc = frame.contentDocument;
      const win = frame.contentWindow;
      if (!doc || !win) throw new Error('Print window unavailable');
      const style = doc.createElement('style');
      style.textContent = RECEIPT_PRINT_CSS;
      doc.head.appendChild(style);
      doc.body.appendChild(doc.importNode(receipt, true));
      const paperHeight = Math.max(80, Math.ceil(doc.body.scrollHeight * 25.4 / 96) + 8);
      style.textContent += `@page { size: 80mm ${paperHeight}mm; }`;
      win.addEventListener('afterprint', cleanup, { once: true });
      timer = setTimeout(cleanup, 60000);
      win.focus();
      win.print();
      return { ok: true, cleanup };
    } catch {
      cleanup();
      return { ok: false, cleanup };
    }
  }
}
