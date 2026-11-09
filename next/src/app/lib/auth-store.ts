"use client";

import { create } from "zustand";

export type AuthenticatedUser = {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  photoUrl: string | null;
  mfaRequired: boolean;
  isAdmin: boolean;
};

export type AuthSession = {
  accessToken: string;
  expiresAtUtc: string;
  user: AuthenticatedUser;
};

type AuthState = {
  session: AuthSession | null;
  isLoginDialogOpen: boolean;
  setSession: (session: AuthSession) => void;
  updateUser: (user: AuthenticatedUser) => void;
  clearSession: () => void;
  setLoginDialogOpen: (isOpen: boolean) => void;
};

/**
 * The access token intentionally remains in memory. Persisting a bearer token
 * in localStorage/sessionStorage exposes it to any successful XSS attack.
 */
export const useAuthStore = create<AuthState>((set) => ({
  session: null,
  isLoginDialogOpen: false,
  setSession: (session) => set({ session }),
  updateUser: (user) =>
    set((state) => (state.session ? { session: { ...state.session, user } } : state)),
  clearSession: () => set({ session: null, isLoginDialogOpen: false }),
  setLoginDialogOpen: (isLoginDialogOpen) => set({ isLoginDialogOpen }),
}));
