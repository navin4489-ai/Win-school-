import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { readError } from '../platform/schools.component';

interface Book {
  id: number; title: string; isbn?: string | null; category?: string | null; author?: string | null;
  totalCopies: number; availableCopies: number; shelf?: string | null;
}
interface Txn {
  id: number; bookId: number; bookTitle: string; borrower: string; type: string;
  issuedAtUtc: string; dueAtUtc?: string | null; returnedAtUtc?: string | null; fineAmount: number;
}
interface StudentRow { id: number; admissionNo: string; fullName: string; className: string; sectionName: string; }
interface Employee { id: number; employeeNo: string; fullName: string; }
type Tab = 'books' | 'issued';

/** Library catalogue, issuing books and returns with fines. */
@Component({
  selector: 'app-library',
  standalone: true,
  imports: [FormsModule, DatePipe, CurrencyPipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Operations</div>
          <h1>Library</h1>
          <p class="subtitle">Catalogue, book issues, returns and fines.</p>
        </div>
      </header>

      @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
      @if (message()) { <div class="success" role="status">{{ message() }}</div> }

      <div class="tabs" role="tablist">
        <button class="tab" role="tab" [class.active]="tab() === 'books'" (click)="tab.set('books')">Books</button>
        <button class="tab" role="tab" [class.active]="tab() === 'issued'" (click)="setIssued()">Issued &amp; returns</button>
      </div>

      @if (tab() === 'books') {
        <section class="card">
          <div class="card-header"><h3>Add book</h3></div>
          <div class="card-body"><div class="form-grid">
            <label class="field"><span>Title *</span><input [(ngModel)]="book.title" /></label>
            <label class="field"><span>Author</span><input [(ngModel)]="book.author" /></label>
            <label class="field"><span>ISBN</span><input [(ngModel)]="book.isbn" /></label>
            <label class="field"><span>Category</span><input [(ngModel)]="book.category" placeholder="e.g. Fiction, Science" /></label>
            <label class="field"><span>Publisher</span><input [(ngModel)]="book.publisher" /></label>
            <label class="field"><span>Copies *</span><input type="number" min="1" max="10000" [(ngModel)]="book.totalCopies" /></label>
            <label class="field"><span>Shelf</span><input [(ngModel)]="book.shelf" /></label>
            <div class="full"><button class="btn btn-primary" (click)="addBook()"
              [disabled]="saving() || !book.title.trim() || !(book.totalCopies >= 1)">Add book</button></div>
          </div></div>
        </section>

        <section class="card">
          <div class="card-header">
            <h3>Books ({{ books().length }})</h3>
            <input [(ngModel)]="search" (keyup.enter)="loadBooks()" aria-label="Search books"
                   placeholder="Search title, author or ISBN" style="width:100%;max-width:260px" />
          </div>
          @if (!books().length) { <div class="empty">No books found.</div> }
          @else {
            <div class="table-wrap"><table>
              <thead><tr><th>Title</th><th>Author</th><th>Category</th><th>Shelf</th><th>Available</th><th></th></tr></thead>
              <tbody>
                @for (b of books(); track b.id) {
                  <tr>
                    <td class="primary">{{ b.title }}<div class="small muted">{{ b.isbn || '' }}</div></td>
                    <td>{{ b.author || '—' }}</td><td>{{ b.category || '—' }}</td><td>{{ b.shelf || '—' }}</td>
                    <td><span class="badge" [class.teal]="b.availableCopies > 0">{{ b.availableCopies }} / {{ b.totalCopies }}</span></td>
                    <td><button class="btn btn-ghost btn-sm" [disabled]="b.availableCopies < 1" (click)="startIssue(b)">
                      {{ b.availableCopies < 1 ? 'None available' : 'Issue' }}</button></td>
                  </tr>
                  @if (issuing()?.id === b.id) {
                    <tr><td colspan="6">
                      <div class="row" style="gap:8px;flex-wrap:wrap">
                        <select [(ngModel)]="borrowerType" aria-label="Borrower type">
                          <option value="student">Student</option><option value="staff">Staff</option>
                        </select>
                        @if (borrowerType === 'student') {
                          <input [(ngModel)]="studentSearch" (keyup.enter)="findStudents()" aria-label="Search student"
                                 placeholder="Admission no. or name" style="width:100%;max-width:220px" />
                          <button class="btn btn-ghost btn-sm" (click)="findStudents()">Search</button>
                        } @else {
                          <select [(ngModel)]="employeeId" aria-label="Staff member">
                            <option [ngValue]="0">Select staff</option>
                            @for (e of employees(); track e.id) { <option [ngValue]="e.id">{{ e.fullName }} ({{ e.employeeNo }})</option> }
                          </select>
                          <button class="btn btn-primary btn-sm" [disabled]="saving() || !employeeId" (click)="issue(b, null, employeeId)">Issue</button>
                        }
                        <label class="row" style="gap:6px">Days <input type="number" min="1" max="365" [(ngModel)]="loanDays" style="width:70px" aria-label="Loan days" /></label>
                        <button class="btn btn-ghost btn-sm" (click)="issuing.set(null)">Cancel</button>
                      </div>
                      @if (borrowerType === 'student') {
                        @for (s of found(); track s.id) {
                          <div class="list-item">
                            <div style="flex:1"><strong>{{ s.fullName }}</strong> <span class="small muted">{{ s.admissionNo }} · {{ s.className }} {{ s.sectionName }}</span></div>
                            <button class="btn btn-primary btn-sm" [disabled]="saving()" (click)="issue(b, s.id, null)">Issue to {{ s.fullName }}</button>
                          </div>
                        }
                      }
                    </td></tr>
                  }
                }
              </tbody>
            </table></div>
          }
        </section>
      }

      @if (tab() === 'issued') {
        <section class="card">
          <div class="card-header"><h3>Issued books</h3>
            <label class="row" style="gap:6px"><input type="checkbox" [(ngModel)]="openOnly" /> Not yet returned</label>
          </div>
          @if (!visibleTxns().length) { <div class="empty">Nothing to show.</div> }
          @else {
            <div class="table-wrap"><table>
              <thead><tr><th>Book</th><th>Borrower</th><th>Issued</th><th>Due</th><th>Returned</th><th>Fine</th><th></th></tr></thead>
              <tbody>
                @for (t of visibleTxns(); track t.id) {
                  <tr>
                    <td class="primary">{{ t.bookTitle }}</td><td>{{ t.borrower }}</td>
                    <td>{{ t.issuedAtUtc | date:'dd MMM yyyy' }}</td>
                    <td [style.color]="isOverdue(t) ? 'var(--red-500)' : ''">{{ t.dueAtUtc ? (t.dueAtUtc | date:'dd MMM yyyy') : '—' }}{{ isOverdue(t) ? ' (overdue)' : '' }}</td>
                    <td>{{ t.returnedAtUtc ? (t.returnedAtUtc | date:'dd MMM yyyy') : (t.type === 'Lost' ? 'Lost' : '—') }}</td>
                    <td>{{ t.fineAmount ? (t.fineAmount | currency:'INR':'symbol':'1.0-0') : '—' }}</td>
                    <td>
                      @if (!t.returnedAtUtc && t.type === 'Issue') {
                        <div class="row" style="gap:6px;justify-content:flex-end">
                          <button class="btn btn-primary btn-sm" [disabled]="saving()" (click)="returnBook(t, false)">Return</button>
                          <button class="btn btn-ghost btn-sm" [disabled]="saving()" (click)="returnBook(t, true)">Mark lost</button>
                        </div>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table></div>
          }
          <p class="small muted" style="padding:0 16px 12px">Late returns are fined automatically per day overdue.</p>
        </section>
      }
    </div>
  `
})
export class LibraryComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly tab = signal<Tab>('books');
  readonly books = signal<Book[]>([]);
  readonly txns = signal<Txn[]>([]);
  readonly employees = signal<Employee[]>([]);
  readonly found = signal<StudentRow[]>([]);
  readonly issuing = signal<Book | null>(null);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  book = this.blankBook();
  search = '';
  studentSearch = '';
  borrowerType: 'student' | 'staff' = 'student';
  employeeId = 0;
  loanDays = 14;
  openOnly = true;

  ngOnInit(): void { this.loadBooks(); }

  visibleTxns(): Txn[] { return this.openOnly ? this.txns().filter(t => !t.returnedAtUtc && t.type === 'Issue') : this.txns(); }
  isOverdue(t: Txn): boolean { return !t.returnedAtUtc && !!t.dueAtUtc && new Date(t.dueAtUtc) < new Date(); }

  setIssued(): void { this.tab.set('issued'); this.loadTxns(); }

  loadBooks(): void {
    this.api.get<Book[]>('library/books', { search: this.search.trim() || null }).subscribe({ next: b => this.books.set(b), error: r => this.fail(r) });
  }
  loadTxns(): void { this.api.get<Txn[]>('library/transactions').subscribe({ next: t => this.txns.set(t), error: r => this.fail(r) }); }

  addBook(): void {
    if (this.saving()) return;
    this.saving.set(true);
    const b = this.book;
    this.api.post('library/books', {
      title: b.title.trim(), author: b.author.trim() || null, isbn: b.isbn.trim() || null, category: b.category.trim() || null,
      publisher: b.publisher.trim() || null, totalCopies: Number(b.totalCopies), shelf: b.shelf.trim() || null
    }).subscribe({
      next: () => { this.saving.set(false); this.ok(`"${b.title}" added.`); this.book = this.blankBook(); this.loadBooks(); },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  startIssue(b: Book): void {
    this.issuing.set(b);
    this.found.set([]);
    if (!this.employees().length) {
      this.api.get<Employee[]>('hr/employees').subscribe({ next: e => this.employees.set(e), error: () => this.employees.set([]) });
    }
  }

  findStudents(): void {
    if (!this.studentSearch.trim()) return;
    this.api.get<{ items: StudentRow[] }>('students', { search: this.studentSearch.trim(), pageSize: 10 }).subscribe({
      next: r => { this.found.set(r.items); if (!r.items.length) this.fail(null, 'No student found.'); },
      error: r => this.fail(r)
    });
  }

  issue(b: Book, studentId: number | null, employeeId: number | null): void {
    if (this.saving()) return;
    const days = Number(this.loanDays);
    if (!(days >= 1 && days <= 365)) { this.fail(null, 'Loan days must be between 1 and 365.'); return; }
    this.saving.set(true);
    this.api.post('library/issues', { bookId: b.id, studentId, employeeId, loanDays: days }).subscribe({
      next: () => { this.saving.set(false); this.ok(`"${b.title}" issued for ${days} days.`); this.issuing.set(null); this.loadBooks(); },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  returnBook(t: Txn, lost: boolean): void {
    if (this.saving()) return;
    if (lost && !confirm(`Mark "${t.bookTitle}" as lost? The copy will be removed from stock.`)) return;
    this.saving.set(true);
    this.api.post<Txn>('library/returns', { bookTransactionId: t.id, isLost: lost }).subscribe({
      next: res => {
        this.saving.set(false);
        this.ok(lost ? `"${t.bookTitle}" marked as lost.` :
          `"${t.bookTitle}" returned${res?.fineAmount ? ` with a fine of ₹${res.fineAmount}` : ''}.`);
        this.loadTxns();
        this.loadBooks();
      },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  private blankBook() { return { title: '', author: '', isbn: '', category: '', publisher: '', totalCopies: 1, shelf: '' }; }
  private ok(text: string): void { this.error.set(null); this.message.set(text); }
  private fail(r: unknown, text?: string): void { this.message.set(null); this.error.set(text ?? readError(r)); }
}
