"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { useLoginMutation } from "@/app/components/auth/use-auth";
import { Button } from "@/app/components/ui/button";
import { Input } from "@/app/components/ui/input";
import { Label } from "@/app/components/ui/label";
import { useAuthStore } from "@/app/lib/auth-store";

type LoginFormProps = {
  onAuthenticated?: () => void;
};

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : "We could not sign you in. Please try again.";
}

export function LoginForm({ onAuthenticated }: LoginFormProps) {
  const login = useLoginMutation();
  const setLoginDialogOpen = useAuthStore((state) => state.setLoginDialogOpen);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    try {
      await login.mutateAsync({ email: email.trim(), password });
      setPassword("");
      onAuthenticated?.();
    } catch {
      // React Query exposes a user-facing error below the form.
    }
  }

  return (
    <form className="grid gap-5" onSubmit={(event) => void submit(event)} noValidate>
      <div className="grid gap-2">
        <Label htmlFor="login-email">Email</Label>
        <Input
          id="login-email"
          type="email"
          autoComplete="email"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          placeholder="you@example.com"
          required
        />
      </div>

      <div className="grid gap-2">
        <Label htmlFor="login-password">Password</Label>
        <Input
          id="login-password"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          required
        />
      </div>

      {login.isError && <p className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{errorMessage(login.error)}</p>}

      <Button className="w-full" type="submit" disabled={login.isPending}>
        {login.isPending ? "Signing in…" : "Login"}
      </Button>

      <div className="flex flex-wrap justify-center gap-x-4 gap-y-2 text-sm text-[#69716a]">
        <Link className="font-semibold text-[#596a45] hover:underline" href="/signup" onClick={() => setLoginDialogOpen(false)}>
          Create an account
        </Link>
        <Link className="font-semibold text-[#596a45] hover:underline" href="mailto:support@example.test?subject=Password%20reset">
          Forgot password?
        </Link>
      </div>
    </form>
  );
}
