import { finalize } from 'rxjs';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { readError } from '../platform/schools.component';

interface ClassRoom { id: number; name: string; code: string; stream?: string | null; displayOrder: number; }
interface Section { id: number; classId: number; name: string; capacity: number; studentCount: number; }
interface Subject { id: number; name: string; code: string; isElective: boolean; }

/** Classes, their sections and the subject list of the school. */
@Component({
  selector: 'app-classes',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Settings</div>
          <h1>Classes, sections &amp; subjects</h1>
          <p class="subtitle">Add classes (e.g. Class 1), then their sections (A, B…). Students are enrolled into a section.</p>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      <div class="grid" style="display:grid;grid-template-columns:repeat(auto-fit,minmax(340px,1fr));gap:16px">
        <section class="card">
          <div class="card-header"><h3>{{ editingClass() ? 'Edit class' : 'Add class' }}</h3></div>
          <div class="card-body">
            <form class="form-grid" [formGroup]="classForm" (ngSubmit)="saveClass()">
              <label class="field"><span>Name *</span><input formControlName="name" placeholder="Class 6" /></label>
              <label class="field"><span>Code *</span><input formControlName="code" placeholder="C6" /></label>
              <label class="field"><span>Stream</span><input formControlName="stream" placeholder="Science (optional)" /></label>
              <label class="field"><span>Display order</span><input formControlName="displayOrder" type="number" /></label>
              <div class="full row">
                <button class="btn btn-primary" type="submit" [disabled]="classForm.invalid || busy()">{{ editingClass() ? 'Update' : 'Add class' }}</button>
                @if (editingClass()) { <button class="btn btn-ghost" type="button" (click)="resetClass()">Cancel</button> }
              </div>
            </form>
          </div>
        </section>

        <section class="card">
          <div class="card-header"><h3>Add section</h3></div>
          <div class="card-body">
            @if (!classes().length) {
              <div class="empty">Add a class first, then add its sections here.</div>
            } @else {
              <form class="form-grid" [formGroup]="sectionForm" (ngSubmit)="saveSection()">
                <label class="field"><span>Class *</span>
                  <select formControlName="classId">
                    <option value="">Select class</option>
                    @for (c of classes(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
                  </select>
                </label>
                <label class="field"><span>Section name *</span><input formControlName="name" placeholder="A" /></label>
                <label class="field"><span>Capacity</span><input formControlName="capacity" type="number" /></label>
                <div class="full"><button class="btn btn-primary" type="submit" [disabled]="sectionForm.invalid || busy()">Add section</button></div>
              </form>
            }
          </div>
        </section>

        <section class="card">
          <div class="card-header"><h3>Add subject</h3></div>
          <div class="card-body">
            <form class="form-grid" [formGroup]="subjectForm" (ngSubmit)="saveSubject()">
              <label class="field"><span>Name *</span><input formControlName="name" placeholder="Mathematics" /></label>
              <label class="field"><span>Code *</span><input formControlName="code" placeholder="MATH" /></label>
              <label class="row" style="gap:8px"><input type="checkbox" formControlName="isElective" /><span>Elective</span></label>
              <div class="full"><button class="btn btn-primary" type="submit" [disabled]="subjectForm.invalid || busy()">Add subject</button></div>
            </form>
            <div class="row" style="flex-wrap:wrap;gap:6px;margin-top:12px">
              @for (s of subjects(); track s.id) {
                <span class="badge">{{ s.name }} ({{ s.code }})
                  <button type="button" class="btn btn-ghost btn-sm" style="padding:0 4px;min-height:auto" [disabled]="busy()"
                    (click)="deleteSubject(s)" [attr.aria-label]="'Delete subject ' + s.name" title="Delete subject">×</button>
                </span>
              }
            </div>
          </div>
        </section>
      </div>

      <section class="card">
        <div class="card-header"><h3>Classes</h3></div>
        @if (!classes().length) {
          <div class="empty">No classes yet. Add the first one above.</div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead><tr><th>Class</th><th>Code</th><th>Stream</th><th>Sections</th><th></th></tr></thead>
              <tbody>
                @for (c of classes(); track c.id) {
                  <tr>
                    <td class="primary">{{ c.name }}</td>
                    <td>{{ c.code }}</td>
                    <td>{{ c.stream || '—' }}</td>
                    <td>
                      <div class="row" style="flex-wrap:wrap;gap:6px">
                        @for (s of sections()[c.id] ?? []; track s.id) {
                          <span class="badge teal">{{ s.name }} · {{ s.studentCount }}/{{ s.capacity }}
                            <button type="button" class="btn btn-ghost btn-sm" style="padding:0 4px;min-height:auto" [disabled]="busy()"
                              (click)="deleteSection(c, s)" [attr.aria-label]="'Delete section ' + s.name + ' of ' + c.name" title="Delete section">×</button>
                          </span>
                        }
                        <input #sec placeholder="New section e.g. A" style="width:130px" />
                        <input #cap type="number" value="40" title="Capacity" style="width:70px" />
                        <button class="btn btn-ghost btn-sm" [disabled]="busy()" (click)="addSection(c, sec.value, +cap.value); sec.value=''">Add</button>
                      </div>
                    </td>
                    <td class="row" style="gap:6px">
                      <button class="btn btn-ghost btn-sm" [disabled]="busy()" (click)="editClass(c)">Edit</button>
                      <button class="btn btn-ghost btn-sm" [disabled]="busy()" (click)="deleteClass(c)" [attr.aria-label]="'Delete class ' + c.name">Delete</button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </div>
  `
})
export class ClassesComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly classes = signal<ClassRoom[]>([]);
  readonly sections = signal<Record<number, Section[] | undefined>>({});
  readonly subjects = signal<Subject[]>([]);
  readonly editingClass = signal<number | null>(null);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly busy = signal(false);

  readonly classForm = this.fb.nonNullable.group({
    name: ['', Validators.required], code: ['', Validators.required], stream: [''], displayOrder: [0]
  });
  readonly subjectForm = this.fb.nonNullable.group({
    name: ['', Validators.required], code: ['', Validators.required], isElective: [false]
  });
  readonly sectionForm = this.fb.nonNullable.group({
    classId: ['', Validators.required], name: ['', Validators.required], capacity: [40]
  });

  ngOnInit(): void {
    this.load();
    this.loadSubjects();
  }

  load(): void {
    this.api.get<ClassRoom[]>('admin/classes').subscribe({
      next: items => {
        this.classes.set(items);
        items.forEach(c => this.loadSections(c.id));
      },
      error: r => this.error.set(readError(r))
    });
  }

  loadSections(classId: number): void {
    this.api.get<Section[]>(`admin/classes/${classId}/sections`).subscribe({
      next: items => this.sections.update(all => ({ ...all, [classId]: items }))
    });
  }

  loadSubjects(): void {
    this.api.get<Subject[]>('admin/subjects').subscribe({ next: s => this.subjects.set(s), error: (r: unknown) => this.error.set(readError(r)) });
  }

  saveClass(): void {
    const raw = this.classForm.getRawValue();
    const body = { ...raw, stream: raw.stream || null, displayOrder: Number(raw.displayOrder) || 0 };
    const id = this.editingClass();
    if (this.busy()) return;
    this.busy.set(true);
    (id ? this.api.put(`admin/classes/${id}`, body) : this.api.post('admin/classes', body))
      .pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => { this.ok(id ? 'Class updated.' : `${raw.name} added. Now add its sections.`); this.resetClass(); this.load(); },
      error: r => this.fail(r)
    });
  }

  editClass(c: ClassRoom): void {
    this.editingClass.set(c.id);
    this.classForm.reset({ name: c.name, code: c.code, stream: c.stream ?? '', displayOrder: c.displayOrder });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  resetClass(): void {
    this.editingClass.set(null);
    this.classForm.reset({ name: '', code: '', stream: '', displayOrder: 0 });
  }

  addSection(c: ClassRoom, name: string, capacity: number, after?: () => void): void {
    if (!name.trim() || this.busy()) return;
    this.busy.set(true);
    this.api.post(`admin/classes/${c.id}/sections`, { name: name.trim(), capacity: capacity || 40 }).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => { this.ok(`Section ${name.trim()} added to ${c.name}.`); this.loadSections(c.id); after?.(); },
      error: r => this.fail(r)
    });
  }

  saveSection(): void {
    const raw = this.sectionForm.getRawValue();
    const c = this.classes().find(x => x.id === Number(raw.classId));
    if (!c) return;
    this.addSection(c, raw.name, Number(raw.capacity),
      () => this.sectionForm.reset({ classId: raw.classId, name: '', capacity: 40 }));
  }

  saveSubject(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.post('admin/subjects', this.subjectForm.getRawValue()).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => { this.ok('Subject added.'); this.subjectForm.reset({ name: '', code: '', isElective: false }); this.loadSubjects(); },
      error: r => this.fail(r)
    });
  }

  deleteClass(c: ClassRoom): void {
    if (this.busy() || !window.confirm(`Delete class ${c.name}? Its sections and subject mappings will also be removed.`)) return;
    this.busy.set(true);
    this.api.delete(`admin/classes/${c.id}`).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => {
        this.ok(`${c.name} deleted.`);
        if (this.editingClass() === c.id) this.resetClass();
        this.sections.update(all => ({ ...all, [c.id]: undefined }));
        this.load();
      },
      error: r => this.fail(r)
    });
  }

  deleteSection(c: ClassRoom, s: Section): void {
    if (this.busy() || !window.confirm(`Delete section ${s.name} of ${c.name}?`)) return;
    this.busy.set(true);
    this.api.delete(`admin/classes/${c.id}/sections/${s.id}`).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => { this.ok(`Section ${s.name} removed from ${c.name}.`); this.loadSections(c.id); },
      error: r => this.fail(r)
    });
  }

  deleteSubject(s: Subject): void {
    if (this.busy() || !window.confirm(`Delete subject ${s.name}?`)) return;
    this.busy.set(true);
    this.api.delete(`admin/subjects/${s.id}`).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => { this.ok(`Subject ${s.name} deleted.`); this.loadSubjects(); },
      error: r => this.fail(r)
    });
  }

  private ok(text: string): void { this.error.set(null); this.message.set(text); }
  private fail(r: unknown): void { this.message.set(null); this.error.set(readError(r)); }
}
