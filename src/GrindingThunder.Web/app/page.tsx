import Link from "next/link";

const features = [
  {
    number: "01",
    title: "Version-accurate trees",
    body: "Browse published snapshots without mixing ranks, costs, or dependencies between game updates.",
  },
  {
    number: "02",
    title: "Real progression paths",
    body: "Plan from explicit prerequisites and rank unlock rules—not from where a vehicle happens to sit on screen.",
  },
  {
    number: "03",
    title: "One player profile",
    body: "Keep stable vehicle ownership and calculator preferences ready across sessions and future tree versions.",
  },
];

export default function HomePage() {
  return (
    <>
      <section className="page-shell grid min-h-[calc(100vh-4rem)] items-center gap-12 py-20 lg:grid-cols-[1.15fr_0.85fr]">
        <div>
          <p className="eyebrow">War Thunder progression planner</p>
          <h1 className="mt-5 max-w-4xl text-5xl font-black uppercase leading-[0.92] tracking-[-0.05em] text-white sm:text-7xl lg:text-8xl">
            Know the grind.<br />
            <span className="text-signal">Choose the route.</span>
          </h1>
          <p className="mt-7 max-w-2xl text-lg leading-8 text-steel">
            Explore published research trees and prepare a route to your next vehicle with clear RP, Silver Lion, and match estimates.
          </p>
          <div className="mt-9 flex flex-wrap gap-3">
            <Link href="/tree" className="button-primary px-6">Explore tech trees</Link>
            <Link href="/register" className="button-secondary px-6">Save your progress</Link>
          </div>
        </div>

        <div className="panel relative overflow-hidden p-7 sm:p-10">
          <div className="absolute right-0 top-0 h-36 w-36 bg-signal/10 blur-3xl" />
          <p className="eyebrow">Mission brief</p>
          <div className="mt-8 space-y-7">
            {[
              ["Select", "Nation · category · update"],
              ["Map", "Prerequisites · rank gates"],
              ["Estimate", "RP · SL · matches"],
            ].map(([title, detail], index) => (
              <div key={title} className="grid grid-cols-[2.5rem_1fr] gap-4 border-b border-white/10 pb-6 last:border-0 last:pb-0">
                <span className="font-mono text-sm text-signal">0{index + 1}</span>
                <div>
                  <h2 className="text-lg font-bold uppercase tracking-wide">{title}</h2>
                  <p className="mt-1 text-sm text-steel">{detail}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="border-y border-white/10 bg-black/20 py-20">
        <div className="page-shell">
          <p className="eyebrow">Built for the long route</p>
          <h2 className="mt-4 max-w-2xl text-3xl font-black uppercase tracking-tight sm:text-5xl">A planner grounded in the actual tree.</h2>
          <div className="mt-12 grid gap-px bg-white/10 md:grid-cols-3">
            {features.map((feature) => (
              <article key={feature.number} className="bg-canvas p-7 sm:p-9">
                <span className="font-mono text-sm text-signal">{feature.number}</span>
                <h3 className="mt-8 text-xl font-bold">{feature.title}</h3>
                <p className="mt-3 leading-7 text-steel">{feature.body}</p>
              </article>
            ))}
          </div>
        </div>
      </section>
    </>
  );
}
