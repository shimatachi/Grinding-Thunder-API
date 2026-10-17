"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { useSession } from "@/components/session-provider";

export default function RegisterPage() {
  const router = useRouter();
  const { register } = useSession();
  const [username, setUsername] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await register({ username, email, password });
      router.replace("/profile");
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Registration failed.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="page-shell grid min-h-[calc(100vh-8rem)] place-items-center py-14">
      <section className="panel w-full max-w-md p-7 sm:p-10">
        <p className="eyebrow">Player enlistment</p>
        <h1 className="mt-3 text-3xl font-black uppercase tracking-tight">Create account</h1>
        <p className="mt-3 text-sm leading-6 text-steel">Save owned vehicles and calculator preferences independently from the admin back office.</p>

        <form className="mt-8 space-y-5" onSubmit={submit}>
          {error && <p className="status-error" role="alert">{error}</p>}
          <label className="block">
            <span className="field-label">Username</span>
            <input className="field" autoComplete="username" required minLength={1} maxLength={64} pattern="[a-zA-Z0-9_.-]+" title="Letters, numbers, dots, underscores, and hyphens only." value={username} onChange={(event) => setUsername(event.target.value)} />
          </label>
          <label className="block">
            <span className="field-label">Email</span>
            <input className="field" type="email" autoComplete="email" required maxLength={254} value={email} onChange={(event) => setEmail(event.target.value)} />
          </label>
          <label className="block">
            <span className="field-label">Password</span>
            <input className="field" type="password" autoComplete="new-password" required minLength={8} maxLength={256} value={password} onChange={(event) => setPassword(event.target.value)} />
            <span className="mt-2 block text-xs text-steel">At least 8 characters.</span>
          </label>
          <button className="button-primary w-full" type="submit" disabled={submitting}>{submitting ? "Creating account…" : "Create player account"}</button>
        </form>

        <p className="mt-6 text-center text-sm text-steel">Already registered? <Link className="font-semibold text-signal hover:text-amber-300" href="/login">Log in</Link></p>
      </section>
    </div>
  );
}
