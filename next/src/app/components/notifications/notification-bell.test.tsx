import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NotificationBell } from "@/app/components/notifications/notification-bell";
import { useAuthStore } from "@/app/lib/auth-store";

const mocks = vi.hoisted(() => ({
  deleteNotification: vi.fn(),
  getNotifications: vi.fn(),
}));

vi.mock("@/app/lib/notification-api", () => ({
  deleteCurrentUserNotification: mocks.deleteNotification,
  getCurrentUserNotifications: mocks.getNotifications,
  NotificationApiError: class NotificationApiError extends Error {
    constructor(
      message: string,
      readonly status: number,
    ) {
      super(message);
    }
  },
}));

function renderBell() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <NotificationBell />
    </QueryClientProvider>,
  );
}

describe("NotificationBell navigation", () => {
  beforeEach(() => {
    mocks.deleteNotification.mockReset().mockResolvedValue(undefined);
    mocks.getNotifications.mockReset().mockResolvedValue([
      {
        id: 5007,
        subject: "Order update",
        body: "Your order has shipped.",
        orderId: 42,
        isRead: true,
        createdAtUtc: "2026-08-28T02:00:00.000Z",
      },
    ]);
    useAuthStore.setState({
      session: {
        accessToken: "test-access-token",
        expiresAtUtc: "2026-08-28T03:00:00.000Z",
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

  it("closes the popover every time the same notification link is selected", async () => {
    const user = userEvent.setup();
    renderBell();

    await user.click(screen.getByRole("button", { name: "View notifications" }));
    let notificationLink = await screen.findByRole("link", { name: /Order update/ });
    notificationLink.addEventListener("click", (event) => event.preventDefault());

    await user.click(notificationLink);

    await waitFor(() => {
      expect(screen.queryByRole("link", { name: /Order update/ })).not.toBeInTheDocument();
    });

    await user.click(screen.getByRole("button", { name: "View notifications" }));
    notificationLink = await screen.findByRole("link", { name: /Order update/ });
    notificationLink.addEventListener("click", (event) => event.preventDefault());

    await user.click(notificationLink);

    await waitFor(() => {
      expect(screen.queryByRole("link", { name: /Order update/ })).not.toBeInTheDocument();
    });
  });
});
