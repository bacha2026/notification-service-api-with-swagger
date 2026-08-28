"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, Inbox, Trash2 } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { Button } from "@/app/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/app/components/ui/popover";
import { useAuthStore } from "@/app/lib/auth-store";
import {
  deleteCurrentUserNotification,
  getCurrentUserNotifications,
  NotificationApiError,
  type OrderNotification,
} from "@/app/lib/notification-api";

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-PH", {
    day: "numeric",
    month: "short",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(value));
}

function notificationLabel(notification: OrderNotification) {
  return notification.orderId ? `Order #${notification.orderId}` : "Account notification";
}

export function NotificationBell() {
  const [isOpen, setIsOpen] = useState(false);
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
    refetchInterval: 30_000,
    retry: 1,
  });
  const deleteNotification = useMutation({
    mutationFn: (id: number) => deleteCurrentUserNotification(id, accessToken!),
    onSuccess: async (_, deletedId) => {
      queryClient.setQueryData<OrderNotification[]>(
        notificationQueryKey,
        (current) => current?.filter((notification) => notification.id !== deletedId),
      );
      await queryClient.invalidateQueries({ queryKey: notificationQueryKey });
    },
  });

  useEffect(() => {
    const error = notifications.error ?? deleteNotification.error;
    if (error instanceof NotificationApiError && error.status === 401) {
      clearSession();
    }
  }, [clearSession, deleteNotification.error, notifications.error]);

  const unreadCount = notifications.data?.filter((notification) => !notification.isRead).length ?? 0;

  return (
    <Popover open={isOpen} onOpenChange={(nextIsOpen) => {
      setIsOpen(nextIsOpen);
      if (nextIsOpen) void notifications.refetch();
    }}>
      <PopoverTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="relative rounded-full text-[#eae9e2] hover:bg-white/10 hover:text-white"
          aria-label={unreadCount ? `View ${unreadCount} unread notifications` : "View notifications"}
        >
          <Bell className="size-5" aria-hidden="true" />
          {unreadCount > 0 && (
            <span className="absolute -right-0.5 -top-0.5 grid min-w-4 place-items-center rounded-full border-2 border-[#1d2420] bg-red-600 px-1 text-[0.625rem] font-extrabold leading-4 text-white">
              {unreadCount > 9 ? "9+" : unreadCount}
            </span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="max-h-[min(28rem,calc(100vh-7rem))] overflow-y-auto p-0">
        <div className="flex items-center justify-between border-b border-[#e8e5dc] px-4 py-3">
          <div>
            <h2 className="font-semibold">Notifications</h2>
            <p className="text-xs text-[#69716a]">Updates sent to your account</p>
          </div>
          {unreadCount > 0 && <span className="rounded-full bg-[#edf2e4] px-2 py-1 text-xs font-medium text-[#4e6540]">{unreadCount} new</span>}
        </div>

        {notifications.isLoading && <p className="px-4 py-6 text-sm text-[#69716a]">Loading notifications…</p>}

        {notifications.isError && (
          <p className="px-4 py-6 text-sm text-[#a6453d]" role="alert">
            We could not load notifications right now.
          </p>
        )}

        {deleteNotification.isError && (
          <p className="border-b border-[#e8e5dc] bg-red-50 px-4 py-2 text-sm text-red-800" role="alert">
            We could not delete that notification.
          </p>
        )}

        {notifications.isSuccess && notifications.data.length === 0 && (
          <div className="grid place-items-center gap-2 px-4 py-8 text-center text-[#69716a]">
            <Inbox className="size-5" aria-hidden="true" />
            <p className="text-sm">There are no notifications yet.</p>
          </div>
        )}

        {notifications.isSuccess && notifications.data.length > 0 && (
          <ul className="divide-y divide-[#e8e5dc]">
            {notifications.data.map((notification) => (
              <li className="relative transition-colors hover:bg-[#f7f6f1]" key={notification.id}>
                <Link
                  className="block px-4 py-3 pr-12"
                  href={`/notifications/${notification.id}`}
                  onClick={() => setIsOpen(false)}
                >
                  <div className="flex items-start justify-between gap-3">
                    <p className="text-sm font-semibold leading-snug text-[#1d2420]">{notification.subject}</p>
                    {!notification.isRead && <span className="mt-1.5 size-2 shrink-0 rounded-full bg-[#81955f]" aria-label="Unread" />}
                  </div>
                  <p className="mt-1 line-clamp-2 text-sm leading-relaxed text-[#69716a]">{notification.body}</p>
                  <p className="mt-2 text-xs font-medium text-[#778566]">{notificationLabel(notification)} · {formatDate(notification.createdAtUtc)}</p>
                </Link>
                <Button
                  variant="ghost"
                  size="icon"
                  className="absolute right-2 top-2 size-8 text-[#69716a] hover:bg-red-50 hover:text-red-700"
                  disabled={deleteNotification.isPending && deleteNotification.variables === notification.id}
                  aria-label={`Delete notification: ${notification.subject}`}
                  title="Delete notification"
                  onClick={(event) => {
                    event.preventDefault();
                    event.stopPropagation();
                    deleteNotification.mutate(notification.id);
                  }}
                >
                  <Trash2 className="size-4" aria-hidden="true" />
                </Button>
              </li>
            ))}
          </ul>
        )}
      </PopoverContent>
    </Popover>
  );
}
