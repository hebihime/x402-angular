import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BoardStore } from './board-store';
import { OrderCard } from './order-card';

@Component({
  selector: 'payment-strip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [OrderCard],
  template: `
    <section class="strip" [class.strip--alert]="store.needsAttention()">
      <header class="strip__header">
        <h2>Payment lifecycle</h2>
        @if (store.needsAttention()) {
          <span class="strip__badge">needs attention</span>
        }
        <span class="column__count">{{ store.paymentStrip().length }}</span>
      </header>
      <div class="strip__cards">
        @for (order of store.paymentStrip(); track order.orderId) {
          <div class="strip__item">
            <span class="strip__status" [attr.data-status]="order.status">{{ order.status }}</span>
            <order-card [order]="order" (open)="store.select(order.orderId)" />
          </div>
        } @empty {
          <p class="column__empty">no refunds in flight</p>
        }
      </div>
    </section>
  `,
})
export class PaymentStrip {
  protected readonly store = inject(BoardStore);
}
