import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor, type RenderResult } from "@testing-library/react";
import { Suspense } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import NotificationPage from "@/app/notifications/[id]/page";
import { useAuthStore } from "@/app/lib/auth-store";

const mocks = vi.hoisted(() => ({
  deleteNotification: vi.fn(),
  getNotifications: vi.fn(),
  markAsRead: vi.fn(),
  replace: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: mocks.replace }),
}));

vi.mock("@/app/lib/notification-api", () => ({
  deleteCurrentUserNotification: mocks.deleteNotification,
  getCurrentUserNotifications: mocks.getNotifications,
  markCurrentUserNotificationAsRead: mocks.markAsRead,
  NotificationApiError: class NotificationApiError extends Error {
    constructor(
      message: string,
      readonly status: number,
    ) {
      super(message);
    }
  },
}));

const notifications = [
  {
    id: 1,
    subject: "First notification",
    body: "First body",
    orderId: null,
    isRead: false,
    createdAtUtc: "2026-08-26T01:00:00.000Z",
  },
  {
    id: 2,
    subject: "Second notification",
    body: "Second body",
    orderId: null,
    isRead: false,
    createdAtUtc: "2026-08-26T02:00:00.000Z",
  },
];

function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
}

function page(queryClient: QueryClient, id: number) {
  return (
    <QueryClientProvider client={queryClient}>
      <Suspense fallback={<p>Resolving route</p>}>
        <NotificationPage params={Promise.resolve({ id: String(id) })} />
      </Suspense>
    </QueryClientProvider>
  );
}

describe("NotificationPage hook lifecycle", () => {
  beforeEach(() => {
    mocks.deleteNotification.mockReset().mockResolvedValue(undefined);
    mocks.getNotifications.mockReset().mockResolvedValue(notifications);
    mocks.markAsRead.mockReset().mockImplementation(async (id: number) => ({
      ...notifications.find((notification) => notification.id === id)!,
      isRead: true,
    }));
    mocks.replace.mockReset();
    useAuthStore.setState({
      session: {
        accessToken: "test-access-token",
        expiresAtUtc: "2026-08-26T03:00:00.000Z",
        user: {
          id: "user-1",
          firstName: "Test",
          lastName: "User",
          email: "test@example.test",
          photoUrl: null,
          mfaRequired: false,
          isAdmin: false,
        },
      },
      isLoginDialogOpen: false,
    });
  });

  it("attempts each unread notification when the dynamic route is reused", async () => {
    const queryClient = createQueryClient();
    let view!: RenderResult;
    await act(async () => {
      view = render(page(queryClient, 1));
    });

    expect(await screen.findByRole("heading", { name: "First notification" })).toBeInTheDocument();
    await waitFor(() => {
      expect(mocks.markAsRead).toHaveBeenCalledWith(1, "test-access-token");
    });

    await act(async () => {
      view.rerender(page(queryClient, 2));
    });

    expect(await screen.findByRole("heading", { name: "Second notification" })).toBeInTheDocument();
    await waitFor(() => {
      expect(mocks.markAsRead).toHaveBeenCalledWith(2, "test-access-token");
    });
    expect(mocks.markAsRead).toHaveBeenCalledTimes(2);
  });

  it("does not loop when the automatic mark-as-read request fails", async () => {
    mocks.markAsRead.mockRejectedValue(new Error("service unavailable"));
    const queryClient = createQueryClient();

    await act(async () => {
      render(page(queryClient, 1));
    });

    expect(
      await screen.findByText("The notification opened, but it could not be marked as read."),
    ).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(mocks.markAsRead).toHaveBeenCalledTimes(1);
  });

  it("passes React Query's cancellation signal to the notification request", async () => {
    let capturedSignal: AbortSignal | undefined;
    mocks.getNotifications.mockImplementation((_accessToken: string, signal?: AbortSignal) => {
      capturedSignal = signal;
      return new Promise((_resolve, reject) => {
        signal?.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")));
      });
    });
    const queryClient = createQueryClient();
    let view!: RenderResult;
    await act(async () => {
      view = render(page(queryClient, 1));
    });

    await waitFor(() => expect(capturedSignal).toBeInstanceOf(AbortSignal));
    expect(capturedSignal?.aborted).toBe(false);

    view.unmount();

    expect(capturedSignal?.aborted).toBe(true);
  });
});
