import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ErrorStateComponent } from './error-state.component';

describe('ErrorStateComponent', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [ErrorStateComponent],
      providers: [provideNoopAnimations()],
    }),
  );

  it('renders the message', () => {
    const fixture = TestBed.createComponent(ErrorStateComponent);
    fixture.componentRef.setInput('message', 'Falhou ao carregar.');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Falhou ao carregar.');
  });

  it('emits retry when the button is clicked', () => {
    const fixture = TestBed.createComponent(ErrorStateComponent);
    fixture.detectChanges();
    let emitted = false;
    fixture.componentInstance.retry.subscribe(() => (emitted = true));

    fixture.nativeElement.querySelector('button').click();

    expect(emitted).toBe(true);
  });
});
