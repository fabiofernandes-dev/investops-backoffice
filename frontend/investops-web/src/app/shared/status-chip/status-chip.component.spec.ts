import { TestBed } from '@angular/core/testing';
import { StatusChipComponent } from './status-chip.component';

describe('StatusChipComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [StatusChipComponent] }));

  function colorFor(status: string): string {
    const fixture = TestBed.createComponent(StatusChipComponent);
    fixture.componentRef.setInput('status', status);
    fixture.detectChanges();
    return fixture.componentInstance.color();
  }

  it('maps known statuses to colors', () => {
    expect(colorFor('Processed')).toBe('#2e7d32');
    expect(colorFor('Rejected')).toBe('#c62828');
    expect(colorFor('Failed')).toBe('#ef6c00');
  });

  it('falls back to a neutral color for unknown status', () => {
    expect(colorFor('Imported')).toBe('#616161');
  });
});
