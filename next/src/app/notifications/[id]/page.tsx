"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { use, useEffect, useState } from "react";
import { Button } from "@/app/components/ui/button";
import { DEMO_VISITOR_EMAIL } from "@/app/components/store-provider";
import { useAuthStore } from "@/app/lib/auth-store";
import {
  deleteCurrentUserNotification,
  deleteVisitorNotification,
  getOrder,
  getCurrentUserNotifications,
  getVisitorNotifications,
  markCurrentUserNotificationAsRead,
  markVisitorNotificationAsRead,
  NotificationApiError,
  updateOrderStatus,
  type OrderNotification,
} from "@/app/lib/notification-api";

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-PH", {
    dateStyle: "long",
    timeStyle: "short",
  }).format(new Date(value));
}

export default function NotificationPage({ params }: { params: Promise<{ id: string }> }) {
  const { id: rawId } = use(params);
  const notificationId = Number(rawId);
  const router = useRouter();
  const queryClient = useQueryClient();
  const session = useAuthStore((state) => state.session);
  const clearSession = useAuthStore((state) => state.clearSession);
  const accessToken = session?.accessToken;
  const visitorEmail = DEMO_VISITOR_EMAIL;
  const identity = accessToken ? `auth:${accessToken}` : `visitor:${visitorEmail}`;
  const notificationQueryKey = ["notifications", identity] as const;
  const notifications = useQuery({
    queryKey: notificationQueryKey,
    queryFn: ({ signal }) => accessToken
      ? getCurrentUserNotifications(accessToken, signal)
      : getVisitorNotifications(visitorEmail, signal),
    enabled: Boolean(accessToken || visitorEmail),
    staleTime: 15_000,
    retry: 1,
  });
  const markAsRead = useMutation({
    mutationFn: () => accessToken
      ? markCurrentUserNotificationAsRead(notificationId, accessToken)
      : markVisitorNotificationAsRead(notificationId, visitorEmail),
    onSuccess: (updated) => {
      queryClient.setQueryData<OrderNotification[]>(
        notificationQueryKey,
        (current) => current?.map((item) => (item.id === updated.id ? updated : item)),
      );
    },
  });
  const deleteNotification = useMutation({
    mutationFn: () => accessToken
      ? deleteCurrentUserNotification(notificationId, accessToken)
      : deleteVisitorNotification(notificationId, visitorEmail),
    onSuccess: () => {
      queryClient.setQueryData<OrderNotification[]>(
        notificationQueryKey,
        (current) => current?.filter((item) => item.id !== notificationId),
      );
      void queryClient.invalidateQueries({ queryKey: notificationQueryKey });
      router.replace("/");
    },
  });
  const notification = notifications.data?.find((item) => item.id === notificationId);
  const isAdmin = Boolean(session?.user.isAdmin && accessToken);
  const orderQuery = useQuery({
    queryKey: ["admin", "order", notification?.orderId, accessToken],
    queryFn: ({ signal }) => getOrder(notification!.orderId!, accessToken!, signal),
    enabled: Boolean(isAdmin && notification?.orderId),
  });
  const [orderStatusSelection, setOrderStatusSelection] = useState<{
    orderId: number;
    value: number;
  } | null>(null);
  const nextOrderStatus = orderQuery.data && orderStatusSelection?.orderId === orderQuery.data.id
    ? orderStatusSelection.value
    : Number(orderQuery.data?.orderStatus ?? 2);
  const updateStatus = useMutation({
    mutationFn: () => updateOrderStatus(orderQuery.data!.id, {
      orderStatus: nextOrderStatus,
      paymentStatus: Number(orderQuery.data!.paymentStatus),
      fulfillmentStatus: Number(orderQuery.data!.fulfillmentStatus),
      deliveryStatus: Number(orderQuery.data!.deliveryStatus),
    }, accessToken!),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["admin", "order", notification?.orderId] }),
        queryClient.invalidateQueries({ queryKey: notificationQueryKey }),
      ]);
    },
  });
  const markAsReadMutation = markAsRead.mutate;
  const markAsReadStatus = markAsRead.status;
  const resetMarkAsRead = markAsRead.reset;

  // A page instance can be reused while navigating between notification IDs.
  // Reset the mutation observer for the new identity so an earlier success or
  // failure does not suppress the new notification's one automatic attempt.
  useEffect(() => {
    resetMarkAsRead();
  }, [accessToken, notificationId, resetMarkAsRead]);

  useEffect(() => {
    if (notification && !notification.isRead && markAsReadStatus === "idle") {
      markAsReadMutation();
    }
  }, [markAsReadMutation, markAsReadStatus, notification]);

  useEffect(() => {
    const error = notifications.error ?? markAsRead.error ?? deleteNotification.error;
    if (error instanceof NotificationApiError && error.status === 401) clearSession();
  }, [clearSession, deleteNotification.error, markAsRead.error, notifications.error]);

  if (!Number.isInteger(notificationId) || notificationId <= 0) {
    return <section className="page-section"><div className="empty-state"><h1>Notification not found</h1><p>This notification does not exist or does not belong to your account.</p><Link className="primary-link" href="/">Return home</Link></div></section>;
  }

  if (notifications.isPending) return <section className="page-section"><div className="page-status">Loading notification…</div></section>;
  if (notifications.isError) return <section className="page-section"><div className="page-status page-status-error">We couldn’t load this notification.</div></section>;
  if (!notification) {
    return <section className="page-section"><div className="empty-state"><h1>Notification not found</h1><p>This notification does not exist or does not belong to your account.</p><Link className="primary-link" href="/">Return home</Link></div></section>;
  }

  return (
    <section className="page-section" aria-labelledby="notification-heading">
      <div className="page-heading">
        <div className="notification-title">
          <p className="eyebrow">{notification.orderId ? "Order notification" : "Notification"}</p>
          <div className="notification-subject-row">
            <h1 className="notification-subject" id="notification-heading">{notification.subject}</h1>
            <Button
              variant="ghost"
              size="icon"
              className="notification-delete-button size-9 border border-red-200 text-red-700 hover:bg-red-50 hover:text-red-800"
              disabled={deleteNotification.isPending || markAsRead.isPending}
              aria-label={`Delete notification: ${notification.subject}`}
              title="Delete notification"
              onClick={() => deleteNotification.mutate()}
            >
              <Trash2 className="size-4" aria-hidden="true" />
            </Button>
          </div>
        </div>
        <p>{notification.orderId ? `Order #${notification.orderId}` : "Account update"}</p>
      </div>
      <article className="order-card">
        <p className="order-date">{formatDate(notification.createdAtUtc)}</p>
        <p className="mt-6 whitespace-pre-wrap text-base leading-7 text-[#454d47]">{notification.body}</p>
        {markAsRead.isError && <p className="mt-5 text-sm text-[#a6453d]" role="alert">The notification opened, but it could not be marked as read.</p>}
        {deleteNotification.isError && <p className="mt-5 text-sm text-[#a6453d]" role="alert">We could not delete this notification.</p>}
        {isAdmin && notification.orderId && (
          <section className="mt-8 border-t border-[#e8e5dc] pt-6" aria-labelledby="admin-order-status">
            <h2 className="text-lg font-semibold" id="admin-order-status">Update order status</h2>
            {orderQuery.isPending && <p className="mt-3 text-sm">Loading order…</p>}
            {orderQuery.isError && <p className="mt-3 text-sm text-red-700" role="alert">Could not load this order.</p>}
            {orderQuery.data && <div className="mt-3 flex flex-wrap items-end gap-3">
              <label className="grid gap-1 text-sm">Order status
                <select className="rounded border border-[#b8beb5] bg-white px-3 py-2" value={nextOrderStatus} onChange={(event) => setOrderStatusSelection({ orderId: orderQuery.data!.id, value: Number(event.target.value) })}>
                  {[[1,"Draft"],[2,"Pending"],[3,"Awaiting payment"],[4,"Payment verified"],[5,"Confirmed"],[6,"Preparing"],[7,"Ready for pickup"],[8,"Out for delivery"],[9,"Delivered"],[10,"Completed"],[11,"Cancelled"],[12,"Rejected"],[13,"Refunded"]].map(([value,label]) => <option key={value} value={value}>{label}</option>)}
                </select>
              </label>
              <Button disabled={updateStatus.isPending || nextOrderStatus === Number(orderQuery.data.orderStatus)} onClick={() => updateStatus.mutate()}>{updateStatus.isPending ? "Saving…" : "Save status"}</Button>
            </div>}
            {updateStatus.isSuccess && <p className="mt-3 text-sm text-green-800" role="status">Order updated. A notification was sent to the guest.</p>}
            {updateStatus.isError && <p className="mt-3 text-sm text-red-700" role="alert">Could not update the order status.</p>}
          </section>
        )}
      </article>
    </section>
  );
}
