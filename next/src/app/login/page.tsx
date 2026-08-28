"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { LoginForm } from "@/app/components/auth/login-form";
import { useAuthStore } from "@/app/lib/auth-store";

export default function LoginPage() {
  const router = useRouter();
  const session = useAuthStore((state) => state.session);

  useEffect(() => {
    if (session) {
      router.replace("/");
    }
  }, [router, session]);

  return (
    <section className="mx-auto grid w-full max-w-md gap-6 rounded-xl border border-[#dedbd2] bg-[#fffdfa] p-6 shadow-sm sm:p-8" aria-labelledby="login-heading">
      <div className="grid gap-2">
        <p className="text-xs font-bold uppercase tracking-[0.14em] text-[#778566]">Your account</p>
        <h1 id="login-heading" className="font-serif text-3xl font-semibold tracking-tight text-[#1d2420]">Welcome back</h1>
        <p className="text-sm leading-relaxed text-[#69716a]">Log in to see your order updates and account details.</p>
      </div>
      <LoginForm onAuthenticated={() => router.replace("/")} />
    </section>
  );
}
