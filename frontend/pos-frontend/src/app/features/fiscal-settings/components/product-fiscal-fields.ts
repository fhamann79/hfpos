import { Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { DeclareProductFiscalClassification } from '../models/fiscal-profile.model';

export function emptyProductClassification(): DeclareProductFiscalClassification {
  return { expectedRevision: 0, expectedProductRevision: 0, kind: null, activity: null, taxBasis: null,
    legalReference: '', evidenceReference: '', sriMaterialCode: null, naturalAgriculturalFoodConfirmed: false,
    foodProcessing: null, constructionMaterialConfirmed: false, confirmed: false };
}

export function classificationComplete(d: DeclareProductFiscalClassification): boolean {
  return d.kind !== null && d.activity !== null && d.taxBasis !== null && d.confirmed
    && !!d.legalReference.trim() && d.legalReference.trim().length <= 500
    && !!d.evidenceReference.trim() && d.evidenceReference.trim().length <= 500
    && (d.taxBasis !== 1 || (d.naturalAgriculturalFoodConfirmed && d.foodProcessing !== null))
    && (d.taxBasis !== 3 || (d.constructionMaterialConfirmed && !!d.sriMaterialCode));
}

@Component({
  selector: 'app-product-fiscal-fields', standalone: true,
  imports: [FormsModule, InputTextModule, SelectModule],
  styles: [`:host { display: grid; gap: 1rem; } .fields { display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); gap: 1rem; }
    label { display: grid; gap: .4rem; min-width: 0; } .wide { grid-column: 1 / -1; }
    .check { display: flex; align-items: flex-start; } input[type=checkbox] { flex: 0 0 auto; margin-top: .25rem; }
    @media(max-width:42rem) { .fields { grid-template-columns: 1fr; } }`],
  template: `
    <div class="fields">
      @if (showKind) { <label>Naturaleza<p-select [options]="kinds" optionLabel="label" optionValue="value" [(ngModel)]="draft.kind" [ngModelOptions]="standalone" placeholder="Seleccionar" appendTo="body" /></label> }
      <label>Actividad<p-select [options]="activities" optionLabel="label" optionValue="value" [(ngModel)]="draft.activity" [ngModelOptions]="standalone" placeholder="Seleccionar" appendTo="body" /></label>
      <label class="wide">Fundamento implementado<p-select [options]="bases" optionLabel="label" optionValue="value" [(ngModel)]="draft.taxBasis" [ngModelOptions]="standalone" placeholder="Seleccionar" appendTo="body" /></label>
      <label class="wide">Referencia normativa concreta<input pInputText [(ngModel)]="draft.legalReference" [ngModelOptions]="standalone" maxlength="500" /></label>
      <label class="wide">Referencia documental del producto<input pInputText [(ngModel)]="draft.evidenceReference" [ngModelOptions]="standalone" maxlength="500" /></label>
      @if (draft.taxBasis === 1) {
        <label class="wide">Procesamiento compatible<p-select [options]="processing" optionLabel="label" optionValue="value" [(ngModel)]="draft.foodProcessing" [ngModelOptions]="standalone" placeholder="Seleccionar" appendTo="body" /></label>
      }
      @if (draft.taxBasis === 3) {
        <label class="wide">Material del listado oficial<p-select [options]="materials" optionLabel="label" optionValue="value" [(ngModel)]="draft.sriMaterialCode" [ngModelOptions]="standalone" placeholder="Seleccionar" appendTo="body" /></label>
      }
    </div>
    @if (draft.taxBasis === 1) { <label class="check"><input type="checkbox" [(ngModel)]="draft.naturalAgriculturalFoodConfirmed" [ngModelOptions]="standalone" /> Confirmo alimento agricola natural, sin mezcla, preparacion ni transformacion incompatible (LRTI 55.1).</label> }
    @if (draft.taxBasis === 3) { <label class="check"><input type="checkbox" [(ngModel)]="draft.constructionMaterialConfirmed" [ngModelOptions]="standalone" /> Confirmo transferencia local y coincidencia exacta con el material de la resolucion 24-13; no toda ferreteria ni contratos de construccion.</label> }
    <label class="check"><input type="checkbox" [(ngModel)]="draft.confirmed" [ngModelOptions]="standalone" /> Confirmo los hechos y su evidencia. La tarifa efectiva depende del perfil vigente de la empresa.</label>
  `,
})
export class ProductFiscalFields {
  @Input() showKind = true;
  @Input({ required: true }) draft = emptyProductClassification();
  readonly standalone = { standalone: true };
  readonly kinds = [{ label: 'Bien', value: 0 }, { label: 'Servicio', value: 1 }];
  readonly activities = [{ label: 'Comercio elegible RIMPE', value: 0 }, { label: 'Servicio elegible RIMPE', value: 1 }, { label: 'Actividad excluida RIMPE', value: 2 }];
  readonly bases = [{ label: 'Operacion gravada ordinaria (15% nominal)', value: 0 }, { label: 'Alimento agricola natural: LRTI 55.1 (0% nominal)', value: 1 }, { label: 'Material listado: resolucion 24-13 (5% nominal)', value: 3 }];
  readonly processing = [{label:'Ninguno',value:0},{label:'Refrigerado',value:1},{label:'Congelado',value:2},{label:'Cortado',value:4},{label:'Empacado',value:8},{label:'Combinacion de estos procesos unicamente',value:15}];
  readonly materials = [
    ['F010101','Varilla AS42, medidas del listado'],['F010201','Arcilla'],['F010202','Arena'],['F010203','Cal'],['F010204','Caliza'],['F010205','Petreos'],
    ['F010301','Hormigon premezclado'],['F010401','Derivados del cemento'],['F010402','Residuos del cemento'],['F010501','Chatarra ferrosa'],['F010601','Morteros'],
    ['F010701','Clinker'],['F010702','Puzolana'],['F010703','Yeso'],['F010801','Adoquines'],['F010802','Bloques'],['F010803','Ladrillos'],['F010804','Prefabricados de hormigon'],
  ].map(([value,label]) => ({value,label:`${value} - ${label}`}));
}
