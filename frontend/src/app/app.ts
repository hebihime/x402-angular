import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { KitchenBoard } from './board';
import { BoardStore } from './board-store';
import { OrderDrawer } from './drawer';
import { PaymentStrip } from './payment-strip';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [KitchenBoard, PaymentStrip, OrderDrawer],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  protected readonly store = inject(BoardStore);

  ngOnInit(): void {
    void this.store.init();
  }

  protected onRestaurantChange(event: Event): void {
    void this.store.selectRestaurant((event.target as HTMLSelectElement).value);
  }
}
