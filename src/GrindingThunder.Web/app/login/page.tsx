"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { useSession } from "@/components/session-provider";

export default function LoginPage() {
  const router = useRouter();
  const { login } = useSession();
  const [usernameOrEmail, setUsernameOrEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await login({ usernameOrEmail, password });
      router.replace("/profile");
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Login failed.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="page-shell grid min-h-[calc(100vh-8rem)] place-items-center py-14">
      <section className="panel w-full max-w-md p-7 sm:p-10">
        <p className="eyebrow">Player access</p>
        <h1 className="mt-3 text-3xl font-black uppercase tracking-tight">Welcome back</h1>
        <p className="mt-3 text-sm leading-6 text-steel">Use your player account. Administrator credentials are intentionally not accepted here.</p>

        <form className="mt-8 space-y-5" onSubmit={submit}>
          {error && <p className="status-error" role="alert">{error}</p>}
          <label className="block">
            <span className="field-label">Username or email</span>
            <input className="field" autoComplete="username" required maxLength={254} value={usernameOrEmail} onChange={(event) => setUsernameOrEmail(event.target.value)} />
          </label>
          <label className="block">
            <span className="field-label">Password</span>
            <input className="field" type="password" autoComplete="current-password" required maxLength={256} value={password} onChange={(event) => setPassword(event.target.value)} />
          </label>
          <button className="button-primary w-full" type="submit" disabled={submitting}>{submitting ? "Signing in…" : "Log in"}</button>
        </form>

        <p className="mt-6 text-center text-sm text-steel">New pilot? <Link className="font-semibold text-signal hover:text-amber-300" href="/register">Create a player account</Link></p>
      </section>
    </div>
  );
}
