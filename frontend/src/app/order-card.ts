import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { OrderSummary } from './api/schemas';
import { MoneyPipe } from './money-pipe';

@Component({
  selector: 'order-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MoneyPipe],
  template: `
    <button type="button" class="card" [class.card--alert]="order().status === 'refund_failed'" (click)="open.emit()">
      <div class="card__row">
        <span class="card__id">#{{ order().orderId.slice(0, 8) }}</span>
        <span class="card__total">{{ order().total | money }}</span>
      </div>
      <div class="card__row card__row--muted">
        <span>{{ order().customerId }}</span>
        <span>{{ order().createdAt | date: 'HH:mm:ss' }}</span>
      </div>
      @if (order().status === 'refund_failed') {
        <div class="card__flag">needs attention — manual refund</div>
      } @else if (order().refundAttempts > 0) {
        <div class="card__note">refund attempts: {{ order().refundAttempts }}</div>
      }
    </button>
  `,
})
export class OrderCard {
  readonly order = input.required<OrderSummary>();
  readonly open = output<void>();
}
