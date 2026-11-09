"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useState, type FormEvent } from "react";
import { Button } from "@/app/components/ui/button";
import { Input } from "@/app/components/ui/input";
import { Label } from "@/app/components/ui/label";
import { resetPassword } from "@/app/lib/notification-api";

function ResetForm() {
  const token = useSearchParams().get("token") ?? "";
  const [password, setPassword] = useState("");
  const [done, setDone] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setPending(true); setError("");
    try { await resetPassword(token, password); setDone(true); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Please request a new reset link."); }
    finally { setPending(false); }
  }
  return <section className="mx-auto grid w-full max-w-md gap-6 rounded-xl border border-[#dedbd2] bg-[#fffdfa] p-6 shadow-sm sm:p-8">
    <div className="grid gap-2"><p className="text-xs font-bold uppercase tracking-[0.14em] text-[#778566]">Account recovery</p><h1 className="font-serif text-3xl font-semibold tracking-tight text-[#1d2420]">Choose a new password</h1><p className="text-sm leading-relaxed text-[#69716a]">Use 12–128 characters with uppercase, lowercase, a number, and a symbol.</p></div>
    {done ? <p role="status" className="text-sm text-green-900">Your password has been changed. You can now log in.</p> : !token ? <p role="alert" className="text-sm text-red-800">This reset link is missing its token. Request a new one.</p> : <form className="grid gap-5" onSubmit={(event) => void submit(event)}>
      <div className="grid gap-2"><Label htmlFor="new-password">New password</Label><Input id="new-password" type="password" autoComplete="new-password" minLength={12} maxLength={128} value={password} onChange={(event) => setPassword(event.target.value)} required /></div>
      {error && <p role="alert" className="text-sm text-red-800">{error}</p>}
      <Button className="w-full" type="submit" disabled={pending}>{pending ? "Updating…" : "Reset password"}</Button>
    </form>}
    <Link className="text-center text-sm font-semibold text-[#596a45] hover:underline" href="/login">Back to login</Link>
  </section>;
}

export default function ResetPasswordPage() { return <Suspense><ResetForm /></Suspense>; }
