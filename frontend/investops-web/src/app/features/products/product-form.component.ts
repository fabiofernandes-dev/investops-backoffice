import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { ProductRequest } from '../../core/models';
import { ProductsActions } from './state/products.actions';
import { selectItems, selectSaveError, selectSaving } from './state/products.feature';

@Component({
  selector: 'app-product-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatButtonModule,
    MatIconModule,
    MatTooltipModule,
  ],
  templateUrl: './product-form.component.html',
  styleUrl: './product-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductFormComponent implements OnInit {
  private readonly store = inject(Store);
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);

  readonly productId = this.route.snapshot.paramMap.get('id');
  readonly isEdit = this.productId !== null;

  readonly saving = this.store.selectSignal(selectSaving);
  readonly saveError = this.store.selectSignal(selectSaveError);
  private readonly items = this.store.selectSignal(selectItems);
  private readonly product = computed(() => this.items().find((p) => p.id === this.productId) ?? null);

  readonly productTypes = ['FixedIncome', 'Fund', 'Stock', 'Derivative'];

  readonly form = this.fb.nonNullable.group({
    productType: ['FixedIncome', Validators.required],
    name: ['', Validators.required],
    ticker: [null as string | null],
    riskLevel: [1, [Validators.required, Validators.min(1), Validators.max(5)]],
    liquidityDays: [0, [Validators.required, Validators.min(0)]],
    isActive: [true],
  });

  private patched = false;

  constructor() {
    effect(() => {
      const product = this.product();
      if (product && !this.patched) {
        this.form.patchValue({
          productType: product.productType,
          name: product.name,
          ticker: product.ticker,
          riskLevel: product.riskLevel,
          liquidityDays: product.liquidityDays,
          isActive: product.isActive,
        });
        this.patched = true;
      }
    });
  }

  ngOnInit(): void {
    if (this.isEdit && this.items().length === 0) {
      this.store.dispatch(ProductsActions.load());
    }
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const ticker = value.ticker?.trim() ? value.ticker.trim() : null;
    const request: ProductRequest = {
      productType: value.productType,
      name: value.name.trim(),
      ticker,
      riskLevel: Number(value.riskLevel),
      liquidityDays: Number(value.liquidityDays),
    };
    if (this.isEdit) {
      request.isActive = value.isActive;
      this.store.dispatch(ProductsActions.save({ id: this.productId!, request }));
    } else {
      this.store.dispatch(ProductsActions.save({ request }));
    }
  }
}
