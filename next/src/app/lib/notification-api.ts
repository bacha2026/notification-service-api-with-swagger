import type { AuthSession, AuthenticatedUser } from "@/app/lib/auth-store";
import type { Order } from "@/app/lib/store-types";

const API_PREFIX = "/api/notification";

export type LoginInput = {
  email: string;
  password: string;
};

export type SignUpInput = LoginInput & {
  firstName: string;
  lastName: string;
  address: string;
  dateOfBirth: string;
  gender: number;
  contactNumber: string;
  photoUrl: string | null;
};

export type OrderNotification = {
  id: number;
  subject: string;
  body: string;
  orderId: number | null;
  isRead: boolean;
  createdAtUtc: string;
};

export class NotificationApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
    this.name = "NotificationApiError";
  }
}

function formatApiError(payload: unknown, fallback: string) {
  if (payload && typeof payload === "object") {
    const problem = payload as { detail?: string; title?: string; errors?: Record<string, string[]> };
    const validationMessage = problem.errors
      ? Object.values(problem.errors).flat().filter(Boolean).join(" ")
      : undefined;

    return validationMessage || problem.detail || problem.title || fallback;
  }

  return fallback;
}

async function request<T>(path: string, init: RequestInit = {}, accessToken?: string): Promise<T> {
  const headers = new Headers(init.headers);

  if (init.body && !headers.has("content-type")) {
    headers.set("content-type", "application/json");
  }

  if (accessToken) {
    headers.set("authorization", `Bearer ${accessToken}`);
  }

  const response = await fetch(`${API_PREFIX}${path}`, {
    ...init,
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    let payload: unknown;

    try {
      payload = await response.json();
    } catch {
      // An upstream proxy or API error can legitimately be empty.
    }

    throw new NotificationApiError(
      formatApiError(payload, `The request could not be completed (${response.status}).`),
      response.status,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

function normalizeUser(value: unknown): AuthenticatedUser {
  const user = value as Partial<AuthenticatedUser>;

  return {
    id: String(user.id ?? ""),
    firstName: user.firstName ?? "",
    lastName: user.lastName ?? "",
    email: user.email ?? "",
    photoUrl: user.photoUrl ?? null,
    mfaRequired: Boolean(user.mfaRequired),
    isAdmin: Boolean(user.isAdmin),
  };
}

function normalizeSession(value: unknown): AuthSession {
  const session = value as Partial<AuthSession>;

  if (!session.accessToken || !session.user) {
    throw new NotificationApiError("The authentication response was incomplete.", 502);
  }

  return {
    accessToken: session.accessToken,
    expiresAtUtc: session.expiresAtUtc ?? "",
    user: normalizeUser(session.user),
  };
}

export async function login(input: LoginInput) {
  return normalizeSession(
    await request<unknown>("/auth/login", {
      method: "POST",
      body: JSON.stringify(input),
    }),
  );
}

export async function requestPasswordReset(email: string) {
  return request<{ message: string }>("/auth/forgot-password", {
    method: "POST", body: JSON.stringify({ email }),
  });
}

export async function resetPassword(token: string, newPassword: string) {
  return request<void>("/auth/reset-password", {
    method: "POST", body: JSON.stringify({ token, newPassword }),
  });
}

export async function signUp(input: SignUpInput) {
  return normalizeSession(
    await request<unknown>("/auth/signup", {
      method: "POST",
      body: JSON.stringify(input),
    }),
  );
}

export async function getCurrentUser(accessToken: string, signal?: AbortSignal) {
  return normalizeUser(await request<unknown>("/auth/me", { signal }, accessToken));
}

export async function getCurrentUserNotifications(accessToken: string, signal?: AbortSignal) {
  return request<OrderNotification[]>("/auth/notifications", { signal }, accessToken);
}

export async function markCurrentUserNotificationAsRead(id: number, accessToken: string) {
  return request<OrderNotification>(
    `/auth/notifications/${id}/read`,
    { method: "PATCH" },
    accessToken,
  );
}

export async function deleteCurrentUserNotification(id: number, accessToken: string) {
  return request<void>(
    `/auth/notifications/${id}`,
    { method: "DELETE" },
    accessToken,
  );
}

export async function getVisitorNotifications(visitorEmail: string, signal?: AbortSignal) {
  return request<OrderNotification[]>(`/notifications/visitor?visitorEmail=${encodeURIComponent(visitorEmail)}`, { signal });
}

export async function markVisitorNotificationAsRead(id: number, visitorEmail: string) {
  return request<OrderNotification>(`/notifications/visitor/${id}/read?visitorEmail=${encodeURIComponent(visitorEmail)}`, { method: "PATCH" });
}

export async function deleteVisitorNotification(id: number, visitorEmail: string) {
  return request<void>(`/notifications/visitor/${id}?visitorEmail=${encodeURIComponent(visitorEmail)}`, { method: "DELETE" });
}

export async function getOrder(id: number, accessToken: string, signal?: AbortSignal) {
  return request<Order>(`/orders/${id}`, { signal }, accessToken);
}

export async function updateOrderStatus(
  id: number,
  status: Pick<Order, "orderStatus" | "paymentStatus" | "fulfillmentStatus" | "deliveryStatus">,
  accessToken: string,
) {
  return request<Order>(`/orders/${id}/status`, { method: "PATCH", body: JSON.stringify(status) }, accessToken);
}
