"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState, type FormEvent } from "react";
import { useSession } from "@/components/session-provider";

const usernamePattern = /^[a-zA-Z0-9_.-]+$/;

function safeReturnTo(value: string | null): string {
  return value?.startsWith("/") && !value.startsWith("//") ? value : "/profile";
}

export default function RegisterPage() {
  return (
    <Suspense fallback={<div className="page-shell grid min-h-[30rem] place-items-center text-steel" role="status">Loading registration…</div>}>
      <RegisterForm />
    </Suspense>
  );
}

function RegisterForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { status, register } = useSession();
  const [username, setUsername] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const returnTo = safeReturnTo(searchParams.get("returnTo"));

  useEffect(() => {
    if (status === "authenticated") router.replace(returnTo);
  }, [returnTo, router, status]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const cleanUsername = username.trim();
    const cleanEmail = email.trim();
    const nextErrors: Record<string, string> = {};
    if (!cleanUsername) nextErrors.username = "Choose a username.";
    else if (!usernamePattern.test(cleanUsername)) nextErrors.username = "Use only letters, numbers, dots, underscores, or hyphens.";
    if (!cleanEmail) nextErrors.email = "Enter your email address.";
    else if (!/^\S+@\S+\.\S+$/.test(cleanEmail)) nextErrors.email = "Enter a valid email address.";
    if (password.length < 8) nextErrors.password = "Use at least 8 characters.";
    setFieldErrors(nextErrors);
    if (Object.keys(nextErrors).length > 0) return;

    setSubmitting(true);
    setError(null);
    try {
      await register({ username: cleanUsername, email: cleanEmail, password });
      router.replace(returnTo);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Registration failed. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  const loginHref = returnTo === "/profile"
    ? "/login"
    : `/login?returnTo=${encodeURIComponent(returnTo)}`;

  return (
    <div className="page-shell grid min-h-[calc(100vh-8rem)] place-items-center py-10 sm:py-14">
      <section className="panel w-full max-w-md p-7 sm:p-10" aria-labelledby="register-title">
        <p className="eyebrow">Player enlistment</p>
        <h1 id="register-title" className="mt-3 text-3xl font-black uppercase tracking-tight">Create account</h1>
        <p className="mt-3 text-sm leading-6 text-steel">Save owned vehicles and planning defaults independently from the admin back office.</p>

        <form className="mt-8 space-y-5" onSubmit={submit} noValidate>
          {error && <p className="status-error" role="alert">{error}</p>}
          <label className="block">
            <span className="field-label">Username</span>
            <input className="field" autoComplete="username" autoFocus required maxLength={64} value={username} aria-invalid={Boolean(fieldErrors.username)} aria-describedby={fieldErrors.username ? "register-username-error" : "register-username-hint"} onChange={(event) => { setUsername(event.target.value); setFieldErrors((current) => ({ ...current, username: "" })); }} />
            <span id={fieldErrors.username ? "register-username-error" : "register-username-hint"} className={fieldErrors.username ? "field-error" : "field-hint"}>{fieldErrors.username || "Letters, numbers, dots, underscores, and hyphens only."}</span>
          </label>
          <label className="block">
            <span className="field-label">Email</span>
            <input className="field" type="email" autoComplete="email" required maxLength={254} value={email} aria-invalid={Boolean(fieldErrors.email)} aria-describedby={fieldErrors.email ? "register-email-error" : undefined} onChange={(event) => { setEmail(event.target.value); setFieldErrors((current) => ({ ...current, email: "" })); }} />
            {fieldErrors.email && <span id="register-email-error" className="field-error">{fieldErrors.email}</span>}
          </label>
          <label className="block">
            <span className="field-label">Password</span>
            <input className="field" type="password" autoComplete="new-password" required minLength={8} maxLength={256} value={password} aria-invalid={Boolean(fieldErrors.password)} aria-describedby={fieldErrors.password ? "register-password-error" : "register-password-hint"} onChange={(event) => { setPassword(event.target.value); setFieldErrors((current) => ({ ...current, password: "" })); }} />
            <span id={fieldErrors.password ? "register-password-error" : "register-password-hint"} className={fieldErrors.password ? "field-error" : "field-hint"}>{fieldErrors.password || "At least 8 characters."}</span>
          </label>
          <button className="button-primary w-full" type="submit" disabled={submitting || status === "loading"}>{submitting ? "Creating account…" : "Create player account"}</button>
        </form>

        <p className="mt-6 text-center text-sm text-steel">Already registered? <Link className="font-semibold text-signal hover:text-amber-300" href={loginHref}>Log in</Link></p>
      </section>
    </div>
  );
}
