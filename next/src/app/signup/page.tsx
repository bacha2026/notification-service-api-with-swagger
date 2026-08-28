import { SignUpForm } from "@/app/components/auth/signup-form";

export const metadata = {
  title: "Create account | Urban Grit",
  description: "Create an Urban Grit account to track your orders.",
};

export default function SignUpPage() {
  return (
    <section className="mx-auto grid w-full max-w-xl gap-6 rounded-xl border border-[#dedbd2] bg-[#fffdfa] p-6 shadow-sm sm:p-8" aria-labelledby="signup-heading">
      <div className="grid gap-2">
        <p className="text-xs font-bold uppercase tracking-[0.14em] text-[#778566]">Your account</p>
        <h1 id="signup-heading" className="font-serif text-3xl font-semibold tracking-tight text-[#1d2420]">Create your account</h1>
        <p className="text-sm leading-relaxed text-[#69716a]">Use your own email so we can show the orders and notifications that belong to you.</p>
      </div>
      <SignUpForm />
    </section>
  );
}
