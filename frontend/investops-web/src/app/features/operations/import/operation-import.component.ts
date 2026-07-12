
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { LookupsApi } from '../../../core/api/lookups.api';
import { AccountLookup, ImportOperationRequest, ProductLookup } from '../../../core/models';
import { OperationsActions } from '../state/operations.actions';
import { selectImportError, selectImporting } from '../state/operations.feature';

@Component({
  selector: 'app-operation-import',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatTooltipModule
],
  templateUrl: './operation-import.component.html',
  styleUrl: './operation-import.component.scss',
})
export class OperationImportComponent implements OnInit {
  private readonly store = inject(Store);
  private readonly lookups = inject(LookupsApi);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly importing = this.store.selectSignal(selectImporting);
  readonly importError = this.store.selectSignal(selectImportError);

  readonly accounts = signal<AccountLookup[]>([]);
  readonly products = signal<ProductLookup[]>([]);
  readonly operationTypes = ['Buy', 'Sell'];

  readonly form = this.fb.nonNullable.group({
    externalId: ['', Validators.required],
    accountId: ['', Validators.required],
    productId: ['', Validators.required],
    operationType: ['Buy', Validators.required],
    quantity: [null as number | null, [Validators.required, Validators.min(0.000001)]],
    unitPrice: [null as number | null, [Validators.required, Validators.min(0.000001)]],
    operationDate: ['', Validators.required],
  });

  ngOnInit(): void {
    this.lookups.accounts().pipe(takeUntilDestroyed(this.destroyRef)).subscribe((a) => this.accounts.set(a));
    this.lookups.products().pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => this.products.set(p));
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const request: ImportOperationRequest = {
      externalId: value.externalId.trim(),
      accountId: value.accountId,
      productId: value.productId,
      operationType: value.operationType,
      quantity: Number(value.quantity),
      unitPrice: Number(value.unitPrice),
      operationDate: value.operationDate,
    };
    this.store.dispatch(OperationsActions.importOperation({ request }));
  }
}
