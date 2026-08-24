import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BoardStore } from './board-store';
import { OrderCard } from './order-card';

@Component({
  selector: 'kitchen-board',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [OrderCard],
  template: `
    <div class="board">
      @for (column of store.kitchenColumns(); track column.key) {
        <section class="column" [attr.data-column]="column.key">
          <header class="column__header">
            <h2>{{ column.title }}</h2>
            <span class="column__count">{{ column.orders.length }}</span>
          </header>
          <div class="column__cards">
            @for (order of column.orders; track order.orderId) {
              <order-card [order]="order" (open)="store.select(order.orderId)" />
            } @empty {
              <p class="column__empty">—</p>
            }
          </div>
        </section>
      }
    </div>
  `,
})
export class KitchenBoard {
  protected readonly store = inject(BoardStore);
}
