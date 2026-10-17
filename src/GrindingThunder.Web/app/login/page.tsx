"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState, type FormEvent } from "react";
import { useSession } from "@/components/session-provider";

function safeReturnTo(value: string | null): string {
  return value?.startsWith("/") && !value.startsWith("//") ? value : "/profile";
}

export default function LoginPage() {
  return (
    <Suspense fallback={<AuthLoading label="Loading sign in…" />}>
      <LoginForm />
    </Suspense>
  );
}

function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { status, login } = useSession();
  const [usernameOrEmail, setUsernameOrEmail] = useState("");
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
    const nextErrors: Record<string, string> = {};
    if (!usernameOrEmail.trim()) nextErrors.usernameOrEmail = "Enter your username or email.";
    if (!password) nextErrors.password = "Enter your password.";
    setFieldErrors(nextErrors);
    if (Object.keys(nextErrors).length > 0) return;

    setSubmitting(true);
    setError(null);
    try {
      await login({ usernameOrEmail: usernameOrEmail.trim(), password });
      router.replace(returnTo);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Login failed. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  const registerHref = returnTo === "/profile"
    ? "/register"
    : `/register?returnTo=${encodeURIComponent(returnTo)}`;

  return (
    <div className="page-shell grid min-h-[calc(100vh-8rem)] place-items-center py-10 sm:py-14">
      <section className="panel w-full max-w-md p-7 sm:p-10" aria-labelledby="login-title">
        <p className="eyebrow">Player access</p>
        <h1 id="login-title" className="mt-3 text-3xl font-black uppercase tracking-tight">Welcome back</h1>
        <p className="mt-3 text-sm leading-6 text-steel">Sign in to synchronize your progress. Any ownership saved in this browser will be merged into your account.</p>

        <form className="mt-8 space-y-5" onSubmit={submit} noValidate>
          {error && <p className="status-error" role="alert">{error}</p>}
          <label className="block">
            <span className="field-label">Username or email</span>
            <input
              className="field"
              autoComplete="username"
              autoFocus
              required
              maxLength={254}
              value={usernameOrEmail}
              aria-invalid={Boolean(fieldErrors.usernameOrEmail)}
              aria-describedby={fieldErrors.usernameOrEmail ? "login-identifier-error" : undefined}
              onChange={(event) => {
                setUsernameOrEmail(event.target.value);
                setFieldErrors((current) => ({ ...current, usernameOrEmail: "" }));
              }}
            />
            {fieldErrors.usernameOrEmail && <span id="login-identifier-error" className="field-error">{fieldErrors.usernameOrEmail}</span>}
          </label>
          <label className="block">
            <span className="field-label">Password</span>
            <input
              className="field"
              type="password"
              autoComplete="current-password"
              required
              maxLength={256}
              value={password}
              aria-invalid={Boolean(fieldErrors.password)}
              aria-describedby={fieldErrors.password ? "login-password-error" : undefined}
              onChange={(event) => {
                setPassword(event.target.value);
                setFieldErrors((current) => ({ ...current, password: "" }));
              }}
            />
            {fieldErrors.password && <span id="login-password-error" className="field-error">{fieldErrors.password}</span>}
          </label>
          <button className="button-primary w-full" type="submit" disabled={submitting || status === "loading"}>
            {submitting ? "Signing in…" : "Log in"}
          </button>
        </form>

        <p className="mt-6 text-center text-sm text-steel">New pilot? <Link className="font-semibold text-signal hover:text-amber-300" href={registerHref}>Create a player account</Link></p>
      </section>
    </div>
  );
}

function AuthLoading({ label }: { label: string }) {
  return <div className="page-shell grid min-h-[30rem] place-items-center text-steel" role="status">{label}</div>;
}
