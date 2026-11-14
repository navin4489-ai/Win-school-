import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { readError } from '../platform/schools.component';

interface Item {
  id: number; name: string; code: string; category?: string | null; unit: string; quantity: number;
  minimumStock: number; unitPrice: number; isBelowMinimum: boolean;
}
type Movement = 'Inward' | 'Outward' | 'Adjustment' | 'Return';

/** Store items, stock levels and stock movements. */
@Component({
  selector: 'app-inventory',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, DecimalPipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Operations</div>
          <h1>Inventory</h1>
          <p class="subtitle">Stationery, uniforms, lab and maintenance stock.</p>
        </div>
      </header>

      @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
      @if (message()) { <div class="success" role="status">{{ message() }}</div> }

      <div class="grid" style="display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:16px">
        <section class="card">
          <div class="card-header"><h3>Add item</h3></div>
          <div class="card-body"><div class="form-grid">
            <label class="field"><span>Name *</span><input [(ngModel)]="item.name" /></label>
            <label class="field"><span>Code *</span><input [(ngModel)]="item.code" placeholder="e.g. NB-200" /></label>
            <label class="field"><span>Category</span><input [(ngModel)]="item.category" /></label>
            <label class="field"><span>Unit</span><input [(ngModel)]="item.unit" placeholder="Nos, Box, Kg" /></label>
            <label class="field"><span>Opening stock</span><input type="number" min="0" [(ngModel)]="item.quantity" /></label>
            <label class="field"><span>Alert below</span><input type="number" min="0" [(ngModel)]="item.minimumStock" /></label>
            <label class="field"><span>Unit price (₹)</span><input type="number" min="0" [(ngModel)]="item.unitPrice" /></label>
            <label class="field"><span>Location</span><input [(ngModel)]="item.location" /></label>
            <div class="full"><button class="btn btn-primary" (click)="addItem()" [disabled]="saving() || !itemValid()">Add item</button></div>
          </div></div>
        </section>

        <section class="card">
          <div class="card-header"><h3>Stock movement</h3></div>
          <div class="card-body"><div class="form-grid">
            <label class="field full"><span>Item *</span>
              <select [(ngModel)]="move.itemId">
                <option [ngValue]="0">Select item</option>
                @for (i of items(); track i.id) { <option [ngValue]="i.id">{{ i.name }} ({{ i.quantity | number:'1.0-2' }} {{ i.unit }})</option> }
              </select>
            </label>
            <label class="field"><span>Type *</span>
              <select [(ngModel)]="move.type">
                <option value="Inward">Received (in)</option>
                <option value="Outward">Issued (out)</option>
                <option value="Return">Returned (in)</option>
                <option value="Adjustment">Stock count (set to)</option>
              </select>
            </label>
            <label class="field"><span>{{ move.type === 'Adjustment' ? 'Counted quantity *' : 'Quantity *' }}</span>
              <input type="number" [min]="move.type === 'Adjustment' ? 0 : 0.01" [(ngModel)]="move.quantity" /></label>
            <label class="field"><span>Reference</span><input [(ngModel)]="move.referenceNo" placeholder="Bill / issue slip no." /></label>
            <label class="field"><span>Remarks</span><input [(ngModel)]="move.remarks" /></label>
            <div class="full"><button class="btn btn-primary" (click)="saveMovement()" [disabled]="saving() || !moveValid()">Save movement</button>
              @if (overIssue()) { <span class="small" style="color:var(--red-500)"> Only {{ selected()?.quantity }} in stock.</span> }
            </div>
          </div></div>
        </section>
      </div>

      <section class="card">
        <div class="card-header">
          <h3>Items ({{ items().length }})</h3>
          <div class="row" style="gap:8px;flex-wrap:wrap">
            <label class="row" style="gap:6px"><input type="checkbox" [(ngModel)]="lowOnly" /> Low stock only</label>
            <input [(ngModel)]="search" (keyup.enter)="load()" aria-label="Search items" placeholder="Search name or code"
                   style="width:100%;max-width:220px" />
          </div>
        </div>
        @if (loading()) { <div class="loading">Loading…</div> }
        @else if (!visible().length) { <div class="empty">No items.</div> }
        @else {
          <div class="table-wrap"><table>
            <thead><tr><th>Item</th><th>Code</th><th>Category</th><th>In stock</th><th>Alert below</th><th>Value</th><th>Status</th></tr></thead>
            <tbody>
              @for (i of visible(); track i.id) {
                <tr>
                  <td class="primary">{{ i.name }}</td><td>{{ i.code }}</td><td>{{ i.category || '—' }}</td>
                  <td>{{ i.quantity | number:'1.0-2' }} {{ i.unit }}</td><td>{{ i.minimumStock | number:'1.0-2' }}</td>
                  <td>{{ i.quantity * i.unitPrice | currency:'INR':'symbol':'1.0-0' }}</td>
                  <td><span class="badge" [class.teal]="!i.isBelowMinimum" [style.color]="i.isBelowMinimum ? 'var(--red-500)' : ''">
                    {{ i.isBelowMinimum ? 'Low stock' : 'OK' }}</span></td>
                </tr>
              }
            </tbody>
          </table></div>
        }
      </section>
    </div>
  `
})
export class InventoryComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly items = signal<Item[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  item = this.blankItem();
  move = { itemId: 0, type: 'Inward' as Movement, quantity: 1, referenceNo: '', remarks: '' };
  search = '';
  lowOnly = false;

  ngOnInit(): void { this.load(); }

  visible(): Item[] { return this.lowOnly ? this.items().filter(i => i.isBelowMinimum) : this.items(); }
  selected(): Item | undefined { return this.items().find(i => i.id === this.move.itemId); }
  overIssue(): boolean { const s = this.selected(); return !!s && this.move.type === 'Outward' && Number(this.move.quantity) > s.quantity; }

  load(): void {
    this.loading.set(true);
    this.api.get<Item[]>('inventory', { search: this.search.trim() || null }).subscribe({
      next: i => { this.items.set(i); this.loading.set(false); },
      error: r => { this.loading.set(false); this.fail(r); }
    });
  }

  itemValid(): boolean {
    const i = this.item;
    return !!i.name.trim() && !!i.code.trim() && Number(i.quantity) >= 0 && Number(i.minimumStock) >= 0 && Number(i.unitPrice) >= 0;
  }

  addItem(): void {
    if (this.saving() || !this.itemValid()) return;
    this.saving.set(true);
    const i = this.item;
    this.api.post('inventory', {
      name: i.name.trim(), code: i.code.trim().toUpperCase(), category: i.category.trim() || null, unit: i.unit.trim() || 'Nos',
      quantity: Number(i.quantity), minimumStock: Number(i.minimumStock), unitPrice: Number(i.unitPrice), location: i.location.trim() || null
    }).subscribe({
      next: () => { this.saving.set(false); this.ok(`${i.name} added.`); this.item = this.blankItem(); this.load(); },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  moveValid(): boolean {
    const q = Number(this.move.quantity);
    return this.move.itemId > 0 && (this.move.type === 'Adjustment' ? q >= 0 : q > 0) && !this.overIssue();
  }

  saveMovement(): void {
    if (this.saving() || !this.moveValid()) return;
    this.saving.set(true);
    const m = this.move;
    const name = this.selected()?.name ?? 'Item';
    this.api.post('inventory/stock', {
      inventoryItemId: m.itemId, type: m.type, quantity: Number(m.quantity),
      referenceNo: m.referenceNo.trim() || null, remarks: m.remarks.trim() || null
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.ok(`${name}: stock updated.`);
        this.move = { itemId: m.itemId, type: m.type, quantity: 1, referenceNo: '', remarks: '' };
        this.load();
      },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  private blankItem() {
    return { name: '', code: '', category: '', unit: 'Nos', quantity: 0, minimumStock: 0, unitPrice: 0, location: '' };
  }
  private ok(text: string): void { this.error.set(null); this.message.set(text); }
  private fail(r: unknown): void { this.message.set(null); this.error.set(readError(r)); }
}
