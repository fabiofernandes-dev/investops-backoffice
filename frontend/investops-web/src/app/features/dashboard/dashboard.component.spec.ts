import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { DashboardSummary, ProcessingMetrics } from '../../core/models';
import { DashboardComponent } from './dashboard.component';
import { selectError, selectLoading, selectMetrics, selectSummary } from './state/dashboard.feature';

const summary: DashboardSummary = {
  importedToday: 2,
  processed: 5,
  rejected: 1,
  failed: 0,
  divergencesPending: 0,
  averageProcessingSeconds: 1.5,
};
const metrics: ProcessingMetrics = { operationsByStatus: { Processed: 5, Rejected: 1 }, outboxPending: 0 };

describe('DashboardComponent', () => {
  let store: MockStore;
  let fixture: ComponentFixture<DashboardComponent>;

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [provideNoopAnimations(), provideMockStore()],
    });
    store = TestBed.inject(MockStore);
    store.overrideSelector(selectSummary, null);
    store.overrideSelector(selectMetrics, null);
    store.overrideSelector(selectLoading, false);
    store.overrideSelector(selectError, null);
    fixture = TestBed.createComponent(DashboardComponent);
  }

  it('renders summary cards', () => {
    setup();
    store.overrideSelector(selectSummary, summary);
    store.overrideSelector(selectMetrics, metrics);
    store.refreshState();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Processadas');
    expect(text).toContain('5');
    expect(text).toContain('Outbox pendente');
  });

  it('shows the error state when there is an error', () => {
    setup();
    store.overrideSelector(selectError, 'Falha ao carregar o dashboard.');
    store.refreshState();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Falha ao carregar o dashboard.');
  });
});
