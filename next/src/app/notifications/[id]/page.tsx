"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { use, useEffect } from "react";
import { Button } from "@/app/components/ui/button";
import { useAuthStore } from "@/app/lib/auth-store";
import {
  deleteCurrentUserNotification,
  getCurrentUserNotifications,
  markCurrentUserNotificationAsRead,
  NotificationApiError,
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
  const notificationQueryKey = ["auth", "notifications", accessToken] as const;
  const notifications = useQuery({
    queryKey: notificationQueryKey,
    queryFn: ({ signal }) => getCurrentUserNotifications(accessToken!, signal),
    enabled: Boolean(accessToken),
    staleTime: 15_000,
    retry: 1,
  });
  const markAsRead = useMutation({
    mutationFn: () => markCurrentUserNotificationAsRead(notificationId, accessToken!),
    onSuccess: (updated) => {
      queryClient.setQueryData<OrderNotification[]>(
        notificationQueryKey,
        (current) => current?.map((item) => (item.id === updated.id ? updated : item)),
      );
    },
  });
  const deleteNotification = useMutation({
    mutationFn: () => deleteCurrentUserNotification(notificationId, accessToken!),
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

  if (!accessToken) {
    return <section className="page-section"><div className="empty-state"><h1>Sign in to view this notification</h1><Link className="primary-link" href="/login">Go to login</Link></div></section>;
  }

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
      </article>
    </section>
  );
}
