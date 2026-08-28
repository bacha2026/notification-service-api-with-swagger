"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { useAuthStore, type AuthSession } from "@/app/lib/auth-store";
import {
  getCurrentUser,
  login,
  NotificationApiError,
  signUp,
  type LoginInput,
  type SignUpInput,
} from "@/app/lib/notification-api";

const currentUserKey = (accessToken: string) => ["auth", "current-user", accessToken] as const;

function useAuthenticationMutation<TInput>(
  authenticate: (input: TInput) => Promise<AuthSession>,
) {
  const queryClient = useQueryClient();
  const setSession = useAuthStore((state) => state.setSession);
  const setLoginDialogOpen = useAuthStore((state) => state.setLoginDialogOpen);

  return useMutation({
    mutationFn: authenticate,
    onSuccess: (session) => {
      setSession(session);
      queryClient.setQueryData(currentUserKey(session.accessToken), session.user);
      setLoginDialogOpen(false);
    },
  });
}

export function useLoginMutation() {
  return useAuthenticationMutation<LoginInput>(login);
}

export function useSignUpMutation() {
  return useAuthenticationMutation<SignUpInput>(signUp);
}

/**
 * Keeps the in-memory profile fresh while treating an expired/invalid JWT as a
 * signed-out state. The bearer token never leaves the browser except in the
 * Authorization header sent to the same-origin proxy.
 */
export function useCurrentUser() {
  const session = useAuthStore((state) => state.session);
  const updateUser = useAuthStore((state) => state.updateUser);
  const clearSession = useAuthStore((state) => state.clearSession);
  const accessToken = session?.accessToken;

  const query = useQuery({
    queryKey: accessToken ? currentUserKey(accessToken) : ["auth", "current-user", "anonymous"],
    queryFn: ({ signal }) => getCurrentUser(accessToken!, signal),
    enabled: Boolean(accessToken),
    staleTime: 2 * 60 * 1000,
    retry: false,
  });

  useEffect(() => {
    if (query.data) {
      updateUser(query.data);
    }
  }, [query.data, updateUser]);

  useEffect(() => {
    if (query.error instanceof NotificationApiError && query.error.status === 401) {
      clearSession();
    }
  }, [clearSession, query.error]);

  return query;
}
