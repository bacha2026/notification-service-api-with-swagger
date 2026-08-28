"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { useSignUpMutation } from "@/app/components/auth/use-auth";
import { Button } from "@/app/components/ui/button";
import { Input } from "@/app/components/ui/input";
import { Label } from "@/app/components/ui/label";
import { Select } from "@/app/components/ui/select";

type SignUpFormValues = {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  address: string;
  dateOfBirth: string;
  gender: string;
  contactNumber: string;
  photoUrl: string;
};

const initialValues: SignUpFormValues = {
  firstName: "",
  lastName: "",
  email: "",
  password: "",
  address: "",
  dateOfBirth: "",
  gender: "0",
  contactNumber: "",
  photoUrl: "",
};

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : "We could not create your account. Please try again.";
}

export function SignUpForm() {
  const router = useRouter();
  const signUp = useSignUpMutation();
  const [values, setValues] = useState(initialValues);

  function update<K extends keyof SignUpFormValues>(key: K, value: SignUpFormValues[K]) {
    setValues((current) => ({ ...current, [key]: value }));
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    try {
      await signUp.mutateAsync({
        ...values,
        email: values.email.trim(),
        firstName: values.firstName.trim(),
        lastName: values.lastName.trim(),
        address: values.address.trim(),
        contactNumber: values.contactNumber.trim(),
        gender: Number(values.gender),
        photoUrl: values.photoUrl.trim() || null,
      });
      update("password", "");
      router.replace("/");
    } catch {
      // React Query exposes a user-facing error below the form.
    }
  }

  return (
    <form className="grid gap-5" onSubmit={(event) => void submit(event)} noValidate>
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="grid gap-2">
          <Label htmlFor="signup-first-name">First name</Label>
          <Input id="signup-first-name" autoComplete="given-name" value={values.firstName} onChange={(event) => update("firstName", event.target.value)} required />
        </div>
        <div className="grid gap-2">
          <Label htmlFor="signup-last-name">Last name</Label>
          <Input id="signup-last-name" autoComplete="family-name" value={values.lastName} onChange={(event) => update("lastName", event.target.value)} required />
        </div>
      </div>

      <div className="grid gap-2">
        <Label htmlFor="signup-email">Email</Label>
        <Input id="signup-email" type="email" autoComplete="email" value={values.email} onChange={(event) => update("email", event.target.value)} placeholder="you@example.com" required />
      </div>

      <div className="grid gap-2">
        <Label htmlFor="signup-password">Password</Label>
        <Input id="signup-password" type="password" autoComplete="new-password" minLength={12} value={values.password} onChange={(event) => update("password", event.target.value)} required />
        <p className="text-xs leading-relaxed text-[#69716a]">Use 12-128 characters with uppercase, lowercase, numeric, and symbol characters. Your password is sent only for signup and is never stored in this app.</p>
      </div>

      <div className="grid gap-2">
        <Label htmlFor="signup-address">Address</Label>
        <Input id="signup-address" autoComplete="street-address" value={values.address} onChange={(event) => update("address", event.target.value)} required />
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="grid gap-2">
          <Label htmlFor="signup-date-of-birth">Date of birth</Label>
          <Input id="signup-date-of-birth" type="date" autoComplete="bday" value={values.dateOfBirth} onChange={(event) => update("dateOfBirth", event.target.value)} required />
        </div>
        <div className="grid gap-2">
          <Label htmlFor="signup-gender">Gender</Label>
          <Select id="signup-gender" value={values.gender} onChange={(event) => update("gender", event.target.value)}>
            <option value="0">Unspecified</option>
            <option value="1">Female</option>
            <option value="2">Male</option>
            <option value="3">Non-binary</option>
            <option value="4">Prefer not to say</option>
          </Select>
        </div>
      </div>

      <div className="grid gap-2">
        <Label htmlFor="signup-contact-number">Contact number</Label>
        <Input id="signup-contact-number" type="tel" autoComplete="tel" value={values.contactNumber} onChange={(event) => update("contactNumber", event.target.value)} placeholder="+63 900 000 0000" required />
      </div>

      <div className="grid gap-2">
        <Label htmlFor="signup-photo-url">Profile photo URL <span className="font-normal text-[#69716a]">(optional)</span></Label>
        <Input id="signup-photo-url" type="url" autoComplete="photo" value={values.photoUrl} onChange={(event) => update("photoUrl", event.target.value)} placeholder="https://…" />
      </div>

      {signUp.isError && <p className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{errorMessage(signUp.error)}</p>}

      <Button className="w-full" type="submit" disabled={signUp.isPending}>
        {signUp.isPending ? "Creating account…" : "Create account"}
      </Button>

      <p className="text-center text-sm text-[#69716a]">
        Already have an account?{" "}
        <Link className="font-semibold text-[#596a45] hover:underline" href="/login">
          Log in
        </Link>
      </p>
    </form>
  );
}
