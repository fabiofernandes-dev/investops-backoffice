import { Directive, ElementRef, HostListener, inject } from '@angular/core';


@Directive({
  selector: 'input[appDateMask]',
  standalone: true,
})
export class DateMaskDirective {
  private readonly el = inject<ElementRef<HTMLInputElement>>(ElementRef);

  @HostListener('input')
  onInput(): void {
    const input = this.el.nativeElement;
    const digits = input.value.replace(/\D/g, '').slice(0, 8);

    let masked = digits;
    if (digits.length > 4) {
      masked = `${digits.slice(0, 2)}/${digits.slice(2, 4)}/${digits.slice(4)}`;
    } else if (digits.length > 2) {
      masked = `${digits.slice(0, 2)}/${digits.slice(2)}`;
    }

    if (masked !== input.value) {
      input.value = masked;
      input.setSelectionRange(masked.length, masked.length);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }
}
