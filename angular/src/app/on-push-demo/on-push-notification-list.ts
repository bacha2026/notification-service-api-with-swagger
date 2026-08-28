import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export interface DemoNotification {
  readonly id: number;
  readonly subject: string;
  readonly detail: string;
}

@Component({
  selector: 'app-notification-list',
  templateUrl: './on-push-notification-list.html',
  styleUrl: './on-push-notification-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OnPushNotificationList {
  readonly notifications = input.required<readonly DemoNotification[]>();
}
