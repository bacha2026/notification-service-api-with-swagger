import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

import {
  OnPushNotificationList,
  type DemoNotification,
} from './on-push-demo/on-push-notification-list';

const initialNotifications: readonly DemoNotification[] = [
  { id: 1, subject: 'Order received', detail: 'Your order is queued for processing.' },
  { id: 2, subject: 'Worker update', detail: 'The notification worker completed delivery.' },
];

@Component({
  selector: 'app-root',
  imports: [OnPushNotificationList],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly notifications = signal<DemoNotification[]>(cloneInitialNotifications());
  protected readonly signalCount = signal(0);
  protected readonly lastAction = signal(
    'Start with the in-place mutation, then compare it with immutable replacement.',
  );

  private nextNotificationId = initialNotifications.length + 1;

  protected sourceCount(): number {
    // Read the current array directly so the parent exposes an in-place mutation
    // when lastAction marks this view. A computed signal would correctly keep its
    // cached value because no notifications signal write occurred.
    return this.notifications().length;
  }

  protected mutateInPlace(): void {
    // Deliberately incorrect for demonstration: no signal write and no new input reference.
    const added = this.createNotification('In-place mutation');
    this.notifications().push(added);
    this.lastAction.set(
      `${added.subject} changed the parent model, but the OnPush child kept the same input reference and stayed unchanged.`,
    );
  }

  protected replaceImmutably(): void {
    this.notifications.update((current) => [
      ...current,
      this.createNotification('Immutable replacement'),
    ]);
    this.lastAction.set(
      'A new array reference crossed the input boundary, so the OnPush child rendered every current item.',
    );
  }

  protected incrementSignal(): void {
    this.signalCount.update((current) => current + 1);
    this.lastAction.set('The signal write marked this OnPush parent for checking automatically.');
  }

  protected reset(): void {
    this.notifications.set(cloneInitialNotifications());
    this.signalCount.set(0);
    this.nextNotificationId = initialNotifications.length + 1;
    this.lastAction.set('The demo is reset with a fresh array reference.');
  }

  private createNotification(updateKind: string): DemoNotification {
    const id = this.nextNotificationId;
    this.nextNotificationId += 1;

    return {
      id,
      subject: `${updateKind} #${id}`,
      detail: `Created by the ${updateKind.toLowerCase()} action.`,
    };
  }
}

function cloneInitialNotifications(): DemoNotification[] {
  return initialNotifications.map((notification) => ({ ...notification }));
}
