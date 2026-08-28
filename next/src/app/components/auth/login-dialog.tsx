"use client";

import { LoginForm } from "@/app/components/auth/login-form";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/app/components/ui/dialog";
import { useAuthStore } from "@/app/lib/auth-store";

export function LoginDialog() {
  const isOpen = useAuthStore((state) => state.isLoginDialogOpen);
  const setOpen = useAuthStore((state) => state.setLoginDialogOpen);

  return (
    <Dialog open={isOpen} onOpenChange={setOpen}>
      <DialogContent aria-describedby="login-dialog-description">
        <DialogHeader>
          <DialogTitle>Welcome back</DialogTitle>
          <DialogDescription id="login-dialog-description">
            Sign in to see order updates and keep your account details close.
          </DialogDescription>
        </DialogHeader>
        <LoginForm />
      </DialogContent>
    </Dialog>
  );
}
