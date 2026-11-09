"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { Button } from "@/app/components/ui/button";
import { Input } from "@/app/components/ui/input";
import { Label } from "@/app/components/ui/label";
import { requestPasswordReset } from "@/app/lib/notification-api";

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setPending(true); setError("");
    try { await requestPasswordReset(email.trim()); setSent(true); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Please try again."); }
    finally { setPending(false); }
  }

  return <section className="mx-auto grid w-full max-w-md gap-6 rounded-xl border border-[#dedbd2] bg-[#fffdfa] p-6 shadow-sm sm:p-8">
    <div className="grid gap-2"><p className="text-xs font-bold uppercase tracking-[0.14em] text-[#778566]">Account recovery</p><h1 className="font-serif text-3xl font-semibold tracking-tight text-[#1d2420]">Forgot your password?</h1><p className="text-sm leading-relaxed text-[#69716a]">Enter the email address on your account and we’ll send a reset link if it matches.</p></div>
    {sent ? <p role="status" className="rounded-md bg-green-50 px-3 py-3 text-sm text-green-900">If an account exists for that email, a reset link will be sent. Check your inbox.</p> : <form className="grid gap-5" onSubmit={(event) => void submit(event)}>
      <div className="grid gap-2"><Label htmlFor="recovery-email">Email</Label><Input id="recovery-email" type="email" autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} required /></div>
      {error && <p role="alert" className="text-sm text-red-800">{error}</p>}
      <Button className="w-full" type="submit" disabled={pending}>{pending ? "Sending…" : "Send reset link"}</Button>
    </form>}
    <Link className="text-center text-sm font-semibold text-[#596a45] hover:underline" href="/login">Back to login</Link>
  </section>;
}
