import { Pipe, PipeTransform } from '@angular/core';

/**
 * The single place money becomes human-readable. Amounts stay integer-cent
 * strings everywhere else; no float ever touches a money value.
 */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(minorUnits: string | null | undefined): string {
    if (minorUnits == null || !/^-?\d+$/.test(minorUnits)) {
      return '—';
    }

    const negative = minorUnits.startsWith('-');
    const digits = (negative ? minorUnits.slice(1) : minorUnits).padStart(3, '0');
    const whole = digits.slice(0, -2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    const cents = digits.slice(-2);
    return `${negative ? '-' : ''}$${whole}.${cents}`;
  }
}
